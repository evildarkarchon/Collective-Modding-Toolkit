using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.Core.Settings;
using CMToolkit.Core.Updates;

namespace CMToolkit.App.Views;

/// <summary>
/// The update banner (<c>CMChecker.check_for_updates</c>, UPD-2): a pale green strip in the main window's row 0 above
/// the tabs (SHELL-7) with the update icon and <c>An update is available:</c>, then a bold underlined link per source
/// that has a newer version, <c>v{version} (NexusMods)</c> and/or <c>v{version} (GitHub)</c>, separated by
/// <c> / </c>. A click opens <c>NEXUS_LINK</c> / <c>GITHUB_LINK</c> in the browser (NET-4). The version comes without
/// the source's own leading <c>v</c>, so a <c>v0.7.0</c> tag reads <c>v0.7.0</c>, where the reference showed
/// <c>vv0.7.0</c> (bug #83, fixed per ADR-0006; see <see cref="UpdateCheck"/>).
/// </summary>
public static class UpdateBanner
{
    /// <summary>
    /// The links' cursor (Tk's <c>hand2</c>). One shared instance, since a <see cref="Cursor"/> doesn't say which
    /// standard cursor it is.
    /// </summary>
    public static Cursor LinkCursor { get; } = new(StandardCursorType.Hand);

    /// <summary>
    /// <see cref="Startup.StartupSequence.CheckForUpdatesAsync"/> for the app: <see cref="CheckAsync"/> with the
    /// loaded settings' <c>update_source</c> and the runtime from the session.
    /// </summary>
    /// <param name="http">The HTTP stack; the app's lives as long as the process, so it is never disposed.</param>
    /// <param name="runningVersion">
    /// The version a release must be newer than; the app's own when null (see <see cref="UpdateCheck"/>'s constructor).
    /// </param>
    public static Func<MainWindow, AppSession, Task> StartupHook(HttpMessageHandler http, string? runningVersion = null) =>
        (window, session) => CheckAsync(window, session.Runtime, session.Settings.Current.UpdateSource, http, runningVersion);

    /// <summary>
    /// The startup hook (UPD-1, SHELL-5): runs the update check for <paramref name="source"/> and, if either source
    /// has a newer version, puts the banner in <paramref name="window"/>'s row 0. Awaited before the window first
    /// shows, as the reference checked synchronously inside <c>CMChecker.__init__</c>.
    /// </summary>
    /// <param name="window">The main window, built and not yet shown.</param>
    /// <param name="runtime">The app runtime: the log the check writes to and the browser the links open in.</param>
    /// <param name="source">The <c>update_source</c> setting.</param>
    /// <param name="http">The HTTP stack: the real network, or a Parity Scenario's canned responses.</param>
    /// <param name="runningVersion">
    /// The version a release must be newer than; the app's own when null (see <see cref="UpdateCheck"/>'s constructor).
    /// </param>
    /// <returns>What the check found.</returns>
    /// <remarks>
    /// Start on the UI thread; the check itself runs on the thread pool. A failed check is logged and is no update,
    /// including a response the reference didn't survive and failed startup on (bug #81, fixed per ADR-0006).
    /// </remarks>
    public static async Task<UpdateCheckResult> CheckAsync(
        MainWindow window, AppRuntime runtime, UpdateSource source, HttpMessageHandler http, string? runningVersion = null)
    {
        var check = new UpdateCheck(http, runtime.Logger, runningVersion);
        var result = await Task.Run(() => check.RunAsync(source));
        window.UpdateBanner = Build(result, runtime.Browser);
        return result;
    }

    /// <summary>The banner for <paramref name="result"/>, or null when neither source is newer (no frame at all).</summary>
    /// <param name="result">What the update check found.</param>
    /// <param name="browser">Where the links open.</param>
    public static TkGrid? Build(UpdateCheckResult result, IBrowser browser)
    {
        if (!result.UpdateAvailable)
        {
            return null;
        }

        var cells = new List<Border>();

        // compound=LEFT, anchor=E, padding=5, FONT: the icon and the text, right-aligned in column 0.
        var icon = new AssetImage("images/update-24.png") { VerticalAlignment = VerticalAlignment.Center };
        var heading = Text("An update is available:", "CmtUpdateText", link: false);
        heading.VerticalAlignment = VerticalAlignment.Center;
        cells.Add(Cell(
            // ttk's label element puts its -space (4 px by default) between a compound image and its text.
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { icon, heading } },
            padding: 5,
            HorizontalAlignment.Right));

        if (result.NexusVersion is { } nexus)
        {
            cells.Add(Link($"v{nexus} (NexusMods)", "Open Nexus Mods", AboutTab.NexusLink, browser));
        }

        if (result.NexusVersion is not null && result.GitHubVersion is not null)
        {
            cells.Add(Cell(Text(" / ", "CmtUpdateText", link: false), padding: 0, HorizontalAlignment.Left));
        }

        if (result.GitHubVersion is { } github)
        {
            cells.Add(Link($"v{github} (GitHub)", "Open GitHub", AboutTab.GitHubLink, browser));
        }

        // grid_columnconfigure(0, weight=1) and (last, weight=1): the spare width is split between the heading's
        // column and the last link's, so the group sits near the middle; row 0 has weight 1 too.
        var weights = new int[cells.Count];
        weights[0] = 1;
        weights[^1] = 1;
        var grid = new TkGrid { ColumnWeights = weights, RowWeights = [1] };
        grid.Bind(Panel.BackgroundProperty, grid.GetResourceObservable("CmtUpdateBanner"));
        for (var i = 0; i < cells.Count; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        return grid;
    }

    /// <summary>A link label: bold underlined SteelBlue4, the hand cursor, a tooltip, and a click that opens the URL.</summary>
    private static Border Link(string text, string tooltip, string url, IBrowser browser)
    {
        var cell = Cell(Text(text, "CmtUpdateLink", link: true), padding: 0, HorizontalAlignment.Left);
        cell.Cursor = LinkCursor;
        ToolTip.SetTip(cell, tooltip);
        // bind("<Button-1>"): a press opens the link, with no release needed.
        cell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
            {
                browser.Open(url);
            }
        };
        return cell;
    }

    /// <summary>
    /// One <c>ttk.Label</c>: its own pale green background and padding, placed by <c>sticky</c> in its column and
    /// centred vertically in the row.
    /// </summary>
    private static Border Cell(Control content, int padding, HorizontalAlignment sticky)
    {
        var cell = new Border
        {
            Child = content,
            Padding = new Thickness(padding),
            HorizontalAlignment = sticky,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cell.Bind(Border.BackgroundProperty, cell.GetResourceObservable("CmtUpdateBanner"));
        return cell;
    }

    /// <summary>Text at <c>FONT</c> (the app-wide normal size) in a palette colour; bold and underlined for a link.</summary>
    private static TextBlock Text(string text, string colourKey, bool link)
    {
        var block = new TextBlock { Text = text };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(colourKey));
        if (link)
        {
            block.FontWeight = FontWeight.Bold;
            block.TextDecorations = TextDecorations.Underline;
        }

        return block;
    }
}
