using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
using CMToolkit.Core.Downgrader;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;

namespace CMToolkit.App.Views;

/// <summary>
/// <c>downgrader.Downgrader</c> (§9.3, 600x334): the detected version of the game's and the Creation Kit's files
/// (DG-1, DG-3), the Desired Version and the two options, the Patch All and About buttons, a log pane and a progress
/// bar (DG-2). The Patch All run itself (DG-4..DG-6) is the next Downgrader slice's.
/// </summary>
/// <remarks>
/// Like the reference's <c>ModalWindow.__init__</c>, the window is on screen before the detection runs, and the
/// detection runs as a blocking operation (ADR-0003; the reference read the files on the UI thread). If it fails, the
/// error is reported and the window stays up, empty, as the half-built Toplevel did.
/// </remarks>
public sealed class DowngraderWindow : ModalWindow
{
    /// <summary><c>ABOUT_DOWNGRADING_TITLE</c>.</summary>
    public const string AboutTitle = "About Downgrading Fallout 4 & Creation Kit";

    /// <summary><c>ABOUT_DOWNGRADING</c>.</summary>
    public const string AboutText =
        "This downgrader makes use of delta patches which are downloaded as-needed from the CMT GitHub page.\n"
        + "Patches range in size from 23KB to 63MB.\n"
        + "\n"
        + "Backups are created prior to patching, and will be used instead of patches if present.\n"
        + "Simple Downgrader's backups will also be used.\n"
        + "Backup naming:\n"
        + "Fallout4_downgradeBackup.exe\n"
        + "Fallout4_upgradeBackup.exe\n"
        + "\n"
        + "Both Creation Kit and the game require steam_api64.dll to match their version, so they must be patched together (for now).";

    /// <summary><c>TOOLTIP_DOWNGRADER_BACKUPS</c>.</summary>
    public const string KeepBackupsTooltip =
        "Backups are created prior to patching, and will be used instead of patches if present.\n"
        + "Simple Downgrader's backups will also be used.\n"
        + "\n"
        + "Backups allow quicker switching between versions.\n"
        + "Uncheck this to delete backups after patching.";

    /// <summary><c>TOOLTIP_DOWNGRADER_DELTAS</c>.</summary>
    public const string DeletePatchesTooltip =
        "Delta patches are downloaded as-needed and used to patch from one version to another.\n"
        + "If backups are present, they will be used instead.\n"
        + "\n"
        + "These xdelta files are only needed during the patching process.\n"
        + "Check this to delete xdeltas after patching.";

    /// <summary>The log pane's first line, shown but not logged (<c>skip_logging=True</c>).</summary>
    public const string SeedMessage = "Patches will be downloaded and applied as-needed.";

    private readonly GameInstall _game;
    private readonly IHostEnvironment _host;
    private readonly OverviewState _overview;
    private readonly SettingsStore _settings;

    /// <param name="runtime">The app runtime: its log, input block and background operations.</param>
    /// <param name="game">The installation whose game folder holds the six files.</param>
    /// <param name="host">The machine the files are read on.</param>
    /// <param name="overview">The Overview's state, whose Install Type decides how a shared Next-Gen &amp; Anniversary file reads (DG-3).</param>
    /// <param name="settings">Where Keep Backups and Delete Patches start from.</param>
    public DowngraderWindow(AppRuntime runtime, GameInstall game, IHostEnvironment host, OverviewState overview, SettingsStore settings)
        : base(runtime, "Downgrader", 600, 334)
    {
        _game = game;
        _host = host;
        _overview = overview;
        _settings = settings;
    }

    /// <summary>
    /// Opens the Downgrader over the main window from the session (what both Downgrade Manager buttons do, OVW-B9 and
    /// TOOL-1): <c>Downgrader(self.cmc.root, self.cmc)</c>. Does nothing without a main window to own it.
    /// </summary>
    public static void Open(AppSession session)
    {
        if (session.MainWindow is { } owner)
        {
            // Not awaited: like the reference's constructor, it shows the window and returns once it is built, and the
            // click handler that called it has nothing left to do.
            _ = new DowngraderWindow(session.Runtime, session.Game, session.Host, session.Overview, session.Settings).OpenAsync(owner);
        }
    }

    /// <summary>
    /// The detected versions (<c>current_versions</c>), in table order; null until the detection has run, and for good
    /// if it failed.
    /// </summary>
    public IReadOnlyList<DowngraderFileVersion>? Versions { get; private set; }

