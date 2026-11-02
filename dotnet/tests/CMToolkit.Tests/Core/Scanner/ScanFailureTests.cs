using CMToolkit.App.Startup;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The two failures the scan worker expects while building the mod file index (SCN-S6): MO2's selected profile has no
/// <c>modlist.txt</c>, or MO2 has no selected profile. The reference's worker raised and its scan stuck (B-3); the port
/// turns each into a problem row and keeps what it found before (bug #22). And one it doesn't expect, a
/// <c>modlist.txt</c> that isn't UTF-8, which stops the scan early with what it found and a stopped-early row.
/// </summary>
public sealed class ScanFailureTests
{
    [Fact]
    [Trait("Parity", "B-3"), Deviation(22, "A missing modlist.txt is a File Not Found problem on its path, listed after the problems the worker found before it, instead of an exception.")]
    public async Task A_missing_modlist_is_a_file_not_found_problem_after_the_race_subgraph_count()
    {
        using var machine = Scenario("scanner/mo2-no-modlist");
        // 101 SADD records in an enabled module: over the threshold, so the count found before the index is a problem.
        WriteSaddRecords(machine, 101);
        var settings = await AllTicked(machine);

        var outcome = Scan.Run(settings);

        // A problem row, not a failure: the scan didn't stop early.
        Assert.Null(outcome.Error);
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

        var outcome = Scan.Run(settings);

        // A problem row, not a failure: the scan didn't stop early.
        Assert.Null(outcome.Error);
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

    [Fact]
    public async Task A_modlist_that_isnt_utf8_stops_the_scan_early_after_the_race_subgraph_count()
    {
        // Real bad input, not an injected fault: MO2's modlist.txt holds bytes that aren't UTF-8, so the index build
        // throws after the Race Subgraphs count has found its problem.
        using var machine = Scenario("scanner/mo2-no-modlist");
        WriteSaddRecords(machine, 101);
        var modlist = machine.PathOf(@"LocalAppData\ModOrganizer\Fallout 4\profiles\Default\modlist.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(modlist)!);
        File.WriteAllBytes(modlist, [0xFF, 0xFE, (byte)'+', (byte)'A', (byte)'\r', (byte)'\n']);
        var settings = await AllTicked(machine);
        var log = new CapturingLogger();

        var outcome = Scan.Run(settings, null, log);

        Assert.IsType<System.Text.DecoderFallbackException>(outcome.Error);
        Assert.Null(outcome.Index);
        Assert.Equal([ScanProblemTypes.RaceSubgraphRecordCount, "Incomplete Scan"], outcome.Problems.Select(p => p.Kind));
        Assert.Equal("101 SADD Records from 1 modules", outcome.Problems[0].ItemText);

        var row = outcome.Problems[1];
        Assert.Equal("Scan stopped early", row.ItemText);
        Assert.Equal(
            "The scan stopped at an unexpected error, so its results are incomplete. The error is shown in the error window and saved to the log.",
            row.Summary);
        Assert.Equal("Scan again. If it stops again, share the error from the log with the Collective Modding Discord.", row.Solution);
        Assert.Null(row.OpenablePath);
        Assert.Equal("", row.Mod);
        // The row writes no log line of its own: the only line is the Race Subgraphs problem's, so the error's own log
        // entry, which the App writes when it reports the error, is the only record of the failure.
        var line = Assert.Single(log.Messages);
        Assert.Contains("Problem: " + ScanProblemTypes.RaceSubgraphRecordCount, line, StringComparison.Ordinal);
    }

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    /// <summary>
    /// Fills <c>Game/Data/BadMagic.esp</c>, an enabled module in the MO2 scanner scenarios, with <paramref name="count"/>
    /// SADD records, so a count over the threshold is a problem found before the index is built.
    /// </summary>
    private static void WriteSaddRecords(MaterializedScenario machine, int count)
        => File.WriteAllBytes(machine.PathOf("Game/Data/BadMagic.esp"), [.. Enumerable.Repeat<byte[]>([0x00, 0x53, 0x41, 0x44, 0x44], count).SelectMany(b => b)]);

    /// <summary>Every box ticked, with the Overview gathered as the scan's refresh would have (its enabled modules).</summary>
    private static async Task<ScanSettings> AllTicked(MaterializedScenario machine)
    {
        var game = await Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());
        return ScanSettings.Snapshot(ScanSettings.All.ToDictionary(s => s, _ => true), game, report, machine.Host);
    }
}
