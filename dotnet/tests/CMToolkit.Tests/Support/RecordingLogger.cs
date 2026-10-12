using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Support;

/// <summary>One log call: its level, its formatted message and the exception passed with it, if any.</summary>
public sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// An <see cref="ILogger"/> that keeps every call in memory, at every level, together with its exception. Core logs
/// Python's <c>logger.exception(...)</c> as a message plus an exception, and goldens record whether a line carried one
/// (<c>"exception": true</c>), which <see cref="CapturingLogger"/> doesn't keep. Thread-safe.
/// </summary>
public sealed class RecordingLogger : ILogger
{
    private readonly ConcurrentQueue<LogRecord> _records = new();

    /// <summary>Everything logged so far, in order.</summary>
    public IReadOnlyList<LogRecord> Records => [.. _records];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _records.Enqueue(new LogRecord(logLevel, formatter(state, exception), exception));
}
