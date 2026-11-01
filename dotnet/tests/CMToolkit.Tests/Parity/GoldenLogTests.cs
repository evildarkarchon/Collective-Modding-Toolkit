using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// The filter every golden's app-log projection goes through: goldens keep the INFO, WARNING and ERROR lines and leave
/// the DEBUG ones out (ADR-0008).
/// </summary>
public sealed class GoldenLogTests
{
    [Fact]
    public void Debug_entries_are_left_out_and_the_rest_keep_their_order()
    {
        LogEntry[] logged =
        [
            new(LogLevel.Debug, "a"),
            new(LogLevel.Information, "b"),
            new(LogLevel.Trace, "c"),
            new(LogLevel.Warning, "d"),
            new(LogLevel.Debug, "e"),
            new(LogLevel.Error, "f"),
            new(LogLevel.Information, "g"),
        ];

        Assert.Equal(["b", "d", "f", "g"], logged.WithoutDebug().Select(e => e.Message));
    }

    [Fact]
    public void Debug_records_are_left_out_and_the_rest_keep_their_order_and_exceptions()
    {
        var failure = new InvalidOperationException();
        LogRecord[] logged =
        [
            new(LogLevel.Error, "a", failure),
            new(LogLevel.Debug, "b", null),
            new(LogLevel.Warning, "c", null),
            new(LogLevel.Debug, "d", failure),
            new(LogLevel.Information, "e", null),
        ];

        Assert.Equal(
            [new(LogLevel.Error, "a", failure), new(LogLevel.Warning, "c", null), new LogRecord(LogLevel.Information, "e", null)],
            logged.WithoutDebug());
    }
}
