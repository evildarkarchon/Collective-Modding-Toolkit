using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Core.Updates;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMToolkit.Tests.App;

/// <summary>
/// The <c>update/*</c> scenarios (UPD-1..UPD-4, NET-1, NET-2, SHELL-7, T-4, T-14, B-16 as each cites), recorded from
/// <c>CMChecker.check_for_updates</c> with the scenario's canned HTTP, and replayed against
/// <see cref="UpdateBanner.CheckAsync"/> on a real main window: what each source returned, the log lines, the requests
/// made, and the banner read back off its controls (texts, image, colours, font, padding, cursor, tooltip and what a
/// click opens). An exception escaping the check fails startup in both (SHELL-13), and only that fact is compared.
/// </summary>
public sealed class UpdateCheckScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("update", "update-check");

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    public async Task The_update_check_and_banner_match_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var settings = SettingsFile.Load(machine.PathOf("Cwd/settings.json"), DownloadSource.Nexus, NullLogger.Instance);
        var logger = new RecordingLogger();
        var browser = new RecordingBrowser();
        var runtime = new AppRuntime(logger) { Browser = browser };
        var window = new MainWindow(runtime, PlaceholderTab.Shell());
        var http = machine.CreateHttpHandler();

        var actual = new JsonObject();
        UpdateCheckResult? result = null;
        try
        {
            result = await UpdateBanner.CheckAsync(window, runtime, settings.UpdateSource, http);
        }
        catch (UpdateCheckCrashException)
        {
            actual["error"] = new JsonObject { ["kind"] = "startupFailure" };
        }

        actual["updateCheck"] = new JsonObject
        {
            ["nexusVersion"] = result?.NexusVersion,
            ["githubVersion"] = result?.GitHubVersion,
            ["display"] = new JsonObject { ["log"] = LogLines(logger) },
        };

        window.Show();
        try
        {
            actual["banner"] = Banner(window.UpdateBanner, browser);
        }
        finally
        {
            window.Close();
        }

        if (http.Requests.Count > 0)
        {
            actual["httpRequests"] = new JsonArray([.. http.Requests.Select(Request)]);
        }

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
    }

    private static JsonArray LogLines(RecordingLogger logger)
    {
        var lines = new JsonArray();
        foreach (var record in logger.Records)
        {
            var line = new JsonObject { ["level"] = LogFile.LevelName(record.Level), ["message"] = record.Message };
            if (record.Exception is not null)
            {
                line["exception"] = true;
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// The request as the driver records <c>requests.get</c>'s keyword arguments. The session-wide defaults requests
    /// adds on its own (<c>User-Agent</c>, <c>Accept: */*</c>) were never keyword arguments, so they are left out here;
    /// the per-read timeout is the client's, and Nexus's response is streamed (<c>stream=True</c>), GitHub's isn't.
    /// </summary>
    private static JsonObject Request(ParityHttpRequest request)
    {
        var kwargs = new JsonObject();
        var headers = request.Headers
            .Where(h => h.Key != "User-Agent" && !(h.Key == "Accept" && h.Value == "*/*"))
            .ToList();
        if (headers.Count > 0)
        {
            kwargs["headers"] = new JsonObject(headers.Select(h => KeyValuePair.Create(h.Key, (JsonNode?)h.Value)));
        }

        if (request.Resource == "nexus-page")
        {
            // Streamed with ResponseHeadersRead and read only up to the version line; UpdateCheckTests proves the
            // read stops there.
            kwargs["stream"] = true;
        }

        kwargs["timeout"] = UpdateCheck.ReadTimeout.TotalSeconds;
        return new JsonObject { ["resource"] = request.Resource, ["kwargs"] = kwargs };
    }

    private static JsonObject? Banner(Control? banner, RecordingBrowser browser)
    {
        if (banner is null)
        {
            return null;
        }

        var grid = Assert.IsType<TkGrid>(banner);
        var labels = new JsonArray();
        foreach (var cell in grid.Children.OfType<Border>().OrderBy(Grid.GetColumn))
        {
            var text = cell.Child switch
            {
                TextBlock block => block,
                StackPanel panel => panel.Children.OfType<TextBlock>().Single(),
                var other => throw new InvalidOperationException($"Unexpected label content {other}"),
            };
            var image = (cell.Child as StackPanel)?.Children.OfType<AssetImage>().SingleOrDefault();
            var opened = browser.Opened.Count;
            MouseClick.On(cell);

            labels.Add(new JsonObject
            {
                ["column"] = Grid.GetColumn(cell),
                ["sticky"] = cell.HorizontalAlignment switch
                {
                    HorizontalAlignment.Left => "w",
                    HorizontalAlignment.Right => "e",
                    var other => throw new InvalidOperationException($"Unexpected alignment {other}"),
                },
                ["text"] = text.Text,
                ["image"] = image?.AssetPath,
                ["foreground"] = Hex(text.Foreground),
                ["background"] = Hex(cell.Background),
                ["font"] = new JsonObject
                {
                    ["family"] = text.FontFamily.Name,
                    // Tk points to DIPs at 96 DPI (issue #13): 12 pt is 16 DIP.
                    ["size"] = text.FontSize * 72 / 96,
                    ["bold"] = text.FontWeight == FontWeight.Bold,
                    ["underline"] = text.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true,
                },
                ["padding"] = UniformPadding(cell),
                ["cursor"] = cell.Cursor is null ? null : CursorName(cell),
                ["tooltip"] = ToolTip.GetTip(cell) as string,
                ["opens"] = browser.Opened.Count > opened ? browser.Opened[^1] : null,
            });
        }

        return new JsonObject
        {
            ["style"] = "Update.TFrame",
            ["columnWeights"] = new JsonArray([.. Enumerable.Range(0, labels.Count).Select(i => JsonValue.Create(Weight(grid.ColumnWeights, i)))]),
            ["rowWeight"] = Weight(grid.RowWeights, 0),
            ["labels"] = labels,
        };
    }

    private static int Weight(IReadOnlyList<int> weights, int index) => index < weights.Count ? weights[index] : 0;

    private static JsonArray UniformPadding(Border cell)
    {
        var padding = cell.Padding;
        Assert.True(padding.Left == padding.Top && padding.Top == padding.Right && padding.Right == padding.Bottom,
            $"Tk's single-value padding is uniform, but {padding} isn't.");
        return [padding.Left];
    }

    private static string CursorName(Border cell)
    {
        // Avalonia's Cursor doesn't expose which standard cursor it is, so the banner's one shared hand cursor
        // (StandardCursorType.Hand, Tk's hand2) is recognised by identity.
        Assert.Same(UpdateBanner.LinkCursor, cell.Cursor);
        return "hand2";
    }

    private static string? Hex(IBrush? brush) => brush switch
    {
        null => null,
        ISolidColorBrush solid => $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}",
        _ => throw new InvalidOperationException($"Unexpected brush {brush}"),
    };
}
