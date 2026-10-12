using System.Collections.Concurrent;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Updates;

namespace CMToolkit.Core.Downgrader;

/// <summary>
/// The Downgrader's Patch All run (DG-4..DG-6): the state one Downgrader window keeps across its runs (its detected
/// versions, the patch queue, the download in flight) and the reference's sequence over it, reproduced step for step,
/// its bugs included (B-9, B-10, B-11).
/// </summary>
/// <remarks>
/// <para>
/// The reference ran all of this on the Tk UI thread except each download, which ran on a worker thread while a 100 ms
/// <c>after()</c> poll waited for it. Here each step runs on a worker thread under the App's runner, and a lock stands
/// for the UI thread: a Patch All click holds it for its whole file loop, and the download poll takes it for each of
/// its steps, so they interleave only where Tk's callbacks could. A click while a download runs (possible once B-11 has
/// re-enabled the button) shares the queue and the in-progress flag with the run already going, as in Tk.
/// </para>
/// <para>
/// The first file that gets past the version checks starts the queue (<c>check_download_queue</c>) right there, in the
/// middle of the loop. When the queue is empty at that moment, the run's "finished" step (refresh the Overview,
/// detect again, redraw, re-enable the button) happens mid-loop too, and the loop goes on with the versions it just
/// detected for the files after it, as the reference's loop over the mutated dict did.
/// </para>
/// </remarks>
public sealed class DowngraderRun
{
    /// <summary><c>iter_content(chunk_size=1024)</c>.</summary>
    private const int ChunkSize = 1024;

    /// <summary><c>self.cmc.root.after(100, self.check_download_progress, ...)</c>.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly GameInstall _game;
    private readonly IHostEnvironment _host;
    private readonly HttpMessageHandler? _http;

    /// <summary>Stands for the Tk UI thread: held by a click's loop and by each step of the download poll.</summary>
    private readonly SemaphoreSlim _ui = new(1, 1);

    /// <summary><c>download_queue</c>. Touched only under <see cref="_ui"/>.</summary>
    private readonly Queue<QueuedPatch> _queue = new();

    /// <summary><c>download_progress_updates</c>: filled by the download, drained by the poll.</summary>
    private readonly ConcurrentQueue<double> _progress = new();

    /// <summary><c>current_versions</c>. Touched only under <see cref="_ui"/>.</summary>
    private DowngraderFileVersion[] _versions;

    /// <summary><c>download_or_patch_in_progress</c>. Written only under <see cref="_ui"/>.</summary>
    private volatile bool _inProgress;

    /// <param name="game">The installation whose game folder holds the six files.</param>
    /// <param name="host">The machine: its working directory is where patches are saved and looked for unless <see cref="PatchFolder"/> is set.</param>
    /// <param name="http">
    /// Where patches are downloaded through: <see cref="DeltaPatches.CreateHandler"/> in the app, a fake in tests. Null
    /// makes every download fail, as a network error would.
    /// </param>
    /// <param name="versions">The versions the window detected when it opened (<see cref="DowngraderDetection.Detect"/>).</param>
    public DowngraderRun(GameInstall game, IHostEnvironment host, HttpMessageHandler? http, IReadOnlyList<DowngraderFileVersion> versions)
    {
        _game = game;
        _host = host;
        _http = http;
        _versions = [.. versions];
    }

    /// <summary>
    /// Whether the patch queue is being worked through (<c>download_or_patch_in_progress</c>): set by the first file a
    /// click patches, cleared when the queue empties. It stays set for good after a failed download (B-10), so a later
    /// click only adds to a queue nothing works through.
    /// </summary>
    public bool InProgress => _inProgress;

    /// <summary>
    /// The folder patches are saved to and looked for in (DG-6). The App sets the app's own folder
    /// (<see cref="AppFolderFiles.DeltaPatch"/>): the reference used the working directory, which scattered patches
    /// wherever the app was started from (bug #36, fixed as a Deviation, ADR-0006). Null keeps the reference's working
    /// directory (<see cref="IHostEnvironment.CurrentDirectory"/>), which only tests rely on.
    /// </summary>
    public string? PatchFolder { get; init; }

