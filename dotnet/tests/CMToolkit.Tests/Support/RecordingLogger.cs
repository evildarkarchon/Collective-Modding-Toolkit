using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Support;

/// <summary>
/// Records each log call's level, message and whether it carried an exception, and projects them the way the driver's
/// <c>log_lines</c> does (Python level names; tracebacks reduced to <c>"exception": true</c>).
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message, bool HasException)> _entries = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Add((logLevel, formatter(state, exception), exception is not null));

    /// <summary>The entries as the golden's <c>display.log</c> array.</summary>
    public JsonArray ToGolden()
    {
        var lines = new JsonArray();
        foreach (var (level, message, hasException) in _entries)
        {
            var line = new JsonObject
            {
                ["level"] = level switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Information => "INFO",
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    LogLevel.Critical => "CRITICAL",
                    _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
                },
                ["message"] = message,
            };
            if (hasException)
            {
                line["exception"] = true;
            }

            lines.Add(line);
        }

        return lines;
    }
}
