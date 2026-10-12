using System.Text;
using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// GP-1..GP-4, INI-1..INI-4 and B-14: the <c>environment/gp-*</c> and <c>environment/ini-*</c> scenarios, recorded from
/// the real <c>GameInfo</c> constructor (mod manager, game path with its dialogs, game INIs) and replayed against
/// <see cref="GameInstallLocator.LocateAsync"/>, the App flow around the Core calls, with the scenario's dialog script.
/// The dialogs' text is compared too, so the comparison is <see cref="GoldenCompareMode.Full"/>.
/// </summary>
public sealed class GameInfoScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("environment", "game-info");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task The_game_install_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var dialogs = new ScriptedStartupDialogs(machine.Host.Block.Dialogs ?? [], machine.Host.Expand);
        var actual = new JsonObject();

        try
        {
            var game = await GameInstallLocator.LocateAsync(machine.Host, dialogs);
            if (game is null)
            {
                actual["exitCode"] = 0;
            }
            else
            {
                actual["game"] = new JsonObject
                {
                    ["gamePath"] = PathText(machine, game.GamePath),
                    ["dataPath"] = PathText(machine, game.DataPath),
                    ["f4sePath"] = PathText(machine, game.F4sePath),
                    ["settings"] = Sections(game.Inis.Settings),
                    ["prefs"] = Sections(game.Inis.Prefs),
                    ["language"] = game.Inis.Language,
                    ["ba2Suffixes"] = new JsonArray([.. game.Inis.Ba2Suffixes.Select(s => JsonValue.Create(s))]),
                };
            }
        }
        catch (Exception ex)
        {
            actual["error"] = ex switch
            {
                DecoderFallbackException => new JsonObject { ["kind"] = "decode" },
                FileNotFoundException => new JsonObject
                {
                    ["kind"] = "fileNotFound",
                    ["message"] = ex.Message.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal),
                },
                _ => throw new InvalidOperationException("Unexpected failure", ex),
            };
        }

        Assert.Empty(dialogs.Unused);
        if (dialogs.Calls.Count > 0)
        {
            actual["dialogs"] = JsonNode.Parse(
                dialogs.Calls.ToJsonString().Replace(
                    JsonEncodedRoot(machine.Root), MaterializedScenario.RootToken, StringComparison.Ordinal));
        }

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    /// <summary>The root as it appears inside JSON-encoded text, so it can be tokenised in the serialised dialog calls.</summary>
    private static string JsonEncodedRoot(string root) => JsonValue.Create(root).ToJsonString()[1..^1];

    private static JsonObject Sections(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sections)
    {
        var json = new JsonObject();
        foreach (var (name, entries) in sections)
        {
            var section = new JsonObject();
            foreach (var (key, value) in entries)
            {
                section[key] = value;
            }

            json[name] = section;
        }

        return json;
    }

    /// <summary>
    /// The driver's <c>_path_text</c>: <c>str(path)</c> with the temp root as <c>&lt;ROOT&gt;</c> (a relative path is
    /// left as it is, T-7).
    /// </summary>
    private static string? PathText(MaterializedScenario machine, PythonPath? path)
        => path?.ToString().Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal);
}
