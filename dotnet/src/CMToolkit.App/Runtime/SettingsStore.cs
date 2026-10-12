using CMToolkit.Core;
using CMToolkit.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The settings in effect for this session, shared by everything that reads or writes them: the reference's single
/// <c>AppSettings</c> instance (<c>cmc.settings</c>). Core owns the file format; the App is the only writer (SET-7), never
/// on the UI thread: through <see cref="SaveAsync"/> from the Settings tab and the Scanner, and through <see cref="Save"/>
/// inside the Downgrader's background operation.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>The operation name a failed <see cref="SaveAsync"/> write would show in the Error Window.</summary>
    public const string SaveOperationName = "Save settings";

    private readonly string? _path;
    private readonly ILogger _logger;
    private readonly Lock _commitLock = new();
    private readonly Lock _writeLock = new();
    private volatile AppSettings _current;

    // Saves are numbered in the order they were made; _written is the number of the newest save on disk.
    private long _issued;
    private long _written;

    private SettingsStore(string? path, AppSettings current, ILogger logger)
    {
        _path = path;
        _current = current;
        _logger = logger;
    }

    /// <summary>The settings in effect.</summary>
    public AppSettings Current => _current;

    /// <summary>
    /// Loads <paramref name="path"/> (SET-1, SET-3..SET-6), which may resave it, and keeps it as the file
    /// <see cref="Save"/> writes. Never throws.
    /// </summary>
    /// <param name="path">The settings file; startup passes <see cref="AppFolderFiles.Settings"/> of the app folder (bug #36, fixed).</param>
    /// <param name="downloadSource">Seeds the default <c>update_source</c>.</param>
    /// <param name="logger">The app log.</param>
    public static SettingsStore Load(string path, DownloadSource downloadSource, ILogger logger)
        => new(path, SettingsFile.Load(path, downloadSource, logger), logger);

    /// <summary>
    /// A store backed by no file, whose <see cref="Save"/> and <see cref="SaveAsync"/> only update <see cref="Current"/>: for view models built
    /// without a settings file, such as in tests.
    /// </summary>
    public static SettingsStore Unsaved(AppSettings settings) => new(null, settings, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    /// <summary>
    /// Makes <paramref name="settings"/> current and writes the file (<c>AppSettings.save</c>, SET-6), on the calling
    /// thread. A failed write is logged and swallowed; the new settings stay in effect for the session either way.
    /// </summary>
    /// <remarks>
    /// For work already running off the UI thread inside a background operation, such as the Downgrader's Patch All.
    /// A UI handler calls <see cref="SaveAsync"/> instead, so the write never runs on the UI thread (ADR-0003).
    /// </remarks>
    public void Save(AppSettings settings) => Write(Commit(settings), settings);

    /// <summary>
    /// A UI handler's save: makes <paramref name="settings"/> current at once, on the UI thread, then writes the file as
    /// a <see cref="OperationPhase.Blocking"/> background operation, because the reference wrote it on the Tk thread
    /// and so froze input until the write was done (ADR-0003).
    /// </summary>
    /// <param name="operations">The runner the write goes through. Call this on the UI thread, as the runner requires.</param>
    /// <param name="settings">The settings to make current and save.</param>
    /// <returns>A task that completes, on the UI thread, once the file is written (or the write failed and was logged).</returns>
    /// <exception cref="OperationFailedException">The write raised past its own logging; the runner has reported it.</exception>
    /// <remarks>
    /// <see cref="Current"/> changes before the write starts, so a save made before an earlier one has landed builds on
    /// it, as every save in the reference built on the one shared dict. Writes land in the order their saves were made,
    /// and the last one wins.
    /// </remarks>
    public Task SaveAsync(BackgroundOperations operations, AppSettings settings)
    {
        var ticket = Commit(settings);
        return operations.RunAsync(SaveOperationName, OperationPhase.Blocking, () => Write(ticket, settings));
    }

    /// <summary>Makes <paramref name="settings"/> current and numbers the save, atomically, so numbers follow saves.</summary>
    private long Commit(AppSettings settings)
    {
        lock (_commitLock)
        {
            _current = settings;
            return ++_issued;
        }
    }

    /// <summary>
    /// Writes save <paramref name="ticket"/>'s <paramref name="settings"/>, unless a later save is already on disk.
    /// </summary>
    private void Write(long ticket, AppSettings settings)
    {
        if (_path is null)
        {
            return;
        }

        // Two saves' writes can run on two pool threads at once, and the pool may start the later one first. One writer at
        // a time keeps the file whole, and skipping a write older than the file keeps the last save's settings there.
        lock (_writeLock)
        {
            if (ticket <= _written)
            {
                return;
            }

            SettingsFile.Save(_path, settings, _logger);
            _written = ticket;
        }
    }
}
