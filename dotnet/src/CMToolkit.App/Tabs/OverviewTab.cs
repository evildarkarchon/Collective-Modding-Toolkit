using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Tabs;

/// <summary>
/// <c>tabs._overview.OverviewTab</c>: the top block (mod manager, game path, Install Type, PC specs and the refresh
/// button, OVW-1..OVW-8) over three label frames side by side: Binaries (OVW-B1..OVW-B9), Archives (OVW-A5, OVW-A6) and
/// Modules (OVW-M9, OVW-M10), with their counts coloured against the limits (OVW-C1).
/// </summary>
/// <remarks>
/// The data comes from <see cref="OverviewReport.Gather(GameInstall, IHostEnvironment, ILogger, OverviewReport?)"/>, run
/// as a blocking operation on load and on refresh, as the reference ran both on the UI thread (ADR-0003). Each report
/// replaces <see cref="OverviewState.Report"/>, which the other tabs read. Only the load shows the gathering's warning
/// boxes (OVW-M3, OVW-M5); the refresh shows none (OVW-8).
/// </remarks>
public sealed class OverviewTab : TabPage
{
    /// <summary><c>TOOLTIP_REFRESH</c>.</summary>
    private const string RefreshTooltip = "Refresh";

    /// <summary><c>TOOLTIP_GAME_PATH</c>.</summary>
    private const string GamePathTooltip = "Click to open folder";

    private const string VortexNote =
        "Note: Vortex is not yet fully supported.\nOverview should be accurate but Scanner will only look in Data and not your staging folders, so it cannot yet identify the source mod for each issue.";

    private const string Win11Note =
        "Note: MO2 2.5.2 and earlier have issues on Windows 11 24H2+.\nPython apps such as Wrye Bash and CLASSIC may give errors\nsuch as FileNotFound or fail to detect files that are only\npresent in the VFS and not the Data folder.";

    /// <summary><c>TOOLTIP_BA2_FORMATS</c>.</summary>
    private const string Ba2FormatsTooltip =
        "General/GNRL/Main: Used for all non-texture files.\nHard limit of 256, after which the game will crash at the main menu.\n\nTexture/DX10: Used only for textures.\nNo hard limit, but functional limit is 255, after which the game fails\nto use correct textures.\n1.11.240 has increased this limit to 1024.";

    /// <summary><c>TOOLTIP_UNREADABLE</c>.</summary>
    private const string UnreadableTooltip = "Files that could not be read due to permissions, unexpected format, or corruption.";

    /// <summary><c>TOOLTIP_BA2_VERSIONS</c>.</summary>
    private const string Ba2VersionsTooltip = "v1 works with all FO4 versions.\nv7/v8 require either BASS or NG.\nSome apps only support v1.";

    /// <summary><c>TOOLTIP_MODULE_TYPES</c>.</summary>
    private const string ModuleTypesTooltip =
        "Full modules include all non-Light modules.\nLight modules are ESL-flagged and/or have the .esl extension.\n\nLight modules give up the capacity to contain more than 4096\nnew records in exchange for having a higher plugin limit.";

    /// <summary><c>TOOLTIP_HEDR_100</c>.</summary>
    private const string Hedr100Tooltip =
        "Plugin header v1.00 has a larger Form ID range.\nAny plugins with IDs below 800 MUST be v1.00.\nFO4VR only supports v0.95.";

    /// <summary><c>TOOLTIP_HEDR_95</c>.</summary>
    private const string Hedr95Tooltip =
        "Plugin header v0.95 has a smaller Form ID range.\nAny plugins with IDs below 800 MUST be v1.00.\nFO4VR only supports v0.95.\n\nIn the base game, these 4 modules are v0.95:\n• DLCCoast.esm\n• DLCworkshop02.esm\n• DLCworkshop03.esm\n• DLCNukaWorld.esm";

    /// <summary><c>TOOLTIP_HEDR_UNKNOWN</c>, typo ("likey") included.</summary>
    private const string HedrUnknownTooltip =
        "These modules have an unsupported header version.\nFallout 4 supports only v0.95 and v1.00.\n\nFiles with v0.94 were likey made with FalloutSnip and\nshould be replaced, recreated, or removed.\nUse such mods AT YOUR OWN RISK as they may result\nin unknown behavior or issues due to having an invalid\nstructure or corruption.";

