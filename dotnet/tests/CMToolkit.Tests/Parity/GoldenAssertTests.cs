using System.Text;
using System.Text.Json.Nodes;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// The recorder behind <see cref="GoldenAssert"/>, run against a throwaway scenario in a temp folder: whether a replay
/// passes or fails, what the failure says, and which files end up beside the golden.
/// </summary>
/// <remarks>
/// Every test passes <c>record</c> explicitly rather than setting <c>CMT_RECORD_GOLDENS</c>: the variable is
/// process-wide, and setting it here would make the scenario tests running in parallel rewrite the committed goldens.
/// </remarks>
public sealed class GoldenAssertTests : IDisposable
{
    private readonly TempDirectory _scenarios = new();

    public void Dispose() => _scenarios.Dispose();

    /// <summary>A scenario <c>m/s</c> whose <c>golden.json</c> holds <paramref name="goldenJson"/>, or none if it is null.</summary>
    private ParityScenario Author(string? goldenJson)
    {
        _scenarios.WriteBytes(Path.Combine("m", "s", "scenario.json"), Encoding.UTF8.GetBytes("""{"operation": "op", "parity": [], "host": {}}"""));
        if (goldenJson is not null)
        {
            _scenarios.WriteBytes(Path.Combine("m", "s", "golden.json"), Encoding.UTF8.GetBytes(goldenJson));
        }

        return new ParityScenario(_scenarios.Path, "m/s");
    }

    // The file names are spelled out, not taken from ParityScenario, so these tests pin the on-disk names that
    // .gitignore and parity/README.md rely on independently of the code under test.
    private static string GoldenPath(ParityScenario scenario) => Path.Combine(scenario.Directory, "golden.json");

    private static string NewGoldenPath(ParityScenario scenario) => Path.Combine(scenario.Directory, "golden.new.json");

    [Fact]
    public void A_mismatch_writes_golden_new_json_and_fails_listing_each_difference_and_how_to_accept_it()
    {
        var scenario = Author("""{"a": 1, "b": 2}""");

        var ex = Assert.ThrowsAny<Exception>(() =>
            GoldenAssert.Matches(scenario, JsonNode.Parse("""{"a": 3, "b": 4}"""), GoldenCompareMode.Full, record: false));

        Assert.Contains("$.a: expected 1, got 3", ex.Message);
        Assert.Contains("$.b: expected 2, got 4", ex.Message);
        Assert.Contains(NewGoldenPath(scenario), ex.Message);
        Assert.Contains("replace golden.json with golden.new.json", ex.Message);
        Assert.Contains("CMT_RECORD_GOLDENS=1", ex.Message);
        // The full actual document is in the file now, not dumped into the message.
        Assert.DoesNotContain("Actual:", ex.Message);
        Assert.Empty(GoldenComparer.Differences(
            JsonNode.Parse(File.ReadAllText(NewGoldenPath(scenario))), JsonNode.Parse("""{"a": 3, "b": 4}""")));
        Assert.Equal("""{"a": 1, "b": 2}""", File.ReadAllText(GoldenPath(scenario)));
    }

    [Fact]
    public void A_match_passes_and_deletes_a_stale_golden_new_json()
    {
        var scenario = Author("""{"a": 1}""");
        File.WriteAllText(NewGoldenPath(scenario), """{"a": 2}""");

        GoldenAssert.Matches(scenario, JsonNode.Parse("""{"a": 1}"""), GoldenCompareMode.Full, record: false);

        Assert.False(File.Exists(NewGoldenPath(scenario)));
        Assert.Equal("""{"a": 1}""", File.ReadAllText(GoldenPath(scenario)));
    }

    [Fact]
    public void Recording_rewrites_golden_json_and_passes()
    {
        var scenario = Author("""{"a": 1}""");
        File.WriteAllText(NewGoldenPath(scenario), """{"a": 2}""");

        GoldenAssert.Matches(scenario, JsonNode.Parse("""{"a": 3}"""), GoldenCompareMode.Full, record: true);

        Assert.Equal("{\n  \"a\": 3\n}\n", File.ReadAllText(GoldenPath(scenario)));
        Assert.False(File.Exists(NewGoldenPath(scenario)));
        // A rerun without recording then passes.
        GoldenAssert.Matches(scenario, JsonNode.Parse("""{"a": 3}"""), GoldenCompareMode.Full, record: false);
    }