    /// <summary>
    /// <c>patch_files</c>, one Patch All click, after the App has disabled the button, cleared the log pane and saved
    /// any changed options: for each of the six files in order, its skip message or its patch (DG-4, DG-5), and the
    /// queue started by the first file patched. Downloads it queues finish later, in the poll the host is asked to run.
    /// </summary>
    /// <param name="desired">Old-Gen or Next-Gen, as the Desired Version radios read when the button was clicked.</param>
    /// <param name="host">The window: live option reads, the Overview refresh, the poll.</param>
    /// <param name="events">Log lines, the progress bar, the button, the close guard and redrawn versions, in order.</param>
    /// <returns>
    /// A task that completes when the click's work is done. It completes normally when an Overview refresh fails (the
    /// host reported it), leaving everything after it undone, as the exception did in Tk.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="desired"/> is neither Old-Gen nor Next-Gen.</exception>
    /// <exception cref="IOException">
    /// A file the run reads outside the reference's <c>except OSError</c> can't be read, such as a file detected at open
    /// that is gone now; the rest of the loop is skipped, as the exception skipped it in Tk.
    /// </exception>
    public async Task PatchAllAsync(InstallType desired, IDowngraderHost host, IProgress<OperationEvent> events)
    {
        if (desired is not (InstallType.OldGen or InstallType.NextGen))
        {
            throw new ArgumentOutOfRangeException(nameof(desired), desired, "The Desired Version is Old-Gen or Next-Gen.");
        }

        await _ui.WaitAsync().ConfigureAwait(false);
        try
        {
            // B-11: reset for every file, so only the last file decides whether the button comes back.
            var patchNeeded = false;
            // Indexed afresh each time: a "finished" step mid-loop replaces the versions of the files still to come.
            for (var i = 0; i < _versions.Length; i++)
            {
                patchNeeded = false;
                var version = _versions[i];
                var path = _game.GamePath.Join(version.FileName);
                if (version.InstallType == desired)
                {
                    events.Report(new LogLine(LogType.Info, $"Skipped {path.Name}: Already {desired.DisplayName()}."));
                    continue;
                }

                switch (version.InstallType)
                {
                    case InstallType.NotFound:
                        events.Report(new LogLine(LogType.Info, $"Skipped {path.Name}: Not Found."));
                        continue;
                    case InstallType.Anniversary or InstallType.Obsolete:
                        events.Report(new LogLine(LogType.Info, $"Skipped {path.Name}: Unsupported Version."));
                        continue;
                }

                patchNeeded = true;
                await PatchFileAsync(path, desired, host, events).ConfigureAwait(false);
            }

            if (!patchNeeded)
            {
                events.Report(new PatchAllEnabled(true));
            }
        }
        catch (RefreshFailedException)
        {
            // The host already reported the refresh's failure; in Tk it raised out of patch_files here.
        }
        finally
        {
            _ui.Release();
        }
    }

