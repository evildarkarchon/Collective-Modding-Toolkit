using System.Text.Json.Nodes;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core;

/// <summary>
/// The <c>settings-load</c> scenarios: <c>AppSettings()</c> recorded from the Reference Implementation against a
/// <c>settings.json</c> in the scenario's working directory, replayed against <see cref="SettingsFile.Load"/>. Each
/// scenario cites the IDs it proves (SET-1, SET-3..SET-6, T-10, LOG-2).
/// </summary>
/// <remarks>
/// Compared in <see cref="GoldenCompareMode.Full"/>, not the usual Semantic, because the log lines under
/// <c>display</c> are Core's own output here: Core writes the <c>Settings : …</c> messages, and the App's log file only
/// prefixes the level.
/// </remarks>
public sealed class SettingsScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("settings", "settings-load");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd!), WorkingDirectoryFiles.Settings);
        var downloadSource = DownloadSourceFile.Read(machine.Host.AppDirectory).Source;
        var logger = new RecordingLogger();

        var settings = SettingsFile.Load(settingsPath, downloadSource, logger);

        GoldenAssert.Matches(scenario.LoadGolden(), Project(settings, logger, settingsPath), GoldenCompareMode.Full);
    }

    private static JsonObject Project(AppSettings settings, RecordingLogger logger, string settingsPath)
    {
        var values = new JsonObject();
        foreach (var (key, value) in SettingsFile.Entries(settings))
        {
            values[key] = value switch
            {
                string s => JsonValue.Create(s),
                bool b => JsonValue.Create(b),
                _ => throw new InvalidOperationException($"Unexpected value type for {key}: {value.GetType()}"),
            };
        }

        var log = new JsonArray();
        foreach (var record in logger.Records)
        {
            var line = new JsonObject { ["level"] = LogFile.LevelName(record.Level), ["message"] = record.Message };
            if (record.Exception is not null)
            {
                line["exception"] = true;
            }

            log.Add(line);
        }

        return new JsonObject
        {
            ["settings"] = new JsonObject
            {
                ["values"] = values,
                ["display"] = new JsonObject { ["log"] = log },
            },
            ["settingsFile"] = FileSnapshot(settingsPath),
        };
    }

    /// <summary>The driver's <c>file_snapshot</c>: null, <c>{"text"}</c> for strict UTF-8, else <c>{"base64"}</c>.</summary>
    private static JsonObject? FileSnapshot(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        try
        {
            return new JsonObject { ["text"] = new System.Text.UTF8Encoding(false, true).GetString(bytes) };
        }
        catch (System.Text.DecoderFallbackException)
        {
            return new JsonObject { ["base64"] = Convert.ToBase64String(bytes) };
        }
    }
}