    private readonly AppRuntime _runtime;
    private readonly IHostEnvironment _host;
    private readonly PcInfo _pc;
    private readonly GameInstall _game;
    private readonly OverviewState _state;
    private readonly Action _openDowngradeManager;
    private readonly Action _openArchivePatcher;
    private readonly PackRow _frames = new();

    /// <param name="runtime">The app runtime: its log, input block and background operations.</param>
    /// <param name="host">The machine the binaries are read on.</param>
    /// <param name="pc">The PC info for the PC Specs row.</param>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="state">Where each report goes, for the other tabs.</param>
    /// <param name="openDowngradeManager">Opens the Downgrader window. Until that slice lands, a no-op.</param>
    /// <param name="openArchivePatcher">Opens the Archive Patcher window (OVW-A6). Until that slice lands, a no-op.</param>
    public OverviewTab(
        AppRuntime runtime,
        IHostEnvironment host,
        PcInfo pc,
        GameInstall game,
        OverviewState state,
        Action? openDowngradeManager = null,
        Action? openArchivePatcher = null)
        : base("Overview", "OverviewTab")
    {
        _runtime = runtime;
        _host = host;
        _pc = pc;
        _game = game;
        _state = state;
        _openDowngradeManager = openDowngradeManager ?? NoWindow;
        _openArchivePatcher = openArchivePatcher ?? NoWindow;
        // cmc.refresh_tab(Tab.Overview): the Scanner refreshes this tab through the shared state.
        state.Refresher = RefreshAsync;
    }

    /// <summary>
    /// Shows one of the first load's warning boxes (OVW-M3, OVW-M5) and completes when it is dismissed: the app's
    /// sv-styled warning box over the main window by default; tests record instead.
    /// </summary>
    public Func<string, string, Task> ShowWarning { get; init; } = ShowWarningBox;

    /// <summary>
    /// The Core gathering, <see cref="OverviewReport.Gather(GameInstall, IHostEnvironment, ILogger, OverviewReport?)"/>
    /// by default. A test seam: the <c>overview-tab</c> scenarios were recorded with the module and archive steps
    /// stubbed out, so their replays swap in the gathering without them.
    /// </summary>
    public Func<GameInstall, IHostEnvironment, ILogger, OverviewReport?, OverviewReport> Gatherer { get; init; } = OverviewReport.Gather;

    /// <summary>
    /// Opens a folder in Explorer (<c>os.startfile</c>; OVW-6). The shell by default; tests record instead. A failure
    /// throws, as <c>os.startfile</c> raised into the Tk callback, and reaches the Error Window.
    /// </summary>
    public Action<string> OpenFolder { get; init; } = StartFile;

    /// <summary>
    /// The text of the MO2 details window (OVW-4): the exe, the INI, whether it is portable and its portable.txt, then
    /// every <c>mo2_settings</c> entry right-justified to the longest key, each value rendered with Python's
    /// <c>str()</c> (T-9).
    /// </summary>
    public static string ModManagerDetails(ModManager manager, Mo2Configuration mo2)
    {
        var text = new StringBuilder()
            .Append("EXE: ").Append(manager.ExePath).Append('\n')
            .Append("INI: ").Append(mo2.IniPath).Append('\n')
            .Append("Portable: ").Append(PythonStr.Of(mo2.Portable)).Append('\n');
        if (mo2.PortableTxtPath is { } portableTxt)
        {
            text.Append("Portable.txt: ").Append(portableTxt).Append('\n');
        }

        var width = mo2.Settings.Count == 0 ? 0 : mo2.Settings.Max(s => PythonStr.Length(s.Key));
        text.AppendJoin('\n', mo2.Settings.Select(s => $"{PythonStr.RightJustify(s.Key, width)}: {Str(s.Value)}"));
        return text.ToString();
    }

