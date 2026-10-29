using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;
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
        var (main, window, runtime) = Open(machine, http);

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
        main.Close();
    }

    [AvaloniaFact]
    [Trait("Parity", "DG-6")]
    public void Keep_backups_unticked_while_the_patch_downloads_deletes_the_backup_once_it_is_applied()
    {
        using var machine = Machine.Materialize();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var (main, window, runtime) = Open(machine, http);
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
        main.Close();
    }

    [AvaloniaFact]
    public void Patch_all_saves_the_changed_boxes_off_the_ui_thread_in_its_blocking_operation()
    {
        // patch_files saved the boxes on the Tk thread before its loop, so the save froze input (ADR-0003).
        using var machine = Machine.Materialize();
        var log = new HeldSaveLogger();
        var (main, window, runtime) = Open(machine, machine.CreateHttpHandler(), log);
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
        main.Close();
    }

    /// <summary>
    /// The Overview refresh that runs once the queue empties fails: the runner reports it once, and the window tells the
    /// run as false at the Core seam, so the poll isn't failed and reported a second time. The rest of the "finished" step
    /// is skipped, leaving the close guard set and Patch All disabled, as the exception left them in Tk.
    /// </summary>
    [AvaloniaFact]
    public void A_failed_overview_refresh_when_the_queue_empties_is_reported_once_and_skips_the_finish()
    {
        using var machine = Machine.Materialize();
        var logger = new CapturingLogger();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var (main, session) = DowngraderRunScenarioTests.Compose(machine, logger, http);
        MouseClick.On(ComposedSession.Named<Button>(main, "DowngradeManager"));
        DispatcherWait.Until(() => main.OwnedWindows.OfType<DowngraderWindow>().Any(w => w.Versions is not null && w.Content is not null));
        var window = main.OwnedWindows.OfType<DowngraderWindow>().Single();
        window.UpdateLayout();
        MouseClick.On(ComposedSession.Named<RadioButton>(window, "NextGen"));
        session.Overview.Refresher = () => session.Runtime.Operations.RunAsync(
            "Overview refresh", OperationPhase.Blocking, () => throw new IOException("The Overview couldn't be gathered."));

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !session.Runtime.Input.IsBlocking);
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var report = Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Overview refresh")}", report.Message, StringComparison.Ordinal);
        Assert.True(window.ProcessingData);
        Assert.False(ComposedSession.Named<Button>(window, "PatchAll").IsEnabled);
        window.ProcessingData = false;
        window.Close();
        main.Close();
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
        var (main, window, runtime) = Open(machine, machine.CreateHttpHandler(), logger: logger);
        File.Delete(Path.Combine(machine.Root, "Game", "Fallout4.exe"));

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Single(logger.Entries, IsErrorReport);
        Assert.StartsWith(ErrorReporter.OperationHeader("Downgrader Patch All"), runtime.Errors.Window?.Text);
        Assert.False(ComposedSession.Named<Button>(window, "PatchAll").IsEnabled);
        Assert.False(window.ProcessingData);
        window.Close();
        main.Close();
    }

    /// <summary>
    /// An error the run doesn't expect raises out of the download poll, here a refresh that throws instead of reporting
    /// its own failure. The runner reports it once, under the poll's name, and the queue stays stuck, as the exception
    /// left the reference's poll: the window can't be closed and Patch All stays disabled. The poll's count still drops.
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "DG-6")]
    public void An_unexpected_error_in_the_poll_is_reported_once_and_leaves_the_queue_stuck()
    {
        using var machine = Machine.Materialize();
        var logger = new CapturingLogger();
        var http = new DowngraderRunScenarioTests.HeldHandler(machine.CreateHttpHandler());
        var (main, session) = DowngraderRunScenarioTests.Compose(machine, logger, http);
        var window = new DowngraderWindow(
            session.Runtime, session.Game, session.Host, session.Overview, session.Settings,
            refreshOverview: () => throw new InvalidOperationException("The refresh broke."),
            deltaPatchHttp: http,
            patchFolder: session.AppFolder);
        var opening = session.Runtime.Errors.Handle(() => window.OpenAsync(main));
        DispatcherWait.Until(() => opening.IsCompleted && window.Content is not null);
        window.UpdateLayout();
        MouseClick.On(ComposedSession.Named<RadioButton>(window, "NextGen"));

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        DispatcherWait.Until(() => !session.Runtime.Input.IsBlocking);
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Single(logger.Entries, IsErrorReport);
        Assert.StartsWith(ErrorReporter.OperationHeader("Downgrader download"), session.Runtime.Errors.Window?.Text);
        Assert.True(window.ProcessingData);
        Assert.False(ComposedSession.Named<Button>(window, "PatchAll").IsEnabled);
        window.ProcessingData = false;
        window.Close();
        main.Close();
    }

    /// <summary>Whether a log entry is an Error Window report rather than a line the app logged.</summary>
    private static bool IsErrorReport(LogEntry entry) => entry.Message.StartsWith("StdErr : ", StringComparison.Ordinal);

    /// <summary>
    /// The app on the machine, its Downgrader opened from the Overview and set to Next-Gen; its settings store logs to
    /// <paramref name="settingsLogger"/> if given, and the app to <paramref name="logger"/> if given.
    /// </summary>
    private static (MainWindow Main, DowngraderWindow Window, AppRuntime Runtime) Open(
        MaterializedScenario machine,
        HttpMessageHandler http,
        Microsoft.Extensions.Logging.ILogger? settingsLogger = null,
        CapturingLogger? logger = null)
    {
        var (main, session) = DowngraderRunScenarioTests.Compose(machine, logger ?? new CapturingLogger(), http, settingsLogger);
        MouseClick.On(ComposedSession.Named<Button>(main, "DowngradeManager"));
        DispatcherWait.Until(() => main.OwnedWindows.OfType<DowngraderWindow>().Any(w => w.Versions is not null && w.Content is not null));
        var window = main.OwnedWindows.OfType<DowngraderWindow>().Single();
        window.UpdateLayout();
        MouseClick.On(ComposedSession.Named<RadioButton>(window, "NextGen"));
        return (main, window, session.Runtime);
    }
}
