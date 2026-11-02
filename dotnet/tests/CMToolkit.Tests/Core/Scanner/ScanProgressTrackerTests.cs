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
        var tracker = new ScanProgressTracker(null);

        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Race Subgraph Records")));
        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Meshes")));
    }

    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void A_folder_in_the_tuple_shows_its_zero_based_index_and_the_share_done()
    {
        var tracker = new ScanProgressTracker(null);

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
        var tracker = new ScanProgressTracker(null);
        tracker.Apply(new ScanUpdate.TopLevelFolders(["Meshes"]));

        Assert.Null(tracker.Apply(new ScanUpdate.Folder("meshes")));
        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Race Subgraph Records")));
    }

    [Fact]
    public void A_status_is_passed_on_only_when_the_label_or_the_progress_changes()
    {
        var shown = new List<ScanStatus>();
        var tracker = new ScanProgressTracker(new ListProgress(shown));

        tracker.Show(new ScanStatus("Building mod file index...", 1));
        tracker.Show(new ScanStatus("Building mod file index...", 1));
        tracker.Report(new ScanUpdate.TopLevelFolders(["Meshes", "Scripts"]));
        tracker.Report(new ScanUpdate.Folder("Unknown"));
        tracker.Show(new ScanStatus("Scanning... 0/2: Meshes", 1));
        tracker.Report(new ScanUpdate.Folder("Meshes"));
        tracker.Report(new ScanUpdate.Folder("Meshes"));

        Assert.Equal(
            [
                new ScanStatus("Building mod file index...", 1),
                // The same label at another percent is a change.
                new ScanStatus("Scanning... 0/2: Meshes", 1),
                new ScanStatus("Scanning... 0/2: Meshes", 0),
            ],
            shown);
    }

    [Fact]
    [Trait("Parity", "SCN-S4")]
    public void With_an_empty_tuple_no_name_is_found()
    {
        var tracker = new ScanProgressTracker(null);
        tracker.Apply(new ScanUpdate.TopLevelFolders([]));

        Assert.Null(tracker.Apply(new ScanUpdate.Folder("Meshes")));
    }

    /// <summary>Records each status as it is reported, on the reporting thread.</summary>
    private sealed class ListProgress(List<ScanStatus> shown) : IProgress<ScanStatus>
    {
        public void Report(ScanStatus value) => shown.Add(value);
    }
}
