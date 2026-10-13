namespace CMToolkit.Core.Downgrader;

/// <summary>
/// What the Downgrader's Patch All run needs from the window it runs in (the Core-App contract of issue #7): live reads
/// of the window's options, the Overview refresh, and somewhere to run the download poll.
/// </summary>
/// <remarks>
/// The run reads the options at exactly the reference's read points, so a box toggled while a patch downloads changes
/// the steps after it, as toggling the Tk variable did. They're read from worker threads, so an implementation keeps a
/// thread-safe copy of each box.
/// </remarks>
public interface IDowngraderHost
{
    /// <summary>Keep Backups, now (<c>bv_keep_backups.get()</c>).</summary>
    bool KeepBackups { get; }

    /// <summary>Delete Patches, now (<c>bv_delete_deltas.get()</c>).</summary>
    bool DeletePatches { get; }

    /// <summary>
    /// <c>cmc.refresh_tab(Tab.Overview)</c>, the first thing the run does once its queue is empty.
    /// </summary>
    /// <returns>
    /// False when the refresh failed (and was reported): in the reference it raised out of the run, so the run stops
    /// there, leaving its state as it was.
    /// </returns>
    Task<bool> RefreshOverviewAsync();

    /// <summary>
    /// Runs <paramref name="poll"/> as an operation of its own, started from the UI thread once it is free: the
    /// reference's <c>after(100, check_download_progress)</c>, the chain of UI-thread callbacks that waits for each
    /// download, applies it and moves on, independently of the Patch All click that queued it.
    /// </summary>
    /// <param name="poll">
    /// The chain. It starts <see cref="OperationPhase.Interactive"/> (the download runs on a worker thread, the UI is
    /// live) and reports <see cref="OperationPhase.Blocking"/> while it applies a patch on the reference's UI thread. A
    /// failed download doesn't fail it: the run logs it and moves on (B-10, bug #15, fixed). It fails only on an
    /// unexpected error, such as a file it can't delete or restore.
    /// </param>
    void SchedulePoll(Func<IProgress<OperationPhase>, Task> poll);
}
