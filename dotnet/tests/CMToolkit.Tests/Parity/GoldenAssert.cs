using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// Assertions over <see cref="GoldenComparer"/> that also record the golden (ADR-0008). A mismatch writes the app's
/// result to <c>golden.new.json</c> beside the scenario's <c>golden.json</c> and fails; accepting it means replacing
/// <c>golden.json</c> with that file. With <c>CMT_RECORD_GOLDENS=1</c> the result is written to <c>golden.json</c>
/// directly and the replay passes.
/// </summary>
public static class GoldenAssert
{
    /// <summary>The environment variable that makes every replay rewrite its <c>golden.json</c>.</summary>
    public const string RecordVariable = "CMT_RECORD_GOLDENS";

    /// <summary>
    /// Every <c>golden.new.json</c> a mismatch wrote in this test run. A scenario can be replayed by more than one
    /// test (a Core test in <see cref="GoldenCompareMode.Semantic"/> and an App test in
    /// <see cref="GoldenCompareMode.Full"/>), so a passing replay may only delete a <c>golden.new.json</c> left from an
    /// earlier run, never one another replay just wrote. Guarded by <see cref="ParityScenario.GoldenFileLock"/>.
    /// </summary>
    private static readonly HashSet<string> WrittenThisRun = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fails, listing every difference by JSON path, unless <paramref name="actual"/> matches
    /// <paramref name="scenario"/>'s golden. On a mismatch it first writes <paramref name="actual"/> to
    /// <c>golden.new.json</c>; on a match it deletes a <c>golden.new.json</c> left from an earlier run. With
    /// <c>CMT_RECORD_GOLDENS=1</c> it rewrites <c>golden.json</c> instead and passes.
    /// </summary>
    /// <exception cref="XunitException">
    /// The result doesn't match, or a <see cref="GoldenCompareMode.Semantic"/> recording can't place the golden's
    /// <c>display</c> sub-objects.
    /// </exception>
    public static void Matches(ParityScenario scenario, JsonNode? actual, GoldenCompareMode mode)
        => Matches(scenario, actual, mode, RecordingRequested(Environment.GetEnvironmentVariable(RecordVariable)));

    /// <summary>
    /// <see cref="Matches(ParityScenario, JsonNode?, GoldenCompareMode)"/> with recording chosen by
    /// <paramref name="record"/> rather than the environment, so the recorder's own tests never touch the process-wide
    /// variable that every other replay reads.
    /// </summary>
    /// <remarks>
    /// The read, compare and write run under <see cref="ParityScenario.GoldenFileLock"/>: replays of one scenario run
    /// in parallel, and each must see the others' files whole. Whichever order they run in, the recordings converge:
    /// a Semantic recording takes its <c>display</c> sub-objects from whatever a Full recording already wrote.
    /// </remarks>
    internal static void Matches(ParityScenario scenario, JsonNode? actual, GoldenCompareMode mode, bool record)
    {
        lock (ParityScenario.GoldenFileLock)
        {
            var recorded = scenario.IsRecorded;
            var golden = recorded ? scenario.LoadGolden() : null;
            IReadOnlyList<string> diffs = recorded
                ? GoldenComparer.Differences(golden, actual, mode)
                : [$"$: the scenario has no {ParityScenario.GoldenFileName} yet"];
            var newGoldenPath = scenario.NewGoldenPath;

            if (!record && diffs.Count == 0)
            {
                if (!WrittenThisRun.Contains(newGoldenPath))
                {
                    File.Delete(newGoldenPath);
                }

                return;
            }

            // Display text and unordered item order come from the newest recording of this scenario: the golden.new.json
            // another replay wrote in this run if there is one, so a Semantic replay doesn't undo a Full one's display.
            var basis = !record && WrittenThisRun.Contains(newGoldenPath) && File.Exists(newGoldenPath)
                ? JsonNode.Parse(File.ReadAllText(newGoldenPath))
                : golden;
            var unplaced = new List<string>();
            var recording = Align(basis, actual, mode, "$", unplaced);
            if (unplaced.Count > 0)
            {
                throw new XunitException(
                    $"Can't record Parity Scenario '{scenario.Id}' ({mode}): the result changed shape so much that the "
                    + "golden's display sub-objects can't be placed at:" + Environment.NewLine
                    + string.Join(Environment.NewLine, unplaced) + Environment.NewLine
                    + "Nothing was written. Record the scenario through a replay that compares in Full mode first; this replay "
                    + "then keeps that recording's display text." + Environment.NewLine
                    + Describe(mode, diffs));
            }

            if (record)
            {
                File.WriteAllText(scenario.GoldenPath, GoldenWriter.Write(recording));
                File.Delete(newGoldenPath);
                return;
            }

            File.WriteAllText(newGoldenPath, GoldenWriter.Write(recording));
            WrittenThisRun.Add(newGoldenPath);
            throw new XunitException(
                $"Parity Scenario '{scenario.Id}': " + Describe(mode, diffs) + Environment.NewLine
                + $"Wrote the app's result to {newGoldenPath}. To accept it, replace {ParityScenario.GoldenFileName} with "
                + $"{ParityScenario.NewGoldenFileName} and review the diff, or set {RecordVariable}=1 to re-record every "
                + "scenario a run replays.");
        }
    }

