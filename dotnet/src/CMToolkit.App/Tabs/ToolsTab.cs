using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;

namespace CMToolkit.App.Tabs;

/// <summary>
/// <c>tabs._tools.ToolsTab</c> (TOOL-1, TOOL-2): three equal-weight columns of buttons under centred titles. The
/// Toolkit Utilities open the app's own windows; every other button opens a web page (NET-4) and has an info icon.
/// </summary>
public sealed class ToolsTab : TabPage
{
    private readonly AppRuntime _runtime;
    private readonly IReadOnlyList<ToolColumn> _columns;

    /// <summary>The Tools tab, with the Toolkit Utilities opening the given windows.</summary>
    /// <param name="runtime">The app runtime; its browser opens the links.</param>
    /// <param name="openDowngradeManager">
    /// Opens the Downgrader window: <see cref="Views.DowngraderWindow.Open"/> in the app (<see cref="MainTabs"/>). Null
    /// makes the button do nothing, for tests that don't open windows.
    /// </param>
    /// <param name="openArchivePatcher">
    /// Opens the Archive Patcher window: <see cref="Views.ArchivePatcherWindow.Open"/> in the app
    /// (<see cref="MainTabs"/>). Null makes the button do nothing, for tests that don't open windows.
    /// </param>
    public ToolsTab(AppRuntime runtime, Action? openDowngradeManager = null, Action? openArchivePatcher = null)
        : base("Tools", "ToolsTab")
    {
        _runtime = runtime;
        _columns = Columns(openDowngradeManager ?? NoWindow, openArchivePatcher ?? NoWindow);
    }

    /// <summary>
    /// The hover text of a link button (TOOL-2), picked by substring as the reference did: Nexus Mods first, then
    /// GitHub, else a plain website.
    /// </summary>
    public static string LinkTooltip(string url)
    {
        if (url.Contains("nexusmods", StringComparison.Ordinal))
        {
            return "View on Nexus Mods";
        }

        return url.Contains("github", StringComparison.Ordinal) ? "View on GitHub" : "Open website";
    }

    /// <inheritdoc/>
    protected internal override Control BuildContent()
    {
        // _build_gui: grid_columnconfigure(i, weight=1, pad=5) per column, and load() had already given row 0
        // weight 1, so the three frames fill the tab's height.
        var grid = new TkGrid { ColumnWeights = [1, 1, 1], ColumnPads = [5, 5, 5], RowWeights = [1] };
        for (var i = 0; i < _columns.Count; i++)
        {
            // padx=5, pady=5, sticky=NSEW, padding=5; the "centred" class is labelanchor=N, the title centred on top.
            var frame = new GroupBox
            {
                Header = Labelframe.Title(_columns[i].Title),
                Content = BuildButtons(_columns[i].Tools),
                Margin = new Thickness(5),
                Padding = Labelframe.Padding(5),
                [Grid.ColumnProperty] = i,
            };
            frame.Classes.Add("centred");
            grid.Children.Add(frame);
        }

        return grid;
    }

