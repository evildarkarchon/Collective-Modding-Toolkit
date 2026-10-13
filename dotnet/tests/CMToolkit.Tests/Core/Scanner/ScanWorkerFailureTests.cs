using CMToolkit.App.Startup;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The two failures the scan worker expects while building the mod file index (SCN-S6): MO2's selected profile has no
/// <c>modlist.txt</c>, or MO2 has no selected profile. The reference's worker raised and its scan stuck (B-3); the port
/// turns each into a problem row and keeps what it found before (bug #22).
/// </summary>
public sealed class ScanWorkerFailureTests
{
    [Fact]
    [Trait("Parity", "B-3"), Deviation(22, "A missing modlist.txt is a File Not Found problem on its path, listed after the problems the worker found before it, instead of an exception.")]
    public async Task A_missing_modlist_is_a_file_not_found_problem_after_the_race_subgraph_count()
    {
        using var machine = Scenario("scanner/mo2-no-modlist");
        // 101 SADD records in an enabled module: over the threshold, so the count found before the index is a problem.
        File.WriteAllBytes(machine.PathOf("Game/Data/BadMagic.esp"), [.. Enumerable.Repeat<byte[]>([0x00, 0x53, 0x41, 0x44, 0x44], 101).SelectMany(b => b)]);
        var settings = await AllTicked(machine);

        var outcome = ScanWorker.Run(settings);

        Assert.Null(outcome.Index);
        Assert.Equal(2, outcome.Problems.Count);
        Assert.Equal(ScanProblemTypes.RaceSubgraphRecordCount, outcome.Problems[0].Kind);
        Assert.Equal("101 SADD Records from 1 modules", outcome.Problems[0].ItemText);

        var modlist = machine.PathOf(@"LocalAppData\ModOrganizer\Fallout 4\profiles\Default\modlist.txt");
        var problem = outcome.Problems[1];
        Assert.Equal("File Not Found", problem.Kind);
        Assert.Equal("modlist.txt", problem.ItemText);
        Assert.Equal(modlist, problem.OpenablePath?.ToString());
        Assert.Equal(modlist, problem.RelativePath);
        Assert.Equal("", problem.Mod);
        Assert.Equal(
            "Mod Organizer 2's selected profile has no modlist.txt, so the Scanner can't tell which mods are enabled.\nThe data checks were skipped.",
            problem.Summary);
        Assert.Equal(
            "Open Mod Organizer 2 and select a working profile so it writes modlist.txt, then relaunch the toolkit from MO2 and scan again.",
            problem.Solution);
    }

    [Fact]
    [Trait("Parity", "B-3"), Deviation(22, "An empty MO2 selected_profile is a Missing MO2 Settings problem on ModOrganizer.ini, with a solution, instead of an exception.")]
    public async Task An_empty_selected_profile_is_a_missing_mo2_settings_problem_on_the_mo2_ini()
    {
        using var machine = Scenario("scanner/mo2-no-profile");
        var settings = await AllTicked(machine);

        var outcome = ScanWorker.Run(settings);

        Assert.Null(outcome.Index);
        var problem = Assert.Single(outcome.Problems);
        var ini = machine.PathOf(@"LocalAppData\ModOrganizer\Fallout 4\ModOrganizer.ini");
        Assert.Equal("Missing MO2 Settings", problem.Kind);
        Assert.Equal("ModOrganizer.ini", problem.ItemText);
        Assert.Equal(ini, problem.OpenablePath?.ToString());
        Assert.Equal(ini, problem.RelativePath);
        Assert.Equal("", problem.Mod);
        Assert.Equal(
            "Mod Organizer 2 has no selected profile (selected_profile in ModOrganizer.ini is empty), so the Scanner can't tell which mods are enabled.\nThe data checks were skipped.",
            problem.Summary);
        Assert.Equal("Open Mod Organizer 2 and select a profile, then relaunch the toolkit from MO2 and scan again.", problem.Solution);
    }

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    /// <summary>Every box ticked, with the Overview gathered as the scan's refresh would have (its enabled modules).</summary>
    private static async Task<ScanSettings> AllTicked(MaterializedScenario machine)
    {
        var game = await Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());
        return ScanSettings.Snapshot(ScanSettings.All.ToDictionary(s => s, _ => true), game, report, machine.Host);
    }
}
