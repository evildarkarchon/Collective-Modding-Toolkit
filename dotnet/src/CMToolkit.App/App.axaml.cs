using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
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
    /// goes up next, as <c>sys.stderr</c> was replaced before the checker was built. The Core startup work (the
    /// Download Source lookup, resolved against the exe folder, then the settings load, after which <c>log_level</c>
    /// applies; SHELL-2) runs off the UI thread, before the main window exists. Only then is a window shown: the main
    /// window, or the startup failure dialog (SHELL-13).
    /// </summary>
    private static async void RunStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        LogFile? log = null;
        Exception? logFailure = null;
        try
        {
            // Kept open for the life of the process, as Python's FileHandler was, so never disposed.
            log = AppStartup.OpenLog(WorkingDirectoryFiles.Log, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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

            var viewModel = await Task.Run(() => MainWindowViewModel.Load(SystemHostEnvironment.Instance, log!));
            return new MainWindow(runtime, PlaceholderTab.Shell(runtime, viewModel.Settings)) { DataContext = viewModel };
        });

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
