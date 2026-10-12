using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
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

namespace CMToolkit.App.Tabs;

/// <summary>
/// <c>tabs._overview.OverviewTab</c>: the top block (mod manager, game path, Install Type, PC specs and the refresh
/// button, OVW-1..OVW-8) over three label frames side by side. The Binaries frame is built here (OVW-B1..OVW-B9); the
/// Archives and Modules frames stay empty until the next Overview slice.
/// </summary>
/// <remarks>
/// The data comes from <see cref="OverviewReport.Gather"/>, run as a blocking operation on load and on refresh, as the
/// reference ran both on the UI thread (ADR-0003). Each report replaces <see cref="OverviewState.Report"/>, which the
/// other tabs read. Neither shows a message box (OVW-8; the module warnings are the next slice's, and only on load).
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

    private readonly AppRuntime _runtime;
    private readonly IHostEnvironment _host;
    private readonly PcInfo _pc;
    private readonly GameInstall _game;
    private readonly OverviewState _state;
    private readonly Action _openDowngradeManager;
    private readonly PackRow _frames = new();

    /// <param name="runtime">The app runtime: its log, input block and background operations.</param>
    /// <param name="host">The machine the binaries are read on.</param>
    /// <param name="pc">The PC info for the PC Specs row.</param>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="state">Where each report goes, for the other tabs.</param>
    /// <param name="openDowngradeManager">Opens the Downgrader window. Until that slice lands, a no-op.</param>
    public OverviewTab(
        AppRuntime runtime, IHostEnvironment host, PcInfo pc, GameInstall game, OverviewState state, Action? openDowngradeManager = null)
        : base("Overview", "OverviewTab")
    {
        _runtime = runtime;
        _host = host;
        _pc = pc;
        _game = game;
        _state = state;
        _openDowngradeManager = openDowngradeManager ?? NoWindow;
    }

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

    /// <inheritdoc/>
    protected internal override async Task<bool> LoadAsync()
    {
        _state.Report = await _runtime.Operations.RunAsync("Overview load", OperationPhase.Blocking, Gather);
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
    /// isn't rebuilt; its Version label follows the Install Type. If the gathering fails, the error is reported and the
    /// frames are left as they were.
    /// </summary>
    public async Task RefreshAsync()
    {
        OverviewReport report;
        try
        {
            report = await _runtime.Operations.RunAsync("Overview refresh", OperationPhase.Blocking, Gather);
        }
        catch (OperationFailedException)
        {
            // Already reported by the runner; the rest of the refresh is skipped, as the exception skipped it in Tk.
            return;
        }

        _state.Report = report;
        BuildFrames();
    }

    private OverviewReport Gather() => OverviewReport.Gather(_game, _host, _runtime.Logger);

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
        // The next Overview slice fills these two.
        _frames.Children.Add(new GroupBox { Name = "Archives", Header = Labelframe.Title("Archives (BA2)") });
        _frames.Children.Add(new GroupBox { Name = "Modules", Header = Labelframe.Title("Modules (ESM/ESL/ESP)") });
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

        // padding=5; sticky=S, pady=10, spanning both columns. Measured against the Tk pair: the sv_ttk button is 34 px
        // tall with its text 2 px below centre, and its bottom sits 10 px above the frame border, of which the frame's
        // gutter already gives 4.
        var button = new Button
        {
            Name = "DowngradeManager",
            Content = "Downgrade Manager...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 10, 0, 6),
            Padding = new Thickness(8, 10, 8, 6),
        };
        button.Click += (_, _) => _openDowngradeManager();
        Place(button, rows + 1, 0);
        Grid.SetColumnSpan(button, 2);
        grid.Children.Add(button);

        return new GroupBox { Name = "Binaries", Header = Labelframe.Title("Binaries (EXE/DLL/BIN)"), Content = grid };
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
        // The Downgrader's slice wires it; until then the button is live but does nothing (OVW-B9).
    }
}
