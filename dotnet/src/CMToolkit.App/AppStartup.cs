using Avalonia.Controls;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;

namespace CMToolkit.App;

/// <summary>What startup produced: the window to show, and the exit code once it closes.</summary>
/// <param name="Window">
/// The main window, unshown; or, if building failed, the startup failure dialog; or null when startup ended on purpose
/// (the reference's <c>sys.exit()</c> in the game path dialogs, GP-2/GP-3), and the process exits at once.
/// </param>
/// <param name="Failed">Whether building the app failed.</param>
public sealed record StartupResult(Window? Window, bool Failed)
{
    /// <summary>
    /// The process exit code once <see cref="Window"/> closes, or at once without one: 1 after a startup failure, as
    /// PyInstaller's; 0 otherwise, including a deliberate exit (<c>sys.exit()</c> with no code).
    /// </summary>
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
    /// Opens the log and writes its three start lines, the first thing <c>main.py</c> did (SHELL-1).
    /// </summary>
    /// <param name="path">The log file; the App passes <see cref="AppFolderFiles.Log"/> of its own folder (bug #36, fixed).</param>
    /// <param name="now">The local time for the start lines.</param>
    /// <returns>The open log, filtering at INFO until settings load (SHELL-2).</returns>
    /// <exception cref="IOException">The log can't be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The log can't be opened.</exception>
    /// <remarks>
    /// A throw here is a startup failure: the reference dies in <c>logging.basicConfig</c> with PyInstaller's dialog
    /// before any window exists, so the caller shows the startup failure dialog (SHELL-13).
    /// </remarks>
    public static LogFile OpenLog(string path, DateTime now)
    {
        var log = LogFile.Open(path);
        log.WriteStartLines(now);
        return log;
    }

    /// <summary>
    /// Runs <paramref name="buildMainWindow"/>, which does the startup Core work (off the UI thread) and builds the main
    /// window without showing it. The caller shows <see cref="StartupResult.Window"/>.
    /// </summary>
    /// <param name="runtime">The app runtime, whose log records a failure.</param>
    /// <param name="buildMainWindow">Builds the unshown main window, or returns null to end startup on purpose. Core
    /// calls inside it run with <see cref="Task.Run(Action)"/> rather than through the runner: there is no window yet to
    /// block, and a failure belongs in the startup dialog, not the Error Window.</param>
    public static async Task<StartupResult> BuildAsync(AppRuntime runtime, Func<Task<Window?>> buildMainWindow)
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
