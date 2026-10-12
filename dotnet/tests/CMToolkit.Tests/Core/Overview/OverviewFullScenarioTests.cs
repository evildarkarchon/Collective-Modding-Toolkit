using System.Text;
using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Overview;

/// <summary>
/// The <c>overview-full</c> scenarios (the real <c>CMChecker</c>'s Overview with nothing stubbed, loaded and then
/// refreshed) replayed against <see cref="OverviewReport.Gather(CMToolkit.Core.EnvironmentDetection.GameInstall, CMToolkit.Core.IHostEnvironment, Microsoft.Extensions.Logging.ILogger, OverviewReport?)"/>:
/// the modules, archives and counts, the Install Type, the problem list, the warnings the load shows, and for a load
/// that fails, what it had gathered by then. The rendered side is the App test's. The scenarios cite their IDs.
/// </summary>
public sealed class OverviewFullScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("overview", "overview-full");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var game = await GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand))
                   ?? throw new InvalidOperationException("The scenario's game wasn't found.");

        var actual = new JsonObject();
        OverviewReport loaded;
        try
        {
            loaded = OverviewReport.Gather(game, machine.Host, new CapturingLogger());
        }
        catch (OverviewIncompleteException failure)
        {
            // The reference's load died with this much gathered; the tab stays on its loading text and can't refresh.
            var failed = Project(machine, failure.Partial);
            failed["loaded"] = false;
            failed["error"] = Error(failure.InnerException!);
            actual["overview"] = failed;
            AddDialogs(actual, failure.Partial);
            GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Semantic);
            return;
        }

        // The refresh gathers a new report from the same machine; nothing changed in between.
        var refreshed = OverviewReport.Gather(game, machine.Host, new CapturingLogger(), loaded);
        actual["overview"] = Project(machine, loaded);
        actual["overview"]!["loaded"] = true;
        actual["refresh"] = Project(machine, refreshed);
        actual["refresh"]!["loaded"] = true;
        // Only the load shows its warnings (OVW-M3, OVW-M5); the refresh's report has them too, unshown.
        AddDialogs(actual, loaded);
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Semantic);
    }

    /// <summary>The report in the golden's shape: the driver's <c>state</c>, Python sets as unordered arrays.</summary>
    internal static JsonObject Project(MaterializedScenario machine, OverviewReport report) => new()
    {
        ["installType"] = report.InstallType.DisplayName(),
        ["modules"] = new JsonObject
        {
            ["enabled"] = Paths(machine, report.Modules.Enabled),
            ["unreadable"] = Unordered(Paths(machine, report.Modules.Unreadable)),
            ["hedr95"] = Unordered(Paths(machine, report.Modules.Hedr95)),
            ["hedrUnknown"] = new JsonArray([.. report.Modules.HedrUnknown.Select(m => (JsonNode)new JsonObject
            {
                ["module"] = OverviewScenarioTests.PathText(machine, m.Module),
                ["version"] = m.Version,
            })]),
            ["full"] = report.Modules.Full,
            ["light"] = report.Modules.Light,
            ["hedr100"] = report.Modules.Hedr100,
        },
        ["archives"] = new JsonObject
        {
            ["enabled"] = Unordered(Paths(machine, report.Archives.Enabled)),
            ["unreadable"] = Unordered(Paths(machine, report.Archives.Unreadable)),
            ["oldGen"] = Unordered(Paths(machine, report.Archives.OldGen)),
            ["nextGen"] = Unordered(Paths(machine, report.Archives.NextGen)),
            ["general"] = report.Archives.General,
            ["texture"] = report.Archives.Texture,
        },
        ["problems"] = UnorderedRuns(
            [.. report.Problems.Select(p => (JsonNode)OverviewScenarioTests.ProjectProblem(machine, p))],
            p => (string?)p["type"] == ProblemTypes.InvalidArchive),
    };

    /// <summary>
    /// The driver's <c>unordered_runs</c>: each run of consecutive items <paramref name="inRun"/> picks becomes one
    /// unordered marker. The archive loop's problems and log lines come in set order (T-15).
    /// </summary>
    internal static JsonArray UnorderedRuns(IReadOnlyList<JsonNode> items, Func<JsonNode, bool> inRun)
    {
        var result = new JsonArray();
        var run = new List<JsonNode>();
        foreach (var item in items.Append(null))
        {
            if (item is not null && inRun(item))
            {
                run.Add(item);
                continue;
            }

            if (run.Count > 0)
            {
                result.Add(Unordered(new JsonArray([.. run])));
                run.Clear();
            }

            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>The load's warnings as the golden's <c>dialogs</c>: the scripted <c>messagebox.showwarning</c> calls.</summary>
    internal static void AddDialogs(JsonObject golden, OverviewReport report)
    {
        if (report.Warnings.Count > 0)
        {
            golden["dialogs"] = new JsonArray([.. report.Warnings.Select(w => (JsonNode)new JsonObject
            {
                ["module"] = "messagebox",
                ["function"] = "showwarning",
                ["args"] = new JsonArray(w.Title, w.Message),
                ["kwargs"] = new JsonObject(),
            })]);
        }
    }

    /// <summary>The driver's <c>_error</c> for the failures an Overview load can die of.</summary>
    internal static JsonObject Error(Exception error) => error switch
    {
        DecoderFallbackException => new JsonObject { ["kind"] = "decode" },
        InvalidDataException => new JsonObject { ["kind"] = "value", ["message"] = error.Message },
        _ => throw new InvalidOperationException($"No golden form for {error.GetType().Name}.", error),
    };

    private static JsonArray Paths(MaterializedScenario machine, IEnumerable<PythonPath> paths)
        => new([.. paths.Select(p => (JsonNode?)OverviewScenarioTests.PathText(machine, p))]);

    private static JsonObject Unordered(JsonArray items) => new() { [GoldenComparer.UnorderedKey] = items };
}