    /// <summary>
    /// <c>patch_file</c> (DG-5): checks the file is the opposite version, backs it up, restores the desired version from
    /// its backup or queues its patch, then starts the queue if it isn't running.
    /// </summary>
    private async Task PatchFileAsync(PythonPath path, InstallType desired, IDowngraderHost host, IProgress<OperationEvent> events)
    {
        var backupOldGen = path.WithName($"{path.Stem}_upgradeBackup{path.Suffix}");
        var backupNextGen = path.WithName($"{path.Stem}_downgradeBackup{path.Suffix}");
        var file = HostPaths.Resolve(_host, path);

        // Outside the try, as in the reference: a file that has gone since it was detected raises out of the run.
        var currentCrc = FileCrc32.Of(file);

        string direction;
        PythonPath desiredBackup;
        PythonPath currentBackup;
        if (desired == InstallType.OldGen)
        {
            if (!DowngraderFiles.CrcsByType[InstallType.NextGen].Contains(currentCrc))
            {
                events.Report(new LogLine(LogType.Info, $"Skipped {path.Name}: Unsupported Version."));
                return;
            }

            direction = DeltaPatches.NextGenToOldGen;
            desiredBackup = backupOldGen;
            currentBackup = backupNextGen;
        }
        else
        {
            if (!DowngraderFiles.CrcsByType[InstallType.OldGen].Contains(currentCrc))
            {
                events.Report(new LogLine(LogType.Info, $"Skipped {path.Name}: Unsupported Version."));
                return;
            }

            direction = DeltaPatches.OldGenToNextGen;
            desiredBackup = backupNextGen;
            currentBackup = backupOldGen;
        }

        var desiredBackupFile = HostPaths.Resolve(_host, desiredBackup);
        var currentBackupFile = HostPaths.Resolve(_host, currentBackup);
        try
        {
            // file_path.chmod(stat.S_IWRITE), which leaves a file read-only when that was its only attribute (bug #91).
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
            {
                PythonChmod.SetWritable(file);
            }

            if (File.Exists(currentBackupFile))
            {
                // An identical backup of the current version makes the live file redundant; a different one is stale.
                if (FileCrc32.Of(currentBackupFile) == FileCrc32.Of(file))
                {
                    Unlink(file);
                }
                else
                {
                    Unlink(currentBackupFile);
                }
            }

            if (File.Exists(file))
            {
                // Path.rename: never over an existing file.
                File.Move(file, currentBackupFile);
            }

            if (File.Exists(desiredBackupFile))
            {
                if (DowngraderFiles.CrcsByType[desired].Contains(FileCrc32.Of(desiredBackupFile)))
                {
                    if (host.KeepBackups)
                    {
                        // shutil.copy2: the data, the attributes and the times.
                        File.Copy(desiredBackupFile, file, overwrite: true);
                    }
                    else
                    {
                        // Path.replace.
                        File.Move(desiredBackupFile, file, overwrite: true);
                    }

                    events.Report(new LogLine(LogType.Good, $"Patched {path.Name}"));
                }
                else
                {
                    Unlink(desiredBackupFile);
                }
            }

            if (!File.Exists(file))
            {
                _queue.Enqueue(new QueuedPatch(DeltaPatches.Url(direction, path.Name), currentBackup, path));
            }
            else if (!host.KeepBackups)
            {
                Unlink(currentBackupFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // except OSError: .NET's IOException family and the access-denied UnauthorizedAccessException.
            events.Report(new LogLine(LogType.Bad, $"Failed patching {path.Name}"));
        }

        if (!_inProgress)
        {
            _inProgress = true;
            events.Report(new QueueRunning(true));
            if (await CheckDownloadQueueAsync(host, events).ConfigureAwait(false) is { } started)
            {
                host.SchedulePoll(phases => PollAsync(started, host, events, phases));
            }
        }
    }

    /// <summary>
    /// <c>check_download_queue</c>: works through the queue until it starts a download or empties. A patch already in
    /// the patch folder (<see cref="PatchFolder"/>) is applied at once, unvalidated (B-10); otherwise its download starts on a worker thread.
    /// An empty queue runs the "finished" step.
    /// </summary>
    /// <returns>The download it started, which the caller polls; null when the queue emptied.</returns>
    private async Task<StartedDownload?> CheckDownloadQueueAsync(IDowngraderHost host, IProgress<OperationEvent> events)
    {
        while (_queue.TryDequeue(out var next))
        {
            var patchFile = PatchFile(next);
            if (File.Exists(patchFile))
            {
                events.Report(new Percent(100));
                // check_download_progress drains the progress queue before it sees there's no download thread.
                DrainProgress(events);
                ApplyPatch(next, host, events);
                continue;
            }

            events.Report(new Percent(0));
            var download = Task.Run(() => DownloadAsync(next.Url, patchFile));
            return new StartedDownload(next, download);
        }

        await FinishedAsync(host, events).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// The download poll (<c>check_download_progress</c> every 100 ms, THR-2): shows the download's progress as it
    /// comes, and once it is done applies the patch and moves on through the queue, until it empties.
    /// </summary>
    private async Task PollAsync(StartedDownload started, IDowngraderHost host, IProgress<OperationEvent> events, IProgress<OperationPhase> phases)
    {
        var current = started;
        while (true)
        {
            var finished = await Task.WhenAny(current.Download, Task.Delay(PollInterval)).ConfigureAwait(false) == current.Download;
            await _ui.WaitAsync().ConfigureAwait(false);
            try
            {
                DrainProgress(events);
                if (!finished)
                {
                    continue;
                }

                phases.Report(OperationPhase.Blocking);
                // B-10: a failed download killed the reference's thread without a word, and the poll waited for it
                // forever, changing nothing: the button stays disabled and the window can't be closed. Here the
                // operation ends with the failure reported, leaving that same state.
                await current.Download.ConfigureAwait(false);
                ApplyPatch(current.Patch, host, events);
                if (await CheckDownloadQueueAsync(host, events).ConfigureAwait(false) is not { } next)
                {
                    return;
                }

                phases.Report(OperationPhase.Interactive);
                current = next;
            }
            catch (RefreshFailedException)
            {
                // The host already reported the refresh's failure; in Tk it raised out of the poll's callback here.
                return;
            }
            finally
            {
                _ui.Release();
            }
        }
    }

    /// <summary>
    /// <c>apply_patch</c> (DG-6): decodes the patch from the current-version backup to the live file, logs the outcome,
    /// then deletes the backup unless Keep Backups is on, even when the decode failed (B-9), and the patch if Delete
    /// Patches is on.
    /// </summary>
    private void ApplyPatch(QueuedPatch patch, IDowngraderHost host, IProgress<OperationEvent> events)
    {
        var source = HostPaths.Resolve(_host, patch.Source);
        var patchFile = PatchFile(patch);
        var decoded = DeltaPatches.Decode(source, patchFile, HostPaths.Resolve(_host, patch.Target));
        events.Report(decoded
            ? new LogLine(LogType.Good, $"Patched {patch.Target.Name}")
            : new LogLine(LogType.Bad, $"Failed patching {patch.Target.Name}"));

        // B-9: the backup is the only copy of the file when the decode failed, and it goes anyway.
        if (!host.KeepBackups)
        {
            Unlink(source);
        }

        if (host.DeletePatches)
        {
            Unlink(patchFile);
        }
    }

    /// <summary>
    /// The queue's "finished" step: refreshes the Overview, detects the six files again and has the window redraw them,
    /// re-enables the button and lets the window close again.
    /// </summary>
    /// <exception cref="RefreshFailedException">The refresh failed; the rest is skipped.</exception>
    private async Task FinishedAsync(IDowngraderHost host, IProgress<OperationEvent> events)
    {
        if (!await host.RefreshOverviewAsync().ConfigureAwait(false))
        {
            throw new RefreshFailedException();
        }

        _versions = [.. DowngraderDetection.Detect(_game, _host)];
        events.Report(new VersionsDetected([.. _versions]));
        events.Report(new PatchAllEnabled(true));
        _inProgress = false;
        events.Report(new QueueRunning(false));
    }

    /// <summary>
    /// <c>_threaded_download</c>: streams the patch to the patch folder in 1 KiB chunks, queueing each chunk's
    /// progress for the poll, then 100. Nothing is checked (B-10): an error page is saved as the patch, a failed request
    /// or read ends the download with what it wrote so far, and a response without a <c>Content-Length</c> fails on its
    /// first chunk, as Python's division by zero did, after writing it.
    /// </summary>
    private async Task DownloadAsync(string url, string patchFile)
    {
        if (_http is null)
        {
            throw new InvalidOperationException($"There is no network to download {url} from.");
        }

        // No total timeout: the handler's per-read timeout is requests' timeout=10 (T-14).
        using var client = new HttpClient(_http, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UpdateCheck.UserAgent);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var file = new FileStream(patchFile, FileMode.Create, FileAccess.Write, FileShare.Read);
        var buffer = new byte[ChunkSize];
        long downloaded = 0;
        while (true)
        {
            // urllib3 fills each chunk unless the body ends, so every chunk but the last is a full 1 KiB.
            var read = await body.ReadAtLeastAsync(buffer, ChunkSize, throwOnEndOfStream: false).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            downloaded += read;
            await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            if (total == 0)
            {
                // downloaded_size / total_size * 100 with no content-length: ZeroDivisionError, after the write.
                throw new DivideByZeroException("division by zero");
            }

            _progress.Enqueue((double)downloaded / total * 100);
        }

        _progress.Enqueue(100);
    }

    /// <summary>
    /// Sets the bar to everything the download has queued since the last poll. Tk drew only the last of the values one
    /// callback set, so only that one is reported.
    /// </summary>
    private void DrainProgress(IProgress<OperationEvent> events)
    {
        double? last = null;
        while (_progress.TryDequeue(out var value))
        {
            last = value;
        }

        if (last is { } shown)
        {
            events.Report(new Percent(shown));
        }
    }

    /// <summary>
    /// Where a queued patch is saved and looked for: its URL's last segment, in <see cref="PatchFolder"/>, or in the
    /// host's working directory when that is null.
    /// </summary>
    private string PatchFile(QueuedPatch patch)
        => PatchFolder is { } folder
            ? AppFolderFiles.DeltaPatch(folder, patch.Url)
            : HostPaths.Resolve(_host, new PythonPath(Path.GetFileName(patch.Url)));

    /// <summary><c>Path.unlink()</c>: unlike <see cref="File.Delete"/>, a missing file is an error.</summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    private static void Unlink(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The system cannot find the file specified: '{path}'", path);
        }

        File.Delete(path);
    }

    /// <summary>One <c>download_queue</c> entry: the patch's URL, the backup it applies to and the file it rebuilds.</summary>
    private sealed record QueuedPatch(string Url, PythonPath Source, PythonPath Target);

    /// <summary>A queued patch whose download is under way.</summary>
    private sealed record StartedDownload(QueuedPatch Patch, Task Download);

    /// <summary>The Overview refresh failed and was reported; the reference's exception ended the callback.</summary>
    private sealed class RefreshFailedException : Exception;
}
