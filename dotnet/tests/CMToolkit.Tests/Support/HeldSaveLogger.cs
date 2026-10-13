using System.Collections.Concurrent;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Support;

/// <summary>
/// A settings store's logger that catches each <c>settings.json</c> write as it starts: <c>SettingsFile.Save</c> logs
/// <see cref="SavingMessage"/> just before it writes, so this logger records which thread the write runs on and, once
/// <see cref="Hold"/> is called, keeps a write that runs off the UI thread waiting there until <see cref="Release"/>.
/// The test can then look at the app mid-write: whether input is blocked, and that the file is still unwritten.
/// </summary>
/// <remarks>
/// A write on the UI thread is recorded but never held: holding it would hang the test on its own UI thread, and the
/// recorded thread already fails the assertion. Every line is also kept, so it can stand in for a capturing logger.
/// Thread-safe.
/// </remarks>
public sealed class HeldSaveLogger : ILogger
{
    /// <summary>The line <c>SettingsFile.Save</c> logs before it writes the file.</summary>
    public const string SavingMessage = "Settings : Saving settings.json";

    private readonly ManualResetEventSlim _released = new(true);
    private readonly ConcurrentQueue<bool> _writes = new();
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private volatile bool _holding;
    private int _held;
    private int _holdLimit = int.MaxValue;

    /// <summary>
    /// For each write since <see cref="Hold"/> was called, in the order they started: whether it ran on the UI thread.
    /// </summary>
    public IReadOnlyList<bool> WritesOnUiThread => [.. _writes];

    /// <summary>Everything logged so far, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>
    /// From now on, records every write and holds the first <paramref name="count"/> that run off the UI thread until
    /// <see cref="Release"/>; later writes go straight through.
    /// </summary>
    public void Hold(int count = int.MaxValue)
    {
        _holdLimit = count;
        _released.Reset();
        _holding = true;
    }

    /// <summary>Lets every held write go on, and stops holding new ones.</summary>
    public void Release()
    {
        _holdLimit = 0;
        _released.Set();
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _entries.Enqueue(new LogEntry(logLevel, message));
        if (!_holding || message != SavingMessage)
        {
            return;
        }

        var onUiThread = Dispatcher.UIThread.CheckAccess();
        _writes.Enqueue(onUiThread);
        if (!onUiThread && Interlocked.Increment(ref _held) <= _holdLimit)
        {
            // Bounded, so a test that forgets to release fails on its own timeout instead of hanging the run.
            _released.Wait(TaskTimeouts.Default);
        }
    }
}
