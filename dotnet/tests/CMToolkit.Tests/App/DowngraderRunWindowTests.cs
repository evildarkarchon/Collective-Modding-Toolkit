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
    /// The app on the machine, its Downgrader opened from the Overview and set to Next-Gen; its settings store logs to
    /// <paramref name="settingsLogger"/> if given.
    /// </summary>
    private static (MainWindow Main, DowngraderWindow Window, AppRuntime Runtime) Open(
        MaterializedScenario machine, HttpMessageHandler http, Microsoft.Extensions.Logging.ILogger? settingsLogger = null)
    {
        var (main, session) = DowngraderRunScenarioTests.Compose(machine, new CapturingLogger(), http, settingsLogger);
        MouseClick.On(ComposedSession.Named<Button>(main, "DowngradeManager"));
        DispatcherWait.Until(() => main.OwnedWindows.OfType<DowngraderWindow>().Any(w => w.Versions is not null && w.Content is not null));
        var window = main.OwnedWindows.OfType<DowngraderWindow>().Single();
        window.UpdateLayout();
        MouseClick.On(ComposedSession.Named<RadioButton>(window, "NextGen"));
        return (main, window, session.Runtime);
    }
}
