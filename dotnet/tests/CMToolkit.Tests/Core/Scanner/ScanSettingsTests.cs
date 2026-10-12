using CMToolkit.App.Startup;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Scanner;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The scan settings snapshot (<c>scan_settings.ScanSettings</c>, SCN-S2): which boxes are ticked, whether the data walk
/// runs, what it skips, and the <c>scanner_*</c> settings it saves when a box differs from the stored value (SET-7).
/// </summary>
public sealed class ScanSettingsTests
{
    private static readonly AppSettings Stored = AppSettings.Defaults(DownloadSource.GitHub);

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

        var settings = ScanSettings.Snapshot(Ticked(ticked), game);

        Assert.Equal(skipDataScan, settings.SkipDataScan);
        Assert.All(ScanSettings.All, s => Assert.Equal(ticked.Contains(s), settings[s]));
    }

    [Fact]
    [Trait("Parity", "SCN-S2")]
    public async Task Without_MO2_only_vortex_backups_and_the_fixed_folders_are_skipped()
    {
        using var machine = Scenario("scanner/overview-issues");
        var game = await Locate(machine);

        var settings = ScanSettings.Snapshot(Ticked([]), game);

        Assert.False(settings.UsingStage);
        Assert.Equal([".vortex_backup"], settings.SkipFileSuffixes);
        Assert.Equal(["bodyslide", "fo4edit", "robco_patcher", "source"], settings.SkipDirectories.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Parity", "SCN-S2")]
    public async Task With_MO2_its_skip_lists_come_first_and_are_joined_to_the_fixed_ones()
    {
        using var machine = Scenario("scanner/overview-issues-mo2");
        var game = await Locate(machine);

        var settings = ScanSettings.Snapshot(Ticked([]), game);

        Assert.True(settings.UsingStage);
        // MO2's defaults, as corrupted by CSV-parsing str(tuple) / str(set) (B-5), then .vortex_backup.
        Assert.Equal([.. game.Mo2!.SkipFileSuffixes, ".vortex_backup"], settings.SkipFileSuffixes);
        Assert.Equal(
            new[] { "bodyslide", "fo4edit", "robco_patcher", "source" }.Union(game.Mo2.SkipDirectories).Order(StringComparer.Ordinal),
            settings.SkipDirectories.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Parity", "SET-7")]
    public async Task Boxes_that_match_the_stored_settings_save_nothing()
    {
        using var machine = Scenario("scanner/overview-issues");
        var settings = ScanSettings.Snapshot(Ticked(ScanSettings.All), await Locate(machine));

        Assert.Null(settings.ChangedSettings(Stored));
    }

    [Fact]
    [Trait("Parity", "SET-7")]
    public async Task A_box_that_differs_saves_every_scanner_value_and_keeps_the_rest()
    {
        using var machine = Scenario("scanner/overview-issues");
        var settings = ScanSettings.Snapshot(Ticked([ScanSetting.OverviewIssues, ScanSetting.JunkFiles]), await Locate(machine));
        var stored = Stored with { LogLevel = SettingsLogLevel.Debug, DowngraderKeepBackups = false };

        var changed = settings.ChangedSettings(stored);

        Assert.Equal(
            stored with
            {
                ScannerErrors = false,
                ScannerWrongFormat = false,
                ScannerLoosePrevis = false,
                ScannerProblemOverrides = false,
                ScannerRaceSubgraphs = false,
            },
            changed);
    }

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    private static Dictionary<ScanSetting, bool> Ticked(IEnumerable<ScanSetting> ticked)
        => ScanSettings.All.ToDictionary(s => s, ticked.Contains);

    private static async Task<GameInstall> Locate(MaterializedScenario machine)
        => await Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            ?? throw new InvalidOperationException("The scenario's game wasn't found.");
}
