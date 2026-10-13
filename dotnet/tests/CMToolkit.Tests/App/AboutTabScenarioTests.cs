using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace CMToolkit.Tests.App;

/// <summary>
/// The About tab against the golden read off the reference's widgets (<c>about-tab</c>): title, icon, text, and per
/// link row its logo, what Open opens, what Copy copies, Copy's state once clicked, its revert delay and its state
/// after (the scenario cites ABOUT-1, NET-4 and THR-3). The clock is a fake, so the 3 s revert is stepped exactly.
/// </summary>
public sealed class AboutTabScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("about", "about-tab");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_about_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var browser = new RecordingBrowser();
        var time = new FakeTimeProvider();
        var runtime = new AppRuntime(new CapturingLogger()) { Browser = browser, Time = time };
        var window = TabHost.Show(runtime, new AboutTab(runtime));
        var content = (Panel)TabHost.Content(window);

        var texts = content.Children.OfType<TextBlock>().ToList();
        var about = content.Children.OfType<StackPanel>().Single();
        var links = new JsonArray();
        foreach (var row in about.Children.OfType<Grid>())
        {
            var buttons = row.Children.OfType<Button>().ToList();
            var (open, copy) = (buttons[0], buttons[1]);
            var copyText = (string)copy.Content!;

            MouseClick.On(open);
            MouseClick.On(copy);
            DispatcherWait.Until(() => !copy.IsEnabled);
            var clicked = State(copy);
            var copied = window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();

            // after(3000): not a millisecond sooner.
            time.Advance(CopyTextButton.RevertAfter - TimeSpan.FromMilliseconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.False(copy.IsEnabled);
            time.Advance(TimeSpan.FromMilliseconds(1));
            DispatcherWait.Until(() => copy.IsEnabled);

            links.Add(new JsonObject
            {
                ["logo"] = row.Children.OfType<AssetImage>().Single().AssetPath,
                ["open"] = new JsonObject { ["text"] = (string)open.Content!, ["opens"] = browser.Opened[^1] },
                ["copy"] = new JsonObject
                {
                    ["text"] = copyText,
                    ["copies"] = copied,
                    ["clicked"] = clicked,
                    ["revertAfterMs"] = CopyTextButton.RevertAfter.TotalMilliseconds,
                    ["reverted"] = State(copy),
                },
            });
        }

        var actual = new JsonObject
        {
            ["aboutTab"] = new JsonObject
            {
                ["title"] = texts.Single(t => t.Classes.Contains("large")).Text,
                ["icon"] = content.GetLogicalDescendants().OfType<AssetImage>().First().AssetPath,
                ["text"] = about.Children.OfType<TextBlock>().Single().Text,
                ["links"] = links,
            },
        };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    private static JsonObject State(Button button) => new()
    {
        ["text"] = (string)button.Content!,
        ["enabled"] = button.IsEnabled,
    };
}
