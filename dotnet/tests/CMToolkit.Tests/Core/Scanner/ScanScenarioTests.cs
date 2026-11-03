using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CMToolkit.App.Runtime;
using CMToolkit.Core.Problems;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The <c>scanner-scan</c> scenarios replayed against <see cref="ScanChecks"/> with no window: one Scan per run (every
/// box, then each box alone, in pane order), each after its own Overview refresh, recorded as the scan returned it: the
/// statuses it reported, its error, its non-DEBUG log lines, the mod file index it built and the groups of its results.
/// The Scanner tab's rendering of those groups is the <c>scanner-tab</c> replay's. The scenarios cite their IDs.
/// Compared in full.
/// </summary>
public sealed partial class ScanScenarioTests
{
    /// <summary>Every box, then each box alone, in pane order.</summary>
    private static readonly IReadOnlyList<IReadOnlySet<ScanSetting>> Runs =
    [
        new HashSet<ScanSetting>(ScanSettings.All),
        .. ScanSettings.All.Select(s => (IReadOnlySet<ScanSetting>)new HashSet<ScanSetting> { s }),
    ];

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("scanner", "scanner-scan");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task The_scan_matches_the_recorded_result(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var located = await ScanScenarioMachine.LocateAsync(machine);

        var usingStage = false;
        var runs = new JsonArray();
        foreach (var ticked in Runs)
        {
            // Each Scan starts with its Overview refresh, which the tab runs before the scan and logs on its own.
            located = located.Refreshed();
            var settings = located.Snapshot(ticked);
            usingStage = settings.UsingStage;
            var log = new CapturingLogger();
            var statuses = new ListProgress<ScanStatus>();

            var outcome = ScanChecks.Run(settings, statuses, log);

            runs.Add(new JsonObject
            {
                ["boxes"] = new JsonArray([.. ScanSettings.All.Where(ticked.Contains).Select(s => JsonValue.Create(s.Label()))]),
                ["statuses"] = new JsonArray([.. statuses.Reported.Select(s => (JsonNode)new JsonObject
                {
                    ["text"] = s.Text,
                    ["progress"] = s.Progress,
                })]),
                ["error"] = outcome.Error is null ? null : Address().Replace(Tokenise(machine, outcome.Error.Message), "0x<ADDRESS>"),
                ["log"] = new JsonArray([.. log.Entries.WithoutDebug().Select(e => (JsonNode)new JsonObject
                {
                    ["level"] = LogFile.LevelName(e.Level),
                    ["message"] = Tokenise(machine, e.Message),
                })]),
                ["index"] = outcome.Index is { } index ? Index(machine, index) : null,
                ["groups"] = Groups(machine, outcome.Groups),
            });
        }

        var actual = new JsonObject
        {
            ["scanner"] = new JsonObject
            {
                ["usingStage"] = usingStage,
                ["runs"] = runs,
            },
        };

        GoldenAssert.Matches(scenario, actual, GoldenCompareMode.Full);
    }

    /// <summary>Each table of the mod file index, its entries in insertion order.</summary>
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

    /// <summary>
    /// The outcome's groups in order, each with its problems in order, except that each same-mod run of the Overview's
    /// archive problems is unordered: their order is the enabled-archive set's (T-15).
    /// </summary>
    private static JsonArray Groups(MaterializedScenario machine, IReadOnlyList<ScanResultGroup> groups)
    {
        var projected = new JsonArray();
        foreach (var group in groups)
        {
            var items = group.Items.Select(p => (JsonNode)ProjectProblem(machine, p)).ToList();
            projected.Add(new JsonObject
            {
                ["kind"] = group.Kind,
                ["items"] = group.Kind == ProblemTypes.InvalidArchive ? ModRuns(items) : new JsonArray([.. items]),
            });
        }

        return projected;
    }

    /// <summary>The Overview projection of <paramref name="problem"/> plus its file list.</summary>
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
        foreach (var run in items.GroupAdjacent(i => (string)i["mod"]!))
        {
            runs.Add(new JsonObject { [GoldenComparer.UnorderedKey] = new JsonArray([.. run]) });
        }

        return runs;
    }

    private static string Tokenise(MaterializedScenario machine, string text)
        => text.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal);

    /// <summary>Masks object addresses in an error message, as the reference driver masked Python's.</summary>
    [GeneratedRegex("0x[0-9A-Fa-f]+")]
    private static partial Regex Address();
}