    [Fact]
    public void A_recording_is_written_in_the_golden_format_and_round_trips_through_the_comparer()
    {
        var scenario = Author(goldenJson: null);
        var actual = new JsonObject
        {
            ["z"] = new JsonObject { ["$unordered"] = new JsonArray("b", "a") },
            ["b"] = 0.5,
            ["a"] = new JsonArray(1, true, null, new JsonObject(), new JsonArray()),
            ["text"] = "é ✔ \U0001F600 \"q\" \\ \n\t\u0001\u007f <ROOT>/x & '",
        };

        GoldenAssert.Matches(scenario, actual, GoldenCompareMode.Full, record: true);

        // Sorted keys, two-space indent, LF, a closing newline, and every character outside printable ASCII as a
        // lowercase \u escape, which is how all the committed goldens were written.
        var expected = string.Join("\n",
            "{",
            "  \"a\": [",
            "    1,",
            "    true,",
            "    null,",
            "    {},",
            "    []",
            "  ],",
            "  \"b\": 0.5,",
            "  \"text\": \"\\u00e9 \\u2714 \\ud83d\\ude00 \\\"q\\\" \\\\ \\n\\t\\u0001\\u007f <ROOT>/x & '\",",
            "  \"z\": {",
            "    \"$unordered\": [",
            "      \"b\",",
            "      \"a\"",
            "    ]",
            "  }",
            "}",
            "");
        var written = File.ReadAllBytes(GoldenPath(scenario));
        Assert.Equal(expected, Encoding.UTF8.GetString(written));
        Assert.Empty(GoldenComparer.Differences(JsonNode.Parse(written), actual));
    }

    /// <summary>The file a recording lands in: <c>golden.json</c> when recording, <c>golden.new.json</c> on a mismatch.</summary>
    private static string RecordedPath(ParityScenario scenario, bool record) => record ? GoldenPath(scenario) : NewGoldenPath(scenario);

