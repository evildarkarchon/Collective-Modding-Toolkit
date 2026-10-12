using Avalonia.Controls;

namespace CMToolkit.App.Tabs;

/// <summary>
/// A tab whose slice hasn't landed yet: it has the reference's header, class name and loading text, loads at once and
/// builds nothing. Each tab's slice replaces its placeholder's line in <see cref="MainTabs.Compose"/> with the real page.
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

    /// <summary>
    /// All six placeholders in the reference's order (SHELL-7), with their loading texts (SHELL-8): the shell on its
    /// own, for tests and the XAML previewer. The app composes its tabs in <see cref="MainTabs.Compose"/>.
    /// </summary>
    public static IReadOnlyList<TabPage> Shell() => [Overview(), F4se(), Scanner(), Tools(), Settings(), About()];

    /// <summary>The Overview tab's placeholder.</summary>
    public static PlaceholderTab Overview() => new("Overview", "OverviewTab");

    /// <summary>The F4SE tab's placeholder, with its loading text.</summary>
    public static PlaceholderTab F4se() => new("F4SE", "F4SETab", "Scanning DLLs...");

    /// <summary>The Scanner tab's placeholder.</summary>
    public static PlaceholderTab Scanner() => new("Scanner", "ScannerTab");

    /// <summary>The Tools tab's placeholder.</summary>
    public static PlaceholderTab Tools() => new("Tools", "ToolsTab");

    /// <summary>The Settings tab's placeholder.</summary>
    public static PlaceholderTab Settings() => new("Settings", "SettingsTab");

    /// <summary>The About tab's placeholder.</summary>
    public static PlaceholderTab About() => new("About", "AboutTab");

    /// <inheritdoc/>
    protected internal override Control BuildContent() => new Panel();
}
