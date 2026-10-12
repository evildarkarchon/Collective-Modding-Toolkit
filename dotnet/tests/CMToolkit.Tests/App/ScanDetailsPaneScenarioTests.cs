using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Scanner's details pane against the <c>scanner-details</c> goldens: the real Overview and Scanner tabs in a main
/// window, an Overview Issues + Race Subgraphs scan, then every result selected in the driver's order and read back:
/// the rows, the clickable path, the solution's link clicks, the buttons, Copy Details with its 3 s revert (THR-3) and
/// the File List window. Compared in full.
/// </summary>
public sealed class ScanDetailsPaneScenarioTests
{
    private const string Sentinel = "<untouched>";

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("scanner", "scanner-details");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    [Trait("Parity", "SCN-R4")]
    [Trait("Parity", "SCN-R5")]
    [Trait("Parity", "SCN-R6")]
    [Trait("Parity", "SCN-R7")]
    [Trait("Parity", "THR-3")]
    public void The_details_pane_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd ?? MaterializedScenario.RootToken), AppFolderFiles.SettingsFileName);
        var logger = new CapturingLogger();
        var settings = SettingsStore.Load(settingsPath, DownloadSource.Nexus, logger);
        var browser = new RecordingBrowser();
        var time = new FakeTimeProvider();
        var runtime = new AppRuntime(logger) { Browser = browser, Time = time };
        var state = new OverviewState();
        var overview = new OverviewTab(runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
        {
            OpenFolder = _ => { },
            ShowWarning = (_, _) => throw new InvalidOperationException("The details scenarios show no warning."),
        };
        var started = new List<string>();
        var scanner = new ScannerTab(runtime, game, state, settings)
        {
            Host = machine.Host,
            OpenFolder = folder => started.Add(folder.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal)),
        };
        var window = new MainWindow(runtime, [overview, scanner]);
        window.Show();
        DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);

        var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        foreach (var setting in ScanSettings.All.Where(s => s is not (ScanSetting.OverviewIssues or ScanSetting.RaceSubgraphs)))
        {
            MouseClick.On(scanner.SidePane!.Checks[setting]);
        }

        Scan(scanner);

        var items = scanner.Tree!.Items.OfType<TreeViewItem>().SelectMany(g => g.Items.OfType<TreeViewItem>())
            .Select(i => (Item: i, Problem: scanner.ProblemOf(i)!))
            .OrderBy(i => Key(machine, i.Problem), KeyComparer.Instance)
            .ToList();
        var details = new JsonArray();
        var first = true;
        foreach (var (item, problem) in items)
        {
            scanner.Tree.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();
            var pane = scanner.DetailsPane!;
            var entry = new JsonObject
            {
                ["type"] = problem.Kind,
                ["itemText"] = problem.ItemText,
                ["display"] = new JsonObject
                {
                    ["rows"] = Rows(pane),
                    ["path"] = new JsonObject { ["cursor"] = CursorName(pane.PathLabel.Cursor), ["tooltip"] = ToolTip.GetTip(pane.PathLabel) as string },
                    ["solution"] = new JsonObject { ["tooltip"] = ToolTip.GetTip(pane.SolutionLabel) as string },
                    ["buttons"] = new JsonArray([.. pane.Buttons.Select(State)]),
                },
                ["pathOpens"] = Click(pane.PathLabel, started),
                ["solutionOpens"] = Click(pane.SolutionLabel, browser.Opened),
                ["solutionCopies"] = RightClickCopies(pane),
            };

            if (!first)
            {
                // The previous Copy Details click has waited 2999 ms: one more and its after(3000) reverts it.
                time.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
            }

            first = false;
            var copy = pane.Buttons.Single(b => (string?)b.Content == "Copy Details");
            SetClipboard(pane, "");
            MouseClick.On(copy);
            var copies = Clipboard(pane);
            var clicked = State(copy);
            time.Advance(CopyTextButton.RevertAfter - TimeSpan.FromMilliseconds(1));
            Dispatcher.UIThread.RunJobs();
            entry["copy"] = new JsonObject
            {
                ["copies"] = copies,
                ["clicked"] = clicked,
                // Still Copied! a millisecond short of 3 s; the next item's step proves it reverts at 3 s exactly.
                ["revertAfterMs"] = (string?)copy.Content == CopyTextButton.CopiedText ? new JsonArray(3000) : new JsonArray(),
            };

            if (pane.Buttons.SingleOrDefault(b => (string?)b.Content == "File List") is { } files)
            {
                MouseClick.On(files);
                entry["fileList"] = ReadTreeWindow(window);
            }

            details.Add(entry);
        }

        Scan(scanner);

        var actual = new JsonObject
        {
            ["scanner"] = new JsonObject
            {
                ["usingStage"] = scanner.UsingStage,
                ["details"] = details,
                ["afterRescan"] = new JsonObject { ["detailsPane"] = scanner.DetailsPane is not null },
                ["errors"] = logger.Entries.Count(e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal)),
            },
        };

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
        window.Close();
    }

    /// <summary>The driver's sort key: (type, item text, relative path, mod).</summary>
    private static string[] Key(MaterializedScenario machine, Problem problem)
        => [problem.Kind, problem.ItemText, problem.RelativePath.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal), problem.Mod];

    private static void Scan(ScannerTab scanner)
    {
        MouseClick.On(scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => (string?)scanner.SidePane!.ScanButton.Content == "Scan Game");
    }

    /// <summary>The pane's rows in grid order: each label (column 0) with the value beside it (column 1).</summary>
    private static JsonArray Rows(ScanDetailsPane pane)
    {
        var rows = new JsonArray();
        var labels = pane.Rows.Children.OfType<TextBlock>()
            .Where(t => Grid.GetColumn(t) == 0 && t.IsVisible)
            .OrderBy(Grid.GetRow);
        foreach (var label in labels)
        {
            var cell = pane.Rows.Children.Single(c => Grid.GetColumn(c) == 1 && Grid.GetRow(c) == Grid.GetRow(label));
            var (value, wraplength) = cell switch
            {
                WraplengthPresenter presenter => ((TextBlock)presenter.Child!, (int?)presenter.Wraplength),
                TextBlock text => (text, null),
                _ => throw new InvalidOperationException("Unexpected value cell."),
            };
            rows.Add(new JsonObject
            {
                ["label"] = label.Text,
                ["labelFont"] = Font(label),
                ["value"] = value.Text,
                ["valueFont"] = Font(value),
                ["valueColor"] = Colour(value),
                ["wraplength"] = wraplength,
            });
        }

        return rows;
    }

    private static JsonObject Font(TextBlock text) => new()
    {
        ["family"] = text.FontFamily.Name,
        // Tk points are whole: 10 pt is 13 px at 96 DPI, which is 9.75 pt back.
        ["size"] = (int)Math.Round(text.FontSize * 72 / 96),
        ["bold"] = text.FontWeight >= FontWeight.Bold,
        ["underline"] = text.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true,
    };

    private static string Colour(TextBlock text)
        => "#" + ((ISolidColorBrush)text.Foreground!).Color.ToUInt32().ToString("X8", System.Globalization.CultureInfo.InvariantCulture)[2..];

    private static string CursorName(Avalonia.Input.Cursor? cursor)
        => ReferenceEquals(cursor, ScanDetailsPane.LocationCursor) ? "hand2"
            : ReferenceEquals(cursor, ScanDetailsPane.NoLocationCursor) ? "X_cursor"
            : throw new InvalidOperationException("The path label has an unexpected cursor.");

    private static JsonObject State(Button button) => new() { ["text"] = (string?)button.Content, ["enabled"] = button.IsEnabled };

    /// <summary>Left-clicks <paramref name="label"/> and returns what it recorded, or null if the click did nothing.</summary>
    private static string? Click(Control label, IReadOnlyList<string> record)
    {
        var before = record.Count;
        MouseClick.On(label);
        return record.Count > before ? record[before] : null;
    }

    private static string? RightClickCopies(ScanDetailsPane pane)
    {
        SetClipboard(pane, Sentinel);
        RightClick.On(pane.SolutionLabel);
        var text = Clipboard(pane);
        return text == Sentinel ? null : text;
    }

    private static void SetClipboard(Control control, string text)
    {
        TopLevel.GetTopLevel(control)!.Clipboard!.SetTextAsync(text).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private static string? Clipboard(Control control)
    {
        Dispatcher.UIThread.RunJobs();
        return TopLevel.GetTopLevel(control)!.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();
    }

    /// <summary>Reads back the TreeWindow File List opened over the main window, then closes it.</summary>
    private static JsonObject? ReadTreeWindow(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        var tree = window.OwnedWindows.OfType<TreeWindow>().SingleOrDefault();
        if (tree is null)
        {
            return null;
        }

        var opened = new JsonObject
        {
            ["window"] = "TreeWindow",
            ["width"] = (int)tree.Width,
            ["height"] = (int)tree.Height,
            ["title"] = tree.Title,
            ["text"] = ((TextBlock)tree.GetLogicalDescendants().OfType<WraplengthPresenter>().First().Child!).Text,
            ["headers"] = new JsonArray([.. tree.GetLogicalDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("heading"))
                .Select(t => (JsonNode?)t.Text)]),
            ["rows"] = new JsonArray([.. tree.Rows.Select(r => (JsonNode)new JsonArray(r.Text, r.Value))]),
        };
        tree.Close();
        Dispatcher.UIThread.RunJobs();
        return opened;
    }

    /// <summary>Python's tuple-of-strings order: element by element, by code point.</summary>
    private sealed class KeyComparer : IComparer<string[]>
    {
        public static KeyComparer Instance { get; } = new();

        public int Compare(string[]? x, string[]? y)
        {
            for (var i = 0; i < Math.Min(x!.Length, y!.Length); i++)
            {
                var c = string.CompareOrdinal(x[i], y[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
