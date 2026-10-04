using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Support;

/// <summary>One line the App logged: its level and the formatted message, as the log provider would receive them.</summary>
public sealed record LogEntry(LogLevel Level, string Message);

/// <summary>
/// An <see cref="ILogger"/> that keeps every entry in memory, at every level, so tests can assert on the App's log lines
/// (LOG-2) without the <c>cm-toolkit.log</c> provider. Thread-safe: background operations log from pool threads.
/// </summary>
public sealed class CapturingLogger : ILogger
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Everything logged so far, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>The messages logged so far, in order, without their levels.</summary>
    public IReadOnlyList<string> Messages => [.. _entries.Select(e => e.Message)];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
}
