using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core.F4se;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Core.F4se;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The F4SE tab against the <c>f4se/*</c> goldens read off the reference's widgets (<c>f4se-tab</c>): the load error
/// or the failure and the loading label left up, or the tree's columns, rows, glyphs and tag colours, the heading and
/// the legend's coloured runs, plus the log lines of the selection. Compared in full.
/// </summary>
public sealed class F4seTabScenarioTests
{
    /// <summary>The Tk colours the reference's tags use, by the colour the port draws them in.</summary>
    private static readonly Dictionary<Color, string> TkColours = new()
    {
        [Color.Parse("#619267")] = "#619267",
        [Color.Parse("#AF5A66")] = "#AF5A66",
        [Color.Parse("#808080")] = "#808080",
        [Color.Parse("#FFE4C4")] = "#FFE4C4",
        [Color.Parse("#C5C464")] = "#C5C464",
        [Color.Parse("#4B92DA")] = "#4B92DA",
    };

    /// <summary>The Tk tag names of the legend's runs, by colour.</summary>
    private static readonly Dictionary<Color, string> AboutTags = new()
    {
        [Color.Parse("#FFE4C4")] = "neutral",
        [Color.Parse("#619267")] = "good",
        [Color.Parse("#4B92DA")] = "independent",
        [Color.Parse("#C5C464")] = "note",
    };

    /// <summary>The glyphs' semantic verdicts, as the driver's <c>_F4SE_VERDICTS</c> reads them.</summary>
    private static readonly Dictionary<string, string> Verdicts = new()
    {
        ["\u2714"] = "supported",
        ["\u2731"] = "independent",
        ["\u26A0"] = "note",
        ["\u2753"] = "unknown",
        ["\u274C"] = "unsupported",
        [""] = "unsupported",
    };

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("f4se", "f4se-tab");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public void The_f4se_tab_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        // The Overview loads first, as the initially selected tab.
        var state = new OverviewState { Report = OverviewReport.Gather(game, machine.Host, new CapturingLogger()) };
        var tab = new F4seTab(runtime, machine.Host, game, state);

        var window = new MainWindow(runtime, [tab]);
        window.Show();
        var lazy = window.GetLogicalDescendants().OfType<LazyTab>().Distinct().Single();
        // Done when built, when the error replaced the loading text, or when the failure was reported.
        DispatcherWait.Until(() => lazy.Content is not TextBlock
                                   || ((TextBlock)lazy.Content).Text != tab.LoadingText
                                   || runtime.Errors.Window is not null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var loaded = lazy.Content is not TextBlock;
        var failure = runtime.Errors.Window is { } errors
            ? errors.Text.Contains(nameof(F4sePluginLoadException), StringComparison.Ordinal)
                ? new JsonObject { ["kind"] = "os" }
                : throw new InvalidOperationException(errors.Text)
            : null;

        JsonObject display;
        JsonArray? verdicts = null;
        if (loaded)
        {
            display = ReadBuilt(window, tab, out verdicts);
        }
        else
        {
            var label = (TextBlock)lazy.Content!;
            var colour = ((ISolidColorBrush)label.Foreground!).Color;
            display = new JsonObject
            {
                ["loading"] = new JsonObject
                {
                    ["text"] = label.Text,
                    // Tk's default foreground is no colour of its own; only the failure colours the label.
                    ["color"] = colour == Color.Parse("#AF5A66") ? "#AF5A66" : null,
                },
            };
        }

        // The Error Window's own log line isn't one the reference's loggers wrote.
        display["log"] = new JsonArray([.. logger.Entries
            .Where(e => !e.Message.StartsWith("StdErr : ", StringComparison.Ordinal))
            .Select(e => (JsonNode)new JsonObject { ["level"] = LogFile.LevelName(e.Level), ["message"] = e.Message })]);

        var actual = new JsonObject
        {
            ["f4se"] = new JsonObject
            {
                ["installType"] = state.InstallType.DisplayName(),
                ["loaded"] = loaded,
                ["loadingError"] = tab.LoadingError,
                ["failure"] = failure,
                ["dlls"] = loaded ? new JsonArray([.. tab.Dlls.Select(d => (JsonNode)F4seScenarioTests.ProjectInfo(d))]) : null,
                ["verdicts"] = verdicts,
                ["display"] = display,
            },
        };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    /// <summary>The built tab as the driver's <c>_f4se_display</c> records it, reading every row as rendered.</summary>
    private static JsonObject ReadBuilt(MainWindow window, F4seTab tab, out JsonArray verdicts)
    {
        var content = TabHost.Content(window);
        var table = content.GetLogicalDescendants().OfType<TableView>().Distinct().Single();
        var heading = content.GetLogicalDescendants().OfType<TextBlock>().Distinct().Single(t => t.Name == "Heading");
        var about = content.GetLogicalDescendants().OfType<SelectableTextBlock>().Distinct().Single(t => t.Name == "About");

        var columns = new JsonArray();
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            columns.Add(new JsonObject
            {
                ["id"] = F4seTab.Columns[i].Id,
                ["heading"] = (string)column.Header!,
                ["width"] = (int)column.Width.Value,
                ["stretch"] = !column.Width.IsAbsolute,
                // Each cell lays its text out itself; the rows below check every cell keeps to its column's anchor.
                ["anchor"] = F4seTab.Columns[i].RightAligned ? "e" : "center",
            });
        }

        var rows = new JsonArray();
        verdicts = [];
        for (var i = 0; i < tab.Rows.Count; i++)
        {
            // The rows are virtualized: bring each into view before reading what it shows.
            table.ScrollIntoView(i);
            window.UpdateLayout();
            var row = (Control)table.ContainerFromIndex(i)!;
            var texts = row.GetVisualDescendants().OfType<TableViewCell>()
                .Select(c => c.GetVisualDescendants().OfType<TextBlock>().Single())
                .ToList();
            for (var c = 0; c < texts.Count; c++)
            {
                Assert.Equal(F4seTab.Columns[c].RightAligned ? HorizontalAlignment.Right : HorizontalAlignment.Center, texts[c].HorizontalAlignment);
            }

            var tags = texts.Select(t => Tag(((ISolidColorBrush)t.Foreground!).Color)).Distinct().ToList();
            Assert.Single(tags);
            // A non-plugin row has three values: its Your Game cell is empty, and Tk doesn't list it.
            var values = texts.Skip(1).Select(t => t.Text ?? "").ToList();
            if (tab.Rows[i].Cells.Count == 3)
            {
                Assert.Equal("", values[3]);
                values.RemoveAt(3);
            }

            rows.Add(new JsonObject
            {
                ["text"] = texts[0].Text,
                ["values"] = new JsonArray([.. values.Select(v => JsonValue.Create(v))]),
                ["tags"] = new JsonArray(JsonValue.Create(tags[0])),
            });
            verdicts.Add(new JsonObject
            {
                ["name"] = texts[0].Text,
                ["og"] = Verdicts[values[0]],
                ["ng"] = Verdicts[values[1]],
                ["ae"] = Verdicts[values[2]],
                ["yourGame"] = values.Count > 3 ? Verdicts[values[3]] : null,
            });
        }

        var tagColors = new JsonObject();
        foreach (var tag in Enum.GetValues<F4seRowTag>().OrderBy(TagName, StringComparer.Ordinal))
        {
            var brush = (ISolidColorBrush)window.FindResource(F4seTab.ColourKey(tag))!;
            tagColors[TagName(tag)] = TkColours[brush.Color];
        }

        // The one scrollbar shown is the tab's own, in the grid column beside the tree's.
        var scrollBar = content.GetLogicalDescendants().OfType<ScrollBar>().Distinct()
            .Concat(table.GetVisualDescendants().OfType<ScrollBar>())
            .Where(s => s.IsEffectivelyVisible && s.Bounds.Width > 0 && s.Visibility == ScrollBarVisibility.Visible)
            .Single();
        return new JsonObject
        {
            ["tree"] = new JsonObject { ["columns"] = columns, ["rows"] = rows, ["tagColors"] = tagColors },
            ["scrollbar"] = new JsonObject
            {
                ["orient"] = scrollBar.Orientation == Orientation.Vertical ? "vertical" : "horizontal",
                ["column"] = Grid.GetColumn(scrollBar),
            },
            ["heading"] = new JsonObject { ["text"] = heading.Text, ["font"] = Font(heading) },
            ["about"] = ReadAbout(about),
        };
    }

    /// <summary>The legend: its text, font, wrapping, read-only state, and each tag's colour and Tk index ranges.</summary>
    private static JsonObject ReadAbout(SelectableTextBlock about)
    {
        var text = new StringBuilder();
        var (line, column) = (1, 0);
        var ranges = new Dictionary<string, (string Colour, JsonArray Ranges)>();
        foreach (var inline in about.Inlines!)
        {
            if (inline is LineBreak)
            {
                text.Append('\n');
                (line, column) = (line + 1, 0);
                continue;
            }

            var run = (Run)inline;
            var colour = ((ISolidColorBrush)run.Foreground!).Color;
            if (run.IsSet(TextElement.ForegroundProperty) || colour != ((ISolidColorBrush)about.Foreground!).Color)
            {
                var tag = AboutTags[colour];
                if (!ranges.TryGetValue(tag, out var entry))
                {
                    ranges[tag] = entry = (TkColours[colour], []);
                }

                entry.Ranges.Add($"{line}.{column}");
                entry.Ranges.Add($"{line}.{column + run.Text!.Length}");
            }

            text.Append(run.Text);
            column += run.Text!.Length;
        }

        return new JsonObject
        {
            ["text"] = text.ToString(),
            ["font"] = Font(about),
            // Tk wraps the Text at any character; nothing in it is wider than the column, so no line wraps either way.
            ["wrap"] = about.TextWrapping == TextWrapping.Wrap ? "char" : "none",
            // A SelectableTextBlock is never editable, as the disabled Text isn't.
            ["state"] = "disabled",
            ["runs"] = new JsonArray([.. new[] { "neutral", "good", "independent", "note" }.Select(tag => (JsonNode)new JsonObject
            {
                ["tag"] = tag,
                ["color"] = ranges[tag].Colour,
                ["ranges"] = ranges[tag].Ranges,
            })]),
        };
    }

    private static JsonObject Font(TextBlock text) => new()
    {
        ["family"] = text.FontFamily.Name,
        // Tk points are whole: 10 pt is 13 px at 96 DPI, which is 9.75 pt back.
        ["size"] = (int)Math.Round(text.FontSize * 72 / 96),
        ["bold"] = text.FontWeight >= FontWeight.Bold,
        ["underline"] = text.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true,
    };

    private static string Tag(Color colour) => $"#{colour.ToUInt32():X8}" switch
    {
        "#FF808080" => "neutral",
        "#FF619267" => "good",
        "#FFAF5A66" => "bad",
        "#FFC5C464" => "note",
        "#FF4B92DA" => "independent",
        var other => throw new InvalidOperationException($"No tag is drawn in {other}."),
    };

    private static string TagName(F4seRowTag tag) => tag switch
    {
        F4seRowTag.Neutral => "neutral",
        F4seRowTag.Good => "good",
        F4seRowTag.Bad => "bad",
        F4seRowTag.Note => "note",
        F4seRowTag.Independent => "independent",
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null),
    };
}
