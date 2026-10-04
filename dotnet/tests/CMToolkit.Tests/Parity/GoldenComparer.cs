using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace CMToolkit.Tests.Parity;

/// <summary>Which parts of a golden a comparison looks at.</summary>
public enum GoldenCompareMode
{
    /// <summary>Everything, <c>display</c> sub-objects included. For App presenter tests that check rendered text.</summary>
    Full,

    /// <summary>
    /// Everything except <c>display</c> sub-objects, on both sides. For Core tests: Core returns semantic values, and
    /// rendering them is the App's job (the Core seams resolution on issue #7).
    /// </summary>
    Semantic,
}

/// <summary>
/// Compares a recorded golden with the C# side's projection of the same results (ADR-0004). The comparison is exact:
/// same object keys, arrays in the same order, same value kinds and values. The one relaxation is the unordered
/// marker, <c>{"$unordered": [...]}</c>, whose items are compared as a multiset; the driver writes it for hash-ordered
/// dimensions (T-15, B-4) and fields a scenario marks unordered, and the C# projection must write it for the same
/// fields. Numbers compare by value, so Python's <c>1.0</c> matches a C# <c>1</c>; the rendered form of a float (T-3)
/// belongs in a <c>display</c> string, where it is compared exactly.
/// </summary>
public static class GoldenComparer
{
    /// <summary>The key of the unordered marker object.</summary>
    public const string UnorderedKey = "$unordered";

    /// <summary>The key of the rendered-text sub-object that <see cref="GoldenCompareMode.Semantic"/> skips.</summary>
    public const string DisplayKey = "display";

    /// <summary>
    /// Lists every difference between <paramref name="expected"/> and <paramref name="actual"/>, each as
    /// <c>&lt;json path&gt;: &lt;what differs&gt;</c>. An empty list means they match.
    /// </summary>
    public static IReadOnlyList<string> Differences(JsonNode? expected, JsonNode? actual, GoldenCompareMode mode = GoldenCompareMode.Full)
    {
        var diffs = new List<string>();
        Compare(expected, actual, "$", mode, diffs);
        return diffs;
    }

    private static void Compare(JsonNode? expected, JsonNode? actual, string path, GoldenCompareMode mode, List<string> diffs)
    {
        var expectedKind = KindOf(expected);
        var actualKind = KindOf(actual);
        if (expectedKind != actualKind)
        {
            diffs.Add(expectedKind is NodeKind.Value && actualKind is NodeKind.Value
                ? $"{path}: expected {Show(expected)}, got {Show(actual)}"
                : $"{path}: expected {Describe(expectedKind, expected)}, got {Describe(actualKind, actual)}");
            return;
        }

        switch (expectedKind)
        {
            case NodeKind.Object:
                CompareObjects(expected!.AsObject(), actual!.AsObject(), path, mode, diffs);
                break;
            case NodeKind.Array:
                CompareArrays(expected!.AsArray(), actual!.AsArray(), path, mode, diffs);
                break;
            case NodeKind.Unordered:
                CompareUnordered(UnorderedItems(expected!), UnorderedItems(actual!), path, mode, diffs);
                break;
            case NodeKind.Value:
                if (!ValuesEqual(expected, actual))
                {
                    diffs.Add($"{path}: expected {Show(expected)}, got {Show(actual)}");
                }

                break;
        }
    }

    private static void CompareObjects(JsonObject expected, JsonObject actual, string path, GoldenCompareMode mode, List<string> diffs)
    {
        bool Included(string key) => mode is GoldenCompareMode.Full || key != DisplayKey;

        foreach (var (key, value) in expected)
        {
            if (!Included(key))
            {
                continue;
            }

            if (actual.TryGetPropertyValue(key, out var other))
            {
                Compare(value, other, $"{path}.{key}", mode, diffs);
            }
            else
            {
                diffs.Add($"{path}.{key}: missing");
            }
        }

        foreach (var (key, _) in actual)
        {
            if (Included(key) && !expected.ContainsKey(key))
            {
                diffs.Add($"{path}.{key}: unexpected");
            }
        }
    }