    /// <summary>Whether a <c>CMT_RECORD_GOLDENS</c> value of <paramref name="value"/> asks for recording: only <c>1</c> does.</summary>
    internal static bool RecordingRequested(string? value) => value == "1";

    private static string Describe(GoldenCompareMode mode, IReadOnlyList<string> diffs)
        => $"the result doesn't match the golden ({mode}, {diffs.Count} difference(s)):{Environment.NewLine}"
           + string.Join(Environment.NewLine, diffs);

    /// <summary>
    /// Builds what to record: a copy of <paramref name="actual"/> lined up with <paramref name="basis"/>, the recording
    /// it replaces. Items of an unordered array that are still there keep the basis's order, so a hash-ordered
    /// dimension doesn't reshuffle on every re-record. In <see cref="GoldenCompareMode.Semantic"/>, where the replay
    /// produced no display text, each <c>display</c> sub-object of the basis is copied to the same place in the copy.
    /// </summary>
    /// <param name="path">The JSON path of the two nodes, for messages.</param>
    /// <param name="unplaced">
    /// Receives the path of each basis <c>display</c> sub-object that has no place in <paramref name="actual"/>: its
    /// parent object is gone, is no longer an object, or sits in an array whose length changed. Only Semantic adds to it.
    /// </param>
    private static JsonNode? Align(JsonNode? basis, JsonNode? actual, GoldenCompareMode mode, string path, List<string> unplaced)
    {
        var semantic = mode is GoldenCompareMode.Semantic;
        switch (basis, actual)
        {
            case (JsonObject b, JsonObject a) when IsUnordered(b) && IsUnordered(a):
                return new JsonObject
                {
                    [GoldenComparer.UnorderedKey] = AlignUnordered(
                        b[GoldenComparer.UnorderedKey]!.AsArray(), a[GoldenComparer.UnorderedKey]!.AsArray(), mode, path, unplaced),
                };

            case (JsonObject b, JsonObject a) when !IsUnordered(b) && !IsUnordered(a):
                var keepDisplay = semantic && b.ContainsKey(GoldenComparer.DisplayKey);
                var aligned = new JsonObject();
                foreach (var (key, value) in a)
                {
                    if (keepDisplay && key == GoldenComparer.DisplayKey)
                    {
                        continue;
                    }

                    aligned[key] = Align(b.TryGetPropertyValue(key, out var other) ? other : null, value, mode, $"{path}.{key}", unplaced);
                }

                if (keepDisplay)
                {
                    aligned[GoldenComparer.DisplayKey] = b[GoldenComparer.DisplayKey]?.DeepClone();
                }

                if (semantic)
                {
                    unplaced.AddRange(b
                        .Where(p => p.Key != GoldenComparer.DisplayKey && !a.ContainsKey(p.Key) && HasDisplay(p.Value))
                        .Select(p => $"{path}.{p.Key}"));
                }

                return aligned;

            case (JsonArray b, JsonArray a) when b.Count == a.Count:
                return new JsonArray(a.Select((item, i) => Align(b[i], item, mode, $"{path}[{i}]", unplaced)).ToArray());

            // A Semantic replay whose basis holds display text in a resized array falls through to default, which
            // reports it as unplaced: only the order of unordered runs is recovered here, not display placement.
            case (JsonArray b, JsonArray a) when !(semantic && HasDisplay(b)):
                return AlignResized(b, a, mode, path, unplaced);

            default:
                if (semantic && HasDisplay(basis))
                {
                    unplaced.Add(basis is JsonArray items && actual is JsonArray
                        ? $"{path} (the golden has {items.Count} items, the result {actual.AsArray().Count})"
                        : path);
                }

                return actual?.DeepClone();
        }
    }

