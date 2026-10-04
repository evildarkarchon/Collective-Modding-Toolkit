using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
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
            StartAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The startup sequence (SHELL-4). Error reporting goes up first, as <c>sys.stderr</c> was replaced before the
    /// checker was built. The Core startup work (the Download Source lookup, resolved against the exe folder) runs off
    /// the UI thread, as it ran at import time in the Reference Implementation, before the main window exists. Only
    /// then is a window shown: the main window, or the startup failure dialog (SHELL-13).
    /// </summary>
    private static async void StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // The Settings-and-log-file slice replaces the null logger with the cm-toolkit.log provider.
        var runtime = new AppRuntime(NullLogger.Instance);
        // Installed for the life of the process, so never disposed.
        runtime.Errors.InstallGlobalHandlers();

        var result = await AppStartup.BuildAsync(runtime, async () =>
        {
            var viewModel = await Task.Run(() => MainWindowViewModel.Load(SystemHostEnvironment.Instance));
            return new MainWindow(runtime, PlaceholderTab.Shell()) { DataContext = viewModel };
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
