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
        Overview(session),
        new F4seTab(session.Runtime, session.Host, session.Game, session.Overview),
        new ScannerTab(session.Runtime, session.Game, session.Overview, session.Settings) { Host = session.Host },
        new ToolsTab(session.Runtime, openDowngradeManager: () => DowngraderWindow.Open(session), openArchivePatcher: () => ArchivePatcherWindow.Open(session)),
        new SettingsTab(session.Settings),
        new AboutTab(session.Runtime),
    ];

    /// <summary>
    /// The Overview tab, with both its utility buttons wired (OVW-B9, OVW-A6), registered as the session's Overview
    /// refresh (<c>cmc.refresh_tab(Tab.Overview)</c>), which the Archive Patcher, the Downgrader and the Scanner call.
    /// </summary>
    private static OverviewTab Overview(AppSession session)
    {
        var tab = new OverviewTab(
            session.Runtime,
            session.Host,
            session.Pc,
            session.Game,
            session.Overview,
            () => DowngraderWindow.Open(session),
            () => ArchivePatcherWindow.Open(session));
        session.OverviewRefresh = tab.RefreshAsync;
        return tab;
    }
}
