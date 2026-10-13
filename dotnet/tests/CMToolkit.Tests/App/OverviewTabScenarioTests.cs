using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Overview tab against the <c>overview/*</c> goldens read off the reference's widgets (<c>overview-tab</c>): the top
/// block, the Binaries frame with each value's hover text, what clicking the MO2 icon, the game path and the Downgrade
/// Manager button does, the log lines, and the report behind them; then the same after the refresh button. Compared in
/// full. The Downgrader and the folder opener are recorders, as the driver's are.
/// </summary>
public sealed class OverviewTabScenarioTests
{
    /// <summary>The Tk colour names the reference's labels use, by the colour the port draws them in.</summary>
    private static readonly Dictionary<Color, string> TkColours = new()
    {
        [Color.Parse("#CACACA")] = "#CACACA",
        [Color.Parse("#619267")] = "#619267",
        [Color.Parse("#AF5A66")] = "#AF5A66",
        [Color.Parse("#808080")] = "gray",
        [Color.Parse("#FFE4C4")] = "bisque",
    };

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("overview", "overview-tab");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_overview_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var pc = PcInfo.Read(machine.Host, new CapturingLogger());
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var state = new OverviewState();
        var opened = new List<JsonObject>();
        var tab = new OverviewTab(runtime, machine.Host, pc, game, state, () => opened.Add(new JsonObject { ["window"] = "Downgrader" }))
        {
            OpenFolder = folder => opened.Add(new JsonObject { ["folder"] = Tokenise(machine, folder) }),
            // Recorded with get_info_modules and get_info_archives stubbed out, so they are skipped here too.
            Gatherer = (g, h, l, previous) => OverviewReport.Gather(g, h, l, previous, modulesAndArchives: false),
        };
        var window = TabHost.Show(runtime, tab);

        var loaded = Read(machine, window, state, logger, opened, firstLine: 0);
        var refreshFrom = logger.Entries.Count;
        var before = state.Report;
        MouseClick.On(Named<Button>(window, "Refresh"));
        DispatcherWait.Until(() => !ReferenceEquals(state.Report, before) && !runtime.Input.IsBlocking);
        window.UpdateLayout();
        var refreshed = Read(machine, window, state, logger, opened, refreshFrom);

        var actual = new JsonObject { ["overview"] = loaded, ["refresh"] = refreshed };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    /// <summary>The tab as the driver's <c>read</c> records it: the report, then what is shown, clicking as it goes.</summary>
    private static JsonObject Read(
        MaterializedScenario machine, MainWindow window, OverviewState state, CapturingLogger logger, List<JsonObject> opened, int firstLine)
    {
        JsonObject? Click(Control control)
        {
            var before = opened.Count;
            MouseClick.On(control);
            var about = window.OwnedWindows.OfType<AboutWindow>().SingleOrDefault();
            if (about is not null)
            {
                opened.Add(new JsonObject
                {
                    ["window"] = "AboutWindow",
                    ["width"] = (int)about.Width,
                    ["height"] = (int)about.Height,
                    ["title"] = about.Title,
                    ["text"] = Tokenise(machine, about.GetLogicalDescendants().OfType<TextBlock>().First().Text!),
                });
                about.Close();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            return opened.Count > before ? opened[^1] : null;
        }

        var managerIcon = NamedOrNull<Control>(window, "ModManagerIcon");
        var managerLabel = Named<TextBlock>(window, "ModManager");
        var pathLabel = Named<TextBlock>(window, "GamePath");
        var versionLabel = Named<TextBlock>(window, "Version");
        var osIcon = NamedOrNull<Control>(window, "OsWarning");
        var refresh = Named<Button>(window, "Refresh");
        var top = new JsonObject
        {
            ["labels"] = Named<TextBlock>(window, "TopLabels").Text,
            ["modManager"] = new JsonObject
            {
                ["text"] = managerLabel.Text,
                ["color"] = Colour(managerLabel),
                ["tooltip"] = ToolTip.GetTip(managerLabel) as string,
            },
            ["modManagerIcon"] = managerIcon is null ? null : new JsonObject
            {
                ["image"] = Image(managerIcon),
                ["tooltip"] = ToolTip.GetTip(managerIcon) as string,
                ["opens"] = Click(managerIcon),
            },
            ["gamePath"] = new JsonObject
            {
                ["text"] = Tokenise(machine, pathLabel.Text!),
                ["color"] = Colour(pathLabel),
                ["tooltip"] = ToolTip.GetTip(pathLabel) as string,
                ["opens"] = Click(pathLabel),
            },
            ["version"] = new JsonObject { ["text"] = versionLabel.Text, ["color"] = Colour(versionLabel) },
            ["specs"] = new JsonArray([.. new[] { "Specs1", "Specs2" }.Select(name =>
            {
                var specs = Named<TextBlock>(window, name);
                return (JsonNode)new JsonObject { ["text"] = specs.Text, ["color"] = Colour(specs) };
            })]),
            ["osWarning"] = osIcon is null ? null : new JsonObject
            {
                ["image"] = Image(osIcon),
                ["tooltip"] = ToolTip.GetTip(osIcon) as string,
            },
            ["refresh"] = new JsonObject { ["image"] = Image(refresh), ["tooltip"] = ToolTip.GetTip(refresh) as string },
        };

        var frame = Named<GroupBox>(window, "Binaries");
        var rows = new JsonArray();
        for (var i = 0; i < state.Report!.Binaries.Count; i++)
        {
            var value = Named<TextBlock>(window, $"Binary{i}");
            var text = value.Text;
            var (hover, left) = Hover(window, value);
            rows.Add(new JsonObject { ["text"] = text, ["color"] = Colour(value), ["hover"] = hover, ["left"] = left });
        }

        var addressLibrary = Named<TextBlock>(window, "AddressLibrary");
        var downgrade = Named<Button>(window, "DowngradeManager");
        var binaries = new JsonObject
        {
            ["title"] = ((TextBlock)frame.Header!).Text,
            ["names"] = Named<TextBlock>(window, "BinaryNames").Text,
            ["rows"] = rows,
            ["addressLibrary"] = new JsonObject
            {
                ["label"] = Named<TextBlock>(window, "AddressLibraryLabel").Text,
                ["text"] = addressLibrary.Text,
                ["color"] = Colour(addressLibrary),
                ["tooltip"] = ToolTip.GetTip(addressLibrary) as string,
            },
            ["button"] = new JsonObject { ["text"] = (string)downgrade.Content!, ["opens"] = Click(downgrade) },
        };

        var report = OverviewScenarioTests.Project(machine, state.Report);
        report["display"] = new JsonObject
        {
            ["top"] = top,
            ["binaries"] = binaries,
            ["log"] = new JsonArray([.. logger.Entries.Skip(firstLine).Select(e => (JsonNode)new JsonObject
            {
                ["level"] = LogFile.LevelName(e.Level),
                ["message"] = Tokenise(machine, e.Message),
            })]),
        };
        return report;
    }

    /// <summary>
    /// The value's text while the pointer is over it, and once the pointer has left it again (OVW-B8), through real
    /// headless pointer moves.
    /// </summary>
    private static (string? Hover, string? Left) Hover(MainWindow window, TextBlock value)
    {
        var centre = value.TranslatePoint(new Point(value.Bounds.Width / 2, value.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centre);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var hover = value.Text;
        // The window's bottom-right corner is part of no Overview value.
        window.MouseMove(new Point(window.Bounds.Width - 1, window.Bounds.Height - 1));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (hover, value.Text);
    }

    private static string Colour(TextBlock text)
    {
        var colour = ((ISolidColorBrush)text.Foreground!).Color;
        return TkColours.TryGetValue(colour, out var name) ? name : colour.ToString();
    }

    /// <summary>The assets path of the image a control shows (itself or inside it).</summary>
    private static string? Image(Control control)
        => (control as AssetImage ?? control.GetVisualDescendants().OfType<AssetImage>().FirstOrDefault()
            ?? control.GetLogicalDescendants().OfType<AssetImage>().FirstOrDefault())?.AssetPath;

    private static T Named<T>(MainWindow window, string name)
        where T : Control
        => NamedOrNull<T>(window, name) ?? throw new InvalidOperationException($"No control named {name}.");

    private static T? NamedOrNull<T>(MainWindow window, string name)
        where T : Control
    {
        var found = TabHost.Content(window).GetLogicalDescendants().OfType<T>().Where(c => c.Name == name).Distinct().ToList();
        return found.Count <= 1
            ? found.SingleOrDefault()
            : throw new InvalidOperationException($"{found.Count} controls named {name}: {string.Join(", ", found.Select(f => f.GetType().Name))}");
    }

    private static string Tokenise(MaterializedScenario machine, string text)
        => text.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal);
}
