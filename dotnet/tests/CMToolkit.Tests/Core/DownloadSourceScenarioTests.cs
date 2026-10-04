using System.Text.Json.Nodes;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Core;

/// <summary>
/// The harness's pipeline proof: the SET-2 Download Source scenarios, recorded by the Python driver from the
/// Reference Implementation, replayed against Core. The scenarios cite SET-2 in their <c>scenario.json</c>.
/// </summary>
public sealed class DownloadSourceScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("settings", "download-source");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();

        var lookup = DownloadSourceFile.Read(machine.Host.AppDirectory);

        // The log lines under "display" are the logging slice's to compare; Core returns the outcome they come from.
        GoldenAssert.Matches(scenario.LoadGolden(), Project(lookup), GoldenCompareMode.Semantic);
    }

    private static JsonObject Project(DownloadSourceLookup lookup) => new()
    {
        ["downloadSource"] = new JsonObject
        {
            ["source"] = lookup.Source switch
            {
                DownloadSource.Nexus => "nexus",
                DownloadSource.GitHub => "github",
                _ => throw new ArgumentOutOfRangeException(nameof(lookup), lookup.Source, null),
            },
            ["outcome"] = lookup.Outcome switch
            {
                DownloadSourceOutcome.Valid => "valid",
                DownloadSourceOutcome.Invalid => "invalid",
                DownloadSourceOutcome.ReadFailed => "readFailed",
                _ => throw new ArgumentOutOfRangeException(nameof(lookup), lookup.Outcome, null),
            },
            ["rawValue"] = lookup.RawValue,
        },
    };
}
