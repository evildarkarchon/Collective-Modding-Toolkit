using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
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
/// The Overview tab against the <c>overview-full</c> goldens (nothing stubbed in the reference): the Archives and Modules
/// frames read off their controls, with what the Archive Patcher button and the HEDR info icon open, the log lines, the
/// warnings the load shows, and the report behind them; then the same after the refresh button. A load that dies stays
/// on its loading text. Compared in full. The Archive Patcher opener and the warning box are recorders, as the driver's
/// are.
/// </summary>
public sealed class OverviewFullTabScenarioTests
{
    /// <summary>The Tk colour names the reference's labels use, by the colour the port draws them in.</summary>
    private static readonly Dictionary<Color, string> TkColours = new()
    {
        [Color.Parse("#CACACA")] = "#CACACA",
        [Color.Parse("#619267")] = "#619267",
        [Color.Parse("#AF5A66")] = "#AF5A66",
        [Color.Parse("#808080")] = "gray",
        [Color.Parse("#FFA500")] = "orange",
    };

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("overview", "overview-full");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_overview_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var state = new OverviewState();
        var opened = new List<JsonObject>();
        var dialogs = new JsonArray();
        OverviewIncompleteException? failure = null;
        var tab = new OverviewTab(
            runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state, null, () => opened.Add(new JsonObject { ["window"] = "ArchivePatcher" }))
        {
            OpenFolder = _ => { },
            // The real gathering, watched so the test knows why a load died.
            Gatherer = (g, h, l, previous) =>
            {
                try
                {
                    return OverviewReport.Gather(g, h, l, previous);
                }
                catch (OverviewIncompleteException ex)
                {
                    failure = ex;
                    throw;
                }
            },
            ShowWarning = (title, message) =>
            {
                dialogs.Add(new JsonObject
                {
                    ["module"] = "messagebox",
                    ["function"] = "showwarning",
                    ["args"] = new JsonArray(title, message),
                    ["kwargs"] = new JsonObject(),
                });
                return Task.CompletedTask;
            },
        };

        var window = new MainWindow(runtime, [tab]);
        window.Show();
        // Loaded and built, or dead with the partial report kept; either way the load's input block is gone.
        DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
        window.UpdateLayout();

        var actual = new JsonObject();
        var content = Lazy(window).Content;
        if (content is TextBlock loading)
        {
            var failed = ReportOf(machine, state, logger, 0);
            failed["loaded"] = false;
            // Reported, as every unhandled exception is (#18), though the reference's Tk callback swallowed it.
            Assert.NotNull(runtime.Errors.Window);
            failed["error"] = Core.Overview.OverviewFullScenarioTests.Error(failure!.InnerException!);
            failed["display"]!["loadingLabel"] = new JsonObject
            {
                ["text"] = loading.Text,
                // Tk leaves the label in the theme's colour when the load raised, rather than returned False.
                ["color"] = loading.IsSet(TextBlock.ForegroundProperty) ? Colour(loading) : "",
            };
            actual["overview"] = failed;
        }
        else
        {
            actual["overview"] = Read(machine, window, state, logger, opened, 0);
            var refreshFrom = logger.Entries.Count;
            var before = state.Report;
            MouseClick.On(Named<Button>(window, "Refresh"));
            DispatcherWait.Until(() => !ReferenceEquals(state.Report, before) && !runtime.Input.IsBlocking);
            window.UpdateLayout();
            actual["refresh"] = Read(machine, window, state, logger, opened, refreshFrom);
        }

        if (dialogs.Count > 0)
        {
            actual["dialogs"] = dialogs;
        }

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    /// <summary>The report and the log lines from <paramref name="firstLine"/> on, as the driver's <c>state</c> records them.</summary>
    private static JsonObject ReportOf(MaterializedScenario machine, OverviewState state, CapturingLogger logger, int firstLine)
    {
        var report = Core.Overview.OverviewFullScenarioTests.Project(machine, state.Report!);
        // The Error Window's StdErr line is this port's error surfacing, which the reference never logged (#40).
        var log = logger.Entries.Skip(firstLine)
            .Where(e => !e.Message.StartsWith("StdErr : ", StringComparison.Ordinal))
            .Select(e => (JsonNode)new JsonObject
            {
                ["level"] = LogFile.LevelName(e.Level),
                ["message"] = Tokenise(machine, e.Message),
            })
            .ToList();
        report["display"] = new JsonObject
        {
            ["log"] = Core.Overview.OverviewFullScenarioTests.UnorderedRuns(
                log, line => ((string)line["message"]!).StartsWith("Archives: Checking ", StringComparison.Ordinal)),
        };
        return report;
    }

