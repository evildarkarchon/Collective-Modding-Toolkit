using CMToolkit.Core.Problems;

namespace CMToolkit.Core.Scanner;

/// <summary>What the scan worker hands back: its problems, appended to the seeded results, and the mod file index.</summary>
/// <param name="Problems">The worker's problems, in the order it found them.</param>
/// <param name="Index">
/// The mod file index (SCN-S6), which only a data walk in MO2 stage mode builds; null otherwise. SCN-R2 looks the
/// Overview problems' mods up in it.
/// </param>
public sealed record ScanOutcome(IReadOnlyList<Problem> Problems, ModFiles? Index)
{
    /// <summary>No problems and no index.</summary>
    public static ScanOutcome Empty { get; } = new([], null);
}

/// <summary>
/// <c>ScannerTab.scan_data_files</c>: the work the reference ran on its scan thread, which the App runs as an
/// interactive operation (THR-1, ADR-0003).
/// </summary>
public static class ScanWorker
{
    /// <summary>
    /// Runs the scan's worker part for <paramref name="settings"/>. Without a Data folder it finds nothing.
    /// </summary>
    /// <remarks>
    /// The race subgraph count (SCN-S5), the mod file index (SCN-S6) and the data walk's checks (SCN-C1..SCN-C9) are
    /// the data-walk slice's (#58); until it lands, every scan's worker finds nothing, so a scan lists the Overview
    /// issues alone.
    /// </remarks>
    /// <param name="settings">The scan's settings snapshot.</param>
    public static ScanOutcome Run(ScanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ScanOutcome.Empty;
    }
}