    /// <summary>
    /// <c>OverviewTab._load</c>: gathers the first report, then shows its warnings in order (OVW-M3, OVW-M5), which the
    /// reference showed mid-gathering, before the frames were built. If the gathering dies (OVW-A2, B-15), what it had
    /// gathered becomes the current report, as the reference's shared state kept it, the warnings raised before it died
    /// are still shown, and the failure propagates so the tab stays on its loading text.
    /// </summary>
    protected internal override async Task<bool> LoadAsync()
    {
        OverviewReport report;
        try
        {
            report = await _runtime.Operations.RunAsync("Overview load", OperationPhase.Blocking, () => Gather(previous: null));
        }
        catch (OperationFailedException ex) when (ex.InnerException is OverviewIncompleteException incomplete)
        {
            _state.Report = incomplete.Partial;
            await ShowWarningsAsync(incomplete.Partial.Warnings);
            throw;
        }

        _state.Report = report;
        await ShowWarningsAsync(report.Warnings);
        return true;
    }

    /// <inheritdoc/>
    protected internal override Control BuildContent()
    {
        var top = BuildTopBlock();
        DockPanel.SetDock(top, Dock.Top);
        BuildFrames();
        return new DockPanel { Children = { top, _frames } };
    }

    /// <summary>
    /// The refresh button (OVW-8): gathers a new report as a blocking operation, then rebuilds the frames. The top block
    /// isn't rebuilt; its Version label follows the Install Type. No warning is shown. If the gathering fails, the error
    /// is reported, what it had gathered becomes the current report (the reference's shared state), and the frames are
    /// left as they were.
    /// </summary>
    /// <returns>
    /// Whether the refresh completed; false when its gathering failed and was reported. The callers refreshing through
    /// <c>cmc.refresh_tab</c> (the Scanner's scan, the Archive Patcher's Patch All) stop on false, as the exception
    /// ended their Tk callback too.
    /// </returns>
    public async Task<bool> RefreshAsync()
    {
        OverviewReport report;
        try
        {
            var previous = _state.Report;
            report = await _runtime.Operations.RunAsync("Overview refresh", OperationPhase.Blocking, () => Gather(previous));
        }
        catch (OperationFailedException ex)
        {
            // Already reported by the runner; the rest of the refresh is skipped, as the exception skipped it in Tk.
            if (ex.InnerException is OverviewIncompleteException incomplete)
            {
                _state.Report = incomplete.Partial;
            }

            return false;
        }

        _state.Report = report;
        BuildFrames();
        return true;
    }

    private OverviewReport Gather(OverviewReport? previous) => Gatherer(_game, _host, _runtime.Logger, previous);

