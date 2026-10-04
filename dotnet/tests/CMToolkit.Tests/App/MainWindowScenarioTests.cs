using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.App;

/// <summary>
/// The shell's window facts against the golden the driver read off the real <c>CMChecker</c>'s Tk root: title, client
/// size, resizability and tab order (the scenario cites SHELL-6 and SHELL-7). The same scenario drives the shell
/// screenshot pair in <c>parity/screenshots/shell/</c>.
/// </summary>
public sealed class MainWindowScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("shell", "main-window");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_shell_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // The shell reads nothing from the machine yet; later slices build the view model from machine.Host.
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, "github", null)),
        };
        window.Show();

        var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
        var actual = new JsonObject
        {
            ["window"] = new JsonObject
            {
                ["title"] = window.Title,
                ["width"] = window.ClientSize.Width,
                ["height"] = window.ClientSize.Height,
                ["resizable"] = window.CanResize,
                ["tabs"] = new JsonArray([.. tabs.Items.Cast<TabItem>().Select(t => JsonValue.Create(t.Header as string))]),
            },
        };

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }
}
