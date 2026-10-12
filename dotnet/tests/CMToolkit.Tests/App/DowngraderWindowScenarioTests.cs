using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Core.Downgrader;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Downgrader window against the <c>downgrader/*</c> goldens read off the reference's widgets
/// (<c>downgrader-window</c>): opened over a main window whose Overview has gathered its report, as at startup, then
/// read back: both Current frames with each value's colour (DG-3), the Desired Version default, the options from
/// settings with their tooltips, the buttons and what About opens, the seeded log pane and the progress bar (DG-2), and
/// the detection behind them (DG-1). Compared in full.
/// </summary>
public sealed class DowngraderWindowScenarioTests
{
    /// <summary>The Tk colour names the reference's labels use, by the colour the port draws them in.</summary>
    private static readonly Dictionary<Color, string> TkColours = new()
    {
        [Color.Parse("#AF5A66")] = "#AF5A66",
        [Color.Parse("#808080")] = "gray",
        [Color.Parse("#1E90FF")] = "dodger blue",
        [Color.Parse("#836FFF")] = "SlateBlue1",
        [Color.Parse("#FA8072")] = "salmon",
    };

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("downgrader", "downgrader-window");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_downgrader_window_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd!), AppFolderFiles.SettingsFileName);
        var settings = SettingsStore.Load(settingsPath, DownloadSource.Nexus, new CapturingLogger());
        var overview = new OverviewState { Report = OverviewReport.Gather(game, machine.Host, new CapturingLogger()) };
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var owner = new Window();
        owner.Show();

        var window = new DowngraderWindow(runtime, game, machine.Host, overview, settings);
        var opening = window.OpenAsync(owner);
        DispatcherWait.Until(() => opening.IsCompleted);
        opening.GetAwaiter().GetResult();
        window.UpdateLayout();

        var about = Named<Button>(window, "About");
        var actual = new JsonObject
        {
            ["downgrader"] = DowngraderScenarioTests.Project(overview.InstallType, window.Versions!),
        };
        var display = new JsonObject
        {
            ["title"] = window.Title,
            ["width"] = (int)window.Width,
            ["height"] = (int)window.Height,
            ["game"] = Current(window, "Game", "Current Game"),
            ["creationKit"] = Current(window, "CreationKit", "Current Creation Kit"),
            ["desiredVersion"] = new JsonObject
            {
                ["title"] = FrameTitle(window, "DesiredVersion"),
                ["radios"] = new JsonArray([.. new[] { "OldGen", "NextGen" }.Select(name =>
                {
                    var radio = Named<RadioButton>(window, name);
                    return (JsonNode)new JsonObject { ["text"] = (string)radio.Content!, ["selected"] = radio.IsChecked == true };
                })]),
            },
            ["options"] = new JsonObject
            {
                ["title"] = FrameTitle(window, "Options"),
                ["checks"] = new JsonArray([.. new[] { "KeepBackups", "DeletePatches" }.Select(name =>
                {
                    var check = Named<CheckBox>(window, name);
                    return (JsonNode)new JsonObject
                    {
                        ["text"] = (string)check.Content!,
                        ["checked"] = check.IsChecked == true,
                        ["tooltip"] = ToolTip.GetTip(check) as string,
                    };
                })]),
            },
            ["patch"] = new JsonObject
            {
                ["text"] = (string)Named<Button>(window, "PatchAll").Content!,
                ["enabled"] = Named<Button>(window, "PatchAll").IsEnabled,
            },
            ["about"] = new JsonObject { ["text"] = (string)about.Content!, ["opens"] = ClickAbout(window, about) },
            ["log"] = LogRuns(Named<LogPane>(window, "Log")),
            ["progress"] = new JsonObject
            {
                ["value"] = Named<ProgressBar>(window, "Progress").Value,
                ["maximum"] = Named<ProgressBar>(window, "Progress").Maximum,
            },
            ["processingData"] = window.ProcessingData,
            ["logLines"] = new JsonArray([.. logger.Entries.Select(e => (JsonNode)new JsonObject
            {
                ["level"] = LogFile.LevelName(e.Level),
                ["message"] = e.Message,
            })]),
        };
        actual["downgrader"]!["display"] = display;

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
        window.Close();
        owner.Close();
    }

    /// <summary>One Current frame as the driver's <c>current</c> reads it: the title, the names label and each value.</summary>
    private static JsonObject Current(Window window, string prefix, string title) => new()
    {
        ["title"] = FrameTitle(window, prefix + "Frame"),
        ["names"] = Named<TextBlock>(window, prefix + "Names").Text,
        ["values"] = new JsonArray([.. Enumerable.Range(0, 3).Select(i =>
        {
            var value = Named<TextBlock>(window, $"{prefix}{i}");
            return (JsonNode)new JsonObject { ["text"] = value.Text, ["color"] = Colour(value.Foreground) };
        })]),
    };

    /// <summary>Clicks About and records the AboutWindow it opens over the Downgrader, then closes it.</summary>
    private static JsonObject? ClickAbout(DowngraderWindow window, Button about)
    {
        // Raised, not clicked with the mouse: the driver invoke()s the button, and where a long version label widens the
        // first columns past the window's 600 px, the weighted third column (Patch All, About) shrinks to nothing, in
        // the reference as here, so there is nothing to click.
        about.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var opened = window.OwnedWindows.OfType<AboutWindow>().SingleOrDefault();
        if (opened is null)
        {
            return null;
        }

        var result = new JsonObject
        {
            ["window"] = "AboutWindow",
            ["width"] = (int)opened.Width,
            ["height"] = (int)opened.Height,
            ["title"] = opened.Title,
            ["text"] = opened.GetLogicalDescendants().OfType<TextBlock>().First().Text,
        };
        opened.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return result;
    }

    /// <summary>
    /// The log pane's text as the driver's <c>_log_pane_runs</c> records it: each run with its own colour, or none when it
    /// only inherits the pane's (an untagged range in Tk).
    /// </summary>
    private static JsonArray LogRuns(LogPane pane) => new([.. pane.Runs.Select(run => (JsonNode)new JsonObject
    {
        ["text"] = run.Text,
        ["color"] = run.IsSet(TextElement.ForegroundProperty) ? Colour(run.Foreground) : null,
    })]);

    private static string FrameTitle(Window window, string name) => ((TextBlock)Named<GroupBox>(window, name).Header!).Text!;

    private static string Colour(IBrush? brush)
    {
        var colour = ((ISolidColorBrush)brush!).Color;
        return TkColours.TryGetValue(colour, out var name) ? name : colour.ToString();
    }

    private static T Named<T>(Window window, string name)
        where T : Control
        => window.GetLogicalDescendants().OfType<T>().Where(c => c.Name == name).Distinct().Single();
}
