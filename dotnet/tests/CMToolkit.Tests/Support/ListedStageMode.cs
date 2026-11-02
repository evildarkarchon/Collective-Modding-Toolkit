using CMToolkit.App.ViewModels;

namespace CMToolkit.Tests.Support;

/// <summary>
/// Whether the app listed its last Scan in stage mode, read from the snapshot <see cref="ScanState.Ended"/> hands the
/// Scanner tab, so a recorder reports what the app used rather than working the rule out again.
/// </summary>
public sealed class ListedStageMode
{
    /// <summary>Watches <paramref name="scan"/> from now on; nothing is known until a Scan ends listing its results.</summary>
    public ListedStageMode(ScanState scan)
        => scan.Ended += (_, ended) =>
        {
            if (ended is ScanEnded.Listed listed)
            {
                UsingStage = listed.Settings.UsingStage;
            }
        };

    /// <summary>The last listed Scan's <see cref="CMToolkit.Core.Scanner.ScanSettings.UsingStage"/>, or null if none has listed yet.</summary>
    public bool? UsingStage { get; private set; }

    /// <summary>
    /// <see cref="UsingStage"/> for a recorder that needs a value: rows and a details pane exist only once a Scan has
    /// listed, so reading it earlier is a bug in the recorder.
    /// </summary>
    /// <exception cref="InvalidOperationException">No Scan has listed its results yet.</exception>
    public bool Listed => UsingStage ?? throw new InvalidOperationException("No Scan has listed its results yet.");
}
