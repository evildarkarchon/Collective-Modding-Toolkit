using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Tools tab against the golden read off the reference's widgets (<c>tools-tab</c>): column titles, button texts,
/// what each button opens when clicked, and the hover and info-icon tooltips (the scenario cites TOOL-1, TOOL-2 and
/// NET-4). The utility windows are recorders here, as the driver's are.
/// </summary>
public sealed class ToolsTabScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("tools", "tools-tab");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_tools_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var browser = new RecordingBrowser();
        var windows = new List<string>();
        var runtime = new AppRuntime(new CapturingLogger()) { Browser = browser };
        var tab = new ToolsTab(runtime, () => windows.Add("Downgrader"), () => windows.Add("ArchivePatcher"));
        var window = TabHost.Show(runtime, tab);

        var columns = new JsonArray();
        foreach (var frame in TabHost.Content(window).GetLogicalChildren().OfType<GroupBox>())
        {
            var buttons = new JsonArray();
            var grid = (Grid)frame.Content!;
            foreach (var button in grid.Children.OfType<Button>())
            {
                var (urls, opened) = (browser.Opened.Count, windows.Count);
                MouseClick.On(button);
                JsonObject? opens = browser.Opened.Count > urls
                    ? new JsonObject { ["url"] = browser.Opened[^1] }
                    : windows.Count > opened ? new JsonObject { ["window"] = windows[^1] } : null;

                var info = grid.Children.OfType<Border>().SingleOrDefault(b => Grid.GetRow(b) == Grid.GetRow(button));
                buttons.Add(new JsonObject
                {
                    ["text"] = (string)button.Content!,
                    ["enabled"] = button.IsEnabled,
                    ["opens"] = opens,
                    ["tooltip"] = ToolTip.GetTip(button) as string,
                    ["info"] = info is null ? null : new JsonObject
                    {
                        ["image"] = ((AssetImage)info.Child!).AssetPath,
                        ["tooltip"] = ToolTip.GetTip(info) as string,
                    },
                });
            }

            columns.Add(new JsonObject { ["title"] = ((TextBlock)frame.Header!).Text, ["buttons"] = buttons });
        }

        var actual = new JsonObject { ["toolsTab"] = new JsonObject { ["columns"] = columns } };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }
}
