using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Tests.Core.ArchivePatcher;
using CMToolkit.Tests.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Archive Patcher against the <c>archive-patcher/*</c> goldens, driven through the driver's script in the app as
/// startup composes it: opened from the Overview's <c>Archive Patcher...</c> button (OVW-A6), Patch All clicked, the
/// <c>v8 (NG)</c> radio clicked, the Name Filter typed into and emptied. After each step the window is read back as the
/// driver reads the Tk one: the caption, the entry, the list, the log pane's lines with their emoji colours (§9.5) and
/// the log lines written meanwhile (LOG-1), and after each run the Data folder's bytes (AP-3). Compared in full.
/// </summary>
public sealed class ArchivePatcherWindowScenarioTests
{
    /// <summary>The Tk colour names the reference's widgets use, by the colour the port draws them in.</summary>
    private static readonly Dictionary<Color, string> TkColours = new()
    {
        [Color.Parse("#CACACA")] = "#CACACA",
        [Color.Parse("#619267")] = "#619267",
        [Color.Parse("#AF5A66")] = "#AF5A66",
        [Color.Parse("#1E90FF")] = "dodger blue",
        [Color.Parse("#FFE4C4")] = "bisque",
    };

    /// <summary>The per-archive lines of a run (and the refresh's per-archive checks), which come in set order.</summary>
    private static readonly string[] SetOrdered =
        ["Patched to v", "Unrecognized format: ", "Skipping already-patched archive: ", "Unrecognized version [", "Failed patching (", "Archives: Checking "];

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("archive-patcher", "archive-patcher");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_archive_patcher_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        using var app = new ComposedSession(machine);
        var changes = ArchivePatcherGolden.ApplyChanges(machine);
        var logged = app.Logger.Entries.Count;

        MouseClick.On(ComposedSession.Named<Button>(app.Window, "ArchivePatcher"));
        var patcher = app.WaitForArchivePatcher();
        Assert.Same(app.Window, patcher.Owner);
        var data = app.Session.Game.DataPath!.ToString();
        var box = ComposedSession.Named<TextBox>(patcher, "NameFilter");

        JsonObject Step(string name, bool patched = false)
        {
            var lines = app.Logger.Entries.Skip(logged).ToList();
            logged = app.Logger.Entries.Count;
            var step = new JsonObject
            {
                ["step"] = name,
                ["desiredVersion"] = (int)patcher.DesiredVersion,
                ["nameFilter"] = patcher.NameFilter,
                ["files"] = ArchivePatcherGolden.Unordered(machine, patcher.FilesToPatch),
                ["display"] = new JsonObject
                {
                    ["caption"] = Label(ComposedSession.Named<TextBlock>(patcher, "FilterCaption")),
                    ["filterEntry"] = box.Text ?? string.Empty,
                    ["tree"] = new JsonArray([.. patcher.Rows.Select(r => (JsonNode)r)]),
                    ["log"] = OverviewFullScenarioTests.UnorderedRuns(PaneLines(patcher.Log), InRun),
                    ["logLines"] = OverviewFullScenarioTests.UnorderedRuns(
                        [.. lines.Select(e => (JsonNode)new JsonObject
                        {
                            ["level"] = LogFile.LevelName(e.Level),
                            ["message"] = e.Message.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal),
                        })],
                        InRun),
                    ["processingData"] = patcher.ProcessingData,
                },
            };
            if (patched)
            {
                step["data"] = ArchivePatcherGolden.DataSnapshot(machine, data);
            }

            return step;
        }

        void PatchAll()
        {
            MouseClick.On(ComposedSession.Named<Button>(patcher, "PatchAll"));
            DispatcherWait.Until(() => !patcher.ProcessingData);
            patcher.UpdateLayout();
        }

        void TypeFilter(string text)
        {
            // Typed into the entry: the text goes in, then the key's release runs on_key_release.
            box.Focus();
            box.SelectAll();
            if (text.Length > 0)
            {
                patcher.KeyTextInput(text);
                patcher.KeyRelease(Key.S, RawInputModifiers.Shift, PhysicalKey.S, "S");
            }
            else
            {
                patcher.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
                patcher.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            }

            Dispatcher.UIThread.RunJobs();
        }

        var opened = Step("opened");
        var about = ComposedSession.Named<Button>(patcher, "About");
        var desired = ComposedSession.Named<GroupBox>(patcher, "DesiredVersion");
        var extras = new JsonObject
        {
            ["title"] = patcher.Title,
            ["width"] = (int)patcher.Width,
            ["height"] = (int)patcher.Height,
            ["desiredVersion"] = new JsonObject
            {
                ["title"] = ((TextBlock)desired.Header!).Text,
                ["radios"] = new JsonArray([.. new[] { "OldGen", "NextGen" }.Select(n =>
                {
                    var radio = ComposedSession.Named<RadioButton>(patcher, n);
                    return (JsonNode)new JsonObject { ["text"] = (string)radio.Content!, ["selected"] = radio.IsChecked == true };
                })]),
            },
            ["patch"] = new JsonObject { ["text"] = (string)ComposedSession.Named<Button>(patcher, "PatchAll").Content! },
            ["about"] = new JsonObject { ["text"] = (string)about.Content!, ["opens"] = ClickAbout(patcher, about) },
            ["nameLabel"] = Label(ComposedSession.Named<TextBlock>(patcher, "NameFilterLabel")),
        };
        foreach (var (key, value) in extras.ToList())
        {
            extras.Remove(key);
            opened["display"]![key] = value;
        }

        var steps = new JsonArray(opened);
        PatchAll();
        steps.Add(Step("patched", patched: true));
        MouseClick.On(ComposedSession.Named<RadioButton>(patcher, "NextGen"));
        steps.Add(Step("v8"));
        TypeFilter(ArchivePatcherGolden.Filter);
        steps.Add(Step("filtered"));
        PatchAll();
        steps.Add(Step("patchedFiltered", patched: true));
        TypeFilter(string.Empty);
        steps.Add(Step("unfiltered"));

        var actual = new JsonObject { ["archivePatcher"] = new JsonObject { ["changes"] = changes, ["steps"] = steps } };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
        patcher.Close();
    }

    /// <summary>
    /// The driver's <c>_log_pane_lines</c>: each message as its emoji (with the space after it), the emoji's colour,
    /// and the text after it.
    /// </summary>
    internal static List<JsonNode> PaneLines(LogPane pane)
    {
        var lines = new List<JsonNode>();
        var runs = pane.Runs;
        for (var i = 0; i + 1 < runs.Count; i += 2)
        {
            lines.Add(new JsonObject
            {
                ["emoji"] = runs[i].Text,
                ["color"] = Colour(runs[i].Foreground),
                ["text"] = runs[i + 1].Text!.TrimEnd('\n'),
            });
        }

        return lines;
    }

    private static bool InRun(JsonNode item)
    {
        var text = (string?)item["text"] ?? (string?)item["message"] ?? string.Empty;
        return SetOrdered.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Clicks About and records the AboutWindow it opens over the patcher, then closes it.</summary>
    private static JsonObject? ClickAbout(ArchivePatcherWindow patcher, Button about)
    {
        MouseClick.On(about);
        var opened = patcher.OwnedWindows.OfType<AboutWindow>().SingleOrDefault();
        if (opened is null)
        {
            return null;
        }

        var result = new JsonObject
        {
            ["window"] = "AboutWindow",
            ["width"] = (int)opened.Width,
            ["height"] = (int)opened.Height,
            ["title"] = opened.Title,
            ["text"] = opened.GetLogicalDescendants().OfType<TextBlock>().First().Text,
        };
        opened.Close();
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    private static JsonObject Label(TextBlock label) => new() { ["text"] = label.Text, ["color"] = Colour(label.Foreground) };

    private static string Colour(IBrush? brush)
    {
        var colour = ((ISolidColorBrush)brush!).Color;
        return TkColours.TryGetValue(colour, out var name) ? name : colour.ToString();
    }
}
