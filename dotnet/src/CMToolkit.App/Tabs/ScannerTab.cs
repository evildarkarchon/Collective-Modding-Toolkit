using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.Scanner;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Tabs;

/// <summary>
/// <c>tabs._scanner.ScannerTab</c>: the results tree under Collapse All / Expand All and the results info label, the
/// scanning label and the progress bar (SCN-1, SCN-2), with the side pane and the details pane docked to the main
/// window as owned windows (SCN-3..SCN-5). Scan Game runs the scan (SCN-S1..SCN-S6, THR-1) and lists its results
/// (SCN-R1..SCN-R3).
/// </summary>
/// <remarks>
/// A scan runs in two phases, as the reference split them (ADR-0003): the Overview refresh, which the reference ran on
/// the UI thread, is a blocking operation; the worker, which it ran on its scan thread, is an interactive one, so the
/// tabs stay live and the reference's mid-scan Failure Outcomes stay reachable. Leaving the tab mid-scan destroys the
/// side pane, so the populate that follows the worker raises, as <c>populate_results</c> raised
/// <c>ValueError</c>: the results are never listed, and the scan's label and progress stay as they were.
/// </remarks>
public sealed class ScannerTab : TabPage
{
    private readonly AppRuntime _runtime;
    private readonly GameInstall _game;
    private readonly OverviewState _overview;
    private readonly SettingsStore _settings;
    private readonly Dictionary<TreeViewItem, Problem> _items = [];
    private OwnedPaneDocker? _docker;
    private TreeView? _tree;
    private TextBlock? _resultsInfo;
    private TextBlock? _scanningText;
    private ProgressBar? _progress;
    private bool _selectable;

    // scan_folders lives on the tab, so the last folder tuple carries over to the next scan (SCN-S4).
    private readonly ScanProgressTracker _tracker = new();

    // The running scan's progress queue, or null when no worker is running; replaced per scan so a late update from an
    // earlier scan can't reach this one.
    private volatile ConcurrentQueue<ScanUpdate>? _pending;

    /// <summary>The Scanner tab, built on first switch; its side pane docks beside the main window when it is shown.</summary>
    /// <param name="runtime">The app runtime: its log, error reporting and background operations.</param>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="overview">The Overview state: its problems seed a scan, and it refreshes the Overview tab.</param>
    /// <param name="settings">The settings the scan saves its boxes to (SET-7).</param>
    public ScannerTab(AppRuntime runtime, GameInstall game, OverviewState overview, SettingsStore settings)
        : base("Scanner", "ScannerTab")
    {
        _runtime = runtime;
        _game = game;
        _overview = overview;
        _settings = settings;
        Progress = new ProgressQueue(this);
        Worker = s => ScanWorker.Run(s, Progress, _runtime.Logger);
    }

    /// <summary>
    /// The scan's worker part (<c>scan_data_files</c>), run off the UI thread as an interactive operation:
    /// <see cref="ScanWorker.Run(ScanSettings, IProgress{ScanUpdate}?, Microsoft.Extensions.Logging.ILogger)"/> reporting
    /// to <see cref="Progress"/> by default. A test seam, so tests can hold a scan mid-run or make the worker fail.
    /// </summary>
    public Func<ScanSettings, ScanOutcome> Worker { get; init; }

    /// <summary>
    /// Where the worker reports its progress (<c>queue_progress</c>, SCN-S4), from any thread. Updates are queued for
    /// the running scan and applied on the UI thread in order; the queue is drained once more when the worker returns,
    /// before the results are listed, as <c>check_scan_progress</c> drained it before testing for the finished thread.
    /// </summary>
    public IProgress<ScanUpdate> Progress { get; }

    /// <summary>
    /// Raised on the UI thread after each progress update is applied to the scanning label and the progress bar
    /// (whether or not it changed them). A test seam for reading the tab after every update.
    /// </summary>
    public event EventHandler<ScanUpdate>? ProgressApplied;

    /// <summary>
    /// Stage mode (<c>using_stage</c>): the mod manager has a staging folder, which only MO2 has. The results tree then
    /// has the mod column (SCN-2).
    /// </summary>
    public bool UsingStage => _game.Mo2 is not null;

    /// <summary>The side pane's view while the tab is current (<c>side_pane</c>), or null.</summary>
    public ScanSidePane? SidePane { get; private set; }

