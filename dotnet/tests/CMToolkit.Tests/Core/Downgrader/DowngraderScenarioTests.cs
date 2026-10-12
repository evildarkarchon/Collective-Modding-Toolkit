using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Core.Downgrader;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Downgrader;

/// <summary>
/// The <c>downgrader/*</c> scenarios (recorded from the real <c>Downgrader</c> opened over a loaded Overview) replayed
/// against <see cref="DowngraderDetection.Detect"/> and <see cref="DowngraderFiles.CrcsByType"/>: each file's detected
/// version in table order (DG-1), the derived CRC sets, and the game's Install Type the labels read. The window itself
/// (<c>display</c>) is the App test's. The scenarios cite their IDs.
/// </summary>
public sealed class DowngraderScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("downgrader", "downgrader-window");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var game = await GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand))
                   ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var overview = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        var versions = DowngraderDetection.Detect(game, machine.Host);

        var actual = new JsonObject { ["downgrader"] = Project(overview.InstallType, versions) };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Semantic);
    }

    /// <summary>The detection in the golden's shape: <c>current_versions</c>, the game's type and <c>CRCs_by_type</c>.</summary>
    internal static JsonObject Project(InstallType gameType, IReadOnlyList<DowngraderFileVersion> versions) => new()
    {
        ["gameInstallType"] = gameType.DisplayName(),
        ["versions"] = new JsonArray([.. versions.Select(v => (JsonNode)new JsonObject
        {
            ["file"] = v.FileName,
            ["installType"] = v.InstallType.DisplayName(),
        })]),
        ["crcsByType"] = new JsonObject(DowngraderFiles.CrcsByType.Select(kv => KeyValuePair.Create(
            kv.Key.DisplayName(),
            (JsonNode?)new JsonArray([.. kv.Value.Select(crc => JsonValue.Create(crc))])))),
    };
}
