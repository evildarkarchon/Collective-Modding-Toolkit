using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The F4SE tab's scrollbar (F4SE-4): a <c>ttk.Scrollbar</c> of its own beside the tree, wired both ways
/// (<c>command=tree_dlls.yview</c>, <c>yscrollcommand=scroll_tree_y.set</c>), on the <c>f4se/og-steam</c> machine, whose
/// 19 rows overflow the tab.
/// </summary>
public sealed class F4seTabTests
{
    [AvaloniaFact]
    [Trait("Parity", "F4SE-4")]
    public void The_scrollbar_and_the_rows_scroll_together()
    {
        var scenario = ParityScenarios.Of("f4se", "f4se-tab").Select(row => row.Data).Single(s => s.Id == "f4se/og-steam");
        using var machine = scenario.Materialize();
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult()!;
        var runtime = new AppRuntime(new CapturingLogger());
        var state = new OverviewState { Report = OverviewReport.Gather(game, machine.Host, new CapturingLogger()) };
        var window = TabHost.Show(runtime, new F4seTab(runtime, machine.Host, game, state));

        var content = TabHost.Content(window);
        var bar = content.GetLogicalDescendants().OfType<ScrollBar>().Distinct().Single(b => b.Name == "DllsScrollBar");
        var table = content.GetLogicalDescendants().OfType<TableView>().Distinct().Single();
        var viewer = table.GetVisualDescendants().OfType<ScrollViewer>().First();

        // Shown, with something to scroll, at the top.
        Assert.True(bar.IsEffectivelyVisible);
        Assert.True(bar.Maximum > 0);
        Assert.Equal(0, bar.Value);

        // The bar moves the rows...
        bar.Value = bar.Maximum;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(bar.Maximum, viewer.Offset.Y, 3);

        // ...and scrolling the rows moves the bar.
        table.ScrollIntoView(0);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(0, viewer.Offset.Y);
        Assert.Equal(0, bar.Value);
    }
}
