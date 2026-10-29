using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Support;

/// <summary>
/// A logger that, once <see cref="Arm"/> is called, throws an <see cref="IOException"/> on every line its match accepts,
/// so the code that logged it raises right there, on whatever thread it runs on. That is how a test makes an operation
/// fail when the operation turns its own I/O errors into log lines: <c>SettingsFile.Save</c> logs and swallows a failed
/// write, and <see cref="CMToolkit.Core.ArchivePatcher.ArchivePatching.Patch"/> turns every per-file error into a pane
/// line, so there is no file a test could break instead.
/// </summary>
/// <remarks>
/// Lines before <see cref="Arm"/>, and lines the match rejects, go through, so a settings store still loads normally.
/// Every line that goes through is also kept, so it can stand in for a capturing logger; a line that throws is not.
/// Thread-safe.
/// </remarks>
public sealed class ThrowingLogger : ILogger
{
    private readonly Func<string, bool> _throwsOn;
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private volatile bool _armed;

    /// <summary>A logger that, once armed, throws on each line <paramref name="throwsOn"/> accepts.</summary>
    /// <param name="throwsOn">Given each formatted message; true makes that line throw.</param>
    public ThrowingLogger(Func<string, bool> throwsOn) => _throwsOn = throwsOn;

    /// <summary>
    /// A settings store's logger that, once armed, throws on <c>SettingsFile.Save</c>'s opening
    /// <see cref="HeldSaveLogger.SavingMessage"/> line, so the write raises on its worker past the save's own catch:
    /// the only way to make <see cref="CMToolkit.App.Runtime.SettingsStore.SaveAsync"/>'s operation fail.
    /// </summary>
    public static ThrowingLogger OnSave() => new(message => message == HeldSaveLogger.SavingMessage);

    /// <summary>Everything logged so far that did not throw, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>The messages logged so far that did not throw, in order, without their levels.</summary>
    public IReadOnlyList<string> Messages => [.. _entries.Select(e => e.Message)];

    /// <summary>Makes every later line the match accepts throw.</summary>
    public void Arm() => _armed = true;

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
        if (_armed && _throwsOn(message))
        {
            throw new IOException("the log is gone");
        }

        _entries.Enqueue(new LogEntry(logLevel, message));
    }
}
