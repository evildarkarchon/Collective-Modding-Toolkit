using CMToolkit.App.Parity;
using CMToolkit.Core;

namespace CMToolkit.App;

/// <summary>
/// The folder the app keeps <c>settings.json</c>, <c>cm-toolkit.log</c> and downloaded Delta Patches in
/// (<see cref="AppFolderFiles"/>): the exe's own folder, or <c>%LOCALAPPDATA%\CM-Toolkit</c> when that can't be written
/// to. The reference used the working directory instead, so the files scattered wherever it was started from (bug #36,
/// fixed as a Deviation, ADR-0006).
/// </summary>
public static class AppFolder
{
    /// <summary>
    /// The folder for this run: <see cref="AppContext.BaseDirectory"/>, the folder holding the exe, if a probe file can
    /// be created there, else the <c>%LOCALAPPDATA%</c> fallback (<see cref="AppFolderFiles.Choose"/>); or, when the exe
    /// runs on a Parity Scenario's machine (<see cref="ScenarioHostEnvironment.IsRequested"/>), its working directory.
    /// </summary>
    /// <exception cref="IOException">The fallback folder can't be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The fallback folder can't be created.</exception>
    /// <remarks>
    /// Scenario trees keep these files where the reference read them, in the scenario's working directory, and the
    /// screenshot and smoke scripts start the exe there. Using it keeps a scenario run on the scenario's own settings
    /// (its <c>update_source</c> keeps the run off the network) and never writes beside the published exe.
    /// </remarks>
    public static AppFolderChoice ForThisRun()
        => ScenarioHostEnvironment.IsRequested
            ? new AppFolderChoice(Environment.CurrentDirectory, UnwritableExeFolder: null)
            : AppFolderFiles.Choose(SystemHostEnvironment.Instance, AppFolderFiles.IsWritable);
}
