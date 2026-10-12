using System.Collections.Concurrent;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Updates;

namespace CMToolkit.Core.Downgrader;

/// <summary>
/// The Downgrader's Patch All run (DG-4..DG-6): the state one Downgrader window keeps across its runs (its detected
/// versions, the patch queue, the download in flight) and the reference's sequence over it, reproduced step for step,
/// except for the reference bugs fixed as Deviations (ADR-0006): the Patch All button follows the queue (B-11, #28),
/// a failed decode puts the backup back (B-9, #14), and a failed download is logged and moved past while nothing
/// unchecked is saved or reused (B-10, #15).
/// </summary>
/// <remarks>
/// <para>
/// The reference ran all of this on the Tk UI thread except each download, which ran on a worker thread while a 100 ms
/// <c>after()</c> poll waited for it. Here each step runs on a worker thread under the App's runner, and a lock stands
/// for the UI thread: a Patch All click holds it for its whole file loop, and the download poll takes it for each of
/// its steps, so they interleave only where Tk's callbacks could. A click while a download runs (possible in the
/// reference once B-11 had re-enabled the button; the port's button stays off then, bug #28) would share the queue and
/// the in-progress flag with the run already going, as in Tk.
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
    /// <param name="host">The machine: its working directory is where patches are saved and looked for.</param>
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
    /// click patches, cleared when the queue empties. In the reference it stayed set for good after a failed download
    /// (B-10); the port's queue moves on past a failed download and empties (bug #15). It stays set only if the poll
    /// itself fails unexpectedly or the Overview refresh fails.
    /// </summary>
    public bool InProgress => _inProgress;

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
            // Indexed afresh each time: a "finished" step mid-loop replaces the versions of the files still to come.
            for (var i = 0; i < _versions.Length; i++)
            {
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

                await PatchFileAsync(path, desired, host, events).ConfigureAwait(false);
            }

            // Bug #28 (B-11), fixed as a Deviation (ADR-0006): the reference reset patch_needed for every file, so only
            // the last file decided whether the button came back. It stayed off for good when that file was skipped by
            // the CRC check, and came back on while a download still ran when it was skipped earlier. The button now
            // follows the queue: on when nothing is queued or running, off while a download or patch is pending (the
            // "finished" step turns it on again). Reported either way, as a "finished" step mid-loop may have turned
            // it on before a later file started a download.
            events.Report(new PatchAllEnabled(!_inProgress));
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
            // file_path.chmod(stat.S_IWRITE), which left a file read-only when that was its only attribute (bug #91,
            // fixed in PythonChmod: the port always leaves the file writable).
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
    /// the working directory is applied at once; decoding it is its check, and one that fails is deleted (bug #15).
    /// Otherwise its download starts on a worker thread. An empty queue runs the "finished" step.
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
                if (await DownloadFailure(current.Download).ConfigureAwait(false) is { } reason)
                {
                    // Bug #15 (B-10), fixed as a Deviation (ADR-0006): a failed download killed the reference's thread
                    // without a word, and the poll waited for it forever: the button stayed disabled and the window
                    // couldn't be closed. The port logs it, puts the file back and moves on through the queue.
                    events.Report(new LogLine(LogType.Bad, $"Failed downloading {PatchName(current.Patch)}: {reason}"));
                    events.Report(new LogLine(LogType.Bad, $"Failed patching {current.Patch.Target.Name}"));
                    RestoreBackup(HostPaths.Resolve(_host, current.Patch.Source), HostPaths.Resolve(_host, current.Patch.Target), host);
                }
                else
                {
                    ApplyPatch(current.Patch, host, events);
                }

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
    /// <c>apply_patch</c> (DG-6): decodes the patch from the current-version backup to the live file and logs the
    /// outcome. After a good decode it deletes the backup unless Keep Backups is on, and the patch if Delete Patches is
    /// on. After a failed one it puts the backup back as the live file instead (bug #14), and deletes the patch whatever
    /// Delete Patches says, so a bad one is never used again (bug #15).
    /// </summary>
    private void ApplyPatch(QueuedPatch patch, IDowngraderHost host, IProgress<OperationEvent> events)
    {
        var source = HostPaths.Resolve(_host, patch.Source);
        var target = HostPaths.Resolve(_host, patch.Target);
        var patchFile = PatchFile(patch);
        var decoded = DeltaPatches.Decode(source, patchFile, target);
        events.Report(decoded
            ? new LogLine(LogType.Good, $"Patched {patch.Target.Name}")
            : new LogLine(LogType.Bad, $"Failed patching {patch.Target.Name}"));

        if (decoded)
        {
            if (!host.KeepBackups)
            {
                Unlink(source);
            }
        }
        else
        {
            RestoreBackup(source, target, host);
        }

        // Bug #15 (B-10), fixed as a Deviation (ADR-0006): the reference reused a patch it found in the working
        // directory without a check, so an error page or a truncated download failed every run after it. Decoding is
        // the check: a patch that fails it goes, and the next Patch All downloads a fresh one.
        if (host.DeletePatches || !decoded)
        {
            Unlink(patchFile);
        }
    }

    /// <summary>
    /// Undoes a patch that failed: deletes whatever part of the live file the decode wrote, then puts the
    /// current-version backup back in its place, copied with Keep Backups on and moved otherwise, as
    /// <c>patch_file</c>'s restore from a backup does.
    /// </summary>
    /// <remarks>
    /// Bug #14 (B-9), fixed as a Deviation (ADR-0006): the reference deleted the backup after a failed decode whenever
    /// Keep Backups was off, and xdelta3 had left nothing, or only the windows it verified, as the live file, so the
    /// game could be left with neither version of the file.
    /// </remarks>
    private static void RestoreBackup(string backup, string target, IDowngraderHost host)
    {
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        if (host.KeepBackups)
        {
            // shutil.copy2, as in patch_file: the data, the attributes and the times.
            File.Copy(backup, target);
        }
        else
        {
            File.Move(backup, target);
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
    /// <c>_threaded_download</c>: streams the patch to the working directory in 1 KiB chunks, queueing each chunk's
    /// progress for the poll, then 100.
    /// </summary>
    /// <remarks>
    /// Bug #15 (B-10), fixed as a Deviation (ADR-0006). The reference checked nothing: it saved an error page as the
    /// patch, left what it had written when a request or read failed, and failed on the first chunk of a response
    /// without a <c>Content-Length</c> (a division by zero). Here an error status fails before anything is written, a
    /// failure part-way deletes the partial file, and a response without a <c>Content-Length</c> downloads in full with
    /// no progress shown until it is done.
    /// </remarks>
    /// <exception cref="HttpRequestException">The server answered with an error status, or the request failed.</exception>
    /// <exception cref="InvalidOperationException">There is no network handler to download through.</exception>
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
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase}).", null, response.StatusCode);
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        try
        {
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
                if (total > 0)
                {
                    _progress.Enqueue((double)downloaded / total * 100);
                }
            }
        }
        catch
        {
            // A partial patch would be found and used by the next run; the file is closed by now (the usings above).
            File.Delete(patchFile);
            throw;
        }

        _progress.Enqueue(100);
    }

    /// <summary>
    /// Waits for a download and says why it failed, if it did: any failure of the download (an error status, the
    /// network, a timeout, the file it writes, no network handler at all) fails that one patch, not the run.
    /// </summary>
    /// <returns>Null when the download succeeded; otherwise the failure's message for the log pane.</returns>
    private static async Task<string?> DownloadFailure(Task download)
    {
        try
        {
            await download.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The patch's file name, the last segment of its URL.</summary>
    private static string PatchName(QueuedPatch patch) => WorkingDirectoryFiles.DeltaPatch(patch.Url);

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

    /// <summary>Where a queued patch is saved and looked for: its URL's last segment, in the working directory.</summary>
    private string PatchFile(QueuedPatch patch)
        => HostPaths.Resolve(_host, new PythonPath(WorkingDirectoryFiles.DeltaPatch(patch.Url)));

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
