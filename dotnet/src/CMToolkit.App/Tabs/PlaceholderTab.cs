using Avalonia.Controls;
using CMToolkit.App.Runtime;

namespace CMToolkit.App.Tabs;

/// <summary>
/// A tab whose slice hasn't landed yet: it has the reference's header, class name and loading text, loads at once and
/// builds nothing. Each tab's slice replaces its placeholder with the real page.
/// </summary>
public sealed class PlaceholderTab : TabPage
{
    private readonly string? _loadingText;

    private PlaceholderTab(string header, string logName, string? loadingText = null)
        : base(header, logName)
    {
        _loadingText = loadingText;
    }

    /// <inheritdoc/>
    public override string? LoadingText => _loadingText;

    /// <summary>The six tabs in the reference's order (SHELL-7), with their loading texts (SHELL-8).</summary>
    public static IReadOnlyList<TabPage> Shell() =>
    [
        new PlaceholderTab("Overview", "OverviewTab"),
        new PlaceholderTab("F4SE", "F4SETab", "Scanning DLLs..."),
        new PlaceholderTab("Scanner", "ScannerTab"),
        new PlaceholderTab("Tools", "ToolsTab"),
        new PlaceholderTab("Settings", "SettingsTab"),
        new PlaceholderTab("About", "AboutTab"),
    ];

    /// <summary>
    /// The six tabs as the app runs them: <see cref="Shell()"/> with every tab whose slice has landed swapped in, in
    /// the same place. Each tab slice adds its arm here.
    /// </summary>
    /// <param name="runtime">The app runtime the real tabs share.</param>
    /// <param name="settings">The settings in effect, which the Settings tab shows and saves.</param>
    public static IReadOnlyList<TabPage> Shell(AppRuntime runtime, SettingsStore settings) =>
    [
        .. Shell().Select(page => page.LogName switch
        {
            "ToolsTab" => new ToolsTab(runtime),
            "SettingsTab" => new SettingsTab(settings),
            "AboutTab" => new AboutTab(runtime),
            _ => page,
        }),
    ];

    /// <inheritdoc/>
    protected internal override Control BuildContent() => new Panel();
}