    /// <summary>The log pane, once the window is built.</summary>
    public LogPane? Log { get; private set; }

    /// <summary>
    /// Shows the window over <paramref name="owner"/>, detects the versions as a blocking operation, then builds the
    /// window's content.
    /// </summary>
    /// <returns>
    /// A task that completes once the window is built, or once a failed detection has been reported (the window then
    /// stays up, empty). It never faults for a detection failure.
    /// </returns>
    public async Task OpenAsync(Window owner)
    {
        // Not awaited: the returned task completes only when the modal closes.
        _ = ShowModalAsync(owner);
        try
        {
            Versions = await Runtime.Operations.RunAsync(
                "Downgrader detection", OperationPhase.Blocking, () => DowngraderDetection.Detect(_game, _host));
        }
        catch (OperationFailedException)
        {
            // Already reported by the runner. The rest of the constructor is skipped, as the exception skipped it in Tk.
            return;
        }

        Content = Build(Versions);
    }

    /// <summary>
    /// The text and colour key a version label shows (<c>draw_versions</c>, DG-3). A shared Next-Gen &amp; Anniversary
    /// file takes the game's Install Type when the game is Anniversary or Next-Gen, and otherwise keeps its own text in
    /// the bad colour.
    /// </summary>
    /// <param name="file">The file's detected version.</param>
    /// <param name="game">The game's Install Type, as the Overview last gathered it.</param>
    public static (string Text, string ColourKey) Label(InstallType file, InstallType game) => file switch
    {
        InstallType.Anniversary => (file.DisplayName(), "CmtDowngraderAnniversary"),
        InstallType.NextGenAndAnniversary when game == InstallType.Anniversary
            => (InstallType.Anniversary.DisplayName(), "CmtDowngraderAnniversary"),
        InstallType.NextGenAndAnniversary when game == InstallType.NextGen
            => (InstallType.NextGen.DisplayName(), "CmtDowngraderNextGen"),
        InstallType.NextGenAndAnniversary => (file.DisplayName(), "CmtBad"),
        InstallType.NextGen => (file.DisplayName(), "CmtDowngraderNextGen"),
        InstallType.NotFound => (file.DisplayName(), "CmtNeutral1"),
        InstallType.Unknown or InstallType.Obsolete => (file.DisplayName(), "CmtBad"),
        _ => (file.DisplayName(), "CmtDowngraderOldGen"),
    };

