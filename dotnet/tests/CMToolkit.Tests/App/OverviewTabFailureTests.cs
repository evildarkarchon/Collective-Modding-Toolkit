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
    /// The refresh button is a UI Action started through <see cref="ErrorReporter.Handle"/>: a gathering that dies is
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

    [AvaloniaFact]
    [Trait("Parity", "OVW-8")]
    public void A_refresh_click_that_fails_after_its_gathering_is_reported_in_the_error_window()
    {
        // A report the frames can't be built from: the gathering succeeded, so the runner had nothing to report, and the
        // failure comes from rebuilding the frames on the UI thread. The click starts RefreshAsync through
        // ErrorReporter.Handle, which reports it, as a Tk callback's exception reached report_callback_exception.
        using var machine = ParityScenarios.All().Single(s => s.Id == "overview/modules-archives-mixed").Materialize();
        var gathered = 0;
        var (window, state, runtime, _) = Show(machine, (game, host, logger, previous) =>
        {
            var report = OverviewReport.Gather(game, host, logger, previous);
            return ++gathered == 1 ? report : report with { Modules = null! };
        });

        MouseClick.On(Named<Button>(window, "Refresh"));
        DispatcherWait.Until(() => runtime.Errors.Window is not null && !runtime.Input.IsBlocking);

        Assert.Equal(2, gathered);
        Assert.Contains(ErrorReporter.UiThreadHeader, runtime.Errors.Window!.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(NullReferenceException), runtime.Errors.Window.Text, StringComparison.Ordinal);
    }

    private static (MainWindow Window, OverviewState State, AppRuntime Runtime, List<string> Warnings) Show(
        MaterializedScenario machine,
        Func<GameInstall, CMToolkit.Core.IHostEnvironment, Microsoft.Extensions.Logging.ILogger, OverviewReport?, OverviewReport>? gatherer = null)
    {
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult()!;
        var runtime = new AppRuntime(new CapturingLogger());
        var state = new OverviewState();
        var warnings = new List<string>();
        var tab = new OverviewTab(runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
        {
            OpenFolder = _ => { },
            ShowWarning = (_, message) =>
            {
                warnings.Add(message);
                return Task.CompletedTask;
            },
            Gatherer = gatherer ?? OverviewReport.Gather,
        };
        return (TabHost.Show(runtime, tab), state, runtime, warnings);
    }

    private static string? Text(MainWindow window, string name) => Named<TextBlock>(window, name).Text;

    private static T Named<T>(MainWindow window, string name)
        where T : Control
        => TabHost.Content(window).GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
}
