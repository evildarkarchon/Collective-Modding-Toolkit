using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
using CMToolkit.Core.ArchivePatcher;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Views;

/// <summary>
/// <c>patcher.ArchivePatcher</c> (§9.4, 700x600): the Desired Version radios and the filter caption, Patch All and
/// About (AP-1); the Name Filter and the list of the archives Patch All would patch, from the current Overview report
/// (AP-2); and the log pane (§9.5). Patch All rewrites each archive's version byte, then refreshes the Overview and
/// lists the archives again (AP-3).
/// </summary>
/// <remarks>
/// Patch All ran on the UI thread in the reference; here the Core run is a blocking operation (ADR-0003), and one input
/// block is held across it, the Overview refresh and the repopulate, as the single Tk callback held the UI.
/// </remarks>
public sealed class ArchivePatcherWindow : ModalWindow
{
    /// <summary><c>ABOUT_ARCHIVES_TITLE</c>.</summary>
    public const string AboutTitle = "Bethesda Archive (BA2) Formats & Versions";

    /// <summary><c>ABOUT_ARCHIVES</c>.</summary>
    public const string AboutText =
        "There are 2 formats and 3 versions for Fallout 4 BA2 files:\n"
        + "\u2022 General (GNRL)\n"
        + "\u2022 Textures (DX10)\n"
        + "\n"
        + "\u2022 v1: Required by FO4 v1.10.163 and earlier. Works in all versions.\n"
        + "\u2022 v7/8: Only supported in FO4 v1.10.980 and later.\n"
        + "\n"
        + "It's suspected there are format differences between versions for XBox/PS, but for PC the only difference is the number itself.\n"
        + "v7/8 are identical so this tool only patches to v1 & v8.\n"
        + "\n"
        + "Why Patch Versions?\n"
        + "\n"
        + "Patching is only needed if you use tools that require it.\n"
        + "Most tools check the version to ensure compatibility but v7/8 didn't exist when these tools were made, so they assume it's a different format and show errors.\n"
        + "Because they're actually identical, you can just patch the version number in the file header so the tools will allow reading them.";

    /// <summary><c>PATCHER_FILTER_OG</c>: the caption while the target is v8, so the v1 archives are listed.</summary>
    public const string FilterOldGen = "Showing all v1\n(Includes Base Game/DLC/CC)";

    /// <summary><c>PATCHER_FILTER_NG</c>: the caption while the target is v1, so the v7 and v8 archives are listed.</summary>
    public const string FilterNextGen = "Showing all v7 & v8\n(Includes Base Game/DLC/CC)";

    /// <summary>The width of the column the tree's vertical <c>ttk.Scrollbar</c> takes, as the log pane's.</summary>
    private const double ScrollBarWidth = 24;

    private readonly GameInstall _game;
    private readonly IHostEnvironment _host;
    private readonly OverviewState _overview;
    private readonly Func<Task> _refreshOverview;
    private readonly TextBlock _caption;
    private readonly StackPanel _rows = new() { Name = "Files" };
    private readonly TextBox _nameFilterBox;

