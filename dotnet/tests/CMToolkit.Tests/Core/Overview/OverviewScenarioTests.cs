using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Overview;

/// <summary>
/// The <c>overview/*</c> scenarios (recorded from the real <c>CMChecker</c>'s Overview tab, its load and then its
/// refresh) replayed against <see cref="OverviewReport.Gather"/>: the binaries, the Install Type, the Address Library and
/// the problems in list order. The rendered side (<c>display</c>) is the App test's. The scenarios cite their IDs.
/// </summary>
public sealed class OverviewScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("overview", "overview-tab");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var game = await GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand))
                   ?? throw new InvalidOperationException("The scenario's game wasn't found.");

        // The refresh gathers a new report from the same machine; nothing changed in between.
        var loaded = OverviewReport.Gather(game, machine.Host, new CapturingLogger(), null, modulesAndArchives: false);
        var refreshed = OverviewReport.Gather(game, machine.Host, new CapturingLogger(), loaded, modulesAndArchives: false);

        var actual = new JsonObject
        {
            ["overview"] = Project(machine, loaded),
            ["refresh"] = Project(machine, refreshed),
        };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Semantic);
    }

    /// <summary>The report in the golden's shape: the driver's <c>file_info</c>, <c>install_type</c> and problem list.</summary>
    internal static JsonObject Project(MaterializedScenario machine, OverviewReport report) => new()
    {
        ["installType"] = report.InstallType.DisplayName(),
        ["binaries"] = new JsonArray([.. report.Binaries.Select(b => (JsonNode)new JsonObject
        {
            ["name"] = b.Name,
            ["file"] = PathText(machine, b.File),
            ["version"] = b.Version is null ? null : new JsonArray([.. b.Version.Select(v => JsonValue.Create(v))]),
            ["versionString"] = b.VersionString,
            ["hash"] = b.Hash,
            ["installType"] = b.InstallType.DisplayName(),
        })]),
        ["addressLibrary"] = PathText(machine, report.AddressLibrary),
        ["problems"] = new JsonArray([.. report.Problems.Select(p => (JsonNode)ProjectProblem(machine, p))]),
    };

    /// <summary>One problem as the driver's <c>_overview_problem</c> records it.</summary>
    internal static JsonObject ProjectProblem(MaterializedScenario machine, Problem problem) => new()
    {
        ["type"] = problem.Kind,
        ["itemText"] = problem.ItemText,
        ["path"] = PathText(machine, problem.OpenablePath),
        ["relativePath"] = Tokenise(machine, problem.RelativePath),
        ["mod"] = problem.ModSource == ModSource.Overview ? "OVERVIEW" : problem.Mod,
        ["summary"] = Tokenise(machine, problem.Summary),
        ["solution"] = problem.Solution,
        ["extraData"] = problem.ExtraData is null ? null : new JsonArray([.. problem.ExtraData.Select(e => JsonValue.Create(e))]),
    };

    /// <summary><c>str(path)</c> with the temp root as <c>&lt;ROOT&gt;</c>, as the driver's <c>_path_text</c> renders it.</summary>
    internal static string? PathText(MaterializedScenario machine, PythonPath? path)
        => path is null ? null : Tokenise(machine, path.ToString());

    private static string Tokenise(MaterializedScenario machine, string text)
        => text.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal);
}
