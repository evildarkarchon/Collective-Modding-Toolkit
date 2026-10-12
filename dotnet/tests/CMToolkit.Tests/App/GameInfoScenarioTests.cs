using System.Text;
using System.Text.Json;
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

    /// <summary>
    /// Answers for dialogs only the port asks, by scenario id. A Deviation can make the port ask where the reference
    /// never did, and <c>host.dialogs</c> can't script that answer: <c>record.py</c> rejects answers the reference leaves
    /// unused. They follow the scenario's own script.
    /// </summary>
    private static readonly Dictionary<string, DialogAnswer[]> PortOnlyAnswers = new(StringComparer.Ordinal)
    {
        // Bug #80: a registry path that isn't a Fallout 4 folder now leads to the "Fallout 4 Not Found" question.
        ["environment/gp-registry-invalid"] = [Answer("askyesno", "false")],
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task The_game_install_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        IReadOnlyList<DialogAnswer> answers =
            [.. machine.Host.Block.Dialogs ?? [], .. PortOnlyAnswers.GetValueOrDefault(scenario.Id, [])];
        var dialogs = new ScriptedStartupDialogs(answers, machine.Host.Expand);
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

    /// <summary>
    /// Bug #80, fixed: when every registry path is stale, the port asks for the location, and if the user's pick isn't
    /// a Fallout 4 folder either, the error still names the stale registry path (the reference's <c>registry_path</c>).
    /// </summary>
    [Fact]
    [Trait("Parity", "GP-3")]
    [Deviation(80, "A stale registry path leads to asking for the location; a bad pick then names the registry path.")]
    public async Task A_stale_registry_path_asks_and_a_bad_pick_names_it()
    {
        var scenario = new ParityScenario(ParityPaths.Scenarios, "environment/gp-registry-invalid");
        using var machine = scenario.Materialize();
        var dialogs = new ScriptedStartupDialogs(
            [Answer("askyesno", "true"), Answer("askopenfilename", "\"<ROOT>/Game/Data\"")], machine.Host.Expand);

        var game = await GameInstallLocator.LocateAsync(machine.Host, dialogs);

        Assert.Null(game);
        Assert.Empty(dialogs.Unused);
        Assert.Equal(["askyesno", "askopenfilename", "showerror"], dialogs.Calls.Select(c => (string)c!["function"]!));
        var error = dialogs.Calls[2]!["args"]!.AsArray();
        Assert.Equal("Game not found", (string)error[0]!);
        Assert.Equal(
            "A Fallout 4 installation could not be found.\n\nThe path set in your registry is:\n"
            + machine.Root + "\\Game\\\n\nIf this is not correct, please run the Fallout 4 Launcher to correct it.",
            (string)error[1]!);
    }

    /// <summary>A scripted answer whose value is the JSON text <paramref name="json"/>.</summary>
    private static DialogAnswer Answer(string function, string json)
        => new(function, JsonDocument.Parse(json).RootElement.Clone());

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
