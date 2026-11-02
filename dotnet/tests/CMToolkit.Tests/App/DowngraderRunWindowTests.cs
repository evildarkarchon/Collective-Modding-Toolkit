using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Downgrader window while its Patch All run is under way, what the recorded scenarios can't show: the UI is live
/// while a patch downloads but the window can't be closed (THR-2, DG-6), and a box ticked meanwhile reaches the run at
/// its next read. The machine is <c>downgrader/run-reenabled-during-download</c>: one Old-Gen <c>Fallout4.exe</c> and its
/// canned OG-to-NG patch, held until the test releases it.
/// </summary>
public sealed class DowngraderRunWindowTests
{
    private static ParityScenario Machine => ParityScenarios.All().Single(s => s.Id == "downgrader/run-reenabled-during-download");

    [AvaloniaFact]
    [Trait("Parity", "THR-2")]
    [Trait("Parity", "DG-6")]
    public void A_download_leaves_the_ui_live_but_the_window_unclosable_until_the_queue_is_done()
    {
        using var machine = Machine.Materialize();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var (owner, window, runtime, _) = Build(machine, http);

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);

        // The download is held: input is live, yet neither a close request nor Escape closes the window.
        Assert.True(window.IsRunning);
        Assert.True(window.ProcessingData);
        // A user close is always cancelled, then honoured by RequestClose unless the guard is up.
        UserClose.Request(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);

        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Assert.False(window.ProcessingData);
        Assert.Equal(100, ComposedSession.Named<ProgressBar>(window, "Progress").Value);
        UserClose.Request(window);
        DispatcherWait.Until(() => !window.IsVisible);
        owner.Close();
    }

    [AvaloniaFact]
    [Trait("Parity", "DG-6")]
    public void Keep_backups_unticked_while_the_patch_downloads_deletes_the_backup_once_it_is_applied()
    {
        using var machine = Machine.Materialize();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var (owner, window, runtime, _) = Build(machine, http);
        var keepBackups = ComposedSession.Named<CheckBox>(window, "KeepBackups");
        Assert.True(keepBackups.IsChecked);

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        var backup = Path.Combine(machine.Root, "Game", "Fallout4_upgradeBackup.exe");
        Assert.True(File.Exists(backup));
        MouseClick.On(keepBackups);
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);

        // apply_patch read the box as it ran: the backup is gone. The settings were saved at the click, not now.
        Assert.False(File.Exists(backup));
        Assert.Equal("C5965A2E", FileCrc32.Of(Path.Combine(machine.Root, "Game", "Fallout4.exe")));
        Assert.Contains("\"downgrader_keep_backups\": true", File.ReadAllText(Path.Combine(machine.Root, "Cwd", "settings.json")), StringComparison.Ordinal);
        window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void Patch_all_saves_the_changed_boxes_off_the_ui_thread_in_its_blocking_operation()
    {
        // patch_files saved the boxes on the Tk thread before its loop, so the save froze input (ADR-0003).
        using var machine = Machine.Materialize();
        var log = new HeldSaveLogger();
        var (owner, window, runtime, _) = Build(machine, machine.CreateHttpHandler(), settingsLogger: log);
        var settingsPath = Path.Combine(machine.Root, "Cwd", "settings.json");
        var before = File.ReadAllBytes(settingsPath);
        MouseClick.On(ComposedSession.Named<CheckBox>(window, "KeepBackups"));

        log.Hold();
        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => log.WritesOnUiThread.Count == 1);

        Assert.False(log.WritesOnUiThread[0]);
        Assert.True(runtime.Input.IsBlocking);
        Assert.Equal(before, File.ReadAllBytes(settingsPath));

        log.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Assert.Contains("\"downgrader_keep_backups\": false", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        window.Close();
        owner.Close();
    }

    /// <summary>
    /// The refresh that runs once the queue empties gathers the Overview Report again from the patched files and makes
    /// it current, and the Overview tab, which the Downgrader was opened from, redraws its Binaries frame from it.
    /// </summary>
    [AvaloniaFact]
    public void A_refresh_when_the_queue_empties_replaces_the_overview_report_and_the_overview_tab_shows_it()
    {
        using var machine = Machine.Materialize();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var (main, session) = DowngraderRunScenarioTests.Compose(machine, new CapturingLogger(), http);
        var before = session.Overview.Report;
        Assert.Equal("Old-Gen", ComposedSession.Named<TextBlock>(main, "Binary0").Text);
        MouseClick.On(ComposedSession.Named<Button>(main, "DowngradeManager"));
        DispatcherWait.Until(() => main.OwnedWindows.OfType<DowngraderWindow>().Any(w => w.Versions is not null && w.Content is not null));
        var window = main.OwnedWindows.OfType<DowngraderWindow>().Single();
        window.UpdateLayout();
        MouseClick.On(ComposedSession.Named<RadioButton>(window, "NextGen"));

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !session.Runtime.Input.IsBlocking);
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(before, session.Overview.Report);
        Assert.Equal(InstallType.NextGen, session.Overview.Report!.Binaries[0].InstallType);
        Assert.Equal("Next-Gen", ComposedSession.Named<TextBlock>(main, "Binary0").Text);
        Assert.Null(session.Runtime.Errors.Window);
        UserClose.Request(window);
        DispatcherWait.Until(() => !window.IsVisible);
        main.Close();
    }

    /// <summary>
    /// The Overview Report's gathering fails when the queue empties: the runner reports it once, and the window tells the
    /// run as false at the Core seam, so the poll isn't failed and reported a second time. The window still recovers: the
    /// version labels are redrawn from the files as they are now, Patch All comes back and the window can be closed.
    /// </summary>
    [AvaloniaFact]
    public void A_failed_overview_refresh_when_the_queue_empties_is_reported_once_and_the_window_recovers()
    {
        using var machine = Machine.Materialize();
        var logger = new CapturingLogger();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var gatherings = 0;
        var (owner, window, runtime, _) = Build(machine, http, logger, real => previous =>
            gatherings++ == 0 ? real(previous) : throw new IOException("The gathering broke."));
        Assert.Equal("Old-Gen", ComposedSession.Named<TextBlock>(window, "Game0").Text);

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, gatherings);
        var report = Assert.Single(logger.Entries, IsErrorReport);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Overview refresh")}", report.Message, StringComparison.Ordinal);
        Assert.StartsWith(ErrorReporter.OperationHeader("Overview refresh"), runtime.Errors.Window?.Text);
        Assert.Equal("Next-Gen", ComposedSession.Named<TextBlock>(window, "Game0").Text);
        Assert.True(ComposedSession.Named<Button>(window, "PatchAll").IsEnabled);
        Assert.False(window.ProcessingData);
        UserClose.Request(window);
        DispatcherWait.Until(() => !window.IsVisible);
        owner.Close();
    }

    /// <summary>
    /// A file gone since the window detected it raises out of the click's run, as it raised out of the reference's
    /// <c>patch_files</c>: the runner reports it once, under the click's name, and the rest is skipped, the button
    /// staying disabled. The click's count still drops, so the window doesn't look busy for good.
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "DG-4")]
    public void A_file_gone_since_it_was_detected_is_reported_once_and_leaves_patch_all_disabled()
    {
        using var machine = Machine.Materialize();
        var logger = new CapturingLogger();
        var (owner, window, runtime, _) = Build(machine, machine.CreateHttpHandler(), logger);
        File.Delete(Path.Combine(machine.Root, "Game", "Fallout4.exe"));

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Single(logger.Entries, IsErrorReport);
        Assert.StartsWith(ErrorReporter.OperationHeader("Downgrader Patch All"), runtime.Errors.Window?.Text);
        Assert.False(ComposedSession.Named<Button>(window, "PatchAll").IsEnabled);
        Assert.False(window.ProcessingData);
        window.Close();
        owner.Close();
    }

    /// <summary>
    /// An error the run doesn't expect raises out of the download poll: here the patched <c>Fallout4.exe</c> is locked by
    /// another process (taken during the refresh's gathering, the step just before) when the queue's finished step
    /// detects the six files again, so the detection's read fails. The runner reports it once, under the poll's name,
    /// and the queue stays stuck: the window can't be closed and Patch All stays disabled. The poll's count still drops.
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "DG-6")]
    public void An_unexpected_error_in_the_poll_is_reported_once_and_leaves_the_queue_stuck()
    {
        using var machine = Machine.Materialize();
        // Declared after the machine, so it is released before the machine's folder is deleted.
        using var exeLock = new ExclusiveLock(Path.Combine(machine.Root, "Game", "Fallout4.exe"));
        var logger = new CapturingLogger();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var gatherings = 0;
        var (owner, window, runtime, _) = Build(machine, http, logger, gather: real => previous =>
        {
            var report = real(previous);
            // The first gathering is Build's load; the second is the refresh at the start of the finished step.
            if (Interlocked.Increment(ref gatherings) == 2)
            {
                exeLock.Take();
            }

            return report;
        });

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, gatherings);
        Assert.Single(logger.Entries, IsErrorReport);
        Assert.StartsWith(ErrorReporter.OperationHeader("Downgrader download"), runtime.Errors.Window?.Text);
        Assert.Contains("System.IO.IOException", runtime.Errors.Window?.Text, StringComparison.Ordinal);
        Assert.Contains("Fallout4.exe", runtime.Errors.Window?.Text, StringComparison.Ordinal);
        Assert.True(window.ProcessingData);
        Assert.False(ComposedSession.Named<Button>(window, "PatchAll").IsEnabled);
        window.ProcessingData = false;
        window.Close();
        owner.Close();
    }

    /// <summary>
    /// The Downgrader built on its own over the machine, without a main window or an Overview tab: its Overview Report
    /// module gathers through <paramref name="gather"/> (the real gathering by default, which the function is handed so
    /// it can wrap it) and is loaded before the window opens over a bare owner. The window is set to Next-Gen. Its
    /// settings store logs to <paramref name="settingsLogger"/> if given, else to the app's log.
    /// </summary>
    /// <returns>The owner, the window with its content built, the app runtime and the window's Overview Report module.</returns>
    private static (Window Owner, DowngraderWindow Window, AppRuntime Runtime, OverviewState Overview) Build(
        MaterializedScenario machine,
        HttpMessageHandler http,
        CapturingLogger? logger = null,
        Func<Func<OverviewReport?, OverviewReport>, Func<OverviewReport?, OverviewReport>>? gather = null,
        Microsoft.Extensions.Logging.ILogger? settingsLogger = null)
    {
        logger ??= new CapturingLogger();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        // The working directory is the app folder: settings.json is read from it and Patch All saves there.
        var appFolder = machine.Host.Expand(machine.Host.Block.Cwd!);
        var settings = SettingsStore.Load(Path.Combine(appFolder, AppFolderFiles.SettingsFileName), DownloadSource.Nexus, settingsLogger ?? logger);
        var runtime = new AppRuntime(logger);
        Func<OverviewReport?, OverviewReport> real = previous => OverviewReport.Gather(game, machine.Host, runtime.Logger, previous);
        var overview = new OverviewState(runtime, gather is null ? real : gather(real));
        var load = overview.LoadAsync();
        DispatcherWait.Until(() => load.IsCompleted);
        load.GetAwaiter().GetResult();

        var owner = new Window();
        owner.Show();
        var window = new DowngraderWindow(runtime, game, machine.Host, overview, settings, deltaPatchHttp: http, patchFolder: appFolder);
        var opening = runtime.Actions.Start(() => window.OpenAsync(owner));
        DispatcherWait.Until(() => opening.IsCompleted && window.Content is not null);
        window.UpdateLayout();
        MouseClick.On(ComposedSession.Named<RadioButton>(window, "NextGen"));
        return (owner, window, runtime, overview);
    }

    /// <summary>Whether a log entry is an Error Window report rather than a line the app logged.</summary>
    private static bool IsErrorReport(LogEntry entry) => entry.Message.StartsWith("StdErr : ", StringComparison.Ordinal);

    /// <summary>
    /// Another process holding a file open with no sharing, taken on demand from any thread: every open of the file
    /// fails with an <see cref="IOException"/> until it is disposed.
    /// </summary>
    private sealed class ExclusiveLock(string path) : IDisposable
    {
        private FileStream? _stream;

        /// <summary>Opens the file with no sharing and keeps it open until disposed. Take it once.</summary>
        public void Take() => Volatile.Write(ref _stream, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));

        /// <summary>Releases the file, if it was taken.</summary>
        public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    }
}