    /// <summary>One column's buttons (<c>add_tool_button</c>), each with its info icon to the right if it has one.</summary>
    private Grid BuildButtons(IReadOnlyList<Tool> tools)
    {
        // The frame's own grid has no weights, so it keeps its content size, anchored top-left.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        foreach (var tool in tools)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // sticky=EW, padx=(10, 0), pady=5: every button in a column is as wide as the widest.
            var button = new Button
            {
                Content = tool.Text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(10, 5, 0, 5),
                [Grid.RowProperty] = row,
            };
            if (tool.Url is { } url)
            {
                button.Click += (_, _) => _runtime.Browser.Open(url);
                ToolTip.SetTip(button, LinkTooltip(url));
            }
            else
            {
                var open = tool.OpenWindow!;
                button.Click += (_, _) => open();
            }

            grid.Children.Add(button);

            if (tool.Info is { } info)
            {
                // padx=(5, 0), pady=(image height // 2 - 1, 0), sticky=NSEW: the 16 px icon sits in a cell 7 px shorter
                // than the row, at its vertical centre (a ttk.Label's default anchor is w).
                var icon = new AssetImage("images/info-16.png") { VerticalAlignment = VerticalAlignment.Center };
                var cell = new Border
                {
                    Child = icon,
                    Background = Avalonia.Media.Brushes.Transparent,
                    Margin = new Thickness(5, (16 / 2) - 1, 0, 0),
                    [Grid.RowProperty] = row,
                    [Grid.ColumnProperty] = 1,
                };
                // The tooltip is on the whole label (the cell), as tktooltip binds <Enter> on the label widget.
                ToolTip.SetTip(cell, info);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    /// <summary>A Toolkit Utilities button's action when no window opener was passed: nothing, for tests that open no windows.</summary>
    private static void NoWindow()
    {
        // Empty on purpose: the app always passes real openers (MainTabs); only tests leave them out (TOOL-1).
    }

    /// <summary>The three columns, <c>tool_buttons</c> in <c>_tools.py</c>: texts keep their deliberate line breaks and padding.</summary>
    private static IReadOnlyList<ToolColumn> Columns(Action openDowngradeManager, Action openArchivePatcher) =>
    [
        new("Toolkit Utilities",
        [
            new("Downgrade Manager", OpenWindow: openDowngradeManager),
            new("Archive Patcher", OpenWindow: openArchivePatcher),
        ]),
        new("Other CM Authors' Tools",
        [
            new("Bethini Pie",
                "https://www.nexusmods.com/site/mods/631",
                "Bethini Pie (Performance INI Editor) makes editing INI config files simple.\nDiscord channel: #bethini-doubleyou-etc"),
            new("CLASSIC Crash Log Scanner",
                "https://www.nexusmods.com/fallout4/mods/56255",
                "Scans Buffout crash logs for key indicators of crashes.\nYou can also post crash logs to the CM Discord for assistance.\nDiscord channel: #fo4-crash-logs"),
            new("  Vault-Tec Enhanced\nFaceGen System (VEFS)",
                "https://www.nexusmods.com/fallout4/mods/86374",
                "Automates the process of generating FaceGen models and textures with xEdit/CK.\nDiscord channel: #bethini-doubleyou-etc"),
            new("PJM's Precombine/Previs\n    Patching Scripts",
                "https://www.nexusmods.com/fallout4/mods/69978",
                "Scripts to find precombine/previs (flickering/occlusion) errors in your mod list, and optionally generate a patch to fix those problems."),
            new("DDS Texture Scanner",
                "https://www.nexusmods.com/fallout4/mods/71588",
                "Sniff out textures that might CTD your game. With BA2 support.\nDiscord channel: #nistonmakemod"),
        ]),
        new("Other Useful Tools",
        [
            new("xEdit / FO4Edit",
                "https://github.com/TES5Edit/TES5Edit#xedit",
                "Module editor and conflict detector for Bethesda games.\nFO4Edit/SSEEdit are xEdit, renamed to auto-set a game mode."),
            new("Creation Kit Platform\n   Extended (CKPE)",
                "https://www.nexusmods.com/fallout4/mods/51165",
                "Various patches and bug fixes for the Creation Kit to make life easier."),
            new("Cathedral Assets\nOptimizer (CAO)",
                "https://www.nexusmods.com/skyrimspecialedition/mods/23316",
                "An automation tool used to optimize BSAs, meshes, textures and animations."),
            new("BA2 Merging Automation\n     Tool (BMAT)",
                "https://www.nexusmods.com/fallout4/mods/89306",
                "Automated BA2 files repackaging and merging."),
            new("IceStorm's Texture Tools",
                "https://storage.icestormng-mods.de/s/QG43aExydefeGXy",
                "Converts textures from various formats into a Fallout 4 compatible format."),
            new("CapFrameX",
                "https://www.capframex.com/",
                "Benchmarking tool - Record FPS, frametime, and sensors; analyse and plot the results."),
        ]),
    ];

    private sealed record ToolColumn(string Title, IReadOnlyList<Tool> Tools);

    /// <summary>One button: a link (<paramref name="Url"/>, with an <paramref name="Info"/> tooltip) or a window opener.</summary>
    private sealed record Tool(string Text, string? Url = null, string? Info = null, Action? OpenWindow = null);
}
