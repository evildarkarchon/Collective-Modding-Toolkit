using CMToolkit.Core.Scanner;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// <c>check_scan_progress</c>'s queue protocol (SCN-S4). The reference's worker never sent the folder tuple (bug #89),
/// so its recordings only see names ignored; these pin the protocol the port's fix relies on, worked out by hand from
/// the reference (the <c>scanner/data-walk*</c> port goldens show it end to end).
/// </summary>
public sealed class ScanProgressTrackerTests
{
    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void Before_any_folder_tuple_every_name_is_ignored()
    {
        var tracker = new ScanProgressTracker();

        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Race Subgraph Records")));
        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Meshes")));
    }

    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void A_folder_in_the_tuple_shows_its_zero_based_index_and_the_share_done()
    {
        var tracker = new ScanProgressTracker();

        Assert.Null(tracker.Apply(new ScanUpdate.TopLevelFolders(["F4SE", "Meshes", "Textures"])));
        Assert.Equal(new ScanStatus("Scanning... 0/3: F4SE", 0.0), tracker.Apply(new ScanUpdate.Folder("F4SE")));
        // (1 / 3) * 100 in doubles, as Python computes it.
        Assert.Equal(new ScanStatus("Scanning... 1/3: Meshes", 33.33333333333333), tracker.Apply(new ScanUpdate.Folder("Meshes")));
        Assert.Equal(new ScanStatus("Scanning... 2/3: Textures", 66.66666666666666), tracker.Apply(new ScanUpdate.Folder("Textures")));
    }

    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void Names_are_matched_exactly_and_unknown_ones_are_ignored()
    {
        var tracker = new ScanProgressTracker();
        tracker.Apply(new ScanUpdate.TopLevelFolders(["Meshes"]));

        Assert.Null(tracker.Apply(new ScanUpdate.Folder("meshes")));
        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Race Subgraph Records")));
    }

    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void The_last_tuple_carries_over_to_the_next_scan()
    {
        var tracker = new ScanProgressTracker();
        tracker.Apply(new ScanUpdate.TopLevelFolders(["Meshes", "Scripts"]));

        // scan_folders lives on the tab: a later scan's names are placed in the earlier tuple until a new one comes.
        Assert.Equal(new ScanStatus("Scanning... 1/2: Scripts", 50.0), tracker.Apply(new ScanUpdate.Folder("Scripts")));
    }

    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void With_an_empty_tuple_no_name_is_found()
    {
        var tracker = new ScanProgressTracker();
        tracker.Apply(new ScanUpdate.TopLevelFolders([]));

        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Meshes")));
    }
}
