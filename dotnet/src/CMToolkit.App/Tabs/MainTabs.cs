using CMToolkit.App.Views;

namespace CMToolkit.App.Tabs;

/// <summary>
/// Where the main window's tabs are composed (<c>CMChecker.setup_window</c>'s <c>self.tabs</c>), in the reference's
/// order (SHELL-7). Each tab's slice replaces only its own line here with its real page, built from the session.
/// </summary>
public static class MainTabs
{
    /// <summary>The six tabs, in strip order, for <paramref name="session"/>.</summary>
    /// <param name="session">What startup produced: runtime, settings, PC info and the game installation.</param>
    public static IReadOnlyList<TabPage> Compose(AppSession session) =>
    [
        new OverviewTab(session.Runtime, session.Host, session.Pc, session.Game, session.Overview, () => DowngraderWindow.Open(session)),
        PlaceholderTab.F4se(),
        PlaceholderTab.Scanner(),
        new ToolsTab(session.Runtime, openDowngradeManager: () => DowngraderWindow.Open(session)),
        new SettingsTab(session.Settings),
        new AboutTab(session.Runtime),
    ];
}
