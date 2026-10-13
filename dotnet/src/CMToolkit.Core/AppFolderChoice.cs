using Microsoft.Extensions.Logging;

namespace CMToolkit.Core;

/// <summary>
/// Where this run keeps <c>settings.json</c>, <c>cm-toolkit.log</c> and downloaded Delta Patches
/// (<see cref="AppFolderFiles.Choose"/>; bug #36, fixed as a Deviation, ADR-0006).
/// </summary>
/// <param name="Folder">The app folder.</param>
/// <param name="UnwritableExeFolder">
/// The exe's folder when it couldn't be written to and <paramref name="Folder"/> is the <c>%LOCALAPPDATA%</c> fallback;
/// null when <paramref name="Folder"/> is the exe's folder (or, on a Parity Scenario's machine, its working directory).
/// </param>
public sealed record AppFolderChoice(string Folder, string? UnwritableExeFolder)
{
    /// <summary>
    /// Logs the fallback, if one was taken, as a WARNING naming both folders, so a user looking for their settings beside
    /// the exe learns where they went. Logs nothing otherwise.
    /// </summary>
    /// <param name="log">The app log, which is already open in <see cref="Folder"/>.</param>
    public void Log(ILogger log)
    {
        if (UnwritableExeFolder is not null)
        {
            // Rendered here and written as is (PlainLog): a brace in a path would break a message template.
            log.Write(
                LogLevel.Warning,
                $"App folder {UnwritableExeFolder} is not writable; settings.json, cm-toolkit.log and Delta Patches are kept in {Folder} instead.");
        }
    }
}
