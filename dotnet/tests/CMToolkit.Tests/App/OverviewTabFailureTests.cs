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

    private static (MainWindow Window, OverviewState State, AppRuntime Runtime, List<string> Warnings) Show(MaterializedScenario machine)
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
        };
        return (TabHost.Show(runtime, tab), state, runtime, warnings);
    }

    private static string? Text(MainWindow window, string name) => Named<TextBlock>(window, name).Text;

    private static T Named<T>(MainWindow window, string name)
        where T : Control
        => TabHost.Content(window).GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
}