    /// <param name="runtime">The app runtime: its log, input block and background operations.</param>
    /// <param name="game">The installation; the patcher needs its Data folder.</param>
    /// <param name="host">The machine the archives are patched on.</param>
    /// <param name="overview">The Overview's state, whose current report's archives are the candidates.</param>
    /// <param name="refreshOverview">
    /// <c>cmc.refresh_tab(Tab.Overview)</c>, run after each Patch All. A failed refresh propagates its
    /// <see cref="OperationFailedException"/> (already reported), which ends the Patch All.
    /// </param>
    public ArchivePatcherWindow(
        AppRuntime runtime, GameInstall game, IHostEnvironment host, OverviewState overview, Func<Task> refreshOverview)
        : base(runtime, "Archive Patcher", 700, 600)
    {
        _game = game;
        _host = host;
        _overview = overview;
        _refreshOverview = refreshOverview;

        _caption = Small(FilterText, "CmtNeutral2", "FilterCaption");
        // A ttk.Entry: 28 px tall in the reference (taller than the label with pady=5, so it sets the row's height), a
        // #292929 field with a light bottom line; Fluent's TextBox is 32 px with a full border.
        _nameFilterBox = new TextBox
        {
            Name = "NameFilter",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 28,
            Padding = new Thickness(6, 3),
            BorderThickness = new Thickness(0, 0, 0, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _nameFilterBox.Bind(TextBox.BackgroundProperty, _nameFilterBox.GetResourceObservable("SvHover"));
        _nameFilterBox.Bind(TextBox.BorderBrushProperty, _nameFilterBox.GetResourceObservable("SvEntryUnderline"));
        Log = new LogPane(runtime.Logger) { Name = "Log", HorizontalAlignment = HorizontalAlignment.Stretch };
        Content = Build();
    }

    /// <summary>
    /// Opens the Archive Patcher over the main window from the session (what both Archive Patcher buttons do, OVW-A6
    /// and TOOL-1): <c>ArchivePatcher(self.cmc.root, self.cmc)</c>. Does nothing without a main window to own it.
    /// </summary>
    public static void Open(AppSession session)
    {
        if (session.MainWindow is { } owner)
        {
            new ArchivePatcherWindow(session.Runtime, session.Game, session.Host, session.Overview, session.RefreshOverviewAsync)
                .ShowOver(owner);
        }
    }

    /// <summary>The Desired Version (<c>desired_version</c>): v1 until the <c>v8 (NG)</c> radio is clicked.</summary>
    public ArchiveVersion DesiredVersion { get; private set; } = ArchiveVersion.OldGen;

    /// <summary>The case-folded Name Filter (<c>name_filter</c>), or null while the entry is empty.</summary>
    public string? NameFilter { get; private set; }

    /// <summary>
    /// <c>files_to_patch</c>: the current Overview report's archives for the Desired Version, narrowed by the Name
    /// Filter. Empty before the Overview's first report.
    /// </summary>
    public IReadOnlyList<PythonPath> FilesToPatch
        => ArchivePatching.FilesToPatch(_overview.Report?.Archives ?? OverviewArchives.None, DesiredVersion, NameFilter);

    /// <summary>The list's rows (each archive's file name), in display order.</summary>
    public IReadOnlyList<string> Rows => [.. _rows.Children.OfType<TextBlock>().Select(row => row.Text ?? string.Empty)];

    /// <summary>The log pane.</summary>
    public LogPane Log { get; }

    /// <summary>
    /// Shows the window over <paramref name="owner"/>, then lists the archives, as the reference's constructor did
    /// once <c>ModalWindow.__init__</c> had shown the window.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The game has no Data folder: the reference's <c>assert self.cmc.game.data_path is not None</c>, which left the
    /// window up with an empty list.
    /// </exception>
    public void ShowOver(Window owner)
    {
        // Not awaited: the returned task completes only when the modal closes.
        _ = ShowModalAsync(owner);
        PopulateTree();
    }

    /// <summary>
    /// Patch All (<c>_patch_wrapper</c>): patches <see cref="FilesToPatch"/> to the Desired Version as a blocking
    /// operation, its log lines going to the pane as they come, then refreshes the Overview and lists the archives
    /// again. The close guard is set throughout. A UI Action: the button starts it through
    /// <see cref="ErrorReporter.Handle"/>.
    /// </summary>
    /// <returns>
    /// A task that completes when the run is over. If the patching or the refresh fails, the error is reported and the
    /// rest is skipped, leaving the close guard set, as the exception left <c>processing_data</c> in Tk.
    /// </returns>
    /// <exception cref="InvalidOperationException">The game has no Data folder (the reference's assert).</exception>
    /// <exception cref="OperationFailedException">The patching or the refresh failed; the runner has reported it.</exception>
    public async Task PatchAllAsync()
    {
        AssertDataPath();
        // One block across the run, the refresh and the repopulate: in the reference they were one UI-thread callback,
        // so no input could land between them.
        using var block = Runtime.Input.Block();
        ProcessingData = true;

        Runtime.Logger.Write(LogLevel.Information, "Patcher Running: ArchivePatcher");
        var files = FilesToPatch;
        var target = DesiredVersion;
        var filter = NameFilter;
        var progress = new UiThreadProgress<OperationEvent>(e =>
        {
            if (e is LogLine line)
            {
                Log.Log(line);
            }
        });
        // A failure propagates as OperationFailedException, already reported by the runner. The rest of the wrapper is
        // skipped, as the exception skipped it in Tk.
        await Runtime.Operations.RunAsync(
            "Archive Patcher", OperationPhase.Blocking, () => ArchivePatching.Patch(files, target, filter, _host, Runtime.Logger, progress));

        Runtime.Logger.Write(LogLevel.Information, "Patcher Finished");
        // A failed refresh propagates too, already reported; in Tk it raised out of the wrapper here.
        await _refreshOverview();

        PopulateTree();
        ProcessingData = false;
    }

    /// <summary><c>filter_text</c>: which archives the list shows for the Desired Version.</summary>
    private string FilterText => DesiredVersion == ArchiveVersion.OldGen ? FilterNextGen : FilterOldGen;

    /// <summary>
    /// <c>populate_tree</c>: lists <see cref="FilesToPatch"/> by file name in path order, and says how many there are
    /// in the pane (not logged).
    /// </summary>
    private void PopulateTree()
    {
        AssertDataPath();
        var files = FilesToPatch;
        _rows.Children.Clear();
        foreach (var file in ArchivePatching.InDisplayOrder(files))
        {
            _rows.Children.Add(Row(file.Name));
        }

        Log.LogMessage(LogType.Info, $"Showing {files.Count} files to be patched.", skipLogging: true);
    }

    /// <summary><c>assert self.cmc.game.data_path is not None</c>.</summary>
    private void AssertDataPath()
    {
        if (_game.DataPath is null)
        {
            throw new InvalidOperationException("assert self.cmc.game.data_path is not None");
        }
    }

    /// <summary><c>on_radio_change</c>: a radio click, even on the selected one, clears the pane and lists again.</summary>
    private void SelectVersion(ArchiveVersion version)
    {
        DesiredVersion = version;
        Log.Clear();
        _caption.Text = FilterText;
        PopulateTree();
    }

    /// <summary>
    /// <c>on_key_release</c>: every key released in the Name Filter re-reads it, clears the pane and lists again.
    /// </summary>
    private void OnNameFilterKeyUp(object? sender, KeyEventArgs e)
    {
        NameFilter = ArchivePatching.NameFilter(_nameFilterBox.Text ?? string.Empty);
        Log.Clear();
        PopulateTree();
    }

    /// <summary><c>_build_gui_primary</c> with <c>build_gui_secondary</c>: the top, middle and bottom frames.</summary>
    private TkGrid Build()
    {
        // grid_columnconfigure(0, weight=1), grid_rowconfigure(1, weight=1): the middle frame takes the spare height.
        var grid = new TkGrid { ColumnWeights = [1], RowWeights = [0, 1, 0] };
        grid.Children.Add(Place(BuildTop(), 0, 0));
        grid.Children.Add(Place(BuildMiddle(), 1, 0));
        grid.Children.Add(Place(Log, 2, 0));
        return grid;
    }

    /// <summary>
    /// <c>frame_top</c>, packed: About then Patch All against the right edge, the Desired Version frame and the filter
    /// caption from the left.
    /// </summary>
    private DockPanel BuildTop()
    {
        var about = TopButton("About", "About");
        // pack(side=RIGHT, padx=24, pady=5).
        about.Margin = new Thickness(24, 5);
        // The AboutWindow is transient to the patcher (parent=self), so it is owned by it.
        about.Click += (_, _) => _ = new AboutWindow(Runtime, 500, 435, AboutTitle, AboutText).ShowModalAsync(this);
        DockPanel.SetDock(about, Dock.Right);

        var patch = TopButton("PatchAll", "Patch All");
        patch.Margin = new Thickness(5);
        patch.Click += (_, _) => _ = Runtime.Errors.Handle(PatchAllAsync);
        DockPanel.SetDock(patch, Dock.Right);

        var oldGen = Radio("OldGen", "v1 (OG)", ArchiveVersion.OldGen, isChecked: true);
        var nextGen = Radio("NextGen", "v8 (NG)", ArchiveVersion.NextGen, isChecked: false);
        Grid.SetColumn(nextGen, 1);
        var radios = new TkGrid { Children = { oldGen, nextGen } };
        var frame = new GroupBox
        {
            Name = "DesiredVersion",
            Header = Labelframe.Title("Desired Version"),
            Content = radios,
            // ipadx=5, ipady=5 make the frame 10 px wider and taller than it asks; its grid keeps the radios top-left.
            Padding = Labelframe.Padding(0) + new Thickness(0, 0, 10, 10),
            Margin = new Thickness(5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(frame, Dock.Left);

        // pack(side=LEFT, padx=10, fill=Y): the label fills the row's height, its text at the default west anchor.
        _caption.Margin = new Thickness(10, 0);
        _caption.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_caption, Dock.Left);

        return new DockPanel { LastChildFill = false, Children = { about, patch, frame, _caption } };
    }

    /// <summary><c>frame_middle</c>: the Name Filter row over the archive list and its scrollbar.</summary>
    private TkGrid BuildMiddle()
    {
        var grid = new TkGrid { ColumnWeights = [0, 1, 0], RowWeights = [0, 1] };

        var label = Small("Name Filter:", "CmtDefault", "NameFilterLabel");
        label.Margin = new Thickness(5);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(Place(label, 0, 0));

        _nameFilterBox.KeyUp += OnNameFilterKeyUp;
        grid.Children.Add(Place(_nameFilterBox, 0, 1));

        // ttk.Treeview(show="tree"), names only; the ttk.Scrollbar is its own widget in column 2, so the bar is a
        // separate control, as in the log pane.
        var scroller = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        var bar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            AllowAutoHide = false,
            Width = ScrollBarWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        scroller.ScrollChanged += (_, _) =>
        {
            bar.Maximum = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            bar.ViewportSize = scroller.Viewport.Height;
            bar.Value = scroller.Offset.Y;
        };
        bar.Scroll += (_, e) => scroller.Offset = scroller.Offset.WithY(e.NewValue);

        // The Treeview's 1 px border (sv_ttk's treeview sprite), and its first row 4 px further down.
        var border = new Border { Child = scroller, BorderThickness = new Thickness(1), Padding = new Thickness(0, 4, 0, 0) };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("SvStrip"));
        var tree = Place(border, 1, 0);
        Grid.SetColumnSpan(tree, 2);
        grid.Children.Add(tree);
        grid.Children.Add(Place(bar, 1, 2));
        return grid;
    }

    /// <summary>A <c>ttk.Button(padding=(6, 2))</c> in the top frame.</summary>
    private static Button TopButton(string name, string text) => new()
    {
        Name = name,
        Content = text,
        // ttk padding (6, 2) in place of the theme's: 2 px narrower and 1 px shorter per side than ButtonPadding.
        Padding = new Thickness(9, 4, 9, 5),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>One Desired Version radio, gridded with <c>padx=5</c>; its click runs <c>on_radio_change</c>.</summary>
    private RadioButton Radio(string name, string text, ArchiveVersion version, bool isChecked)
    {
        var radio = new RadioButton { Name = name, Content = text, IsChecked = isChecked, GroupName = "DesiredVersion", Margin = new Thickness(5, 0) };
        // Click, not a checked change: a Tk radiobutton runs its command on every click, the selected one included.
        radio.Click += (_, _) => SelectVersion(version);
        return radio;
    }

    /// <summary>One list row: the archive's file name, placed as a <c>show="tree"</c> Treeview row.</summary>
    private static TextBlock Row(string text)
    {
        // Measured against the reference: the Treeview leaves its indicator's room before each name (31 px from its
        // border to the text) and its rows are 21 px apart here.
        var row = new TextBlock { Text = text, Padding = new Thickness(30, 2, 0, 2) };
        row.Classes.Add("small");
        return row;
    }

    /// <summary>A <c>ttk.Label(font=FONT_SMALL, foreground=...)</c>.</summary>
    private static TextBlock Small(string text, string colourKey, string name)
    {
        var label = new TextBlock { Name = name, Text = text };
        label.Classes.Add("small");
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(colourKey));
        return label;
    }

    private static T Place<T>(T control, int row, int column)
        where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }
}
