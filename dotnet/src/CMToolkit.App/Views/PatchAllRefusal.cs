using Avalonia.Controls;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Views;

/// <summary>
/// Patch All's refusal while a Scan is running, shared by the Downgrader and the Archive Patcher: one INFO line in the
/// app log, then a warning over the window. Each window decides when to ask whether a Scan is running; this is only what
/// it does when the answer is yes.
/// </summary>
internal static class PatchAllRefusal
{
    /// <summary>What Patch All's warning says when it refuses because a Scan is running.</summary>
    public const string ScanRunningWarning = "A Scan is running. Patch All can start once it ends.";

    /// <summary>
    /// Logs <c>{logPrefix} : Patch All : A Scan is running, refused</c> at INFO, then shows
    /// <see cref="ScanRunningWarning"/> over <paramref name="window"/> under its title. It is not a failure: nothing
    /// reaches the Error Window, and nothing else is changed. UI thread only.
    /// </summary>
    /// <param name="window">The patch window the warning is modal over; its title is the warning's title.</param>
    /// <param name="runtime">The runtime whose app log takes the INFO line.</param>
    /// <param name="logPrefix">The window's name in the log line: <c>Downgrader</c> or <c>Archive Patcher</c>.</param>
    /// <returns>A task that completes once the warning is dismissed.</returns>
    public static async Task ShowAsync(Window window, AppRuntime runtime, string logPrefix)
    {
        // Logged before the warning is shown, so the line is in the log even while the warning is still up.
        runtime.Logger.Write(LogLevel.Information, $"{logPrefix} : Patch All : A Scan is running, refused");
        await MessageBox.ShowWarningAsync(window, window.Title!, ScanRunningWarning);
    }
}
