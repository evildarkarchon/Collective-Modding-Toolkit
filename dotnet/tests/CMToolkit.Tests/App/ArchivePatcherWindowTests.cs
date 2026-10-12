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
/// How the Archive Patcher is reached and how it behaves around a run, beyond what its scenarios replay: both Archive
/// Patcher buttons open it over the main window (OVW-A6, TOOL-1), a radio click on the selected radio still clears and
/// lists again (AP-2), Patch All holds the UI through the Overview refresh and keeps the close guard after a failure
/// (AP-3), Escape closes it even while the log pane has focus (bug #88, fixed), and a game without a Data folder fails the
/// reference's assert.
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
    public void Patch_all_holds_the_input_block_through_the_overview_refresh()
    {
        using var machine = Scenario("archive-patcher/nothing-to-do").Materialize();
        var (runtime, game, overview) = Parts(machine);
        bool? blockedDuringRefresh = null;
        var patcher = Show(runtime, game, machine, overview, () =>
        {
            blockedDuringRefresh = runtime.Input.IsBlocking;
            return Task.FromResult(true);
        });

        Run(patcher.PatchAllAsync());

        Assert.True(blockedDuringRefresh);
        Assert.False(runtime.Input.IsBlocking);
        Assert.False(patcher.ProcessingData);
    }

    /// <summary>
    /// The reference's refresh raised out of <c>_patch_wrapper</c>, skipping the repopulate and leaving
    /// <c>processing_data</c> set, so Escape and the close button do nothing afterwards.
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "AP-3")]
    public void A_failed_overview_refresh_skips_the_repopulate_and_leaves_the_close_guard_set()
    {
        using var machine = Scenario("archive-patcher/nothing-to-do").Materialize();
        var (runtime, game, overview) = Parts(machine);
        var patcher = Show(runtime, game, machine, overview, () => Task.FromResult(false));

        Run(patcher.PatchAllAsync());

        Assert.True(patcher.ProcessingData);
        // The "Showing" line of the open, then the run's, and no second "Showing" line.
        Assert.Equal(
            ["Showing 0 files to be patched.\n", "Nothing to do!\n"],
            patcher.Log.Runs.Where((_, i) => i % 2 == 1).Select(r => r.Text));
        patcher.RequestClose();
        Assert.True(patcher.IsVisible);
        patcher.ProcessingData = false;
        patcher.Close();
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
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview, () => Task.FromResult(true));

        Assert.Throws<InvalidOperationException>(() => patcher.ShowOver(owner));

        Assert.True(patcher.IsVisible);
        Assert.Empty(patcher.Rows);
        Assert.Empty(patcher.Log.Runs);
        patcher.Close();
        owner.Close();
    }

    private static ParityScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id);

    /// <summary>The runtime, game and a loaded Overview state for a patcher built by hand.</summary>
    private static (AppRuntime Runtime, GameInstall Game, OverviewState Overview) Parts(MaterializedScenario machine)
    {
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var overview = new OverviewState { Report = OverviewReport.Gather(game, machine.Host, new CapturingLogger()) };
        return (new AppRuntime(new CapturingLogger()), game, overview);
    }

    private static ArchivePatcherWindow Show(
        AppRuntime runtime, GameInstall game, MaterializedScenario machine, OverviewState overview, Func<Task<bool>> refresh)
    {
        var owner = new Window();
        owner.Show();
        var patcher = new ArchivePatcherWindow(runtime, game, machine.Host, overview, refresh);
        patcher.ShowOver(owner);
        return patcher;
    }

    private static void Run(Task task)
    {
        DispatcherWait.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
