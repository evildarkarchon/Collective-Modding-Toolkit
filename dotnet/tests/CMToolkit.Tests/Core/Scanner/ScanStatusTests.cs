using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The statuses a scan reports, which the Scanner tab shows as they come: the scanning label's text and the progress
/// bar's percent, reported only when one of them changes. Run over the <c>scanner/data-walk</c> machine, whose Data
/// folder has twelve top-level folders, the first (BodySlide) a skip folder that is never entered.
/// </summary>
public sealed class ScanStatusTests
{
    // Today's per-folder statuses over scanner/data-walk, as the scanner-scan golden records them.
    private static readonly ScanStatus[] DataWalkFolders =
    [
        new("Scanning... 1/12: DLCCoast.esm", 8.333333333333332),
        new("Scanning... 2/12: F4SE", 16.666666666666664),
        new("Scanning... 3/12: fomod", 25),
        new("Scanning... 4/12: Interface", 33.33333333333333),
        new("Scanning... 5/12: Materials", 41.66666666666667),
        new("Scanning... 6/12: Meshes", 50),
        new("Scanning... 7/12: Music", 58.333333333333336),
        new("Scanning... 8/12: Scripts", 66.66666666666666),
        new("Scanning... 9/12: Sound", 75),
        new("Scanning... 10/12: Textures", 83.33333333333334),
        new("Scanning... 11/12: Vis", 91.66666666666666),
    ];

    [Fact]
    public async Task Race_subgraphs_alone_reports_counting_its_records_and_nothing_else()
    {
        using var machine = Scenario("scanner/data-walk");
        var snapshot = await Ticked(machine, ScanSetting.RaceSubgraphs);

        var statuses = Statuses(snapshot);

        Assert.Equal([new ScanStatus("Counting Race Subgraph records...", 1)], statuses);
    }

    [Fact]
    public async Task Every_box_counts_race_subgraph_records_then_builds_the_index_then_walks_each_folder()
    {
        using var machine = Scenario("scanner/data-walk");
        var snapshot = await Ticked(machine, [.. ScanSettings.All]);

        var statuses = Statuses(snapshot);

        Assert.Equal(
            [
                new ScanStatus("Counting Race Subgraph records...", 1),
                new ScanStatus("Building mod file index...", 1),
                .. DataWalkFolders,
            ],
            statuses);
    }

    [Fact]
    public async Task A_data_check_alone_builds_the_index_at_one_percent_then_walks_each_folder()
    {
        using var machine = Scenario("scanner/data-walk");
        var snapshot = await Ticked(machine, ScanSetting.JunkFiles);

        var statuses = Statuses(snapshot);

        Assert.Equal([new ScanStatus("Building mod file index...", 1), .. DataWalkFolders], statuses);
    }

    [Fact]
    public async Task Without_a_data_check_or_race_subgraphs_no_status_is_reported()
    {
        using var machine = Scenario("scanner/data-walk");
        var snapshot = await Ticked(machine, ScanSetting.OverviewIssues);

        Assert.Empty(Statuses(snapshot));
    }

    [Fact]
    public async Task A_second_scan_reports_its_own_statuses_whatever_the_first_scan_walked()
    {
        using var machine = Scenario("scanner/data-walk");
        var first = Statuses(await Ticked(machine, [.. ScanSettings.All]));
        Assert.Contains(new ScanStatus("Scanning... 6/12: Meshes", 50), first);

        // Fewer top-level folders the second time: its folders are placed among its own, not the first scan's.
        Directory.Delete(machine.PathOf("Game/Data/Meshes"), recursive: true);
        var second = Statuses(await Ticked(machine, [.. ScanSettings.All]));

        Assert.Equal(
            [
                // Reported again, though the first scan reported the same status: nothing about it was kept.
                new ScanStatus("Counting Race Subgraph records...", 1),
                new ScanStatus("Building mod file index...", 1),
                new ScanStatus("Scanning... 1/11: DLCCoast.esm", 9.090909090909092),
                new ScanStatus("Scanning... 2/11: F4SE", 18.181818181818183),
                new ScanStatus("Scanning... 3/11: fomod", 27.27272727272727),
                new ScanStatus("Scanning... 4/11: Interface", 36.36363636363637),
                new ScanStatus("Scanning... 5/11: Materials", 45.45454545454545),
                new ScanStatus("Scanning... 6/11: Music", 54.54545454545454),
                new ScanStatus("Scanning... 7/11: Scripts", 63.63636363636363),
                new ScanStatus("Scanning... 8/11: Sound", 72.72727272727273),
                new ScanStatus("Scanning... 9/11: Textures", 81.81818181818183),
                new ScanStatus("Scanning... 10/11: Vis", 90.9090909090909),
            ],
            second);
    }

    /// <summary>Runs one scan of <paramref name="snapshot"/> and returns the statuses it reported, in order.</summary>
    private static List<ScanStatus> Statuses(ScanSnapshot snapshot)
    {
        var statuses = new ListProgress<ScanStatus>();
        ScanChecks.Run(snapshot, statuses, NullLogger.Instance);
        return statuses.Reported;
    }

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    /// <summary>
    /// The snapshot with only <paramref name="ticked"/> ticked, with the Overview gathered as the scan's refresh would have
    /// (its enabled modules, which the Race Subgraphs count reads).
    /// </summary>
    private static async Task<ScanSnapshot> Ticked(MaterializedScenario machine, params ScanSetting[] ticked)
        => (await ScanScenarioMachine.LocateAsync(machine)).Snapshot(ticked);
}