    /// <summary>Runs the assertion, swallowing the mismatch failure that a non-recording run is expected to raise.</summary>
    private static void Replay(ParityScenario scenario, string actualJson, GoldenCompareMode mode, bool record)
    {
        try
        {
            GoldenAssert.Matches(scenario, JsonNode.Parse(actualJson), mode, record);
        }
        catch (Exception) when (!record)
        {
            // The mismatch is the point; the test checks the file it left behind.
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_semantic_recording_keeps_the_goldens_display_sub_objects(bool record)
    {
        var scenario = Author("""
            {"source": "nexus", "display": {"log": "A"}, "items": [{"v": 1, "display": {"text": "one"}}]}
            """);

        Replay(scenario, """{"source": "github", "items": [{"v": 2}]}""", GoldenCompareMode.Semantic, record);

        Assert.Empty(GoldenComparer.Differences(
            JsonNode.Parse("""{"source": "github", "display": {"log": "A"}, "items": [{"v": 2, "display": {"text": "one"}}]}"""),
            JsonNode.Parse(File.ReadAllText(RecordedPath(scenario, record)))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_semantic_recording_fails_and_writes_nothing_when_a_display_sub_object_cant_be_placed(bool record)
    {
        const string golden = """{"item": {"v": 1, "display": {"text": "one"}}, "rows": [{"display": "r"}]}""";
        var scenario = Author(golden);

        var ex = Assert.ThrowsAny<Exception>(() =>
            GoldenAssert.Matches(scenario, JsonNode.Parse("""{"item": 2, "rows": []}"""), GoldenCompareMode.Semantic, record));

        Assert.Contains("display sub-objects can't be placed", ex.Message);
        Assert.Contains("Nothing was written", ex.Message);
        Assert.Contains("$.item", ex.Message);
        Assert.Contains("$.rows", ex.Message);
        Assert.False(File.Exists(NewGoldenPath(scenario)));
        Assert.Equal(golden, File.ReadAllText(GoldenPath(scenario)));
    }

    [Fact]
    public void A_semantic_recording_places_display_in_unordered_items_by_matching_their_semantic_fields()
    {
        var scenario = Author("""{"$unordered": [{"k": "a", "display": "A"}, {"k": "b", "display": "B"}]}""");

        Replay(scenario, """{"$unordered": [{"k": "b"}, {"k": "c"}, {"k": "a"}]}""", GoldenCompareMode.Semantic, record: true);

        // Matched items keep the golden's order; a new item follows them.
        Assert.Equal(
            """{"$unordered":[{"display":"A","k":"a"},{"display":"B","k":"b"},{"k":"c"}]}""",
            JsonNode.Parse(File.ReadAllText(GoldenPath(scenario)))!.ToJsonString());
    }

    [Fact]
    public void A_recording_keeps_the_goldens_order_for_unordered_items_it_still_has()
    {
        // Hash-ordered items come out in a different order on each run; keeping the golden's order keeps that noise
        // out of a re-record's diff.
        var scenario = Author("""{"s": {"$unordered": ["a", "b", "c"]}}""");

        Replay(scenario, """{"s": {"$unordered": ["d", "c", "a"]}}""", GoldenCompareMode.Full, record: true);

        Assert.Equal("""{"s":{"$unordered":["a","c","d"]}}""", JsonNode.Parse(File.ReadAllText(GoldenPath(scenario)))!.ToJsonString());
    }

    [Fact]
    public void An_unordered_run_keeps_the_goldens_order_when_the_array_around_it_changes_length()
    {
        // A log that loses lines (DEBUG dropped, a trace removed) shifts its runs to new indexes. Each run is still
        // matched to the golden's run it shares an item with, in order, rather than to the item at its old index.
        var scenario = Author("""{"log": ["x", {"$unordered": ["a", "b"]}, "y", {"$unordered": ["c", "d"]}, "z"]}""");

        Replay(scenario, """{"log": [{"$unordered": ["b", "a"]}, "y", {"$unordered": ["e", "d", "c"]}]}""", GoldenCompareMode.Full, record: true);

        Assert.Equal(
            """{"log":[{"$unordered":["a","b"]},"y",{"$unordered":["c","d","e"]}]}""",
            JsonNode.Parse(File.ReadAllText(GoldenPath(scenario)))!.ToJsonString());
    }

    [Fact]
    public void A_semantic_match_keeps_the_golden_new_json_a_full_mismatch_wrote_in_the_same_run()
    {
        // A Core test (Semantic) and an App test (Full) replay the same scenario. A change to display text only fails
        // the App test; the Core test's pass mustn't delete the recording the App test just left.
        var scenario = Author("""{"v": 1, "display": "old"}""");

        Replay(scenario, """{"v": 1, "display": "new"}""", GoldenCompareMode.Full, record: false);
        GoldenAssert.Matches(scenario, JsonNode.Parse("""{"v": 1}"""), GoldenCompareMode.Semantic, record: false);

        Assert.Equal("new", JsonNode.Parse(File.ReadAllText(NewGoldenPath(scenario)))!["display"]!.GetValue<string>());
    }

    [Fact]
    public void A_semantic_mismatch_keeps_the_display_a_full_mismatch_recorded_in_the_same_run()
    {
        var scenario = Author("""{"v": 1, "display": "old"}""");

        Replay(scenario, """{"v": 2, "display": "new"}""", GoldenCompareMode.Full, record: false);
        Replay(scenario, """{"v": 2}""", GoldenCompareMode.Semantic, record: false);

        Assert.Empty(GoldenComparer.Differences(
            JsonNode.Parse("""{"v": 2, "display": "new"}"""), JsonNode.Parse(File.ReadAllText(NewGoldenPath(scenario)))));
    }

    [Fact]
    public void An_unrecorded_scenario_fails_and_writes_golden_new_json()
    {
        var scenario = Author(goldenJson: null);

        var ex = Assert.ThrowsAny<Exception>(() =>
            GoldenAssert.Matches(scenario, JsonNode.Parse("""{"a": 1}"""), GoldenCompareMode.Full, record: false));

        Assert.Contains("no golden.json", ex.Message);
        Assert.Equal("{\n  \"a\": 1\n}\n", File.ReadAllText(NewGoldenPath(scenario)));
        Assert.False(File.Exists(GoldenPath(scenario)));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    public void Only_CMT_RECORD_GOLDENS_1_turns_recording_on(string? value, bool expected)
    {
        Assert.Equal(expected, GoldenAssert.RecordingRequested(value));
    }
}
