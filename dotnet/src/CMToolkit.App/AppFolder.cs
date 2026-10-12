using CMToolkit.App.Parity;
using CMToolkit.Core;

namespace CMToolkit.App;

/// <summary>
/// The folder the app keeps <c>settings.json</c>, <c>cm-toolkit.log</c> and downloaded Delta Patches in
/// (<see cref="AppFolderFiles"/>): the exe's own folder. The reference used the working directory instead, so the files
/// scattered wherever it was started from (bug #36, fixed as a Deviation, ADR-0006).
/// </summary>
public static class AppFolder
{
    /// <summary>
    /// The folder for this run: <see cref="AppContext.BaseDirectory"/>, the folder holding the exe; or, when the exe
    /// runs on a Parity Scenario's machine (<see cref="ScenarioHostEnvironment.IsRequested"/>), its working directory.
    /// </summary>
    /// <remarks>
    /// Scenario trees keep these files where the reference read them, in the scenario's working directory, and the
    /// screenshot and smoke scripts start the exe there. Using it keeps a scenario run on the scenario's own settings
    /// (its <c>update_source</c> keeps the run off the network) and never writes beside the published exe.
    /// </remarks>
    public static string ForThisRun()
        => ScenarioHostEnvironment.IsRequested ? Environment.CurrentDirectory : AppContext.BaseDirectory;
}