    /// <summary>Shows each warning in turn, waiting for one to be dismissed before the next, as the Tk boxes blocked.</summary>
    private async Task ShowWarningsAsync(IReadOnlyList<OverviewWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            await ShowWarning(warning.Title, warning.Message);
        }
    }

    /// <summary>
    /// <c>messagebox.showwarning</c> with no parent, which Tk puts over its root: here, the desktop's main window, or
    /// none when there isn't one yet.
    /// </summary>
    private static Task ShowWarningBox(string title, string message)
        => MessageBox.ShowWarningAsync(
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow, title, message);

    /// <summary>
    /// <c>frame_top</c>: right-aligned row labels, an icon column, the values (the weighted column) and the refresh
    /// button spanning the first two rows.
    /// </summary>
    private TkGrid BuildTopBlock()
    {
        // pack(anchor=W, fill=X, pady=5), grid_columnconfigure(2, weight=1).
        var grid = new TkGrid { ColumnWeights = [0, 0, 1, 0], Margin = new Thickness(0, 5) };

        // The trailing newline gives the label a fifth, empty line, which Tk sizes the rowspan by.
        var labels = Label("Mod Manager:\nGame Path:\nVersion:\nPC Specs:\n", "CmtDefault", "TopLabels");
        labels.TextAlignment = TextAlignment.Right;
        labels.HorizontalAlignment = HorizontalAlignment.Right;
        labels.Margin = new Thickness(5, 0);
        Grid.SetRowSpan(labels, 4);
        grid.Children.Add(labels);

        var manager = _game.Manager;
        if (manager is not null)
        {
            Control icon;
            if (manager.Kind == ModManagerKind.ModOrganizer)
            {
                icon = Icon("images/info-16.png", "ModManagerIcon", 0, "Detection details");
                icon.Cursor = new Cursor(StandardCursorType.Hand);
                var details = ModManagerDetails(manager, _game.Mo2!);
                icon.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(icon).Properties.IsLeftButtonPressed)
                    {
                        ShowDetails(icon, details);
                    }
                };

                // OVW-5, B-8: the exact OS string, so 25H2 and later get no warning despite the tooltip's "24H2+".
                if (_pc.Os == "Windows 11 24H2" && AtMost(manager.Version, [2, 5, 2]))
                {
                    var osIcon = Icon("images/warning-16.png", "OsWarning", 3, Win11Note);
                    osIcon.VerticalAlignment = VerticalAlignment.Top;
                    grid.Children.Add(osIcon);
                }
            }
            else
            {
                icon = Icon("images/warning-16.png", "ModManagerIcon", 0, VortexNote);
            }

            grid.Children.Add(icon);
        }

        var managerText = manager is null
            ? "Not Found"
            : $"{manager.Name} v{manager.VersionText} [Profile: {(_game.Mo2?.SelectedProfile is { Length: > 0 } profile ? profile : "Unknown")}]";
        var managerLabel = Label(managerText, manager is null ? "CmtBad" : "CmtNeutral2", "ModManager");
        if (manager is null)
        {
            ToolTip.SetTip(managerLabel, OverviewReport.NoModManagerText);
        }

        Place(managerLabel, 0, 2);
        grid.Children.Add(managerLabel);

        var path = Label(_game.GamePath.ToString(), "CmtNeutral2", "GamePath");
        path.Cursor = new Cursor(StandardCursorType.Hand);
        path.Background = Brushes.Transparent;
        ToolTip.SetTip(path, GamePathTooltip);
        path.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(path).Properties.IsLeftButtonPressed)
            {
                OpenFolder(_game.GamePath.ToString());
            }
        };
        Place(path, 1, 2);
        grid.Children.Add(path);

        // textvariable=install_type_sv: the Install Type as it is now, whichever report set it.
        var version = Label(_state.InstallType.DisplayName(), "CmtGood", "Version");
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverviewState.InstallType))
            {
                version.Text = _state.InstallType.DisplayName();
            }
        };
        Place(version, 2, 2);
        grid.Children.Add(version);

        // frame_specs: two labels side by side, the second padx=(30, 0) (OVW-7, PC-6).
        var specs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                Label($"{_pc.Os}\n{_pc.Ram}GB RAM", "CmtNeutral2", "Specs1"),
                Label($"{_pc.Cpu}\n{_pc.Gpu} {_pc.Vram}GB", "CmtNeutral2", "Specs2", new Thickness(30, 0, 0, 0)),
            },
        };
        Place(specs, 3, 2);
        grid.Children.Add(specs);

        // ttk.Button(image=refresh-32.png, padding=0), grid(column=3, row=0, rowspan=2, sticky=E, padx=10). sv_ttk still
        // draws its button border around the image, which makes the Tk button 39 px square: Padding 3 matches it.
        var refresh = new Button
        {
            Name = "Refresh",
            Content = new AssetImage("images/refresh-32.png"),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0),
        };
        ToolTip.SetTip(refresh, RefreshTooltip);
        refresh.Click += (_, _) => _ = RefreshAsync();
        Place(refresh, 0, 3);
        Grid.SetRowSpan(refresh, 2);
        grid.Children.Add(refresh);

        return grid;
    }

    /// <summary>
    /// <c>build_gui_binaries</c>, <c>build_gui_archives</c> and <c>build_gui_modules</c>: the three frames, rebuilt from
    /// the current report (the refresh destroys and rebuilds them).
    /// </summary>
    private void BuildFrames()
    {
        _frames.Children.Clear();
        _frames.Children.Add(BuildBinaries(_state.Report!));
        _frames.Children.Add(BuildArchives(_state.Report!));
        _frames.Children.Add(BuildModules(_state.Report!));
    }

    /// <summary>
    /// <c>build_gui_archives</c> (OVW-A5, OVW-A6, OVW-C1): the format rows and their counts against the limits (raised for
    /// Anniversary, T-2), the unreadable count, a separator, the version counts, and the Archive Patcher button at the
    /// bottom. Column 2 takes the spare width and the button's row the spare height.
    /// </summary>
    private Control BuildArchives(OverviewReport report)
    {
        var archives = report.Archives;
        var grid = new TkGrid { ColumnWeights = [0, 0, 1], RowWeights = [0, 0, 0, 0, 0, 0, 0, 2] };

        var formats = RowLabel("General:\nTexture:\nTotal:", "CmtDefault", "ArchiveFormats", 0, Ba2FormatsTooltip);
        Grid.SetRowSpan(formats, 3);
        grid.Children.Add(formats);
        var unreadable = archives.Unreadable.Count > 0 ? "CmtBad" : "CmtNeutral1";
        grid.Children.Add(RowLabel("Unreadable:", unreadable, "ArchiveUnreadableLabel", 3, UnreadableTooltip));
        grid.Children.Add(Separator());
        var versions = RowLabel("v1 (OG):\nv7/8 (NG):", "CmtDefault", "ArchiveVersions", 5, Ba2VersionsTooltip);
        Grid.SetRowSpan(versions, 2);
        grid.Children.Add(versions);

        grid.Children.Add(CountLabel(report.GeneralArchives, "ArchiveCount0", 0));
        grid.Children.Add(CountLabel(report.TextureArchives, "ArchiveCount1", 1));
        grid.Children.Add(CountLabel(report.TotalArchives, "ArchiveCount2", 2));
        grid.Children.Add(PlainCount(archives.Unreadable.Count, unreadable, "ArchiveCount3", 3));
        grid.Children.Add(PlainCount(archives.OldGen.Count, "CmtDefault", "ArchiveCount5", 5));
        grid.Children.Add(PlainCount(archives.NextGen.Count, "CmtDefault", "ArchiveCount6", 6));

        // Column 2: sticky=EW, left-justified, so the slashes line up under each other whatever the digits.
        var limits = Label(
            $" / {report.GeneralArchives.Limit}\n / {report.TextureArchives.Limit}\n / {report.TotalArchives.Limit}",
            "CmtDefault",
            "ArchiveLimits");
        limits.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(limits, Ba2FormatsTooltip);
        Place(limits, 0, 2);
        Grid.SetRowSpan(limits, 3);
        grid.Children.Add(limits);

        // grid_size() is (3, 7) here, so the button goes in row 7, spanning the three columns, like the Binaries one.
        var button = FrameButton("Archive Patcher...", "ArchivePatcher", 7);
        Grid.SetColumnSpan(button, 3);
        button.Click += (_, _) => _openArchivePatcher();
        grid.Children.Add(button);

        return Frame("Archives", "Archives (BA2)", grid);
    }

    /// <summary>
    /// <c>build_gui_modules</c> (OVW-M9, OVW-M10, OVW-C1): the type rows and their counts against the limits, the
    /// unreadable count, a separator, the HEDR version counts, and, when there are unknown versions, an info icon that
    /// opens their list. No row or column takes spare space, so the content sits at the top left.
    /// </summary>
    private Control BuildModules(OverviewReport report)
    {
        var modules = report.Modules;
        var grid = new TkGrid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

        var types = RowLabel("Full:\nLight:\nTotal:", "CmtDefault", "ModuleTypes", 0, ModuleTypesTooltip);
        Grid.SetRowSpan(types, 3);
        grid.Children.Add(types);
        var unreadable = modules.Unreadable.Count > 0 ? "CmtBad" : "CmtNeutral1";
        grid.Children.Add(RowLabel("Unreadable:", unreadable, "ModuleUnreadableLabel", 3, UnreadableTooltip));
        grid.Children.Add(Separator());
        grid.Children.Add(RowLabel("HEDR v1.00:", "CmtDefault", "Hedr100Label", 5, Hedr100Tooltip));
        grid.Children.Add(RowLabel("HEDR v0.95:", "CmtDefault", "Hedr95Label", 6, Hedr95Tooltip));
        var unknown = modules.HedrUnknown.Count > 0 ? "CmtBad" : "CmtNeutral1";
        grid.Children.Add(RowLabel("HEDR v????:", unknown, "HedrUnknownLabel", 7, HedrUnknownTooltip));

        grid.Children.Add(CountLabel(report.FullModules, "ModuleCount0", 0));
        grid.Children.Add(CountLabel(report.LightModules, "ModuleCount1", 1));
        grid.Children.Add(CountLabel(report.TotalModules, "ModuleCount2", 2));
        grid.Children.Add(PlainCount(modules.Unreadable.Count, unreadable, "ModuleCount3", 3));
        grid.Children.Add(PlainCount(modules.Hedr100, "CmtDefault", "ModuleCount5", 5));
        grid.Children.Add(PlainCount(modules.Hedr95.Count, "CmtDefault", "ModuleCount6", 6));
        grid.Children.Add(PlainCount(modules.HedrUnknown.Count, unknown, "ModuleCount7", 7));

        // Column 2: sticky=EW and right-justified; " /  254" has two spaces so the three lines are the same length.
        var limits = Label(
            $" /  {report.FullModules.Limit}\n / {report.LightModules.Limit}\n / {report.TotalModules.Limit}",
            "CmtDefault",
            "ModuleLimits");
        limits.HorizontalAlignment = HorizontalAlignment.Stretch;
        limits.TextAlignment = TextAlignment.Right;
        ToolTip.SetTip(limits, ModuleTypesTooltip);
        Place(limits, 0, 2);
        Grid.SetRowSpan(limits, 3);
        grid.Children.Add(limits);

        if (modules.HedrUnknown.Count > 0)
        {
            // grid(column=2, row=7, sticky=W, padx=(5, 0), ipady=3).
            var icon = Icon("images/info-16.png", "HedrUnknownIcon", 7, "Detection details");
            icon.Margin = new Thickness(5, 0, 0, 0);
            Grid.SetColumn(icon, 2);
            icon.Cursor = new Cursor(StandardCursorType.Hand);
            var items = modules.HedrUnknown.Select(m => (m.Version, m.Module.ToString())).ToList();
            icon.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(icon).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(icon) is Window owner)
                {
                    // Not awaited: like the reference's modal, it runs on while the click handler returns.
                    _ = TreeWindow.Create(
                            _runtime, 400, 500, "Detected Invalid Module Versions", "", ["HEDR", " Module"], items, PythonFloat.Repr)
                        .ShowModalAsync(owner);
                }
            };
            grid.Children.Add(icon);
        }

        return Frame("Modules", "Modules (ESM/ESL/ESP)", grid);
    }

    /// <summary>
    /// One of the three label frames. Tk grids an unpadded labelframe's content from its border line (the grid's bounding
    /// box starts 2 px into a frame whose outline is inset by as much), where the GroupBox theme keeps a 4 px gutter for
    /// the padded labelframes of the other tabs; measured off the screenshot pairs, the content sat 4 px right of Tk's,
    /// so the gutter is taken back out at the sides. The frames' widths don't change: all three lose the same width,
    /// and the pack row shares the space out equally.
    /// </summary>
    private static GroupBox Frame(string name, string title, Control content)
        => new() { Name = name, Header = Labelframe.Title(title), Content = content, Padding = new Thickness(-4, 0, -4, 0) };

    /// <summary>
    /// A right-aligned row label in column 0 (<c>sticky=E</c>, <c>padx=(5, 0)</c>, <c>justify=RIGHT</c>) with its tooltip.
    /// </summary>
    private static TextBlock RowLabel(string text, string colourKey, string name, int row, string tooltip)
    {
        var label = Label(text, colourKey, name, new Thickness(5, 0, 0, 0));
        label.TextAlignment = TextAlignment.Right;
        label.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTip.SetTip(label, tooltip);
        Place(label, row, 0);
        return label;
    }

    /// <summary>
    /// <c>add_count_label</c>: the count right-justified to four characters, green, orange or red by how close it is to
    /// its limit (OVW-C1), in column 1.
    /// </summary>
    private static TextBlock CountLabel(LimitedCount count, string name, int row)
    {
        var colour = count.Level switch
        {
            CountLevel.Good => "CmtGood",
            CountLevel.Warning => "CmtWarning",
            _ => "CmtBad",
        };
        return PlainCount(count.Count, colour, name, row, PythonStr.RightJustify(count.Count.ToString(CultureInfo.InvariantCulture), 4));
    }

    /// <summary>A count in column 1 (<c>text=len(...)</c>, <c>sticky=E</c>, <c>padx=(5, 0)</c>).</summary>
    private static TextBlock PlainCount(int count, string colourKey, string name, int row, string? text = null)
    {
        var label = Label(text ?? count.ToString(CultureInfo.InvariantCulture), colourKey, name, new Thickness(5, 0, 0, 0));
        label.HorizontalAlignment = HorizontalAlignment.Right;
        Place(label, row, 1);
        return label;
    }

    /// <summary>
    /// <c>add_separator(frame, HORIZONTAL, 0, 4, 3)</c>: row 4 across the three columns, <c>padx=10</c>, <c>pady=10</c>,
    /// <c>ipady=1</c>, so a 3 px bar.
    /// </summary>
    private static Border Separator()
    {
        var separator = new Border { Height = 3, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Stretch };
        separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable("SvSep"));
        Place(separator, 4, 0);
        Grid.SetColumnSpan(separator, 3);
        return separator;
    }

    /// <summary>
    /// A frame's bottom button (<c>padding=5</c>, <c>sticky=S</c>, <c>pady=10</c>). Measured against the Tk pair: the sv_ttk
    /// button is 34 px tall with its text 2 px below centre, and its bottom sits 10 px above the frame border, of which
    /// the frame's gutter already gives 4.
    /// </summary>
    private static Button FrameButton(string text, string name, int row)
    {
        var button = new Button
        {
            Name = name,
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 10, 0, 6),
            Padding = new Thickness(8, 10, 8, 6),
        };
        Place(button, row, 0);
        return button;
    }

    /// <summary>
    /// The Binaries frame (OVW-B1..OVW-B9): the names, one value per binary coloured by its status, the Address Library
    /// row, and the Downgrade Manager button at the bottom.
    /// </summary>
    private Control BuildBinaries(OverviewReport report)
    {
        var rows = report.Binaries.Count;
        // The button's row is the only weighted one (weight=2), so it takes the spare height and the button sits at
        // the bottom. Tk's grid anchors its content at the top left.
        var grid = new TkGrid
        {
            RowWeights = [.. Enumerable.Repeat(0, rows + 1), 2],
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // One right-justified label spanning the rows: each name minus its extension, plus a colon.
        var names = Label(
            string.Join('\n', report.Binaries.Select(b => (b.Name.LastIndexOf('.') is var dot and >= 0 ? b.Name[..dot] : b.Name) + ":")),
            "CmtDefault",
            "BinaryNames");
        names.TextAlignment = TextAlignment.Right;
        names.HorizontalAlignment = HorizontalAlignment.Right;
        names.Margin = new Thickness(5, 0);
        Grid.SetRowSpan(names, rows);
        grid.Children.Add(names);

        var addressLabel = Label("Address Library:", "CmtDefault", "AddressLibraryLabel");
        addressLabel.TextAlignment = TextAlignment.Right;
        addressLabel.HorizontalAlignment = HorizontalAlignment.Right;
        addressLabel.Margin = new Thickness(5, 0);
        Place(addressLabel, rows, 0);
        grid.Children.Add(addressLabel);

        var installed = report.AddressLibrary is not null;
        var address = Label(installed ? "Installed" : "Not Found", installed ? "CmtGood" : "CmtBad", "AddressLibrary");
        if (!installed)
        {
            ToolTip.SetTip(address, OverviewReport.AddressLibraryMissingText);
        }

        Place(address, rows, 1);
        grid.Children.Add(address);

        for (var i = 0; i < rows; i++)
        {
            grid.Children.Add(BinaryValue(report.Binaries[i], i));
        }

        // padding=5; sticky=S, pady=10, spanning both columns. FrameButton has the measurements against the Tk pair.
        var button = FrameButton("Downgrade Manager...", "DowngradeManager", rows + 1);
        button.Click += (_, _) => _openDowngradeManager();
        Grid.SetColumnSpan(button, 2);
        grid.Children.Add(button);

        return Frame("Binaries", "Binaries (EXE/DLL/BIN)", grid);
    }

    /// <summary>
    /// One binary's value: its Install Type, swapped for its version on hover (<c>a.b.c.d</c>, or <c>Not Found</c>
    /// without a version resource) and back on leave (OVW-B8). <c>width=11</c>: always 11 characters wide, so a longer
    /// type is cut off, as Tk cuts it.
    /// </summary>
    private static TextBlock BinaryValue(BinaryFile file, int row)
    {
        var text = file.InstallType.DisplayName();
        var hover = file.Version is null ? "Not Found" : string.Join('.', file.Version);
        var key = file.Status switch
        {
            BinaryStatus.Good => "CmtGood",
            BinaryStatus.Neutral => "CmtNeutral1",
            _ => "CmtBad",
        };
        var value = Label(text, key, $"Binary{row}");
        // 11 characters of 9 px: Tk's GDI-rounded Cascadia Mono advance at 16 px.
        value.Width = 11 * 9;
        value.ClipToBounds = true;
        value.Background = Brushes.Transparent;
        value.PointerEntered += (_, _) => value.Text = hover;
        value.PointerExited += (_, _) => value.Text = text;
        Place(value, row, 1);
        return value;
    }

    /// <summary>A normal-size label in one of the palette's colours, named for tests.</summary>
    private static TextBlock Label(string text, string colourKey, string name, Thickness margin = default)
    {
        var label = new TextBlock
        {
            Name = name,
            Text = text,
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(colourKey));
        return label;
    }

    /// <summary>
    /// An image-only label in the icon column: <c>padx=(0, 5)</c>, <c>ipady=3</c> (3 px above and below the image
    /// inside the label), <c>sticky=W</c>, with its tooltip on the whole label.
    /// </summary>
    private static Border Icon(string asset, string name, int row, string tooltip)
    {
        var icon = new Border
        {
            Name = name,
            Child = new AssetImage(asset),
            Padding = new Thickness(0, 3),
            Margin = new Thickness(0, 0, 5, 0),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(icon, tooltip);
        Place(icon, row, 1);
        return icon;
    }

    private static void Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    /// <summary>
    /// <c>manager.version &lt;= Version("2.5.2")</c>. Both are three-part release versions, for which PEP 440 ordering is
    /// the tuple order.
    /// </summary>
    private static bool AtMost(IReadOnlyList<int> version, int[] limit)
    {
        for (var i = 0; i < limit.Length; i++)
        {
            if (version[i] != limit[i])
            {
                return version[i] < limit[i];
            }
        }

        return true;
    }

    /// <summary>A <c>mo2_settings</c> value as Python's <c>str()</c> renders it (T-9).</summary>
    private static string Str(Mo2SettingValue value) => value switch
    {
        Mo2SettingValue.PathValue path => path.Path.ToString(),
        Mo2SettingValue.TextValue text => text.Text,
        Mo2SettingValue.FlagValue flag => PythonStr.Of(flag.Flag),
        Mo2SettingValue.ListValue list => PythonStr.Of(list.Items),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Opens the MO2 details in an AboutWindow, 750x350 (OVW-4), owned by the main window.</summary>
    private void ShowDetails(Control source, string details)
    {
        if (TopLevel.GetTopLevel(source) is Window owner)
        {
            // Not awaited: like the reference's modal, it runs on while the click handler returns.
            _ = new AboutWindow(_runtime, 750, 350, "Detected Mod Manager Settings", details).ShowModalAsync(owner);
        }
    }

    /// <summary><c>os.startfile(path)</c>: the shell's default action, which for a folder opens it in Explorer.</summary>
    private static void StartFile(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void NoWindow()
    {
        // The Downgrader's and the Archive Patcher's slices wire them; until then the buttons are live but do nothing
        // (OVW-B9, OVW-A6).
    }
}
