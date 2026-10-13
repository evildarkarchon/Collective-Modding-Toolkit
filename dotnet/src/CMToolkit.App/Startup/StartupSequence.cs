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
    private readonly string? _settingsPath;

    /// <summary>
    /// The app's own folder (<see cref="global::CMToolkit.App.AppFolder"/>), which holds <c>settings.json</c> and the
    /// Downgrader's patches (<see cref="AppSession.AppFolder"/>). The exe's folder by default; the app passes
    /// <see cref="global::CMToolkit.App.AppFolder.ForThisRun"/>, which falls back to <c>%LOCALAPPDATA%\CM-Toolkit</c>
    /// when the exe's folder can't be written to. The reference used the working directory (bug #36, fixed as a
    /// Deviation, ADR-0006); a <c>settings.json</c> still there is copied to <see cref="SettingsPath"/> before it
    /// loads, if that file doesn't exist yet (<see cref="AppFolderFiles.MigrateSettings"/>).
    /// </summary>
    public string AppFolder { get; init; } = AppContext.BaseDirectory;

    /// <summary>The settings file: <c>settings.json</c> in <see cref="AppFolder"/> unless set.</summary>
    public string SettingsPath
    {
        get => _settingsPath ?? AppFolderFiles.Settings(AppFolder);
        init => _settingsPath = value;
    }

    /// <summary>
    /// The update check (UPD-1, SHELL-5): awaited after the main window is built and before it is shown, so a banner it
    /// adds is there from the first frame. The app sets <see cref="UpdateBanner.StartupHook"/> (in <c>App.RunStartup</c>,
    /// with its HTTP stack); the default, <see cref="NoUpdateCheckAsync"/>, keeps tests that don't set it offline.
    /// </summary>
    public Func<MainWindow, AppSession, Task> CheckForUpdatesAsync { get; init; } = NoUpdateCheckAsync;

    /// <summary>
    /// What the Downgrader downloads Delta Patches through (<see cref="AppSession.DeltaPatchHttp"/>). The app sets it in
    /// <c>App.RunStartup</c>; the default, none, keeps tests that don't set it offline.
    /// </summary>
    public HttpMessageHandler? DeltaPatchHttp { get; init; }

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
        var model = await Task.Run(() =>
        {
            // Bug #36, fixed: a settings.json left in the working directory, where the reference kept it, moves into
            // the app folder the first time, before it loads. Scenario runs, whose app folder is cwd, copy nothing.
            AppFolderFiles.MigrateSettings(SettingsPath, host.CurrentDirectory, log);
            return MainWindowViewModel.Load(host, log, SettingsPath);
        });
        // CMChecker.__init__: self.pc = PCInfo() comes before GameInfo, whose constructor does the rest.
        var pc = await Task.Run(() => PcInfo.Read(host, log));
        var game = await GameInstallLocator.LocateAsync(host, dialogs);
        if (game is null)
        {
            return null;
        }

        var session = new AppSession(runtime, model, pc, game) { Host = host, AppFolder = AppFolder, DeltaPatchHttp = DeltaPatchHttp };
        var window = new MainWindow(runtime, MainTabs.Compose(session)) { DataContext = model };
        session.MainWindow = window;
        await CheckForUpdatesAsync(window, session);
        return window;
    }

    /// <summary>The update check hook's default: <c>update_source</c> is not consulted, as if it were <c>none</c>.</summary>
    public static Task NoUpdateCheckAsync(MainWindow window, AppSession session) => Task.CompletedTask;
}