    private static void CompareArrays(JsonArray expected, JsonArray actual, string path, GoldenCompareMode mode, List<string> diffs)
    {
        if (expected.Count != actual.Count)
        {
            diffs.Add($"{path}: expected {expected.Count} items, got {actual.Count}");
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            Compare(expected[i], actual[i], $"{path}[{i}]", mode, diffs);
        }
    }

    /// <summary>
    /// Multiset comparison: each expected item consumes the first still-unmatched actual item equal to it. Greedy
    /// matching is exact here because equality (an empty difference list) is an equivalence relation.
    /// </summary>
    private static void CompareUnordered(JsonArray expected, JsonArray actual, string path, GoldenCompareMode mode, List<string> diffs)
    {
        var unmatched = actual.ToList();
        var missing = new List<JsonNode?>();
        foreach (var item in expected)
        {
            var index = unmatched.FindIndex(candidate => Differences(item, candidate, mode).Count == 0);
            if (index < 0)
            {
                missing.Add(item);
            }
            else
            {
                unmatched.RemoveAt(index);
            }
        }

        diffs.AddRange(missing.Select(item => $"{path}: unordered item missing: {Show(item)}"));
        diffs.AddRange(unmatched.Select(item => $"{path}: unordered item unexpected: {Show(item)}"));
    }

    private static bool ValuesEqual(JsonNode? expected, JsonNode? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        var kind = expected.GetValueKind();
        if (kind != actual.GetValueKind())
        {
            // True and False are distinct value kinds, which is the only way two booleans can differ.
            return false;
        }

        return kind switch
        {
            JsonValueKind.String => string.Equals(expected.GetValue<string>(), actual.GetValue<string>(), StringComparison.Ordinal),
            JsonValueKind.Number => ParseNumber(expected) == ParseNumber(actual),
            _ => true,
        };
    }

    /// <summary>
    /// Reads a number through its JSON text as a <see cref="decimal"/>, which holds every value the goldens use
    /// exactly, whatever CLR type a constructed <see cref="JsonValue"/> wraps.
    /// </summary>
    private static decimal ParseNumber(JsonNode node)
        => decimal.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private enum NodeKind
    {
        Value,
        Object,
        Array,
        Unordered,
    }

    /// <summary>JSON null counts as a value, so <c>null</c> against <c>false</c> is reported as two values.</summary>
    private static NodeKind KindOf(JsonNode? node) => node switch
    {
        JsonObject obj when obj.Count == 1 && obj.ContainsKey(UnorderedKey) => NodeKind.Unordered,
        JsonObject => NodeKind.Object,
        JsonArray => NodeKind.Array,
        _ => NodeKind.Value,
    };

    private static JsonArray UnorderedItems(JsonNode marker)
        => marker[UnorderedKey] as JsonArray
           ?? throw new InvalidDataException($"The value of \"{UnorderedKey}\" must be an array: {marker.ToJsonString()}");

    private static string Describe(NodeKind kind, JsonNode? node) => kind switch
    {
        NodeKind.Object => "an object",
        NodeKind.Array => "an array",
        NodeKind.Unordered => "an unordered array",
        _ => Show(node),
    };

    private static string Show(JsonNode? node) => node?.ToJsonString() ?? "null";
}

/// <summary>Assertions over <see cref="GoldenComparer"/>.</summary>
public static class GoldenAssert
{
    /// <summary>Fails, listing every difference, unless <paramref name="actual"/> matches the golden.</summary>
    /// <exception cref="XunitException">The documents differ.</exception>
    public static void Matches(JsonNode? expected, JsonNode? actual, GoldenCompareMode mode)
    {
        var diffs = GoldenComparer.Differences(expected, actual, mode);
        if (diffs.Count > 0)
        {
            throw new XunitException(
                $"The result doesn't match the golden ({mode}, {diffs.Count} difference(s)):{Environment.NewLine}"
                + string.Join(Environment.NewLine, diffs)
                + $"{Environment.NewLine}Actual:{Environment.NewLine}{actual?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
        }
    }
}
