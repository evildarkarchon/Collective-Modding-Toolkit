using CMToolkit.App.Startup;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The Scan Settings and the <see cref="ScanSnapshot"/> a Scan takes of them: the seven boxes with their labels and
/// keys, which boxes the snapshot holds as ticked, whether the data walk runs, what it skips, and what it carries from
/// the Overview Report.
/// </summary>
public sealed class ScanSettingsTests
{
    [Fact]
    [Trait("Parity", "SCN-S2")]
    public void The_settings_are_the_seven_boxes_in_enum_order_with_their_reference_labels_and_keys()
    {
        Assert.Equal(
            ["Overview Issues", "Errors", "Wrong File Formats", "Loose Previs", "Junk Files", "Problem Overrides", "Race Subgraphs"],
            ScanSettings.All.Select(s => s.Label()));
        Assert.Equal(
            ["scanner_OverviewIssues", "scanner_Errors", "scanner_WrongFormat", "scanner_LoosePrevis", "scanner_JunkFiles", "scanner_ProblemOverrides", "scanner_RaceSubgraphs"],
            ScanSettings.All.Select(s => s.SettingsKey()));
        Assert.Equal("Report details of issues from Overview", ScanSetting.OverviewIssues.Tooltip());
        Assert.Equal(
            "Counts race animation subgraph records (RACE \\ SADD).\nDepending on your PC, adding too many of these may result in stutter when loading Cells.\nThis issue needs more investigation as this may be mere correlation and not causation.\n\nBase Game Count: 37\nWarning Threshold: 100",
            ScanSetting.RaceSubgraphs.Tooltip());
    }

    [Theory]
    [Trait("Parity", "SCN-S2")]
    [InlineData(new[] { ScanSetting.OverviewIssues }, true)]
    [InlineData(new[] { ScanSetting.RaceSubgraphs }, true)]
    [InlineData(new[] { ScanSetting.OverviewIssues, ScanSetting.RaceSubgraphs }, true)]
    [InlineData(new[] { ScanSetting.Errors }, false)]
    [InlineData(new[] { ScanSetting.OverviewIssues, ScanSetting.JunkFiles }, false)]
    [InlineData(new ScanSetting[0], true)]
    public async Task Only_a_ticked_data_check_runs_the_data_walk(ScanSetting[] ticked, bool skipDataScan)
    {
        using var machine = Scenario("scanner/overview-issues");
        var game = await Locate(machine);

        var snapshot = ScanSettings.Snapshot(Ticked(ticked), game, null, machine.Host);

        Assert.Equal(skipDataScan, snapshot.SkipDataScan);
        Assert.All(ScanSettings.All, s => Assert.Equal(ticked.Contains(s), snapshot[s]));
    }

    [Fact]
    [Trait("Parity", "SCN-S2")]
    public async Task Without_MO2_only_vortex_backups_and_the_fixed_folders_are_skipped()
    {
        using var machine = Scenario("scanner/overview-issues");
        var game = await Locate(machine);

        var snapshot = ScanSettings.Snapshot(Ticked([]), game, null, machine.Host);

        Assert.False(snapshot.UsingStage);
        Assert.Equal([".vortex_backup"], snapshot.SkipFileSuffixes);
        Assert.Equal(["bodyslide", "fo4edit", "robco_patcher", "source"], snapshot.SkipDirectories.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Parity", "SCN-S2")]
    [Trait("Parity", "B-5")]
    [Deviation(24, "An INI without skip_file_suffixes / skip_directories gives MO2's defaults, .mohidden and .git, not corrupted ones.")]
    public async Task With_MO2_its_skip_lists_come_first_and_are_joined_to_the_fixed_ones()
    {
        using var machine = Scenario("scanner/overview-issues-mo2");
        var game = await Locate(machine);

        var snapshot = ScanSettings.Snapshot(Ticked([]), game, null, machine.Host);

        Assert.True(snapshot.UsingStage);
        // The INI sets neither key, so MO2's defaults apply (bug #24, fixed: the reference CSV-parsed str(tuple) /
        // str(set) into garbage, B-5), then .vortex_backup.
        Assert.Equal([".mohidden", ".vortex_backup"], snapshot.SkipFileSuffixes);
        Assert.Equal(
            [".git", "bodyslide", "fo4edit", "robco_patcher", "source"],
            snapshot.SkipDirectories.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Without_an_overview_report_the_snapshot_carries_no_overview_problems()
    {
        using var machine = Scenario("scanner/overview-issues");

        var snapshot = ScanSettings.Snapshot(Ticked(ScanSettings.All), await Locate(machine), null, machine.Host);

        Assert.Empty(snapshot.OverviewProblems);
    }

    [Fact]
    public async Task The_snapshot_carries_the_overview_reports_problems_in_their_list_order()
    {
        using var machine = Scenario("scanner/overview-issues");
        var game = await Locate(machine);
        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());
        // More than one problem, or the order isn't being checked at all.
        Assert.True(report.Problems.Count > 1, $"The scenario's report has {report.Problems.Count} problems.");

        var snapshot = ScanSettings.Snapshot(Ticked(ScanSettings.All), game, report, machine.Host);

        Assert.Equal(report.Problems, snapshot.OverviewProblems);
    }

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    private static Dictionary<ScanSetting, bool> Ticked(IEnumerable<ScanSetting> ticked)
        => ScanSettings.All.ToDictionary(s => s, ticked.Contains);

    private static async Task<GameInstall> Locate(MaterializedScenario machine)
        => await Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            ?? throw new InvalidOperationException("The scenario's game wasn't found.");
}
