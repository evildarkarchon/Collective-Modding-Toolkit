using Avalonia.Controls;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;

namespace CMToolkit.App;

/// <summary>What startup produced: the window to show, and the exit code once it closes.</summary>
/// <param name="Window">The main window, unshown; or, if building failed, the startup failure dialog.</param>
/// <param name="Failed">Whether building the app failed.</param>
public sealed record StartupResult(Window Window, bool Failed)
{
    /// <summary>The process exit code once <see cref="Window"/> closes: 1 after a startup failure, as PyInstaller's.</summary>
    public int ExitCode => Failed ? 1 : 0;
}

/// <summary>
/// Builds the app before anything is shown, as <c>main.py</c> did with its root window withdrawn (SHELL-4). An
/// exception while building is logged and turned into the startup failure dialog (SHELL-13); the main window, however
/// far it got, is never shown.
/// </summary>
public static class AppStartup
{
    /// <summary>
    /// Runs <paramref name="buildMainWindow"/>, which does the startup Core work (off the UI thread) and builds the main
    /// window without showing it. The caller shows <see cref="StartupResult.Window"/>.
    /// </summary>
    /// <param name="runtime">The app runtime, whose log records a failure.</param>
    /// <param name="buildMainWindow">Builds the unshown main window. Core calls inside it run with
    /// <see cref="Task.Run(Action)"/> rather than through the runner: there is no window yet to block, and a failure
    /// belongs in the startup dialog, not the Error Window.</param>
    public static async Task<StartupResult> BuildAsync(AppRuntime runtime, Func<Task<Window>> buildMainWindow)
    {
        try
        {
            return new StartupResult(await buildMainWindow(), Failed: false);
        }
        catch (Exception ex)
        {
            // The reference's StdErr Toplevel would have opened here, but the app dies before mainloop; PyInstaller's
            // dialog is all the user saw, so only the log gets the full report.
            runtime.Errors.Log(ErrorReporter.StartupHeader, ex);
            return new StartupResult(new StartupFailureDialog(ex), Failed: true);
        }
    }
}
