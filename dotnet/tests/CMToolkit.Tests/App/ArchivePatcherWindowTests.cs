using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// How the Archive Patcher is reached and how it behaves around a run, beyond what its scenarios replay: the Archive
/// Patcher buttons on the Overview and Tools tabs both open it over the main window, a click on the radio that is already
/// selected still clears the pane and lists again, Patch All holds the input block through the Overview refresh, a
/// failed patch run or Overview refresh still leaves a window that closes and lists the archives' current versions,
/// Escape closes it even while the log pane has focus (bug #88, fixed), and a game without a Data folder fails Patch
/// All's Data folder check.
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
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview);

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
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview);
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

    private static ArchivePatcherWindow Show(
        AppRuntime runtime, GameInstall game, MaterializedScenario machine, OverviewState overview)
    {
        var owner = new Window();
        owner.Show();
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview);
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