    /// <summary><c>build_gui</c>: the window's grid, its third column the only weighted one.</summary>
    private TkGrid Build(IReadOnlyList<DowngraderFileVersion> versions)
    {
        // Column 2 (Patch All, About) takes the spare width of the fixed window, and gives it up first: a long version
        // label (Next-Gen & Anniversary) widens column 0 past 600 px and leaves the buttons no width at all (bug #85).
        var grid = new TkGrid { ColumnWeights = [0, 0, 1] };

        grid.Children.Add(Place(Current("Game", "Current Game", [.. versions.Where(v => !v.IsCreationKit)]), 0, 0, rowSpan: 2, margin: new Thickness(10, 0)));
        grid.Children.Add(Place(Current("CreationKit", "Current Creation Kit", [.. versions.Where(v => v.IsCreationKit)]), 2, 0, rowSpan: 2, margin: new Thickness(10, 0)));

        // BooleanVar(value=current_versions["Fallout4.exe"] == OG): Old-Gen is preselected only for an Old-Gen game exe.
        var wantsDowngrade = versions[0].InstallType == InstallType.OldGen;
        var oldGen = new RadioButton { Name = "OldGen", Content = "Old-Gen", IsChecked = wantsDowngrade, GroupName = "DesiredVersion" };
        var nextGen = new RadioButton { Name = "NextGen", Content = "Next-Gen", IsChecked = !wantsDowngrade, GroupName = "DesiredVersion" };
        var desired = new TkGrid { Children = { Place(oldGen, 0, 0), Place(nextGen, 1, 0) } };
        grid.Children.Add(Place(Frame("DesiredVersion", "Desired Version", desired), 0, 1, rowSpan: 2, margin: new Thickness(5, 0)));

        var current = _settings.Current;
        var keepBackups = Check("KeepBackups", "Keep Backups", current.DowngraderKeepBackups, KeepBackupsTooltip);
        var deletePatches = Check("DeletePatches", "Delete Patches", current.DowngraderDeleteDeltas, DeletePatchesTooltip);
        var options = new TkGrid { Children = { Place(keepBackups, 0, 0), Place(deletePatches, 1, 0) } };
        grid.Children.Add(Place(Frame("Options", "Options", options), 2, 1, rowSpan: 2, margin: new Thickness(5, 0)));

        // The run is the next slice's (DG-4); until then the button is live and does nothing.
        var patch = new Button
        {
            Name = "PatchAll",
            Content = "Patch\n All",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(Place(patch, 0, 2, rowSpan: 2, margin: new Thickness(5, 10, 10, 0)));

        var about = new Button
        {
            Name = "About",
            Content = "About",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        // The AboutWindow is transient to the Downgrader (parent=self), so it is owned by it.
        about.Click += (_, _) => _ = new AboutWindow(Runtime, 500, 300, AboutTitle, AboutText).ShowModalAsync(this);
        grid.Children.Add(Place(about, 2, 2, margin: new Thickness(5, 5, 10, 0)));

        Log = new LogPane(Runtime.Logger) { Name = "Log", HorizontalAlignment = HorizontalAlignment.Stretch };
        Log.LogMessage(LogType.Info, SeedMessage, skipLogging: true);
        var bottom = Place(Log, 5, 0, margin: new Thickness(0, 5, 0, 0));
        Grid.SetColumnSpan(bottom, 3);
        grid.Children.Add(bottom);

        // ttk.Progressbar(maximum=100), sticky=EW, ipady=1.
        var progress = new ProgressBar
        {
            Name = "Progress",
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumnSpan(Place(progress, 6, 0), 3);
        grid.Children.Add(progress);

        return grid;
    }

    /// <summary>
    /// One Current frame: the file names right-justified in one label spanning the rows, with each file's version in
    /// its own right-aligned label beside it.
    /// </summary>
    private GroupBox Current(string prefix, string title, IReadOnlyList<DowngraderFileVersion> files)
    {
        // grid_columnconfigure(0, weight=1): the names column takes any spare width, pushing both columns right.
        var grid = new TkGrid { ColumnWeights = [1, 0] };
        var names = new TextBlock
        {
            Name = prefix + "Names",
            Text = string.Join('\n', files.Select(f => PythonPathName(f.FileName) + ":")),
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        names.Bind(TextBlock.ForegroundProperty, names.GetResourceObservable("CmtDefault"));
        Grid.SetRowSpan(Place(names, 0, 0, margin: new Thickness(5, 0)), files.Count);
        grid.Children.Add(names);

        for (var i = 0; i < files.Count; i++)
        {
            var (text, colourKey) = Label(files[i].InstallType, _overview.InstallType);
            var value = new TextBlock
            {
                Name = $"{prefix}{i}",
                Text = text,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            value.Bind(TextBlock.ForegroundProperty, value.GetResourceObservable(colourKey));
            grid.Children.Add(Place(value, i, 1, margin: new Thickness(5, 0)));
        }

        return Frame(prefix + "Frame", title, grid);
    }

    /// <summary>A <c>ttk.Labelframe(padding=5)</c>.</summary>
    private static GroupBox Frame(string name, string title, Control content) => new()
    {
        Name = name,
        Header = Labelframe.Title(title),
        Content = content,
        Padding = Labelframe.Padding(5),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    /// <summary>A <c>ttk.Checkbutton(padding=0)</c> gridded <c>sticky=W, pady=5</c>, with its tooltip.</summary>
    private static CheckBox Check(string name, string text, bool isChecked, string tooltip)
    {
        var check = new CheckBox
        {
            Name = name,
            Content = text,
            IsChecked = isChecked,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5),
        };
        ToolTip.SetTip(check, tooltip);
        return check;
    }

    /// <summary><c>Path(f).name</c> for a table name, which may hold a relative folder (<c>Tools\Archive2\…</c>).</summary>
    private static string PythonPathName(string fileName) => fileName[(fileName.LastIndexOf('\\') + 1)..];

    private static T Place<T>(T control, int row, int column, int rowSpan = 1, Thickness? margin = null)
        where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetRowSpan(control, rowSpan);
        if (margin is { } m)
        {
            control.Margin = m;
        }

        return control;
    }
}
