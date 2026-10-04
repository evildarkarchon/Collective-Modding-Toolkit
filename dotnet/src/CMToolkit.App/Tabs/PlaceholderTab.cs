using Avalonia.Controls;

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

    /// <inheritdoc/>
    protected internal override Control BuildContent() => new Panel();
}
