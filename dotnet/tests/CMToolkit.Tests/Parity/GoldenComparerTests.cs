using System.Text.Json.Nodes;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// The golden comparer (ADR-0004): exact, except that <c>{"$unordered": [...]}</c> arrays are compared as multisets,
/// and <see cref="GoldenCompareMode.Semantic"/> skips the <c>display</c> sub-objects that only App presenters check.
/// </summary>
public sealed class GoldenComparerTests
{
    private static IReadOnlyList<string> Diff(string expected, string actual, GoldenCompareMode mode = GoldenCompareMode.Full)
        => GoldenComparer.Differences(JsonNode.Parse(expected), JsonNode.Parse(actual), mode);

    [Fact]
    public void Identical_documents_match()
    {
        Assert.Empty(Diff("""{"a": [1, "x", true, null], "b": {"c": 2.5}}""", """{"b": {"c": 2.5}, "a": [1, "x", true, null]}"""));
    }

    [Fact]
    public void Object_key_order_does_not_matter_but_the_key_set_does()
    {
        var diffs = Diff("""{"a": 1, "b": 2}""", """{"a": 1, "c": 2}""");

        Assert.Equal(["$.b: missing", "$.c: unexpected"], diffs);
    }

    [Fact]
    public void Arrays_are_compared_in_order()
    {
        var diffs = Diff("""{"tabs": ["Overview", "F4SE"]}""", """{"tabs": ["F4SE", "Overview"]}""");

        Assert.Equal(["$.tabs[0]: expected \"Overview\", got \"F4SE\"", "$.tabs[1]: expected \"F4SE\", got \"Overview\""], diffs);
    }

    [Fact]
    public void Array_length_differences_are_reported()
    {
        Assert.Equal(["$: expected 2 items, got 1"], Diff("[1, 2]", "[1]"));
    }

    [Fact]
    public void Strings_compare_ordinally_including_invisible_characters()
    {
        Assert.Empty(Diff("\"\\ufeffgithub\"", "\"\uFEFFgithub\""));
        Assert.Single(Diff("\"github\"", "\"GitHub\""));
        Assert.Single(Diff("\"\\ufeffgithub\"", "\"github\""));
    }

    [Fact]
    public void Numbers_compare_by_value_so_python_floats_match_their_integral_csharp_projection()
    {
        // Python's json writes 1.0 for a float; a C# projection may write 1. Rendered forms (T-3) live in display strings.
        Assert.Empty(Diff("1.0", "1"));
        Assert.Empty(Diff("0.95", "0.950"));
        Assert.Single(Diff("1.7", "1.70001"));
    }

    [Fact]
    public void Value_kinds_must_match()
    {
        Assert.Equal(["$: expected \"1\", got 1"], Diff("\"1\"", "1"));
        Assert.Equal(["$: expected null, got false"], Diff("null", "false"));
        Assert.Equal(["$: expected an object, got an array"], Diff("{}", "[]"));
    }

    [Fact]
    public void Unordered_arrays_are_compared_as_multisets()
    {
        Assert.Empty(Diff("""{"$unordered": ["a", "b", "a"]}""", """{"$unordered": ["a", "a", "b"]}"""));
    }

    [Fact]
    public void Unordered_arrays_report_missing_and_unexpected_items_by_multiplicity()
    {
        var diffs = Diff("""{"$unordered": ["a", "b", "a"]}""", """{"$unordered": ["a", "b", "c"]}""");

        Assert.Equal(["$: unordered item missing: \"a\"", "$: unordered item unexpected: \"c\""], diffs);
    }

    [Fact]
    public void Unordered_items_can_be_objects_and_nest_their_own_markers()
    {
        const string expected = """{"$unordered": [{"g": "x", "items": {"$unordered": [1, 2]}}, {"g": "y", "items": {"$unordered": []}}]}""";
        const string actual = """{"$unordered": [{"g": "y", "items": {"$unordered": []}}, {"g": "x", "items": {"$unordered": [2, 1]}}]}""";

        Assert.Empty(Diff(expected, actual));
    }

    [Fact]
    public void An_unordered_marker_must_be_matched_by_an_unordered_marker()
    {
        // The projection on both sides must agree on which fields are unordered, so a plain array doesn't match one.
        Assert.Equal(["$: expected an unordered array, got an array"], Diff("""{"$unordered": [1]}""", "[1]"));
    }

    [Fact]
    public void Full_mode_compares_display_sub_objects()
    {
        var diffs = Diff(
            """{"source": "nexus", "display": {"log": "A"}}""",
            """{"source": "nexus", "display": {"log": "B"}}""");

        Assert.Equal(["$.display.log: expected \"A\", got \"B\""], diffs);
    }

    [Fact]
    public void Semantic_mode_skips_display_sub_objects_on_both_sides()
    {
        Assert.Empty(Diff(
            """{"source": "nexus", "display": {"log": "A"}}""",
            """{"source": "nexus"}""",
            GoldenCompareMode.Semantic));
        Assert.Single(Diff(
            """{"source": "nexus", "display": {"log": "A"}}""",
            """{"source": "github"}""",
            GoldenCompareMode.Semantic));
    }

    [Fact]
    public void Assert_matches_fails_with_every_difference_listed()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GoldenAssert.Matches(JsonNode.Parse("""{"a": 1, "b": 2}"""), JsonNode.Parse("""{"a": 3, "b": 4}"""), GoldenCompareMode.Full));

        Assert.Contains("$.a: expected 1, got 3", ex.Message);
        Assert.Contains("$.b: expected 2, got 4", ex.Message);
    }
}
