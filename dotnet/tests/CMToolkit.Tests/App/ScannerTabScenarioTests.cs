using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Scanner tab against the <c>scanner-tab</c> goldens: the real Overview, Scanner and Tools tabs in a main window,
/// driven by the driver's script (select the Scanner, untick boxes, scan, select rows, leave and come back, rescan) and
/// read back at each step: the side pane, the results tree, the info and scanning labels, the progress bar, which panes
/// exist, <c>settings.json</c> and the log lines. A tab that fails to load is left to the Overview and then the Tools
/// tab, as the driver does (bug #87: fixed, so <c>scanner/no-data</c>'s port golden has both switches complete).
/// Compared in full.
/// </summary>
public sealed class ScannerTabScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("scanner", "scanner-tab");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    [Trait("Parity", "SCN-1")]
    [Trait("Parity", "SCN-2")]
    [Trait("Parity", "SCN-3")]
    [Trait("Parity", "SCN-S1")]
    [Trait("Parity", "SCN-S2")]
    [Trait("Parity", "SCN-S3")]
    [Trait("Parity", "SCN-R1")]
    [Trait("Parity", "SCN-R3")]
    [Trait("Parity", "SET-8")]
    [Trait("Parity", "B-1")]
    [Trait("Parity", "B-4")]
    public void The_scanner_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd ?? MaterializedScenario.RootToken), AppFolderFiles.SettingsFileName);
        var logger = new CapturingLogger();
        // The app log, which gets the scan's "Settings : Saving" line.
        var settings = SettingsStore.Load(settingsPath, DownloadSource.Nexus, logger);
        var runtime = new AppRuntime(logger);
        var state = new OverviewState();
        var overview = new OverviewTab(runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
        {
            OpenFolder = _ => { },
            ShowWarning = (_, _) => throw new InvalidOperationException("The scanner scenarios show no warning."),
        };
        var worker = new GatedWorker();
        var scanner = new ScannerTab(runtime, game, state, settings) { Worker = worker.Run };
        var window = new MainWindow(runtime, [overview, scanner, new ToolsTab(runtime)]);
        window.Show();
        DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);

        var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
        var firstLine = logger.Entries.Count;
        Select(tabs, 1);
        var lazy = (LazyTab)((TabItem)tabs.Items[1]!).Content!;

        if (scanner.Tree is null)
        {
            var label = (TextBlock)lazy.Content!;
            var errors = Errors(logger);
            Select(tabs, 0);
            var toOverview = new JsonObject { ["errors"] = Errors(logger) - errors, ["currentTab"] = window.CurrentPage!.LogName };
            errors = Errors(logger);
            Select(tabs, 2);
            var tools = (LazyTab)((TabItem)tabs.Items[2]!).Content!;
            var toTools = new JsonObject
            {
                ["errors"] = Errors(logger) - errors,
                ["currentTab"] = window.CurrentPage!.LogName,
                ["loaded"] = tools.Content is not null and not TextBlock,
            };
            var failed = new JsonObject
            {
                ["scanner"] = new JsonObject
                {
                    ["loaded"] = false,
                    ["display"] = new JsonObject
                    {
                        ["loadingLabel"] = new JsonObject { ["text"] = label.Text, ["color"] = ((ISolidColorBrush)label.Foreground!).Color.ToString().ToUpperInvariant().Replace("#FF", "#", StringComparison.Ordinal) },
                        ["switchAway"] = new JsonObject { ["overview"] = toOverview, ["tools"] = toTools },
                        ["log"] = Log(machine, logger, firstLine),
                    },
                },
            };
            GoldenAssert.Matches(scenario.LoadGolden(), failed, GoldenCompareMode.Full);
            window.Close();
            return;
        }

        var side = ReadSide(scanner);
        var initial = ReadTab(machine, scanner);
        var toggles = UntickDataBoxes(scanner);
        var overviewCheck = scanner.SidePane!.Checks[ScanSetting.OverviewIssues];
        MouseClick.On(overviewCheck);
        toggles.Add(Toggle(scanner, ScanSetting.OverviewIssues));
        MouseClick.On(overviewCheck);
        toggles.Add(Toggle(scanner, ScanSetting.OverviewIssues));

        // Each toggle saved the boxes at once (bug #20, fixed: the reference saved them only at Scan Game), as a
        // blocking write off the UI thread; the file is read once they have all landed.
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        var settingsBefore = FileSnapshot.Of(settingsPath);
        var (during, results, log) = Scan(machine, scanner, worker, logger);
        var settingsAfter = FileSnapshot.Of(settingsPath);

        JsonObject? selection = null;
        if (scanner.Tree.Items.Count > 0)
        {
            var group = FirstGroup(scanner);
            selection = new JsonObject
            {
                ["group"] = SelectRow(scanner, group),
                ["item"] = SelectRow(scanner, (TreeViewItem)group.Items[0]!),
            };
        }

        Select(tabs, 0);
        var left = new JsonObject { ["sidePane"] = scanner.SidePane is not null, ["detailsPane"] = scanner.DetailsPane is not null };
        Select(tabs, 1);
        var returned = ReadTab(machine, scanner);
        returned["side"] = ReadSide(scanner);
        returned["selection"] = scanner.Tree.SelectedItem is null ? new JsonArray() : new JsonArray("selected");

        UntickDataBoxes(scanner);
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        File.Delete(settingsPath);
        if (scanner.Tree.Items.Count > 0)
        {
            SelectRow(scanner, (TreeViewItem)FirstGroup(scanner).Items[0]!);
        }

        var (rescanDuring, rescan, _) = Scan(machine, scanner, worker, logger);

        var actual = new JsonObject
        {
            ["scanner"] = new JsonObject
            {
                ["loaded"] = true,
                ["usingStage"] = scanner.UsingStage,
                ["columns"] = scanner.UsingStage ? new JsonArray("mod") : new JsonArray(),
                ["display"] = new JsonObject
                {
                    ["side"] = side,
                    ["initial"] = initial,
                    ["toggles"] = new JsonArray([.. toggles]),
                    ["during"] = during,
                    ["results"] = results,
                    ["log"] = log,
                    ["selection"] = selection,
                    ["left"] = left,
                    ["returned"] = returned,
                    ["rescanDuring"] = rescanDuring,
                    ["rescan"] = rescan,
                },
                ["settingsBefore"] = settingsBefore,
                ["settingsAfter"] = settingsAfter,
                ["settingsAfterRescan"] = FileSnapshot.Of(settingsPath),
                ["errors"] = Errors(logger),
            },
        };

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
        window.Close();
    }

    /// <summary>Scan Game, read at once once the worker holds (<c>during</c>), then once the results are in.</summary>
    private static (JsonObject During, JsonObject Results, JsonArray Log) Scan(
        MaterializedScenario machine, ScannerTab scanner, GatedWorker worker, CapturingLogger logger)
    {
        var first = logger.Entries.Count;
        worker.Hold();
        MouseClick.On(scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => worker.Entered);
        var during = ReadTab(machine, scanner);
        worker.Release();
        DispatcherWait.Until(() => (string?)scanner.SidePane!.ScanButton.Content == "Scan Game");
        return (during, ReadTab(machine, scanner), Log(machine, logger, first));
    }

    private static JsonArray Log(MaterializedScenario machine, CapturingLogger logger, int first)
    {
        // The Error Window's StdErr lines are this port's error surfacing, which the reference never logged (#40).
        var lines = logger.Entries.Skip(first)
            .Where(e => !e.Message.StartsWith("StdErr : ", StringComparison.Ordinal))
            .Select(e => (JsonNode)new JsonObject
            {
                ["level"] = LogFile.LevelName(e.Level),
                ["message"] = e.Message.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal),
            })
            .ToList();
        return Core.Overview.OverviewFullScenarioTests.UnorderedRuns(
            lines, line => ((string)line["message"]!).StartsWith("Archives: Checking ", StringComparison.Ordinal));
    }

    private static int Errors(CapturingLogger logger)
        => logger.Entries.Count(e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));

    private static List<JsonNode> UntickDataBoxes(ScannerTab scanner)
    {
        var toggles = new List<JsonNode>();
        foreach (var setting in ScanSettings.All.Skip(1))
        {
            // Only a ticked box is clicked. In the reference every box was ticked again on return (B-1), so this is
            // the driver's script; the port's returned pane already has the saved, unticked boxes (bug #20 fixed).
            if (scanner.SidePane!.Checks[setting].IsChecked != true)
            {
                continue;
            }

            MouseClick.On(scanner.SidePane!.Checks[setting]);
            toggles.Add(Toggle(scanner, setting));
        }

        return toggles;
    }

    private static JsonObject Toggle(ScannerTab scanner, ScanSetting setting)
        => new() { ["check"] = setting.Label(), ["scanEnabled"] = scanner.SidePane!.ScanButton.IsEnabled };

    private static JsonObject ReadSide(ScannerTab scanner)
    {
        var side = scanner.SidePane!;
        var frame = side.GetLogicalDescendants().OfType<GroupBox>().Single(g => g.Name == "ScanSettings");
        return new JsonObject
        {
            ["title"] = ((TextBlock)frame.Header!).Text,
            ["checks"] = new JsonArray([.. ScanSettings.All.Select(s =>
            {
                var check = side.Checks[s];
                return (JsonNode)new JsonObject
                {
                    ["text"] = (string)check.Content!,
                    ["checked"] = check.IsChecked == true,
                    ["enabled"] = check.IsEnabled,
                    ["tooltip"] = ToolTip.GetTip(check) as string,
                };
            })]),
            ["scan"] = new JsonObject
            {
                ["text"] = (string)side.ScanButton.Content!,
                ["enabled"] = side.ScanButton.IsEnabled,
                ["style"] = side.ScanButton.Classes.Contains("accent") ? "Accent.TButton" : "TButton",
            },
        };
    }

    private static JsonObject ReadTab(MaterializedScenario machine, ScannerTab scanner)
    {
        var content = scanner.Tree!.FindLogicalAncestorOfType<Grid>()!;
        var info = Named<TextBlock>(content, "ResultsInfo");
        var scanning = Named<TextBlock>(content, "ScanningText");
        var groups = new JsonArray();
        foreach (var group in scanner.Tree!.Items.OfType<TreeViewItem>())
        {
            var items = new List<JsonNode>();
            foreach (var item in group.Items.OfType<TreeViewItem>())
            {
                var problem = scanner.ProblemOf(item)!;
                items.Add(new JsonObject
                {
                    ["text"] = ItemText(item),
                    ["values"] = scanner.UsingStage ? new JsonArray(ModText(item)) : new JsonArray(),
                    ["problem"] = OverviewScenarioTests.ProjectProblem(machine, problem),
                });
            }

            var kind = (string)((TextBlock)group.Header!).Text!;
            groups.Add(new JsonObject
            {
                ["text"] = kind,
                ["open"] = group.IsExpanded,
                // The driver marks each same-mod run of archive problems unordered (T-15).
                ["items"] = kind == ProblemTypes.InvalidArchive ? ModRuns(items) : new JsonArray([.. items]),
            });
        }

        return new JsonObject
        {
            ["resultsInfo"] = info.Text ?? "",
            ["scanningLabel"] = scanning.IsVisible ? scanning.Text : null,
            ["progress"] = Named<ProgressBar>(content, "Progress").Value,
            ["selectable"] = scanner.IsSelectable,
            // In order: the reference's group order was random per run (B-4), which the port fixes (bug #23), so the
            // scenarios' port goldens list the groups in the port's order.
            ["groups"] = groups,
            ["scan"] = scanner.SidePane is null ? null : new JsonObject
            {
                ["text"] = (string)scanner.SidePane.ScanButton.Content!,
                ["enabled"] = scanner.SidePane.ScanButton.IsEnabled,
            },
            ["detailsPane"] = scanner.DetailsPane is not null,
            ["sidePane"] = scanner.SidePane is not null,
        };
    }

    private static JsonArray ModRuns(List<JsonNode> items)
    {
        var runs = new JsonArray();
        foreach (var run in items.GroupAdjacent(i => (string)i["problem"]!["mod"]!))
        {
            runs.Add(new JsonObject { [GoldenComparer.UnorderedKey] = new JsonArray([.. run]) });
        }

        return runs;
    }

    private static string ItemText(TreeViewItem item) => item.Header switch
    {
        TextBlock text => text.Text!,
        DockPanel row => row.Children.OfType<TextBlock>().Single(t => t.Name != "Mod").Text!,
        _ => throw new InvalidOperationException("Unexpected row header."),
    };

    private static string ModText(TreeViewItem item)
        => ((DockPanel)item.Header!).Children.OfType<TextBlock>().Single(t => t.Name == "Mod").Text!;

    private static TreeViewItem FirstGroup(ScannerTab scanner)
        => scanner.Tree!.Items.OfType<TreeViewItem>().OrderBy(g => (string)((TextBlock)g.Header!).Text!, StringComparer.Ordinal).First();

    private static bool SelectRow(ScannerTab scanner, TreeViewItem row)
    {
        scanner.Tree!.SelectedItem = row;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return scanner.DetailsPane is not null;
    }

    private static void Select(TabControl tabs, int index)
    {
        // The Scanner and Tools tabs load synchronously (no Core work), so running the queued jobs settles the switch.
        tabs.SelectedIndex = index;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Control root, string name)
        where T : Control
        => root.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);

    /// <summary>The default worker, which a scan can be made to hold in so the tab can be read mid-scan.</summary>
    private sealed class GatedWorker
    {
        private readonly ManualResetEventSlim _gate = new(true);
        private volatile bool _entered;

        public bool Entered => _entered;

        public void Hold()
        {
            _entered = false;
            _gate.Reset();
        }

        public void Release() => _gate.Set();

        public ScanOutcome Run(ScanSettings settings)
        {
            _entered = true;
            _gate.Wait(TimeSpan.FromSeconds(30));
            return ScanWorker.Run(settings);
        }
    }
}

/// <summary>Grouping of adjacent elements with equal keys, as Python's <c>itertools.groupby</c> does.</summary>
internal static class AdjacentGrouping
{
    public static IEnumerable<List<T>> GroupAdjacent<T>(this IEnumerable<T> items, Func<T, string> key)
    {
        List<T>? run = null;
        string? current = null;
        foreach (var item in items)
        {
            var k = key(item);
            if (run is not null && k == current)
            {
                run.Add(item);
                continue;
            }

            if (run is not null)
            {
                yield return run;
            }

            run = [item];
            current = k;
        }

        if (run is not null)
        {
            yield return run;
        }
    }
}
