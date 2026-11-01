using CMToolkit.App.Views;

namespace CMToolkit.App.Tabs;

/// <summary>
/// Where the main window's tabs are composed (<c>CMChecker.setup_window</c>'s <c>self.tabs</c>), in the reference's
/// order (SHELL-7), each a real page built from the session.
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
        new SettingsTab(session.Runtime, session.Settings),
        new AboutTab(session.Runtime),
    ];

    /// <summary>
    /// The Overview tab over the session's Overview Report module (<see cref="AppSession.Overview"/>), with both its
    /// utility buttons wired to open the Downgrader and the Archive Patcher.
    /// </summary>
    private static OverviewTab Overview(AppSession session) => new(
        session.Runtime,
        session.Pc,
        session.Game,
        session.Overview,
        () => DowngraderWindow.Open(session),
        () => ArchivePatcherWindow.Open(session));
}
