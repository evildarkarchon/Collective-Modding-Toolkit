using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;

namespace CMToolkit.App.Startup;

/// <summary>
/// The app's construction, in <c>main.py</c> and <c>CMChecker.__init__</c>'s order (SHELL-5), once the log is open
/// (SHELL-1): settings, PC info, mod manager, game path, game INIs, main window, the update check, and then the caller
/// shows the window. Nothing is shown before then except the game path dialogs (GP-2, GP-3).
/// </summary>
/// <param name="runtime">The app runtime the main window and the tabs use.</param>
/// <param name="host">The machine: the real one, or a Parity Scenario's for screenshots and tests.</param>
/// <param name="log">The open log file, whose level the settings set (SHELL-2).</param>
/// <param name="dialogs">The game path dialogs.</param>
public sealed class StartupSequence(AppRuntime runtime, IHostEnvironment host, LogFile log, IStartupDialogs dialogs)
{
    /// <summary>The settings file; the CWD-relative <see cref="WorkingDirectoryFiles.Settings"/> by default.</summary>
    public string SettingsPath { get; init; } = WorkingDirectoryFiles.Settings;

    /// <summary>
    /// The update check (UPD-1, SHELL-5): awaited after the main window is built and before it is shown, so a banner it
    /// adds is there from the first frame. <see cref="NoUpdateCheckAsync"/> until the update check slice (#53) fills it.
    /// </summary>
    public Func<MainWindow, AppSession, Task> CheckForUpdatesAsync { get; init; } = NoUpdateCheckAsync;

    /// <summary>
    /// Runs the sequence and builds the main window, unshown.
    /// </summary>
    /// <returns>
    /// The main window; or null where the reference calls <c>sys.exit()</c> while locating the game (GP-2, GP-3), which
    /// ends the process silently with exit code 0.
    /// </returns>
    /// <remarks>
    /// Must start on the UI thread: the dialogs and the window are created there, and the Core work runs on the thread
    /// pool in between. An exception is a startup failure (SHELL-13); <see cref="AppStartup.BuildAsync"/> turns it into
    /// the startup failure dialog.
    /// </remarks>
    public async Task<MainWindow?> BuildMainWindowAsync()
    {
        var model = await Task.Run(() => MainWindowViewModel.Load(host, log, SettingsPath));
        // CMChecker.__init__: self.pc = PCInfo() comes before GameInfo, whose constructor does the rest.
        var pc = await Task.Run(() => PcInfo.Read(host, log));
        var game = await GameInstallLocator.LocateAsync(host, dialogs);
        if (game is null)
        {
            return null;
        }

        var session = new AppSession(runtime, model, pc, game);
        var window = new MainWindow(runtime, MainTabs.Compose(session)) { DataContext = model };
        await CheckForUpdatesAsync(window, session);
        return window;
    }

    /// <summary>The update check hook's placeholder: <c>update_source</c> is never consulted yet, as if it were <c>none</c>.</summary>
    public static Task NoUpdateCheckAsync(MainWindow window, AppSession session) => Task.CompletedTask;
}
