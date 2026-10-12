using System.Text.Json.Nodes;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

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
        var logger = new RecordingLogger();
        DownloadSourceFile.LogOutcome(lookup, logger);

        // Full, so the log lines under "display" are compared too: Core writes them. The recorded lines are the
        // reference's own; only where they end up differs (bug #77, fixed: the port's reach cm-toolkit.log).
        GoldenAssert.Matches(scenario.LoadGolden(), Project(lookup, logger), GoldenCompareMode.Full);
    }

    private static JsonObject Project(DownloadSourceLookup lookup, RecordingLogger logger) => new()
    {
        ["downloadSource"] = new JsonObject
        {
            ["display"] = new JsonObject
            {
                ["log"] = new JsonArray([.. logger.Records.Select(record =>
                {
                    var line = new JsonObject { ["level"] = LogFile.LevelName(record.Level), ["message"] = record.Message };
                    if (record.Exception is not null)
                    {
                        line["exception"] = true;
                    }

                    return (JsonNode)line;
                })]),
            },
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
