using CMToolkit.Core.Problems;

namespace CMToolkit.Core.Scanner;

/// <summary>
/// What <see cref="ScanChecks"/> hands back: everything the Scanner tab lists, the mod file index, and the error that
/// stopped the scan early, if one did.
/// </summary>
public sealed class ScanOutcome
{
    private const string StoppedEarlyItem = "Scan stopped early";

    private const string StoppedEarlySummary =
        "The scan stopped at an unexpected error, so its results are incomplete. The error is shown in the error window and saved to the log.";

    private const string StoppedEarlySolution =
        "Scan again. If it stops again, share the error from the log with the Collective Modding Discord.";

    /// <summary>
    /// The outcome of a scan that found <paramref name="problems"/>, with the groups of the results tree made from them
    /// and <paramref name="index"/>, so the groups always match the list.
    /// </summary>
    /// <param name="problems">
    /// The full ordered problem list: the seed, then the checks' problems in the order found, then the
    /// <see cref="StoppedEarlyRow"/> when the scan stopped early.
    /// </param>
    /// <param name="index">The mod file index, or null when none was built.</param>
    /// <param name="error">The error that stopped the scan early, or null for a scan that finished.</param>
    public ScanOutcome(IReadOnlyList<Problem> problems, ModFiles? index, Exception? error = null)
    {
        ArgumentNullException.ThrowIfNull(problems);
        Problems = problems;
        Index = index;
        Error = error;
        // Grouping is a pure function of the list and the index, so the outcome is the one place it is called.
        Groups = ScanResults.Group(problems, index);
    }

    /// <summary>No problems and no index.</summary>
    public static ScanOutcome Empty { get; } = new([], null);

    /// <summary>
    /// Everything the scan lists, in order: the Overview Report's problems when Overview Issues is ticked, then the
    /// checks' problems in the order found, then the <see cref="StoppedEarlyRow"/> when the scan stopped early.
    /// </summary>
    public IReadOnlyList<Problem> Problems { get; }

    /// <summary>
    /// The results tree's groups (<see cref="ScanResults.Group"/>): one per problem type, in the order the types first
    /// appear in <see cref="Problems"/>, each sorted by type + mod, with the Overview problems' mods looked up in the
    /// <see cref="Index"/>.
    /// </summary>
    public IReadOnlyList<ScanResultGroup> Groups { get; }

    /// <summary>
    /// The mod file index, which only a data walk in MO2 stage mode builds; null otherwise, and always null when the scan
    /// stopped early. <see cref="Groups"/> looks the Overview problems' mods up in it.
    /// </summary>
    public ModFiles? Index { get; }

    /// <summary>
    /// The unexpected error that stopped the scan early, as it was thrown; null for a scan that finished. The scan doesn't
    /// log or report it: its caller does, once.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// The row that ends a scan that stopped early: a simple problem in its own group,
    /// <see cref="ScanProblemTypes.IncompleteScan"/>, so it isn't mistaken for a problem with the user's setup.
    /// </summary>
    public static Problem StoppedEarlyRow { get; } =
        Problem.Simple(StoppedEarlyItem, ScanProblemTypes.IncompleteScan, StoppedEarlySummary, StoppedEarlySolution);

    /// <summary>
    /// The outcome of a scan that <paramref name="error"/> stopped early: the problems found before it, then the
    /// <see cref="StoppedEarlyRow"/>, with no mod file index.
    /// </summary>
    /// <param name="found">
    /// The problems listed before the error, in order: the seed, then what the checks found. Copied, so the caller may
    /// reuse it.
    /// </param>
    /// <param name="error">The error, kept as thrown so the Error Window shows it and not a wrapper.</param>
    public static ScanOutcome StoppedEarly(IEnumerable<Problem> found, Exception error)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(error);
        // Added directly, not through the Overview's problem-logging helper: the error's own log entry, with its
        // traceback, is the only record of the failure.
        return new([.. found, StoppedEarlyRow], null, error);
    }
}
