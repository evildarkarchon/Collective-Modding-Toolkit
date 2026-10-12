using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Settings tab against the golden read off the reference's widgets (<c>settings-tab</c>): the frames and radios as
/// built over the scenario's <c>settings.json</c>, then every radio clicked in turn, the last one twice, each click's
/// selection, the file it saved and its log lines (the scenarios cite SETT-1 and SET-7).
/// </summary>
public sealed class SettingsTabScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("settings", "settings-tab");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_settings_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd!), WorkingDirectoryFiles.Settings);
        var logger = new RecordingLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSourceFile.Read(machine.Host.AppDirectory).Source, logger);
        // As in the driver, the load's own lines are the settings-load scenarios' business.
        var loaded = logger.Records.Count;
        var runtime = new AppRuntime(new CapturingLogger());
        var window = TabHost.Show(runtime, new SettingsTab(store));

        var frames = TabHost.Content(window).GetLogicalChildren().OfType<GroupBox>().ToList();
        var radios = frames.SelectMany(Radios).ToList();
        var built = new JsonArray();
        foreach (var frame in frames)
        {
            built.Add(new JsonObject
            {
                ["title"] = ((TextBlock)frame.Header!).Text,
                ["radios"] = new JsonArray([.. Radios(frame).Select(radio => new JsonObject
                {
                    ["text"] = (string)radio.Content!,
                    ["value"] = Value(radio),
                    ["selected"] = radio.IsChecked == true,
                    ["tooltip"] = ToolTip.GetTip(radio) as string,
                })]),
            });
        }

        var clicks = new JsonArray();
        var seen = loaded;
        foreach (var radio in radios.Append(radios[^1]))
        {
            MouseClick.On(radio);
            var log = new JsonArray();
            foreach (var record in logger.Records.Skip(seen))
            {
                log.Add(new JsonObject { ["level"] = LogFile.LevelName(record.Level), ["message"] = record.Message });
            }

            seen = logger.Records.Count;
            clicks.Add(new JsonObject
            {
                ["radio"] = (string)radio.Content!,
                ["selected"] = new JsonArray([.. radios.Where(r => r.IsChecked == true).Select(r => JsonValue.Create((string)r.Content!))]),
                ["settingsFile"] = FileSnapshot.Of(settingsPath),
                ["log"] = log,
            });
        }

        var actual = new JsonObject
        {
            ["settingsTab"] = new JsonObject { ["frames"] = built },
            ["clicks"] = clicks,
        };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    private static IEnumerable<RadioButton> Radios(GroupBox frame) => ((Panel)frame.Content!).Children.OfType<RadioButton>();

    /// <summary>
    /// The settings value a radio stands for. An Avalonia radio has no value of its own, so this restates the
    /// reference's; what each radio actually saves is proven by the clicks' <c>settingsFile</c> snapshots.
    /// </summary>
    private static string Value(RadioButton radio)
        => ((string)radio.Content!) switch
        {
            "All: GitHub & Nexus Mods" => "both",
            "Early: GitHub" => "github",
            "Stable: Nexus Mods" => "nexus",
            "Never: Don't Check" => "none",
            "Debug" => "DEBUG",
            "Info" => "INFO",
            "Error" => "ERROR",
            var text => throw new InvalidOperationException($"Unknown radio {text}"),
        };
}
