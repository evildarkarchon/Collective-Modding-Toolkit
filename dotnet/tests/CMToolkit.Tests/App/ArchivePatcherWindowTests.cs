using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// How the Archive Patcher is reached and how it behaves around a run, beyond what its scenarios replay: the Archive
/// Patcher buttons on the Overview and Tools tabs both open it over the main window, a click on the radio that is already
/// selected still clears the pane and lists again, Patch All holds the input block through the Overview refresh, a
/// failed patch run or Overview refresh still leaves a window that closes and lists the archives' current versions,
/// Escape closes it even while the log pane has focus (bug #88, fixed), and a game without a Data folder fails Patch
/// All's Data folder check. Patch All also refuses to start while a Scan is running, and runs once it has ended.
/// </summary>
public sealed class ArchivePatcherWindowTests
{
    [AvaloniaFact]
    [Trait("Parity", "OVW-A6")]
    public void The_overview_archive_patcher_button_opens_the_patcher_over_the_main_window()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        using var app = new ComposedSession(machine);

        MouseClick.On(ComposedSession.Named<Button>(app.Window, "ArchivePatcher"));

        var patcher = app.WaitForArchivePatcher();
        Assert.Same(app.Window, patcher.Owner);
        Assert.Equal(11, patcher.Rows.Count);
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void The_tools_tab_archive_patcher_button_opens_the_patcher_over_the_main_window()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        using var app = new ComposedSession(machine);

        MouseClick.On(app.TabButton(3, "Archive Patcher"));

