using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.Scanner;

namespace CMToolkit.App.Tabs;

/// <summary>
/// The Scanner tab: the results tree under Collapse All / Expand All and the results info label, the scanning label and
/// the progress bar, with the side pane and the details pane docked to the main window as owned windows. Scan Game
/// starts a Scan through the <see cref="ScanState"/>, and the tab draws what it raises: it clears the results when a
/// Scan starts, shows each status, and lists the results when the Scan ends.
/// </summary>
/// <remarks>
/// The tab decides nothing about the Scan. The Scan's events reach the tab whether or not it is current, so a Scan the
/// user leaves the tab during keeps its label and progress bar up to date and lists its results when it ends, and coming
/// back shows them.
/// </remarks>
public sealed class ScannerTab : TabPage
{
    private readonly AppRuntime _runtime;
    private readonly GameInstall _game;
    private readonly ScanState _scan;
    private readonly SettingsStore _settings;
    private readonly Dictionary<TreeViewItem, Problem> _items = [];
    private OwnedPaneDocker? _docker;
    private TreeView? _tree;
    private TextBlock? _resultsInfo;
    private TextBlock? _scanningText;
    private ProgressBar? _progress;
    private bool _selectable;

    // Stage mode as the listed Scan's snapshot had it, so the details pane agrees with the rows' mod column.
    private bool _usingStage;

    /// <summary>The Scanner tab, built on first switch; its side pane docks beside the main window when it is shown.</summary>
    /// <param name="runtime">The app runtime: its log, error reporting and UI Action runner.</param>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="scan">The Scan module, which Scan Game starts and whose events the tab draws.</param>
    /// <param name="settings">The settings the side pane's boxes start from (SET-8) and are saved to as they change (SET-7).</param>
    public ScannerTab(AppRuntime runtime, GameInstall game, ScanState scan, SettingsStore settings)
        : base("Scanner", "ScannerTab")
    {
        _runtime = runtime;
        _game = game;
        _scan = scan;
        _settings = settings;
        // Never unsubscribed: the module and the tab are built together and live as long as the main window. Only the
        // side pane starts a Scan, and it opens after the tab has built, so no event arrives before there is a tree.
        _scan.Started += (_, _) => OnStarted();
        _scan.StatusChanged += (_, status) => Apply(status);
        _scan.Ended += (_, ended) => OnEnded(ended);
    }

    /// <summary>
    /// Raised on the UI thread after each status is applied to the scanning label and the progress bar. A test seam for
    /// reading the tab after every status.
    /// </summary>
    public event EventHandler<ScanStatus>? ProgressApplied;

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
    /// The machine the details pane checks result paths on: a relative path is relative to its working directory.
    /// Required, with no default: a tab built without naming its machine would silently check the real one.
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

        // The boxes start as last saved (bug #20 fixed), so a scan's choices carry over to the next pane and launch. A
        // pane opened mid-scan, after the user comes back to the tab, starts on Scanning... so no second Scan can start.
        SidePane = new ScanSidePane(ScanSettings.Stored(_settings.Current), _scan.IsRunning);
        var created = SidePane;
        // Both UI Actions start through runtime.Actions.Start: what they raise is reported once, at once (a failed
        // Operation already by the runner), and the task they return never faults, so discarding it is safe.
        created.ScanRequested += (_, _) => _ = _runtime.Actions.Start(() => _scan.StartAsync(created.Values));
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
    /// Draws a Scan's start: Scan Game shows Scanning... in the open side pane, if there is one; the tree, its data and
    /// the info label are cleared and nothing can be selected; the details pane closes; and the scanning label appears.
    /// </summary>
    private void OnStarted()
    {
        SidePane?.SetScanning(true);
        _selectable = false;
        _tree!.Items.Clear();
        _items.Clear();
        _resultsInfo!.Text = "";
        CloseDetails();
        _scanningText!.IsVisible = true;
    }

    /// <summary>
    /// Draws a Scan's end, wherever the user is: the scanning label hides, a <see cref="ScanEnded.Listed"/> Scan's
    /// results are listed, and the open side pane, if there is one, shows Scan Game again, enabled unless every box is
    /// unticked. A
    /// <see cref="ScanEnded.NotScanned"/> Scan lists nothing, so the tree stays cleared and unselectable.
    /// </summary>
    /// <param name="ended">How the Scan ended.</param>
    private void OnEnded(ScanEnded ended)
    {
        _scanningText!.IsVisible = false;
        _scanningText.Text = "";
        if (ended is ScanEnded.Listed listed)
        {
            Populate(listed.Snapshot, listed.Outcome);
        }

        SidePane?.SetScanning(false);
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
    /// Lists a Scan's results: sets the info label, lists the groups and makes the results selectable. It reads no pane,
    /// so it lists them whether or not the tab is current.
    /// </summary>
    /// <param name="snapshot">
    /// The Scan's snapshot, which says whether rows get the mod column; the details pane follows it too.
    /// </param>
    /// <param name="outcome">The Scan's outcome: its problems are counted and its groups listed as they are.</param>
    private void Populate(ScanSnapshot snapshot, ScanOutcome outcome)
    {
        _usingStage = snapshot.UsingStage;
        _resultsInfo!.Text = ScanResults.InfoText(outcome.Problems.Count);

        foreach (var group in outcome.Groups)
        {
            var groupItem = new TreeViewItem { Header = Text(group.Kind), IsExpanded = true };
            foreach (var problem in group.Items)
            {
                var item = new TreeViewItem { Header = Row(problem, _usingStage) };
                item.Classes.Add("leaf");
                _items[item] = problem;
                groupItem.Items.Add(item);
            }

            _tree!.Items.Add(groupItem);
        }

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

        DetailsPane.Show(problem, _usingStage);
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
    /// Sets the scanning label and progress bar to <paramref name="status"/> as the Scan raised it, then raises
    /// <see cref="ProgressApplied"/>. UI thread only.
    /// </summary>
    private void Apply(ScanStatus status)
    {
        _scanningText!.Text = status.Text;
        _progress!.Value = status.Progress;
        ProgressApplied?.Invoke(this, status);
    }
}
