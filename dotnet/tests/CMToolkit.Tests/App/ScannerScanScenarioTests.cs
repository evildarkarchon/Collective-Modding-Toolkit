using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
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
/// The Scanner's data checks against the <c>scanner-scan</c> goldens: the real Overview and Scanner tabs in a main
/// window, scanned once per driver run (every box, then each box alone), each run read back as the driver reads it:
/// every progress update the worker reported with the scanning label and progress bar after the tab applied it, the
/// worker's error and log lines, the mod file index it built, and the results tree. A worker that fails left the
/// reference's scan stuck (B-3); the port ends the scan listing the seeded Overview problems only, the worker's results dying with it (bug #22, in
/// the port goldens). Either
/// way the script stops there. Compared in full.
/// </summary>
public sealed partial class ScannerScanScenarioTests
{
    /// <summary>The driver's <c>_SCAN_RUNS</c>: every box, then each box alone, in pane order.</summary>
    private static readonly IReadOnlyList<IReadOnlySet<ScanSetting>> Runs =
    [
        new HashSet<ScanSetting>(ScanSettings.All),
        .. ScanSettings.All.Select(s => (IReadOnlySet<ScanSetting>)new HashSet<ScanSetting> { s }),
    ];

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("scanner", "scanner-scan");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    [Trait("Parity", "SCN-S4")]
    [Trait("Parity", "SCN-S5")]
    [Trait("Parity", "SCN-S6")]
    [Trait("Parity", "SCN-C1")]
    [Trait("Parity", "SCN-C2")]
    [Trait("Parity", "SCN-C3")]
    [Trait("Parity", "SCN-C4")]
    [Trait("Parity", "SCN-C5")]
    [Trait("Parity", "SCN-C6")]
    [Trait("Parity", "SCN-C7")]
    [Trait("Parity", "SCN-C8")]
    [Trait("Parity", "SCN-C9")]
    [Trait("Parity", "SCN-R2")]
    [Trait("Parity", "B-3")]
    [Trait("Parity", "B-5")]
    public void The_scanner_data_checks_match_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd ?? MaterializedScenario.RootToken), AppFolderFiles.SettingsFileName);
        var logger = new CapturingLogger();
        var settings = SettingsStore.Load(settingsPath, DownloadSource.Nexus, logger);
        var runtime = new AppRuntime(logger);
        var state = new OverviewState();
        var overview = new OverviewTab(runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
        {
            OpenFolder = _ => { },
            ShowWarning = (_, _) => throw new InvalidOperationException("The scanner scenarios show no warning."),
        };

        // The real worker, watched: what it returns or raises, and the lines it logs (the only logging while it runs).
        var watch = new WorkerWatch();
        ScannerTab? scannerRef = null;
        var scanner = new ScannerTab(runtime, game, state, settings)
        {
            Host = machine.Host,
            Worker = s =>
            {
                var first = logger.Entries.Count;
                try
                {
                    watch.Outcome = ScanWorker.Run(s, scannerRef!.Progress, logger);
                    return watch.Outcome;
                }
                catch (Exception ex)
                {
                    watch.Error = ex;
                    throw;
                }
                finally
                {
                    watch.Log = [.. logger.Entries.Skip(first)];
                }
            },
        };
        scannerRef = scanner;
        var updates = new List<JsonNode>();
        scanner.ProgressApplied += (_, update) => updates.Add(new JsonObject
        {
            ["update"] = update switch
            {
                ScanUpdate.TopLevelFolders folders => new JsonObject { ["folders"] = new JsonArray([.. folders.Names.Select(n => JsonValue.Create(n))]) },
                ScanUpdate.Folder folder => new JsonObject { ["folder"] = folder.Name },
                _ => throw new InvalidOperationException("Unexpected update."),
            },
            ["scanningText"] = Named<TextBlock>(scanner, "ScanningText").Text,
            ["progress"] = Named<ProgressBar>(scanner, "Progress").Value,
        });

        var window = new MainWindow(runtime, [overview, scanner]);
        window.Show();
        DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
        var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var runs = new JsonArray();
        foreach (var ticked in Runs)
        {
            foreach (var setting in ScanSettings.All)
            {
                var check = scanner.SidePane!.Checks[setting];
                if ((check.IsChecked == true) != ticked.Contains(setting))
                {
                    MouseClick.On(check);
                }
            }

            updates.Clear();
            watch.Reset();
            var scan = scanner.ScanAsync();
            DispatcherWait.Until(() => scan.IsCompleted);
            scan.GetAwaiter().GetResult();

            runs.Add(new JsonObject
            {
                ["boxes"] = new JsonArray([.. ScanSettings.All.Where(ticked.Contains).Select(s => JsonValue.Create(s.Label()))]),
                ["updates"] = new JsonArray([.. updates]),
                ["workerError"] = watch.Error is null ? null : Address().Replace(Tokenise(machine, watch.Error.Message), "0x<ADDRESS>"),
                ["workerLog"] = new JsonArray([.. watch.Log.Select(e => (JsonNode)new JsonObject
                {
                    ["level"] = LogFile.LevelName(e.Level),
                    ["message"] = Tokenise(machine, e.Message),
                })]),
                ["index"] = watch.Outcome?.Index is { } index ? Index(machine, index) : null,
                ["results"] = ReadTab(machine, scanner),
            });
            if (watch.Error is not null)
            {
                break;
            }
        }

        var actual = new JsonObject
        {
            ["scanner"] = new JsonObject
            {
                ["usingStage"] = scanner.UsingStage,
                ["runs"] = runs,
                // The reference's Tk callback errors; a dead worker (B-3) died on its thread, outside them.
                ["errors"] = logger.Entries.Count(e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal)
                    && !e.Message.StartsWith("StdErr : " + ErrorReporter.OperationHeader("Scanner scan"), StringComparison.Ordinal)),
            },
        };

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
        window.Close();
    }

    /// <summary>The driver's <c>_mod_file_index</c>: each table's entries in order.</summary>
    private static JsonObject Index(MaterializedScenario machine, ModFiles index)
    {
        JsonArray Entries<TKey>(IEnumerable<KeyValuePair<TKey, ModFile>> table)
            where TKey : notnull
            => new([.. table.Select(e => (JsonNode)new JsonObject
            {
                ["key"] = e.Key.ToString(),
                ["mod"] = e.Value.Mod,
                ["path"] = OverviewScenarioTests.PathText(machine, e.Value.Path),
            })]);

        return new JsonObject
        {
            ["folders"] = Entries(index.Folders),
            ["files"] = Entries(index.Files),
            ["modules"] = Entries(index.Modules),
            ["archives"] = Entries(index.Archives),
        };
    }

    /// <summary>The driver's <c>read_tab</c> for this operation.</summary>
    private static JsonObject ReadTab(MaterializedScenario machine, ScannerTab scanner)
    {
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
                    ["problem"] = ProjectProblem(machine, problem),
                });
            }

            var kind = (string)((TextBlock)group.Header!).Text!;
            groups.Add(new JsonObject
            {
                ["text"] = kind,
                ["open"] = group.IsExpanded,
                // The driver marks each same-mod run of the Overview's archive problems unordered (T-15).
                ["items"] = kind == ProblemTypes.InvalidArchive ? ModRuns(items) : new JsonArray([.. items]),
            });
        }

        var scanning = Named<TextBlock>(scanner, "ScanningText");
        var side = scanner.SidePane!;
        return new JsonObject
        {
            ["resultsInfo"] = Named<TextBlock>(scanner, "ResultsInfo").Text ?? "",
            ["scanningLabel"] = scanning.IsVisible ? scanning.Text : null,
            ["progress"] = Named<ProgressBar>(scanner, "Progress").Value,
            // In order: the reference's group order was random per run (B-4), which the port fixes (bug #23), so the
            // scenarios' port goldens list the groups in the port's order.
            ["groups"] = groups,
            ["scan"] = new JsonObject { ["text"] = (string)side.ScanButton.Content!, ["enabled"] = side.ScanButton.IsEnabled },
        };
    }

    /// <summary>The driver's <c>_scan_problem</c>: the Overview projection plus the file list.</summary>
    private static JsonObject ProjectProblem(MaterializedScenario machine, Problem problem)
    {
        var projected = OverviewScenarioTests.ProjectProblem(machine, problem);
        projected["fileList"] = problem.FileList is null
            ? null
            : new JsonArray([.. problem.FileList.Select(e => (JsonNode)new JsonArray(e.Count, OverviewScenarioTests.PathText(machine, e.File)))]);
        return projected;
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

    private static T Named<T>(ScannerTab scanner, string name)
        where T : Control
        => scanner.Tree!.FindLogicalAncestorOfType<Grid>()!.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);

    private static string Tokenise(MaterializedScenario machine, string text)
        => text.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal);

    /// <summary>The driver masks Python's object addresses the same way.</summary>
    [GeneratedRegex("0x[0-9A-Fa-f]+")]
    private static partial Regex Address();

    /// <summary>What the watched worker did in the current run.</summary>
    private sealed class WorkerWatch
    {
        // Set on the worker thread and read on the UI thread once the scan's task has completed, which orders them.
        public ScanOutcome? Outcome { get; set; }

        public Exception? Error { get; set; }

        public IReadOnlyList<LogEntry> Log { get; set; } = [];

        public void Reset()
        {
            Outcome = null;
            Error = null;
            Log = [];
        }
    }
}