    /// <summary>The details pane's view once a result has been selected (<c>details_pane</c>), or null.</summary>
    public ScanDetailsPane? DetailsPane { get; private set; }

    /// <summary>The results tree, or null before the tab is built.</summary>
    public TreeView? Tree => _tree;

    /// <summary>Whether results can be selected (<c>selectmode</c> browse rather than none): only once a scan has populated them.</summary>
    public bool IsSelectable => _selectable;

    /// <summary>The problem a results tree item stands for (<c>tree_results_data</c>), or null for a group row.</summary>
    public Problem? ProblemOf(TreeViewItem item) => _items.GetValueOrDefault(item);

    /// <summary>
    /// The machine the scan reads (SCN-S5, SCN-S6, the data walk) and the details pane checks result paths on (SCN-R5):
    /// a relative path is relative to its working directory. The real machine by default.
    /// </summary>
    public IHostEnvironment Host { get; init; } = SystemHostEnvironment.Instance;

    /// <summary>What the details pane's path click opens a folder with: the shell by default, a recorder in tests.</summary>
    public Action<string> OpenFolder { get; init; } = ScanDetailsPane.StartFile;

    /// <summary><c>ScannerTab._load</c>: fails with <c>Data folder not found</c> when there is no Data folder (SCN-1).</summary>
    protected internal override Task<bool> LoadAsync()
    {
        if (_game.DataPath is null)
        {
            LoadingError = "Data folder not found";
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary><c>ScannerTab._build_gui</c> (SCN-2).</summary>
    protected internal override Control BuildContent()
    {
        // grid_columnconfigure(0, weight=1); rows 0 and 2..4 unweighted, row 1 weight 1 (the tree spans rows 1 and 2).
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"),
        };

        // frame_tree_controls: grid(row=0, columnspan=2, sticky=EW, padx=5, pady=5); the buttons packed left with
        // padx=(0, 5), the info label packed right with padx=(0, 5).
        var collapse = ControlButton("CollapseAll", "Collapse All", expanded: false);
        var expand = ControlButton("ExpandAll", "Expand All", expanded: true);
        _resultsInfo = new TextBlock
        {
            Name = "ResultsInfo",
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        _resultsInfo.Classes.Add("small");
        _resultsInfo.Bind(TextBlock.ForegroundProperty, _resultsInfo.GetResourceObservable("CmtNeutral2"));
        DockPanel.SetDock(collapse, Dock.Left);
        DockPanel.SetDock(expand, Dock.Left);
        DockPanel.SetDock(_resultsInfo, Dock.Right);
        var controls = new DockPanel { Margin = new Thickness(5), Children = { collapse, expand, _resultsInfo, new Panel() } };
        Grid.SetColumnSpan(controls, 2);
        grid.Children.Add(controls);

        // The tree (sticky=NSEW in column 0, inside sv_ttk's 1 px rounded field border) scrolls in a viewer of its own,
        // and scroll_results_y is a separate bar in column 1 (sticky=NS), shown whether or not there is anything to
        // scroll, as a ttk.Scrollbar is.
        _tree = new TreeView { Name = "Results" };
        _tree.Classes.Add("results");
        _tree.SelectionChanged += (_, _) => OnRowSelect();
        var scroller = new ScrollViewer
        {
            Content = _tree,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        };
        var field = new Border { Name = "ResultsField", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = scroller };
        field.Bind(Border.BorderBrushProperty, field.GetResourceObservable("SvStrip"));
        Grid.SetRow(field, 1);
        Grid.SetRowSpan(field, 2);
        grid.Children.Add(field);

        var bar = new Avalonia.Controls.Primitives.ScrollBar
        {
            Name = "ResultsScroll",
            Orientation = Orientation.Vertical,
            AllowAutoHide = false,
            // 24: the width of the reference's scrollbar column here, its 12 px sv_ttk trough centred with 6 px either
            // side (measured on parity/screenshots/scanner/results/tk.png, from the tree's card to the tab's edge).
            // F4seTab's bar is 12 px with a 5 px margin because that is what its own pair measures; both widths copy
            // the reference's screens rather than one rule.
            Width = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        scroller.ScrollChanged += (_, _) =>
        {
            bar.Maximum = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            bar.ViewportSize = scroller.Viewport.Height;
            bar.Value = scroller.Offset.Y;
        };
        bar.Scroll += (_, e) => scroller.Offset = scroller.Offset.WithY(e.NewValue);
        Grid.SetRow(bar, 1);
        Grid.SetRowSpan(bar, 2);
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);

        // label_scanning_text: created on the first scan, grid(row=3, sticky=EW, padx=5, pady=5); here built once and
        // shown only while a scan runs.
        _scanningText = new TextBlock { Name = "ScanningText", Margin = new Thickness(5), IsVisible = false };
        _scanningText.Bind(TextBlock.ForegroundProperty, _scanningText.GetResourceObservable("CmtNeutral2"));
        Grid.SetRow(_scanningText, 3);
        grid.Children.Add(_scanningText);

        // ttk.Progressbar(maximum=100): grid(row=4, columnspan=2, sticky=EW, ipady=1).
        _progress = new ProgressBar { Name = "Progress", Minimum = 0, Maximum = 100, Value = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(_progress, 4);
        Grid.SetColumnSpan(_progress, 2);
        grid.Children.Add(_progress);

        return grid;
    }

    /// <summary><c>ScannerTab._switch_to</c>: opens the side pane, docked to the main window, if it isn't open.</summary>
    protected internal override void SwitchTo()
    {
        if (SidePane is not null)
        {
            return;
        }

        // The boxes start as last saved (bug #20 fixed), so a scan's choices carry over to the next pane and launch.
        SidePane = new ScanSidePane(ScanSettings.Stored(_settings.Current));
        SidePane.ScanRequested += (_, _) => _ = RunScanAsync();
        if (TopLevel.GetTopLevel(_tree) is not null)
        {
            Docker().Show(PaneSlot.Side, SidePane);
            return;
        }

        // A tab selected by the user is switched to before the TabControl presents its content, so the tab isn't in
        // the window yet: the pane opens as soon as it is (Tk mapped its Toplevel at the next idle too).
        var pending = SidePane;
        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _tree!.AttachedToVisualTree -= OnAttached;
            if (ReferenceEquals(SidePane, pending))
            {
                Docker().Show(PaneSlot.Side, pending);
            }
        }

        _tree!.AttachedToVisualTree += OnAttached;
    }

    /// <summary>
    /// <c>ScannerTab.switch_from</c>: clears the selection and closes both panes (SCN-5). Does nothing when the tab
    /// never built because its load failed (SCN-1), so the tab change completes.
    /// </summary>
    protected internal override void SwitchFrom()
    {
        if (_tree is null)
        {
            // Bug #87, fixed (ADR-0006): the reference's switch_from raised AttributeError on the missing tree, which
            // left the main window on this tab and broke every later tab change. A tab that never built has no tree
            // and never opened its panes (SwitchTo runs only after a successful load), so there is nothing to undo.
            return;
        }

        _tree.SelectedItem = null;
        if (SidePane is not null)
        {
            _docker?.Close(PaneSlot.Side);
            SidePane = null;
        }

        CloseDetails();
    }

    /// <summary>
    /// Scan Game (<c>start_threaded_scan</c>, then <c>check_scan_progress</c> and <c>populate_results</c> once the worker
    /// is done): SCN-S1..SCN-S3, SCN-R1..SCN-R3.
    /// </summary>
    /// <returns>A task that completes when the results are listed, or when the scan stopped short.</returns>
    /// <exception cref="InvalidOperationException">
    /// The side pane is gone (<c>ValueError</c>): when the scan starts, or, after leaving the tab mid-scan, when the
    /// results would be listed.
    /// </exception>
    public async Task ScanAsync()
    {
        var side = SidePane ?? throw new InvalidOperationException("start_threaded_scan: the side pane is gone.");
        var tree = _tree!;

        // SCN-S1: the button shows Scanning..., the tree, its data and the info label are cleared, the details pane closes.
        side.SetScanning(true);
        _selectable = false;
        tree.Items.Clear();
        _items.Clear();
        _resultsInfo!.Text = "";
        CloseDetails();

        _scanningText!.IsVisible = true;
        _scanningText.Text = "Refreshing Overview...";
        _runtime.Logger.Write(LogLevel.Debug, "Refresh Tab : Overview");
        bool refreshed;
        try
        {
            refreshed = await _overview.RefreshAsync();
        }
        catch (Exception ex)
        {
            // The refresh raised past its own reporting (rebuilding the Overview's frames, say): reported here, as the
            // Scan Game handler would have, so the scan can still end below.
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
            refreshed = false;
        }

        if (!refreshed)
        {
            // Bug #22's loose end, fixed (ADR-0006): the refresh raised in the reference's start_threaded_scan, which
            // left Scan Game disabled on Scanning... for good. The error is reported; the scan ends before its worker.
            EndBeforeWorker(side);
            return;
        }

        // SCN-S2: the snapshot, saving the boxes only when one differs from its stored value (SET-7). The reference saved
        // on the Tk thread before it started the scan thread, so the write is a blocking operation, awaited before the
        // worker starts (ADR-0003).
        var settings = ScanSettings.Snapshot(side.Values, _game, _overview.Report, Host);
        if (settings.ChangedSettings(_settings.Current) is { } changed)
        {
            try
            {
                await _settings.SaveAsync(_runtime.Operations, changed);
            }
            catch (OperationFailedException)
            {
                // Already reported by the runner. A save that raised ended start_threaded_scan in Tk and left the
                // button disabled; as after a refresh that died, the scan now ends before its worker (bug #22).
                EndBeforeWorker(side);
                return;
            }
        }

        // SCN-S3.
        var seeded = ScanResults.Seed(settings[ScanSetting.OverviewIssues], _overview.Report?.Problems ?? []);
        _progress!.Value = 1;
        if (!settings.SkipDataScan)
        {
            _scanningText.Text = "Building mod file index...";
        }

        ScanOutcome outcome;
        var pending = new ConcurrentQueue<ScanUpdate>();
        _pending = pending;
        try
        {
            outcome = await _runtime.Operations.RunAsync("Scanner scan", OperationPhase.Interactive, () => Worker(settings));
        }
        catch (OperationFailedException)
        {
            // B-3, bug #22 fixed (ADR-0006): the reference's thread died without clearing thread_scan, so polling never
            // ended and the scan stayed on Scanning... with the button disabled. The failures the worker expects (MO2's
            // missing profile or modlist.txt) come back as problem rows now; this is anything else. The runner has
            // already reported the error; the scan ends as a finished one does, listing the results it has (the seeded
            // Overview problems; the worker's died with it) and enabling Scan Game again.
            outcome = ScanOutcome.Empty;
        }

        // check_scan_progress drains the queue before it finds the thread done; nothing is applied after this.
        DrainProgress(pending);
        if (ReferenceEquals(_pending, pending))
        {
            _pending = null;
        }

        _progress.Value = 100;
        Populate(settings, [.. seeded, .. outcome.Problems], outcome.Index);
    }

    /// <summary>
    /// Ends a scan that stopped before its worker started (its Overview refresh or settings save failed, already
    /// reported): the scanning label hides and Scan Game is enabled again. Nothing is listed, since nothing was
    /// scanned, and the tree stays cleared and unselectable.
    /// </summary>
    /// <param name="side">
    /// The side pane the scan started from. The main window's input was blocked since, so the tab can't have been left
    /// and it is still the open one.
    /// </param>
    private void EndBeforeWorker(ScanSidePane side)
    {
        _scanningText!.IsVisible = false;
        _scanningText.Text = "";
        side.SetScanning(false);
    }

    /// <summary>Collapse All / Expand All (<c>set_expanded</c>): opens or closes every group row.</summary>
    public void SetExpanded(bool expanded)
    {
        foreach (var group in _tree!.Items.OfType<TreeViewItem>())
        {
            group.IsExpanded = expanded;
        }
    }

    /// <summary>
    /// <c>populate_results</c>: hides the scanning label, sets the info label, lists the groups and re-enables Scan Game
    /// and selection.
    /// </summary>
    private void Populate(ScanSettings settings, IReadOnlyList<Problem> results, ModFiles? index)
    {
        // Left the tab mid-scan: the reference raised ValueError here, before touching anything.
        var side = SidePane ?? throw new InvalidOperationException("populate_results: the side pane is gone.");

        _scanningText!.IsVisible = false;
        _scanningText.Text = "";
        _resultsInfo!.Text = ScanResults.InfoText(results.Count);

        foreach (var group in ScanResults.Group(results, index))
        {
            var groupItem = new TreeViewItem { Header = Text(group.Kind), IsExpanded = true };
            foreach (var problem in group.Items)
            {
                var item = new TreeViewItem { Header = Row(problem, settings.UsingStage) };
                item.Classes.Add("leaf");
                _items[item] = problem;
                groupItem.Items.Add(item);
            }

            _tree!.Items.Add(groupItem);
        }

        side.SetScanning(false);
        _selectable = true;
    }

    /// <summary><c>on_row_select</c>: an item row (not a group) opens the details pane if needed and shows its problem.</summary>
    private void OnRowSelect()
    {
        if (_tree!.SelectedItem is not TreeViewItem selected)
        {
            return;
        }

        if (!_selectable)
        {
            // selectmode=none: nothing can be selected until a scan has populated the tree.
            _tree.SelectedItem = null;
            return;
        }

        if (!_items.TryGetValue(selected, out var problem))
        {
            return;
        }

        if (DetailsPane is null)
        {
            DetailsPane = new ScanDetailsPane(_runtime, Host) { OpenFolder = OpenFolder };
            Docker().Show(PaneSlot.Details, DetailsPane);
        }

        DetailsPane.Show(problem, UsingStage);
    }

    /// <summary>Runs a scan from the Scan Game button, reporting what it raises as a Tk callback's exception was.</summary>
    private async Task RunScanAsync()
    {
        try
        {
            await ScanAsync();
        }
        catch (Exception ex)
        {
            // Reported here, not left in the task: the click handler discards it, so a fault would only surface if the
            // garbage collector raised it as unobserved, maybe never.
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
        }
    }

    /// <summary>Closes the details pane if one is open (<c>details_pane.destroy()</c>), so the next selection makes a new one.</summary>
    private void CloseDetails()
    {
        if (DetailsPane is not null)
        {
            _docker?.Close(PaneSlot.Details);
            DetailsPane = null;
        }
    }

    /// <summary>The docker over the main window this tab is shown in, made on first use.</summary>
    private OwnedPaneDocker Docker()
        => _docker ??= new OwnedPaneDocker(TopLevel.GetTopLevel(_tree) as Window
            ?? throw new InvalidOperationException("The Scanner tab isn't in a window."));

    /// <summary>Collapse All or Expand All: a tight button that sets every result group's open state.</summary>
    /// <param name="name">The control name, for tests.</param>
    /// <param name="text">The caption.</param>
    /// <param name="expanded">The open state the click sets on every group.</param>
    private Button ControlButton(string name, string text, bool expanded)
    {
        // ttk.Button(padding=0): the "tight" class.
        var button = new Button { Name = name, Content = text, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("tight");
        button.Click += (_, _) => SetExpanded(expanded);
        return button;
    }

    /// <summary>An item row: the item text, and in stage mode the mod right-aligned in its column.</summary>
    private static Control Row(Problem problem, bool usingStage)
    {
        if (!usingStage)
        {
            return Text(problem.ItemText);
        }

        var mod = Text(problem.Mod);
        mod.Name = "Mod";
        mod.HorizontalAlignment = HorizontalAlignment.Right;
        // Tk's right-anchored mod column ends 6 px short of the tree's edge (parity/screenshots/scanner/results-mo2).
        mod.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(mod, Dock.Right);
        return new DockPanel { Children = { mod, Text(problem.ItemText) } };
    }

    private static TextBlock Text(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// <c>check_scan_progress</c>'s drain: applies <paramref name="queue"/>'s updates in order, if it is still the
    /// running scan's queue, setting the scanning label and progress bar for each update the tracker accepts (SCN-S4).
    /// UI thread only.
    /// </summary>
    private void DrainProgress(ConcurrentQueue<ScanUpdate> queue)
    {
        while (ReferenceEquals(queue, _pending) && queue.TryDequeue(out var update))
        {
            if (_tracker.Apply(update) is { } status)
            {
                _scanningText!.Text = status.Text;
                _progress!.Value = status.Progress;
            }

            ProgressApplied?.Invoke(this, update);
        }
    }

    /// <summary>
    /// <see cref="Progress"/>: queues each update for the running scan and asks the UI thread to drain it, standing in
    /// for the reference's 100 ms polling. Updates reported while no scan's worker runs are dropped.
    /// </summary>
    private sealed class ProgressQueue(ScannerTab tab) : IProgress<ScanUpdate>
    {
        public void Report(ScanUpdate value)
        {
            if (tab._pending is not { } queue)
            {
                return;
            }

            queue.Enqueue(value);
            Dispatcher.UIThread.Post(() => tab.DrainProgress(queue));
        }
    }
}
