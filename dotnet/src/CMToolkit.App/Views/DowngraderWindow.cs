using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
/// bar (DG-2), and the Patch All run (DG-4..DG-6), which Core's <see cref="DowngraderRun"/> owns.
/// </summary>
/// <remarks>
/// <para>
/// Like the reference's <c>ModalWindow.__init__</c>, the window is on screen before the detection runs, and the
/// detection runs as a blocking operation (ADR-0003; the reference read the files on the UI thread). If it fails, the
/// error is reported and the window stays up, empty, as the half-built Toplevel did.
/// </para>
/// <para>
/// A Patch All click is one blocking operation (the reference's <c>patch_files</c> ran on the UI thread). The download
/// poll it may start is an operation of its own, interactive while a patch downloads and blocking while one is applied,
/// as the run's phase markers say (THR-2). The run's events drive the log pane, the progress bar, the button, the close
/// guard and the version labels, on the UI thread, in the run's order.
/// </para>
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

    /// <summary>The window's width as the reference fixed it; the port widens it only for a label too long for it (bug #85).</summary>
    private const double DefaultWidth = 600;

    private readonly GameInstall _game;
    private readonly IHostEnvironment _host;
    private readonly OverviewState _overview;
    private readonly SettingsStore _settings;
    private readonly Func<Task<bool>> _refreshOverview;
    private readonly HttpMessageHandler? _deltaPatchHttp;
    private readonly IProgress<OperationEvent> _events;

    /// <summary>The six version labels in table order (the game's three, then the Creation Kit's), and their colour bindings.</summary>
    private readonly List<(TextBlock Label, IDisposable Colour)> _versionLabels = [];

    private DowngraderRun? _run;
    private Button? _patch;
    private RadioButton? _oldGen;
    private CheckBox? _keepBackups;
    private CheckBox? _deletePatches;
    private ProgressBar? _progress;

    /// <summary>Keep Backups as last ticked, for the run's live reads from its worker thread.</summary>
    private volatile bool _keepBackupsNow;

    /// <summary>Delete Patches as last ticked, for the run's live reads from its worker thread.</summary>
    private volatile bool _deletePatchesNow;

    /// <summary>How many of the run's operations (clicks and polls) are scheduled or running.</summary>
    private int _operations;

    /// <param name="runtime">The app runtime: its log, input block and background operations.</param>
    /// <param name="game">The installation whose game folder holds the six files.</param>
    /// <param name="host">The machine the files are read on.</param>
    /// <param name="overview">The Overview's state, whose Install Type decides how a shared Next-Gen &amp; Anniversary file reads (DG-3).</param>
    /// <param name="settings">Where Keep Backups and Delete Patches start from, and where Patch All saves them (SET-7).</param>
    /// <param name="refreshOverview">
    /// <c>cmc.refresh_tab(Tab.Overview)</c>, run when the patch queue empties; false when it failed (already reported).
    /// By default there is no Overview to refresh, and it succeeds.
    /// </param>
    /// <param name="deltaPatchHttp">What patches are downloaded through; none makes every download fail.</param>
    public DowngraderWindow(
        AppRuntime runtime,
        GameInstall game,
        IHostEnvironment host,
        OverviewState overview,
        SettingsStore settings,
        Func<Task<bool>>? refreshOverview = null,
        HttpMessageHandler? deltaPatchHttp = null)
        : base(runtime, "Downgrader", DefaultWidth, 334)
    {
        _game = game;
        _host = host;
        _overview = overview;
        _settings = settings;
        _refreshOverview = refreshOverview ?? (() => Task.FromResult(true));
        _deltaPatchHttp = deltaPatchHttp;
        _events = new UiThreadProgress<OperationEvent>(Apply);
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
            _ = new DowngraderWindow(
                session.Runtime, session.Game, session.Host, session.Overview, session.Settings, session.RefreshOverviewAsync, session.DeltaPatchHttp)
                .OpenAsync(owner);
        }
    }

    /// <summary>Whether a Patch All click or the download poll it started is scheduled or still running.</summary>
    public bool IsRunning => Volatile.Read(ref _operations) > 0;

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

        _run = new DowngraderRun(_game, _host, _deltaPatchHttp, Versions);
        Content = Build(Versions);
        FitWidth();
    }

    /// <summary>
    /// Widens the window past its 600 px to the width its grid asks for, or narrows it back to 600 px when that is
    /// enough, keeping it centred where it was. The reference kept the window at 600 px, so a long version label
    /// (<c>Next-Gen &amp; Anniversary</c>) left the weighted Patch All / About column no width at all (bug #85, fixed
    /// as a Deviation, ADR-0006).
    /// </summary>
    private void FitWidth()
    {
        if (Content is not Control grid)
        {
            return;
        }

        // TkGrid asks for the sum of its columns' requested widths whatever it is offered.
        grid.Measure(Size.Infinity);
        var width = Math.Max(DefaultWidth, Math.Ceiling(grid.DesiredSize.Width));
        if (width == Width)
        {
            return;
        }

        var grown = width - Width;
        Width = width;
        // Keep the centre where CenterScreen put it: move left by half the growth (Position is in device pixels).
        Position = new PixelPoint(Position.X - (int)Math.Round(grown * DesktopScaling / 2), Position.Y);
    }

    /// <summary>
    /// Patch All (<c>patch_files</c>, DG-4): disables the button, clears the log pane and saves Keep Backups and Delete
    /// Patches if either differs from the settings (SET-7), then runs the click as a blocking operation for the Desired
    /// Version the radios show now.
    /// </summary>
    /// <returns>
    /// A task that completes when the click is done; a download it started goes on in the poll. If the click fails, the
    /// error is reported and the rest is skipped, the button staying disabled, as the exception left it in Tk.
    /// </returns>
    /// <exception cref="InvalidOperationException">The window isn't built (its detection failed, or it never opened).</exception>
    public async Task PatchAllAsync()
    {
        var run = _run ?? throw new InvalidOperationException("The Downgrader isn't built.");
        _patch!.IsEnabled = false;
        Log!.Clear();

        var current = _settings.Current;
        var keepBackups = _keepBackups!.IsChecked == true;
        var deletePatches = _deletePatches!.IsChecked == true;
        if (current.DowngraderKeepBackups != keepBackups || current.DowngraderDeleteDeltas != deletePatches)
        {
            _settings.Save(current with { DowngraderKeepBackups = keepBackups, DowngraderDeleteDeltas = deletePatches });
        }

        var desired = _oldGen!.IsChecked == true ? InstallType.OldGen : InstallType.NextGen;
        Interlocked.Increment(ref _operations);
        try
        {
            await Runtime.Operations.RunPhasedAsync(
                "Downgrader Patch All", OperationPhase.Blocking, async _ =>
                {
                    await run.PatchAllAsync(desired, new WindowHost(this), _events).ConfigureAwait(false);
                    return true;
                });
        }
        catch (OperationFailedException)
        {
            // Already reported by the runner; the rest of patch_files was skipped, as the exception skipped it in Tk.
        }
        finally
        {
            Interlocked.Decrement(ref _operations);
        }
    }

    /// <summary>Shows one of the run's events, on the UI thread, in the run's order.</summary>
    private void Apply(OperationEvent e)
    {
        switch (e)
        {
            case LogLine line:
                Log!.Log(line);
                break;
            case Percent percent:
                _progress!.Value = percent.Value;
                break;
            case PatchAllEnabled button:
                _patch!.IsEnabled = button.Enabled;
                break;
            case QueueRunning queue:
                ProcessingData = queue.Running;
                break;
            case VersionsDetected detected:
                Versions = detected.Versions;
                DrawVersions(detected.Versions);
                // The redrawn labels may be longer or shorter than before (bug #85).
                FitWidth();
                break;
        }
    }

    /// <summary>
    /// <c>draw_versions</c>: each version label's text and colour from the versions just detected and the game's
    /// Install Type as the Overview last gathered it (DG-3).
    /// </summary>
    private void DrawVersions(IReadOnlyList<DowngraderFileVersion> versions)
    {
        for (var i = 0; i < _versionLabels.Count; i++)
        {
            var (label, colour) = _versionLabels[i];
            colour.Dispose();
            _versionLabels[i] = (label, Colour(label, versions[i].InstallType));
        }
    }

    /// <summary>Sets a version label's text, and binds its colour, for a file of <paramref name="type"/>.</summary>
    private IDisposable Colour(TextBlock label, InstallType type)
    {
        var (text, colourKey) = Label(type, _overview.InstallType);
        label.Text = text;
        return label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(colourKey));
    }

    /// <summary>Runs the download poll as its own operation once the UI thread is free (the reference's <c>after()</c>).</summary>
    private void SchedulePoll(Func<IProgress<OperationPhase>, Task> poll)
    {
        // Counted now, on the run's thread, so the window never looks idle between the click and its poll.
        Interlocked.Increment(ref _operations);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Runtime.Operations.RunPhasedAsync(
                    "Downgrader download", OperationPhase.Interactive, async phases =>
                    {
                        await poll(phases).ConfigureAwait(false);
                        return true;
                    });
            }
            catch (OperationFailedException)
            {
                // Already reported by the runner. A failed download no longer gets here (bug #15, fixed: the run logs
                // it and moves on); an unexpected error does, and leaves the queue stuck, as the exception left the
                // reference's poll.
            }
            finally
            {
                Interlocked.Decrement(ref _operations);
            }
        });
    }

    /// <summary>The window as the run sees it: live reads of the boxes, the Overview refresh and the poll.</summary>
    private sealed class WindowHost(DowngraderWindow window) : IDowngraderHost
    {
        public bool KeepBackups => window._keepBackupsNow;

        public bool DeletePatches => window._deletePatchesNow;

        public Task<bool> RefreshOverviewAsync() => Dispatcher.UIThread.InvokeAsync(window._refreshOverview);

        public void SchedulePoll(Func<IProgress<OperationPhase>, Task> poll) => window.SchedulePoll(poll);
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
        // Column 2 (Patch All, About) takes the spare width of the window, and gives it up first: in the reference's
        // fixed 600 px window a long version label (Next-Gen & Anniversary) widened column 0 past it and left the
        // buttons no width at all (bug #85). FitWidth widens the window instead, so the buttons keep their own width.
        var grid = new TkGrid { ColumnWeights = [0, 0, 1] };

        grid.Children.Add(Place(Current("Game", "Current Game", [.. versions.Where(v => !v.IsCreationKit)]), 0, 0, rowSpan: 2, margin: new Thickness(10, 0)));
        grid.Children.Add(Place(Current("CreationKit", "Current Creation Kit", [.. versions.Where(v => v.IsCreationKit)]), 2, 0, rowSpan: 2, margin: new Thickness(10, 0)));

        // BooleanVar(value=current_versions["Fallout4.exe"] == OG): Old-Gen is preselected only for an Old-Gen game exe.
        var wantsDowngrade = versions[0].InstallType == InstallType.OldGen;
        var oldGen = _oldGen = new RadioButton { Name = "OldGen", Content = "Old-Gen", IsChecked = wantsDowngrade, GroupName = "DesiredVersion" };
        var nextGen = new RadioButton { Name = "NextGen", Content = "Next-Gen", IsChecked = !wantsDowngrade, GroupName = "DesiredVersion" };
        var desired = new TkGrid { Children = { Place(oldGen, 0, 0), Place(nextGen, 1, 0) } };
        grid.Children.Add(Place(Frame("DesiredVersion", "Desired Version", desired), 0, 1, rowSpan: 2, margin: new Thickness(5, 0)));

        var current = _settings.Current;
        var keepBackups = _keepBackups = Check("KeepBackups", "Keep Backups", current.DowngraderKeepBackups, KeepBackupsTooltip);
        var deletePatches = _deletePatches = Check("DeletePatches", "Delete Patches", current.DowngraderDeleteDeltas, DeletePatchesTooltip);
        // The run reads the boxes from its worker thread, whenever the reference read the Tk variables (a download
        // can be under way), so it reads copies kept current here.
        _keepBackupsNow = current.DowngraderKeepBackups;
        _deletePatchesNow = current.DowngraderDeleteDeltas;
        keepBackups.IsCheckedChanged += (_, _) => _keepBackupsNow = keepBackups.IsChecked == true;
        deletePatches.IsCheckedChanged += (_, _) => _deletePatchesNow = deletePatches.IsChecked == true;
        var options = new TkGrid { Children = { Place(keepBackups, 0, 0), Place(deletePatches, 1, 0) } };
        grid.Children.Add(Place(Frame("Options", "Options", options), 2, 1, rowSpan: 2, margin: new Thickness(5, 0)));

        var patch = _patch = new Button
        {
            Name = "PatchAll",
            Content = "Patch\n All",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        patch.Click += (_, _) => _ = PatchAllAsync();
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
        var progress = _progress = new ProgressBar
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
            var value = new TextBlock
            {
                Name = $"{prefix}{i}",
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _versionLabels.Add((value, Colour(value, files[i].InstallType)));
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
