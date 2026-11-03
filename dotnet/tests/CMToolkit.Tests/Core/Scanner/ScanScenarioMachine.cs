using CMToolkit.App.Startup;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// A materialized scanner Parity Scenario, located and with its Overview Report gathered as a Scan's Overview refresh
/// would have gathered it, so a test can take the scan's snapshot without a window.
/// </summary>
/// <param name="Machine">The materialized scenario. Its owner disposes it.</param>
/// <param name="Game">The installation found on it.</param>
/// <param name="Report">The Overview Report gathered from it.</param>
internal sealed record ScanScenarioMachine(MaterializedScenario Machine, GameInstall Game, OverviewReport Report)
{
    /// <summary>Locates the game on <paramref name="machine"/> and gathers its Overview Report, logging nowhere a test reads.</summary>
    /// <exception cref="InvalidOperationException">The scenario's game wasn't found.</exception>
    public static async Task<ScanScenarioMachine> LocateAsync(MaterializedScenario machine)
    {
        // Off the calling thread: the locator's continuations must not need it. No dialog is scripted.
        var game = await Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        return new(machine, game, OverviewReport.Gather(game, machine.Host, new CapturingLogger()));
    }

    /// <summary>
    /// This machine with its Overview Report gathered again, as the next Scan's Overview refresh would gather it: the
    /// current report is the one it replaces.
    /// </summary>
    public ScanScenarioMachine Refreshed()
        => this with { Report = OverviewReport.Gather(Game, Machine.Host, new CapturingLogger(), Report) };

    /// <summary>The scan's snapshot with only <paramref name="ticked"/> ticked, reading this machine.</summary>
    public ScanSnapshot Snapshot(IEnumerable<ScanSetting> ticked)
    {
        var set = ticked.ToHashSet();
        return ScanSettings.Snapshot(ScanSettings.All.ToDictionary(s => s, set.Contains), Game, Report, Machine.Host);
    }
}
