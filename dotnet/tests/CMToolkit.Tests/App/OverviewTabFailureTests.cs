using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Overview tab when its gathering dies on a refresh, which the scenarios (each recording one machine) can't
/// reach: the error is reported, the frames stay as they were, and the shared report becomes what the gathering had
/// got to, as the reference's shared state did; and the load's warning boxes are not shown again by the refresh.
/// </summary>
public sealed class OverviewTabFailureTests
{
    [AvaloniaFact]
    [Trait("Parity", "OVW-8")]
    public void A_refresh_that_dies_keeps_the_frames_and_shares_what_it_had_gathered()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var (window, state, runtime, warnings) = Show(machine);
        var loaded = state.Report!;
        var countBefore = Text(window, "ArchiveCount0");

        // Since the load: the local app data folder went away, which kills the modules step when it looks for
        // plugins.txt. (An archive format that isn't UTF-8 did this before the port fixed bug #86.)
        Directory.Delete(machine.PathOf("LocalAppData"), recursive: true);
        MouseClick.On(Named<Button>(window, "Refresh"));
        DispatcherWait.Until(() => !ReferenceEquals(state.Report, loaded) && !runtime.Input.IsBlocking);

        Assert.NotNull(runtime.Errors.Window);
        // The modules were gathered again up to plugins.txt; the archives step never ran, so they are the load's; the
        // frames weren't rebuilt and their problems weren't appended.
        Assert.InRange(state.Report!.Modules.Enabled.Count, 1, loaded.Modules.Enabled.Count - 1);
        Assert.Same(loaded.Archives, state.Report.Archives);
        Assert.DoesNotContain(state.Report.Problems, p => p.Kind == "Wrong Version");
        Assert.Equal(countBefore, Text(window, "ArchiveCount0"));
        // This machine has its CC list and plugins.txt, so neither the load nor the failed refresh showed a warning.
        Assert.Empty(warnings);
    }

    /// <summary>
    /// A refresh whose gathering dies signals it by propagating the runner's <see cref="OperationFailedException"/>, so a
    /// caller's rest (the Archive Patcher's repopulate, the Scanner's worker) is skipped as the exception skipped it in
    /// Tk. It still shares what the gathering had got to first, and the runner's report is the only one.
    /// </summary>
    [AvaloniaFact]
    public void A_refresh_whose_gathering_dies_propagates_the_failure_after_sharing_what_it_had_gathered()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var (_, state, runtime, _) = Show(machine);
        var loaded = state.Report!;
        var logger = (CapturingLogger)runtime.Logger;
        Directory.Delete(machine.PathOf("LocalAppData"), recursive: true);

        Task refresh = state.RefreshAsync();
        DispatcherWait.Until(() => refresh.IsCompleted);

        Assert.IsType<OperationFailedException>(refresh.Exception?.InnerException);
        Assert.NotSame(loaded, state.Report);
        Assert.Same(loaded.Archives, state.Report!.Archives);
        Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The refresh button is a UI Action started through <see cref="UiActions.Start"/>: a gathering that dies is
    /// reported once, by the runner under the refresh's name, and not a second time by the click.
    /// </summary>
    [AvaloniaFact]
    public void A_refresh_click_whose_gathering_dies_is_reported_once()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var (window, state, runtime, _) = Show(machine);
        var loaded = state.Report;
        var logger = (CapturingLogger)runtime.Logger;
        Directory.Delete(machine.PathOf("LocalAppData"), recursive: true);

        MouseClick.On(Named<Button>(window, "Refresh"));
        DispatcherWait.Until(() => !ReferenceEquals(state.Report, loaded) && !runtime.Input.IsBlocking);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var report = Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Overview refresh")}", report.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    [Trait("Parity", "OVW-M5")]
    public void The_warnings_are_shown_by_the_load_only()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/plugins-fallback").Materialize();
        var (window, state, runtime, warnings) = Show(machine);
        Assert.Equal(["Fallout4.ccc not found.", "plugins.txt not found."], warnings.Select(w => w.Split('\n')[0]));

        var loaded = state.Report;
        MouseClick.On(Named<Button>(window, "Refresh"));
        DispatcherWait.Until(() => !ReferenceEquals(state.Report, loaded) && !runtime.Input.IsBlocking);

        Assert.Equal(2, warnings.Count);
        Assert.Equal(2, state.Report!.Warnings.Count);
    }

    /// <summary>
    /// A load that dies part-way still shows the warnings it had raised, from the partial report it left current, and
    /// the tab stays on its loading text.
    /// </summary>
    [AvaloniaFact]
    public void A_load_that_dies_part_way_shows_the_warnings_it_had_raised()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var partial = new OverviewReport(InstallType.OldGen, [], null, [])
        {
            Warnings = [new OverviewWarning("Warning", "Fallout4.ccc not found.")],
        };
        var (_, runtime, state, warnings, tab) = Build(
            machine, _ => _ => throw new OverviewIncompleteException(partial, new IOException("plugins.txt couldn't be read.")));
        var window = new MainWindow(runtime, [tab]);
        window.Show();

        DispatcherWait.Until(() => runtime.Errors.Window is not null && !runtime.Input.IsBlocking);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(partial, state.Report);
        Assert.Equal(["Fallout4.ccc not found."], warnings);
        var lazy = window.GetLogicalDescendants().OfType<LazyTab>().Distinct().Single();
        Assert.Equal(tab.LoadingText ?? "", Assert.IsType<TextBlock>(lazy.Content).Text);
    }

    /// <summary>
    /// The frames follow every successful refresh, not only the tab's own button's: here the refresh is started on the
    /// module directly, as the Scanner, the Archive Patcher and the Downgrader start it.
    /// </summary>
    [AvaloniaFact]
    public void A_refresh_started_anywhere_rebuilds_the_frames()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var gathered = 0;
        var (window, state, runtime, _) = Show(machine, gather => previous =>
        {
            var report = gather(previous);
            return ++gathered == 1 ? report : report with { Archives = OverviewArchives.None };
        });
        var countBefore = Text(window, "ArchiveCount0");

        var refresh = state.RefreshAsync();
        DispatcherWait.Until(() => refresh.IsCompleted);
        refresh.GetAwaiter().GetResult();

        Assert.NotEqual(countBefore, Text(window, "ArchiveCount0"));
        Assert.Null(runtime.Errors.Window);
    }

    /// <summary>
    /// A report the frames can't be built from: the gathering succeeded, so the runner had nothing to report, and the
    /// failure comes from rebuilding the frames. The rebuild is a UI Action of its own, so the failure goes to the Error
    /// Window, and the refresh itself, whoever started it, still succeeds.
    /// </summary>
    [AvaloniaFact]
    public void A_frame_rebuild_that_fails_is_reported_in_the_error_window_and_not_to_the_refresh()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var gathered = 0;
        var (_, state, runtime, _) = Show(machine, gather => previous =>
        {
            var report = gather(previous);
            return ++gathered == 1 ? report : report with { Modules = null! };
        });

        var refresh = state.RefreshAsync();
        DispatcherWait.Until(() => refresh.IsCompleted);

        Assert.True(refresh.IsCompletedSuccessfully);
        Assert.Equal(2, gathered);
        Assert.Contains(ErrorReporter.UiThreadHeader, runtime.Errors.Window!.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(NullReferenceException), runtime.Errors.Window.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tab whose content was never built has no frames to rebuild, so it ignores a refresh: a report its frames
    /// couldn't be built from reaches no Error Window.
    /// </summary>
    [AvaloniaFact]
    public void A_refresh_before_the_content_is_built_is_ignored()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var report = new OverviewReport(InstallType.OldGen, [], null, []);
        var gathered = 0;
        var (_, runtime, state, _, _) = Build(machine, _ => _ => ++gathered == 1 ? report : report with { Modules = null! });

        var load = state.LoadAsync();
        DispatcherWait.Until(() => load.IsCompleted);
        var refresh = state.RefreshAsync();
        DispatcherWait.Until(() => refresh.IsCompleted);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(refresh.IsCompletedSuccessfully);
        Assert.Equal(2, gathered);
        Assert.Null(runtime.Errors.Window);
    }

    /// <summary>Shows the Overview tab, loaded; see <see cref="Build"/> for <paramref name="gatherer"/>.</summary>
    private static (MainWindow Window, OverviewState State, AppRuntime Runtime, List<string> Warnings) Show(
        MaterializedScenario machine, Func<Func<OverviewReport?, OverviewReport>, Func<OverviewReport?, OverviewReport>>? gatherer = null)
    {
        var (_, runtime, state, warnings, tab) = Build(machine, gatherer);
        return (TabHost.Show(runtime, tab), state, runtime, warnings);
    }

    /// <summary>Builds the Overview tab over a new module, with its warning boxes recorded.</summary>
    /// <param name="machine">The scenario's machine.</param>
    /// <param name="gatherer">
    /// Makes the module's gather function from the real gathering on <paramref name="machine"/>; null keeps the real one.
    /// </param>
    private static (GameInstall Game, AppRuntime Runtime, OverviewState State, List<string> Warnings, OverviewTab Tab) Build(
        MaterializedScenario machine, Func<Func<OverviewReport?, OverviewReport>, Func<OverviewReport?, OverviewReport>>? gatherer = null)
    {
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult()!;
        var runtime = new AppRuntime(new CapturingLogger());
        Func<OverviewReport?, OverviewReport> real = previous => OverviewReport.Gather(game, machine.Host, runtime.Logger, previous);
        var state = new OverviewState(runtime, gatherer?.Invoke(real) ?? real);
        var warnings = new List<string>();
        var tab = new OverviewTab(runtime, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
        {
            OpenFolder = _ => { },
            ShowWarning = (_, message) =>
            {
                warnings.Add(message);
                return Task.CompletedTask;
            },
        };
        return (game, runtime, state, warnings, tab);
    }

    private static string? Text(MainWindow window, string name) => Named<TextBlock>(window, name).Text;

    private static T Named<T>(MainWindow window, string name)
        where T : Control
        => TabHost.Content(window).GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
}