    /// <summary>The loaded tab as the driver's <c>read</c> records it: the report, then the two frames, clicking as it goes.</summary>
    private static JsonObject Read(
        MaterializedScenario machine, MainWindow window, OverviewState state, CapturingLogger logger, List<JsonObject> opened, int firstLine)
    {
        var result = ReportOf(machine, state, logger, firstLine);
        result["loaded"] = true;
        var display = result["display"]!.AsObject();

        var patcher = Named<Button>(window, "ArchivePatcher");
        var before = opened.Count;
        MouseClick.On(patcher);
        display["archives"] = new JsonObject
        {
            ["title"] = ((TextBlock)Named<GroupBox>(window, "Archives").Header!).Text,
            ["formats"] = Label(window, "ArchiveFormats"),
            ["unreadable"] = Label(window, "ArchiveUnreadableLabel"),
            ["versions"] = Label(window, "ArchiveVersions"),
            ["counts"] = Counts(window, "ArchiveCount", [0, 1, 2, 3, 5, 6]),
            ["limits"] = Label(window, "ArchiveLimits"),
            ["button"] = new JsonObject
            {
                ["text"] = (string)patcher.Content!,
                ["opens"] = opened.Count > before ? opened[^1].DeepClone() : null,
            },
        };

        var icon = NamedOrNull<Control>(window, "HedrUnknownIcon");
        display["modules"] = new JsonObject
        {
            ["title"] = ((TextBlock)Named<GroupBox>(window, "Modules").Header!).Text,
            ["types"] = Label(window, "ModuleTypes"),
            ["unreadable"] = Label(window, "ModuleUnreadableLabel"),
            ["hedr100"] = Label(window, "Hedr100Label"),
            ["hedr95"] = Label(window, "Hedr95Label"),
            ["hedrUnknown"] = Label(window, "HedrUnknownLabel"),
            ["counts"] = Counts(window, "ModuleCount", [0, 1, 2, 3, 5, 6, 7]),
            ["limits"] = Label(window, "ModuleLimits"),
            ["hedrIcon"] = icon is null ? null : new JsonObject
            {
                ["image"] = (icon.GetLogicalDescendants().OfType<AssetImage>().FirstOrDefault()
                             ?? icon.GetVisualDescendants().OfType<AssetImage>().First()).AssetPath,
                ["tooltip"] = ToolTip.GetTip(icon) as string,
                ["opens"] = OpenTreeWindow(window, icon),
            },
        };
        return result;
    }

    /// <summary>Clicks the HEDR icon and reads back the TreeWindow it opens, then closes it.</summary>
    private static JsonObject? OpenTreeWindow(MainWindow window, Control icon)
    {
        MouseClick.On(icon);
        Dispatcher.UIThread.RunJobs();
        var tree = window.OwnedWindows.OfType<TreeWindow>().SingleOrDefault();
        if (tree is null)
        {
            return null;
        }

        var opened = new JsonObject
        {
            ["window"] = "TreeWindow",
            ["width"] = (int)tree.Width,
            ["height"] = (int)tree.Height,
            ["title"] = tree.Title,
            ["text"] = ((TextBlock)tree.GetLogicalDescendants().OfType<WraplengthPresenter>().First().Child!).Text,
            ["headers"] = new JsonArray([.. tree.GetLogicalDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("heading"))
                .Select(t => (JsonNode?)t.Text)]),
            ["rows"] = new JsonArray([.. tree.Rows.Select(r => (JsonNode)new JsonArray(r.Text, r.Value))]),
        };
        tree.Close();
        Dispatcher.UIThread.RunJobs();
        return opened;
    }

    private static JsonObject Label(MainWindow window, string name)
    {
        var label = Named<TextBlock>(window, name);
        return new JsonObject { ["text"] = label.Text, ["color"] = Colour(label), ["tooltip"] = ToolTip.GetTip(label) as string };
    }

    private static JsonArray Counts(MainWindow window, string prefix, int[] rows)
        => new([.. rows.Select(row =>
        {
            var label = Label(window, prefix + row);
            label["row"] = row;
            return (JsonNode)label;
        })]);

    private static LazyTab Lazy(MainWindow window)
        => (LazyTab)window.GetLogicalDescendants().OfType<TabItem>().Single().Content!;

    private static string Colour(TextBlock text)
    {
        var colour = ((ISolidColorBrush)text.Foreground!).Color;
        return TkColours.TryGetValue(colour, out var name) ? name : colour.ToString();
    }

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