    /// <summary>
    /// Lines up the items of two unordered arrays. Each basis item, in order, takes the first unclaimed actual item
    /// that matches it under <paramref name="mode"/>, which is how <see cref="GoldenComparer"/> pairs them too. The
    /// actual items left over follow in the order the replay produced them.
    /// </summary>
    private static JsonArray AlignUnordered(JsonArray basis, JsonArray actual, GoldenCompareMode mode, string path, List<string> unplaced)
    {
        var unclaimed = actual.ToList();
        var aligned = new JsonArray();
        for (var i = 0; i < basis.Count; i++)
        {
            var index = unclaimed.FindIndex(candidate => GoldenComparer.Differences(basis[i], candidate, mode).Count == 0);
            if (index < 0)
            {
                if (mode is GoldenCompareMode.Semantic && HasDisplay(basis[i]))
                {
                    unplaced.Add($"{path} (unordered item {i} of the golden matches nothing in the result)");
                }

                continue;
            }

            aligned.Add(Align(basis[i], unclaimed[index], mode, $"{path}[{aligned.Count}]", unplaced));
            unclaimed.RemoveAt(index);
        }

        foreach (var item in unclaimed)
        {
            aligned.Add(item?.DeepClone());
        }

        return aligned;
    }

    /// <summary>
    /// Lines up two arrays whose lengths differ, such as a log that lost or gained lines, where items can't be paired
    /// by index. Each unordered run of <paramref name="actual"/> is paired with the first basis run after the last one
    /// paired that shares an item with it, and keeps that run's order (<see cref="AlignUnordered"/>). Every other item
    /// is recorded as the replay produced it.
    /// </summary>
    private static JsonArray AlignResized(JsonArray basis, JsonArray actual, GoldenCompareMode mode, string path, List<string> unplaced)
    {
        var aligned = new JsonArray();
        var next = 0;
        for (var i = 0; i < actual.Count; i++)
        {
            if (actual[i] is JsonObject run && IsUnordered(run) && FindRun(basis, next, run, mode) is var match and >= 0)
            {
                aligned.Add(Align(basis[match], run, mode, $"{path}[{i}]", unplaced));
                next = match + 1;
                continue;
            }

            aligned.Add(actual[i]?.DeepClone());
        }

        return aligned;
    }

    /// <summary>
    /// The index of the first unordered run in <paramref name="basis"/>, from <paramref name="from"/> on, that has an
    /// item matching one of <paramref name="run"/>'s under <paramref name="mode"/>; -1 if there is none.
    /// </summary>
    private static int FindRun(JsonArray basis, int from, JsonObject run, GoldenCompareMode mode)
    {
        var items = run[GoldenComparer.UnorderedKey]!.AsArray();
        for (var j = from; j < basis.Count; j++)
        {
            if (basis[j] is JsonObject candidate && IsUnordered(candidate)
                && candidate[GoldenComparer.UnorderedKey]!.AsArray()
                    .Any(b => items.Any(a => GoldenComparer.Differences(b, a, mode).Count == 0)))
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>The marker <see cref="GoldenComparer"/> compares as a multiset: an object whose only key is <c>$unordered</c>.</summary>
    private static bool IsUnordered(JsonObject node) => node.Count == 1 && node[GoldenComparer.UnorderedKey] is JsonArray;

    /// <summary>Whether <paramref name="node"/> holds a <c>display</c> sub-object anywhere inside it.</summary>
    private static bool HasDisplay(JsonNode? node) => node switch
    {
        JsonObject obj => obj.ContainsKey(GoldenComparer.DisplayKey) || obj.Any(p => HasDisplay(p.Value)),
        JsonArray array => array.Any(HasDisplay),
        _ => false,
    };
}
