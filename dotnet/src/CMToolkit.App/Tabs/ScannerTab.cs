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
using CMToolkit.Core.Overview;
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
    // The scan's background operation: the runner reports an escaped exception under its name, and the tab reports a
    // scan's returned error under the same header, so both read alike in the Error Window.
    private const string ScanOperation = "Scanner scan";

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

    // The progress sink the tab hands the scan through RunScan: the only way a status reaches the label and the bar.
    private readonly IProgress<ScanStatus> _statuses;

    // The running scan's progress queue, or null when no worker is running; replaced per scan so a late update from an
    // earlier scan can't reach this one.
    private volatile ConcurrentQueue<ScanStatus>? _pending;

    /// <summary>The Scanner tab, built on first switch; its side pane docks beside the main window when it is shown.</summary>
    /// <param name="runtime">The app runtime: its log, error reporting and UI Action runner.</param>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="overview">The Overview Report module: each scan refreshes it first, and its problems seed the scan.</param>
    /// <param name="settings">The settings the side pane's boxes start from (SET-8) and are saved to as they change (SET-7).</param>
    public ScannerTab(AppRuntime runtime, GameInstall game, OverviewState overview, SettingsStore settings)
        : base("Scanner", "ScannerTab")
    {
        _runtime = runtime;
        _game = game;
        _overview = overview;
        _settings = settings;
        _statuses = new ProgressQueue(this);
        RunScan = (s, progress) => Scan.Run(s, progress, _runtime.Logger);
    }

    /// <summary>
    /// Everything in the Scan after its Overview refresh, run off the UI thread as an interactive operation and given the
    /// scan's snapshot and the tab's progress sink:
    /// <see cref="Scan.Run(ScanSettings, IProgress{ScanStatus}?, Microsoft.Extensions.Logging.ILogger)"/> with the app
    /// log by default. A test seam, so tests can hold a scan mid-run, report statuses or hand the tab a canned outcome.
    /// </summary>
    /// <remarks>
    /// The sink takes statuses from any thread. They are queued for the running scan and applied on the UI thread in
    /// order; the queue is drained once more when the scan returns, before the results are listed. A status reported
    /// once the scan is over is dropped.
    /// </remarks>
    public Func<ScanSettings, IProgress<ScanStatus>, ScanOutcome> RunScan { get; init; }

    /// <summary>
    /// Raised on the UI thread after each status is applied to the scanning label and the progress bar. A test seam for
    /// reading the tab after every status.
    /// </summary>
    public event EventHandler<ScanStatus>? ProgressApplied;

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
    /// The machine the scan reads (the Race Subgraphs count, the mod file index and the data walk) and the details pane
    /// checks result paths on: a relative path is relative to its working directory. Required, with no default, because the scan's snapshot is
    /// taken with it: a tab built without naming its machine would silently scan the real one.
    /// </summary>
    public required IHostEnvironment Host { get; init; }

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
        // Both UI Actions start through runtime.Actions.Start: what they raise is reported once, at once (a failed
        // operation already by the runner), and the task they return never faults, so discarding it is safe.
        SidePane.ScanRequested += (_, _) => _ = _runtime.Actions.Start(ScanAsync);
        var created = SidePane;
        created.BoxChanged += (_, _) => _ = _runtime.Actions.Start(() => SaveBoxesAsync(created));
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
    /// is done): SCN-S1..SCN-S3, SCN-R1..SCN-R3. A UI Action: the Scan Game button starts it through
    /// <see cref="UiActions.Start"/>, which reports a failure in the Error Window.
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
        if (await _overview.TryRefreshAsync() is OperationOutcome<OverviewReport>.Failed)
        {
            // Bug #22's loose end, fixed (ADR-0006): the refresh raised in the reference's start_threaded_scan, which
            // left Scan Game disabled on Scanning... for good. The runner has reported the error; the scan ends before
            // its worker.
            EndBeforeWorker(side);
            return;
        }

        // SCN-S2: the snapshot. The reference saved the boxes here too when one differed from its stored value (SET-7);
        // the port has saved each box as it changed (bug #20), so the settings already hold these and nothing is written.
        var settings = ScanSettings.Snapshot(side.Values, _game, _overview.Report, Host);

        // The label stays on Refreshing Overview... until the scan reports a status of its own, and it reports none when
        // only Overview Issues is ticked.
        _progress!.Value = 1;

        ScanOutcome outcome;
        var errorReported = false;
        var pending = new ConcurrentQueue<ScanStatus>();
        _pending = pending;
        var run = await _runtime.Actions.TryRun(
            ScanOperation, OperationPhase.Interactive, () => RunScan(settings, _statuses));
        if (run is OperationOutcome<ScanOutcome>.Failed failed)
        {
            // The last resort, for an exception that escaped the scan, which returns its own failures on its outcome.
            // Everything the scan would have listed died with it (it seeds its own results), so the list is the
            // stopped-early row alone: an empty list would read as a clean scan. The runner has already reported the
            // error, so it must not reach the Error Window a second time below.
            outcome = ScanOutcome.StoppedEarly([], failed.Error);
            errorReported = true;
        }
        else
        {
            outcome = ((OperationOutcome<ScanOutcome>.Succeeded)run).Value;
        }

        // The statuses still queued when the scan returned are applied first: the await's continuation can run ahead of
        // the drains the sink posted. Nothing is applied after this.
        DrainProgress(pending);
        if (ReferenceEquals(_pending, pending))
        {
            _pending = null;
        }

        if (outcome.Error is { } error && !errorReported)
        {
            // The scan stopped early and returned its error rather than throwing it, so no runner reported it. Shown
            // now, on the UI thread, so the Error Window is up before the partial results are listed.
            _runtime.Actions.ReportFailure(ScanOperation, error);
        }

        _progress.Value = 100;
        Populate(settings, outcome);
    }

    /// <summary>
    /// Ends a scan that stopped before its worker started (its Overview refresh failed, already reported): the
    /// scanning label hides and Scan Game is enabled again. Nothing is listed, since nothing was
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
    /// <param name="settings">The scan's snapshot, which says whether rows get the mod column.</param>
    /// <param name="outcome">The scan's outcome: its problems are counted and its groups listed as they are.</param>
    /// <exception cref="InvalidOperationException">The side pane is gone: the tab was left mid-scan.</exception>
    private void Populate(ScanSettings settings, ScanOutcome outcome)
    {
        // Left the tab mid-scan: the reference raised ValueError here, before touching anything.
        var side = SidePane ?? throw new InvalidOperationException("populate_results: the side pane is gone.");

        _scanningText!.IsVisible = false;
        _scanningText.Text = "";
        _resultsInfo!.Text = ScanResults.InfoText(outcome.Problems.Count);

        foreach (var group in outcome.Groups)
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

    /// <summary>
    /// Saves the side pane's boxes after one changed (SET-7; bug #20, fixed): the reference saved them only when a scan
    /// started, and never read them back. Through <see cref="SettingsStore.SaveAsync"/>, so the settings in effect
    /// change at once and the file is written off the UI thread under an input block, quick changes landing in order
    /// with the last one winning. Nothing is written when the boxes already match the settings in effect. A UI Action:
    /// each box change starts it through <see cref="UiActions.Start"/>.
    /// </summary>
    /// <param name="side">The pane whose box changed.</param>
    /// <returns>A task that completes once the file is written, or at once when nothing changed.</returns>
    /// <exception cref="OperationFailedException">
    /// The write failed; the runner has reported it. The new boxes stay in effect for the session either way.
    /// </exception>
    private Task SaveBoxesAsync(ScanSidePane side)
        => ScanSettings.ChangedSettings(_settings.Current, side.Values) is { } changed
            ? _settings.SaveAsync(_runtime.Actions, changed)
            : Task.CompletedTask;

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
    /// Applies <paramref name="queue"/>'s statuses in order, if it is still the running scan's queue, setting the scanning
    /// label and progress bar to each as the scan reported it. UI thread only.
    /// </summary>
    private void DrainProgress(ConcurrentQueue<ScanStatus> queue)
    {
        while (ReferenceEquals(queue, _pending) && queue.TryDequeue(out var status))
        {
            _scanningText!.Text = status.Text;
            _progress!.Value = status.Progress;
            ProgressApplied?.Invoke(this, status);
        }
    }

    /// <summary>
    /// The progress sink <see cref="RunScan"/> is given: queues each status for the running scan and asks the UI thread to
    /// drain it, so statuses reach the tab as soon as the UI thread is free. Statuses reported while no scan is running
    /// are dropped.
    /// </summary>
    private sealed class ProgressQueue(ScannerTab tab) : IProgress<ScanStatus>
    {
        public void Report(ScanStatus value)
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
