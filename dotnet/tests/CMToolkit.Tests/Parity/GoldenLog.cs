using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// The one filter between the app log and a golden. Goldens keep the INFO, WARNING and ERROR lines, which scenarios
/// check a failure by, and leave out the DEBUG traces, which move whenever code is reorganised (ADR-0008). A window's
/// visible Log pane is UI text and doesn't go through here.
/// </summary>
public static class GoldenLog
{
    /// <summary>The entries a golden records, in order: every one above DEBUG.</summary>
    public static IEnumerable<LogEntry> WithoutDebug(this IEnumerable<LogEntry> entries)
        => entries.Where(e => IsRecorded(e.Level));

    /// <summary>The records a golden records, in order and with their exceptions: every one above DEBUG.</summary>
    public static IEnumerable<LogRecord> WithoutDebug(this IEnumerable<LogRecord> records)
        => records.Where(r => IsRecorded(r.Level));

    /// <summary>
    /// Whether a golden records a line at <paramref name="level"/>. Trace counts as DEBUG, as it does in
    /// <c>cm-toolkit.log</c> (<c>LogFile.LevelName</c>).
    /// </summary>
    private static bool IsRecorded(LogLevel level) => level >= LogLevel.Information;
}