        var patcher = app.WaitForArchivePatcher();
        Assert.Same(app.Window, patcher.Owner);
        Assert.Equal("Archive Patcher", patcher.Title);
    }

    [AvaloniaFact]
    [Trait("Parity", "AP-2")]
    public void Clicking_the_selected_radio_again_clears_the_pane_and_lists_again()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        using var app = new ComposedSession(machine);
        MouseClick.On(ComposedSession.Named<Button>(app.Window, "ArchivePatcher"));
        var patcher = app.WaitForArchivePatcher();
        patcher.Log.LogMessage(CMToolkit.Core.LogType.Bad, "left over", skipLogging: true);

        MouseClick.On(ComposedSession.Named<RadioButton>(patcher, "OldGen"));

        Assert.Equal(["\U0001F4AD ", "Showing 11 files to be patched.\n"], patcher.Log.Runs.Select(r => r.Text));
    }

    [AvaloniaFact]
    [Trait("Parity", "AP-3")]
    public async Task Patch_all_holds_the_input_block_through_the_overview_refresh()
    {
        using var machine = Scenario("archive-patcher/nothing-to-do").Materialize();
        var (runtime, game, overview) = Parts(machine);
        bool? blockedAfterRefresh = null;
        // Refreshed is raised once the refresh's own operation is over, so this sees the window's block, not the
        // refresh's.
        overview.Refreshed += (_, _) => blockedAfterRefresh = runtime.Input.IsBlocking;
        var patcher = Show(runtime, game, machine, overview);

        await patcher.PatchAllAsync();

        Assert.True(blockedAfterRefresh);
        Assert.False(runtime.Input.IsBlocking);
    }

    /// <summary>
    /// While a Scan is running, Patch All says why it won't start and changes nothing: no Operation runs and input is
    /// never blocked, the archives, the list and the pane stay as they were, and Patch All stays enabled. The refusal is
    /// one INFO line in the app log, not a failure.
    /// </summary>
    [AvaloniaFact]
    public async Task While_a_scan_runs_patch_all_shows_a_warning_and_changes_nothing()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        var game = Locate(machine);
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var overview = FixedOverview.Load(runtime, Gather(game, machine));
        var patcher = Show(runtime, game, machine, overview, scanRunning: () => true);
        var archives = ArchiveBytes(machine);
        var rows = patcher.Rows;
        var pane = patcher.Log.Runs.Select(r => r.Text).ToList();
        var logged = logger.Entries.Count;

        var patchAll = PatchAll(runtime, patcher);
        var box = patcher.OwnedWindows.OfType<MessageBoxWindow>().Single();

        Assert.Equal(MessageBoxKind.Warning, box.Kind);
        Assert.Equal("Archive Patcher", box.Title);
        Assert.Contains(box.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "A Scan is running. Patch All can start once it ends.");
        Assert.False(runtime.Input.IsBlocking);
        box.Close();
        await patchAll.WithTimeout();

        Assert.Equal(archives, ArchiveBytes(machine));
        Assert.Equal(rows, patcher.Rows);
        Assert.Equal(pane, patcher.Log.Runs.Select(r => r.Text));
        Assert.True(ComposedSession.Named<Button>(patcher, "PatchAll").IsEnabled);
        Assert.Equal(
            [new LogEntry(LogLevel.Information, "Archive Patcher : Patch All : A Scan is running, refused")],
            logger.Entries.Skip(logged));
        Assert.Null(runtime.Errors.Window);
        patcher.RequestClose();
        Assert.False(patcher.IsVisible);
    }

    /// <summary>
    /// Through the composed app: a Scan started from the Scanner tab's Scan Game runs on the session's Scan module, and
    /// the Archive Patcher opened from the session asks that same module. Patch All refuses while the Scan is held
    /// mid-run, and runs once the Scan has ended.
    /// </summary>
    [AvaloniaFact]
    public void Patch_all_from_the_session_refuses_while_its_scan_runs_and_runs_once_it_ends()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        using var app = new ComposedSession(machine);
        // Set, never disposed: a worker still inside Wait would throw on a disposed event, and a slim event that never
        // handed out its wait handle holds nothing to free.
        var release = new ManualResetEventSlim();
        var entered = false;
        app.Session.Scan.RunScan = (_, _) =>
        {
            Volatile.Write(ref entered, true);
            release.Wait(TaskTimeouts.Default);
            return ScanOutcome.Empty;
        };
        try
        {
            var tabs = app.Window.GetLogicalDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 2;
            var scanner = Assert.IsType<ScannerTab>(app.Window.CurrentPage);
            DispatcherWait.Until(() => scanner.SidePane is not null);
            MouseClick.On(scanner.SidePane!.ScanButton);
            DispatcherWait.Until(() => Volatile.Read(ref entered) && !app.Session.Runtime.Input.IsBlocking);
            Assert.True(app.Session.Scan.IsRunning);

            MouseClick.On(app.TabButton(3, "Archive Patcher"));
            var patcher = app.WaitForArchivePatcher();
            Assert.Equal(11, patcher.Rows.Count);
            MouseClick.On(ComposedSession.Named<Button>(patcher, "PatchAll"));
            var box = patcher.OwnedWindows.OfType<MessageBoxWindow>().Single();
            Assert.Contains(box.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "A Scan is running. Patch All can start once it ends.");
            box.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(11, patcher.Rows.Count);

            release.Set();
            DispatcherWait.Until(() => !app.Session.Scan.IsRunning);
            MouseClick.On(ComposedSession.Named<Button>(patcher, "PatchAll"));
            DispatcherWait.Until(() => !app.Session.Runtime.Input.IsBlocking && patcher.Rows.Count == 1);

            // Everything but the read-only archive was patched to v1, so only it is still listed.
            Assert.Equal(["ReadOnly - Main.ba2"], patcher.Rows);
            Assert.Empty(patcher.OwnedWindows.OfType<MessageBoxWindow>());
            Assert.Null(app.Session.Runtime.Errors.Window);
        }
        finally
        {
            // Released even when an assert failed, so the held scan's worker doesn't wait out its timeout.
            release.Set();
        }
    }

    /// <summary>With no Scan running, Patch All patches the listed archives and lists what is left, with no warning.</summary>
    [AvaloniaFact]
    public async Task With_no_scan_running_patch_all_patches_the_archives()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        var (runtime, game, _) = Parts(machine);
        var overview = Loaded(runtime, previous => Gather(game, machine, previous));
        var patcher = Show(runtime, game, machine, overview, scanRunning: () => false);
        Assert.Equal(11, patcher.Rows.Count);

        await PatchAll(runtime, patcher).WithTimeout();

        // Everything but the read-only archive was patched to v1, so only it is still listed.
        Assert.Equal(["ReadOnly - Main.ba2"], patcher.Rows);
        Assert.Empty(patcher.OwnedWindows.OfType<MessageBoxWindow>());
        Assert.Null(runtime.Errors.Window);
    }

    /// <summary>
    /// A user close during Patch All waits for the run, the refresh and the repopulate to finish, then closes the
    /// window: the input block alone defers it, with nothing else guarding the close.
    /// </summary>
    [AvaloniaFact]
    public async Task A_user_close_during_patch_all_is_deferred_until_it_ends_then_closes_the_window()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        var game = Locate(machine);
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var refreshing = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var overview = Loaded(runtime, previous =>
        {
            if (previous is null)
            {
                return Gather(game, machine);
            }

            refreshing.SetResult();
            release.Task.Wait();
            return Gather(game, machine, previous);
        });
        var patcher = Show(runtime, game, machine, overview);

        var patchAll = PatchAll(runtime, patcher);
        await refreshing.Task.WithTimeout();
        UserClose.Request(patcher);
        Assert.True(patcher.IsVisible);
        release.SetResult();
        await patchAll;
        Dispatcher.UIThread.RunJobs();

        Assert.False(patcher.IsVisible);
        Assert.Null(runtime.Errors.Window);
    }

    /// <summary>
    /// The patch run dies on its worker after patching two archives. The window still refreshes the Overview Report, so
    /// its list drops the two it patched, input comes back so the window can close, and the failure is shown once.
    /// </summary>
    [AvaloniaFact]
    public async Task After_a_failed_patch_run_the_window_closes_and_lists_the_archives_current_versions()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        var game = Locate(machine);
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var report = Gather(game, machine);
        string[] patchedFirst = ["Plain - Main.ba2", "BOSS - Textures.ba2"];
        var nextGen = report.Archives.NextGen;
        // An embedded NUL is a path no file system accepts: resolving it throws past the run's per-archive failures,
        // so the run dies there, after the two archives listed before it are patched.
        var unpatchable = game.DataPath!.Join("Bad\0 - Main.ba2");
        var listed = report with
        {
            Archives = report.Archives with
            {
                NextGen = [.. nextGen.Where(p => patchedFirst.Contains(p.Name)), unpatchable, .. nextGen.Where(p => !patchedFirst.Contains(p.Name))],
            },
        };
        // The load lists the doctored report; the refresh gathers what is on disk.
        var overview = Loaded(runtime, previous => previous is null ? listed : Gather(game, machine, previous));
        var patcher = Show(runtime, game, machine, overview);
        Assert.Equal(12, patcher.Rows.Count);

        await PatchAll(runtime, patcher);

        Assert.Equal(
            ["Alias - Main.ba2", "Changed - Main.ba2", "Corrupt - Main.ba2", "Flipped - Main.ba2", "Gone - Main.ba2",
                "ReadOnly - Main.ba2", "Short - Main.ba2", "Straße - Main.ba2", "Alias - Main.ba2"],
            patcher.Rows);
        Assert.Single(logger.Messages, m => m.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith(ErrorReporter.OperationHeader("Archive Patcher"), runtime.Errors.Window!.Text);
        Assert.False(runtime.Input.IsBlocking);
        patcher.RequestClose();
        Assert.False(patcher.IsVisible);
    }

    /// <summary>
    /// The patch run succeeds, then the Overview refresh dies part-way with a report gathered after the run. The list
    /// shows that report's archives, input comes back so the window can close, and the failure is shown once.
    /// </summary>
    [AvaloniaFact]
    public async Task After_a_failed_overview_refresh_the_window_closes_and_lists_the_archives_new_versions()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        var game = Locate(machine);
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var overview = Loaded(runtime, previous => previous is null
            ? Gather(game, machine)
            : throw new OverviewIncompleteException(Gather(game, machine, previous), new IOException("The modules couldn't be read.")));
        var patcher = Show(runtime, game, machine, overview);
        Assert.Equal(11, patcher.Rows.Count);

        await PatchAll(runtime, patcher);

        // Everything but the read-only archive was patched to v1, so only it is still listed.
        Assert.Equal(["ReadOnly - Main.ba2"], patcher.Rows);
        Assert.Single(logger.Messages, m => m.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith(ErrorReporter.OperationHeader("Overview refresh"), runtime.Errors.Window!.Text);
        Assert.False(runtime.Input.IsBlocking);
        patcher.RequestClose();
        Assert.False(patcher.IsVisible);
    }

    /// <summary>
    /// Bug #88, fixed (ADR-0006): Escape closes the patcher while the log pane has keyboard focus. In the reference the
    /// pane's <c>&lt;Key&gt;</c> binding returned <c>break</c> for Escape too, so the window's Escape binding never ran.
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "T-13"), Deviation(88, "Ctrl+A and Ctrl+C work in the log pane whatever the lock keys and Shift, and Escape still closes its window.")]
    public void Escape_closes_the_patcher_while_the_log_pane_has_focus()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        using var app = new ComposedSession(machine);
        MouseClick.On(ComposedSession.Named<Button>(app.Window, "ArchivePatcher"));
        var patcher = app.WaitForArchivePatcher();

        MouseClick.On(patcher.Log.GetLogicalDescendants().OfType<SelectableTextBlock>().Single());
        Assert.True(patcher.Log.HasKeyboardFocus);
        patcher.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(patcher.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "AP-1")]
    public void Without_a_data_folder_the_window_opens_with_an_empty_list_and_the_assert_fails()
    {
        using var machine = Scenario("overview/no-data").Materialize();
        var (runtime, game, overview) = Parts(machine);
        Assert.Null(game.DataPath);
        var owner = new Window();
        owner.Show();
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview, scanRunning: () => false);

        Assert.Throws<InvalidOperationException>(() => patcher.ShowOver(owner));

        Assert.True(patcher.IsVisible);
        Assert.Empty(patcher.Rows);
        Assert.Empty(patcher.Log.Runs);
        patcher.Close();
        owner.Close();
    }

    /// <summary>
    /// The assert throws before Patch All's first operation, so no runner reports it. It reaches the Error Window the
    /// moment the button is clicked, not whenever (if ever) the garbage collector finds the task unobserved.
    /// </summary>
    [AvaloniaFact]
    public void Without_a_data_folder_patch_all_reports_the_assert_at_once()
    {
        using var machine = Scenario("overview/no-data").Materialize();
        var (runtime, game, overview) = Parts(machine);
        var owner = new Window();
        owner.Show();
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview, scanRunning: () => false);
        Assert.Throws<InvalidOperationException>(() => patcher.ShowOver(owner));

        MouseClick.On(ComposedSession.Named<Button>(patcher, "PatchAll"));

        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: assert self.cmc.game.data_path is not None",
            runtime.Errors.Window?.Text);
        // The check came before the input block, so nothing holds the window open.
        Assert.False(runtime.Input.IsBlocking);
        patcher.RequestClose();
        Assert.False(patcher.IsVisible);
        owner.Close();
    }

    /// <summary>
    /// An Archive Patcher whose window fails to open stops there: the opener throws, so the synchronous click that ran it
    /// reports the failure once through the UI thread's handler, and the list a window nobody can see would show is never
    /// filled.
    /// </summary>
    [AvaloniaFact]
    public void An_archive_patcher_that_fails_to_open_reports_it_once_and_lists_nothing()
    {
        using var machine = Scenario("archive-patcher/round-trip").Materialize();
        var logger = new CapturingLogger();
        var game = Locate(machine);
        var runtime = new AppRuntime(logger);
        var overview = FixedOverview.Load(runtime, Gather(game, machine));
        var owner = new Window();
        owner.Show();
        owner.Close();
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview, scanRunning: () => false);

        using (runtime.Errors.InstallGlobalHandlers())
        {
            // Posted, as a click's handler is dispatched, so the opener's throw reaches Dispatcher.UnhandledException.
            Dispatcher.UIThread.Post(() => patcher.ShowOver(owner));
            Dispatcher.UIThread.RunJobs();
        }

        // Counted by its header: with the global handlers in, a stray unobserved task from another test may land too.
        Assert.Single(logger.Entries, e => e.Message.StartsWith(
            $"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: ", StringComparison.Ordinal));
        Assert.False(patcher.IsVisible);
        Assert.Empty(patcher.Rows);
    }

    private static ParityScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id);

    /// <summary>The runtime, game and a loaded Overview Report module for a patcher built by hand.</summary>
    private static (AppRuntime Runtime, GameInstall Game, OverviewState Overview) Parts(MaterializedScenario machine)
    {
        var game = Locate(machine);
        var runtime = new AppRuntime(new CapturingLogger());
        return (runtime, game, FixedOverview.Load(runtime, Gather(game, machine)));
    }

    /// <summary>The scenario's game, found as startup finds it.</summary>
    private static GameInstall Locate(MaterializedScenario machine)
        => Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");

    /// <summary>Every archive on the machine and its bytes, by path, for telling whether anything was patched.</summary>
    private static Dictionary<string, byte[]> ArchiveBytes(MaterializedScenario machine)
        => Directory.EnumerateFiles(machine.Root, "*.ba2", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);

    /// <summary>A real Overview Report of the scenario's game as it is on disk now, logged nowhere the test reads.</summary>
    private static OverviewReport Gather(GameInstall game, MaterializedScenario machine, OverviewReport? previous = null)
        => OverviewReport.Gather(game, machine.Host, new CapturingLogger(), previous);

    /// <summary>An Overview Report module over <paramref name="gather"/>, loaded. Call it on the UI thread.</summary>
    private static OverviewState Loaded(AppRuntime runtime, Func<OverviewReport?, OverviewReport> gather)
    {
        var overview = new OverviewState(runtime, gather);
        Run(overview.LoadAsync());
        return overview;
    }

    /// <summary>A patcher built by hand and shown over a bare owner, its archives listed.</summary>
    /// <param name="scanRunning">Whether a Scan is running, as Patch All asks it; none says no Scan is running.</param>
    private static ArchivePatcherWindow Show(
        AppRuntime runtime, GameInstall game, MaterializedScenario machine, OverviewState overview, Func<bool>? scanRunning = null)
    {
        var owner = new Window();
        owner.Show();
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview, scanRunning ?? (() => false));
        patcher.ShowOver(owner);
        return patcher;
    }

    /// <summary>
    /// Starts Patch All as the button does, as a UI Action through <see cref="UiActions.Start"/>, so its failure is
    /// reported as a click's would be.
    /// </summary>
    /// <returns>The action's task, which completes when the run is over, failed or not, and never faults.</returns>
    private static Task PatchAll(AppRuntime runtime, ArchivePatcherWindow patcher)
        => runtime.Actions.Start(patcher.PatchAllAsync);

    private static void Run(Task task)
    {
        DispatcherWait.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
