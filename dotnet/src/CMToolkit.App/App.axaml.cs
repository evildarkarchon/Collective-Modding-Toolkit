using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CMToolkit.App.Parity;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.Downgrader;
using CMToolkit.Core.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMToolkit.App;

public partial class App : Application
{
    /// <summary>Loads the compiled App.axaml: theme, palette and app-wide styles.</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Starts the app. Headless test sessions have no desktop lifetime, so they skip this and build their windows
    /// themselves.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RunStartup(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The startup sequence (SHELL-4). The log file opens first and gets its start lines (SHELL-1). Error reporting
    /// goes up next, as <c>sys.stderr</c> was replaced before the checker was built. Then <see cref="StartupSequence"/>
    /// builds the app in the reference's order (SHELL-5): the Download Source lookup, resolved against the exe folder,
    /// and the settings load, after which <c>log_level</c> applies (SHELL-2); PC info; the mod manager; the game path,
    /// with its dialogs; the game INIs; the main window; the update check. Only then is a window shown: the main
    /// window, or the startup failure dialog (SHELL-13). A deliberate exit in the game path dialogs shows nothing.
    /// </summary>
    private static async void RunStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // The game path dialogs are the only windows until startup ends, and closing one must not end the app, as
        // OnLastWindowClose would.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The exe's folder, not the working directory (bug #36, fixed): the log, settings.json and patches live there.
        // If the exe's folder can't be written to (Program Files, say), they live in %LOCALAPPDATA%\CM-Toolkit instead.
        var appFolder = AppContext.BaseDirectory;
        LogFile? log = null;
        Exception? logFailure = null;
        try
        {
            var choice = AppFolder.ForThisRun();
            appFolder = choice.Folder;
            // Kept open for the life of the process, as Python's FileHandler was, so never disposed.
            log = AppStartup.OpenLog(AppFolderFiles.Log(appFolder), DateTime.Now);
            choice.Log(log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Either the fallback folder or the log couldn't be created: no log, so the startup dialog reports it.
            logFailure = ex;
        }

        // With no log there is nothing to write to; the failure is rethrown below and shown in the startup dialog.
        var runtime = new AppRuntime(log ?? (ILogger)NullLogger.Instance);
        // Installed for the life of the process, so never disposed.
        runtime.Errors.InstallGlobalHandlers();

        var result = await AppStartup.BuildAsync(runtime, async () =>
        {
            // main.py died in logging.basicConfig before anything else ran, so nothing else runs here either.
            if (logFailure is not null)
            {
                ExceptionDispatchInfo.Throw(logFailure);
            }

            // A Parity Scenario's machine when the screenshot driver asks for one, else the real one.
            var host = ScenarioHostEnvironment.FromEnvironmentVariables() ?? (IHostEnvironment)SystemHostEnvironment.Instance;
            // Likewise its canned HTTP, so a scenario run never reaches the network. Lives as long as the process.
            var scenarioHttp = ScenarioHttpHandler.FromEnvironmentVariables();
            var http = scenarioHttp ?? (HttpMessageHandler)UpdateCheck.CreateHandler();
            return await new StartupSequence(runtime, host, log!, new WindowStartupDialogs())
            {
                AppFolder = appFolder,
                CheckForUpdatesAsync = UpdateBanner.StartupHook(http),
                // The Downgrader's downloads have their own 10 s per-read timeout (T-14), or the scenario's canned patches.
                DeltaPatchHttp = scenarioHttp ?? (HttpMessageHandler)DeltaPatches.CreateHandler(),
            }.BuildMainWindowAsync();
        });

        if (result.Window is null)
        {
            // sys.exit() in the game path dialogs: no window, nothing logged, exit code 0.
            desktop.Shutdown(result.ExitCode);
            return;
        }

        if (result.Failed)
        {
            // Explicit, so the exit code is ours: closing a main window would shut down with 0 first.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            result.Window.Closed += (_, _) => desktop.Shutdown(result.ExitCode);
        }
        else
        {
            // Closing the main window ends the process at once, taking every other window with it (root.destroy()).
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        desktop.MainWindow = result.Window;
        result.Window.Show();
    }
}
