using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Scanner;

/// <summary>One mod file index entry: the mod a path belongs to and where it is (<c>tuple[str, Path]</c>).</summary>
/// <param name="Mod">The mod's folder name.</param>
/// <param name="Path">The file or folder in the mod's staging folder.</param>
public sealed record ModFile(string Mod, PythonPath Path);

/// <summary>
/// <c>scan_settings.ModFiles</c>: the mod file index the data walk builds in MO2 stage mode (SCN-S6), the
/// highest-priority mod winning each path. Path keys match case-insensitively, as <c>PureWindowsPath</c> keys do
/// (T-8); the module and archive keys are plain file-name strings, so they don't.
/// </summary>
public sealed class ModFiles
{
    /// <summary>Every indexed folder, by mod-relative path (<c>folders</c>).</summary>
    public Dictionary<PythonPath, ModFile> Folders { get; } = new(PythonPath.Equality);

    /// <summary>Every indexed file, by mod-relative path (<c>files</c>). SCN-R2 looks Overview problems up here.</summary>
    public Dictionary<PythonPath, ModFile> Files { get; } = new(PythonPath.Equality);

    /// <summary>The modules at a mod's root, by file name (<c>modules</c>).</summary>
    public Dictionary<string, ModFile> Modules { get; } = new(StringComparer.Ordinal);

    /// <summary>The archives at a mod's root, by file name (<c>archives</c>).</summary>
    public Dictionary<string, ModFile> Archives { get; } = new(StringComparer.Ordinal);
}

/// <summary>One results tree group: a problem type and its items, in display order.</summary>
/// <param name="Kind">The problem type, the group row's text.</param>
/// <param name="Items">The problems of that type, stable-sorted by type + mod.</param>
public sealed record ScanResultGroup(string Kind, IReadOnlyList<Problem> Items);

/// <summary>
/// The Scanner's results, from the scan's start to the tree (<c>ScannerTab.start_threaded_scan</c> and
/// <c>populate_results</c>; the Core seams resolution on issue #7: the SCN-R2 remap returns new records).
/// </summary>
public static class ScanResults
{
    /// <summary>
    /// The results a scan starts with (SCN-S3): the Overview problems, in their list order, when Overview Issues is
    /// ticked; none otherwise. The worker's problems are appended after them.
    /// </summary>
    /// <param name="overviewIssues">Whether Overview Issues was ticked.</param>
    /// <param name="overviewProblems">The problems of the Overview refresh the scan just ran.</param>
    public static IReadOnlyList<Problem> Seed(bool overviewIssues, IReadOnlyList<Problem> overviewProblems)
        => overviewIssues ? [.. overviewProblems] : [];

    /// <summary>The results info label (SCN-R1).</summary>
    public static string InfoText(int count) => $"{count} Results ~ Select an item for details";

    /// <summary>
    /// The tree's groups (SCN-R2, SCN-R3). Each Overview module or archive problem (<see cref="ModSource.Overview"/>) first
    /// gets its mod: the index's mod for its relative path, which is just the file name, so only a file at a mod's root
    /// matches; an empty mod without a match or without an index. Then one group per problem type, in the order the
    /// types first appear, each holding its problems stable-sorted by type + mod, compared by code point as Python
    /// compares strings.
    /// </summary>
    /// <remarks>
    /// The reference iterates a Python set for the groups, so their order is random per run (B-4); first-seen order is
    /// one of the orders it can produce. Items with equal keys keep their results order, which for the Overview's
    /// archive problems is the enabled-archive set order (T-15).
    /// </remarks>
    /// <param name="results">The scan's results: the seeded Overview problems, then the worker's.</param>
    /// <param name="index">The mod file index, which only an MO2 stage-mode data walk builds; null otherwise.</param>
    public static IReadOnlyList<ScanResultGroup> Group(IReadOnlyList<Problem> results, ModFiles? index)
    {
        var remapped = results.Select(p => p.ModSource == ModSource.Overview ? WithIndexedMod(p, index) : p).ToList();
        // OrderBy is a stable sort, as Python's sorted is.
        var sorted = remapped.OrderBy(p => p.Kind + p.Mod, CodePointComparer.Instance).ToList();
        return
        [
            .. remapped.Select(p => p.Kind).Distinct(StringComparer.Ordinal)
                .Select(kind => new ScanResultGroup(kind, [.. sorted.Where(p => p.Kind == kind)])),
        ];
    }

    /// <summary><c>mod_files.files.get(Path(problem.relative_path), [""])[0]</c>, or "" without an index.</summary>
    private static Problem WithIndexedMod(Problem problem, ModFiles? index)
    {
        var mod = index is not null && index.Files.TryGetValue(new PythonPath(problem.RelativePath), out var file) ? file.Mod : string.Empty;
        return problem with { Mod = mod, ModSource = ModSource.Given };
    }

    /// <summary>Python's <c>str</c> ordering: by Unicode code point, not by UTF-16 code unit.</summary>
    private sealed class CodePointComparer : IComparer<string>
    {
        public static readonly CodePointComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null)
            {
                return x is null ? (y is null ? 0 : -1) : 1;
            }

            var left = x.EnumerateRunes();
            var right = y.EnumerateRunes();
            while (true)
            {
                var hasLeft = left.MoveNext();
                var hasRight = right.MoveNext();
                if (!hasLeft || !hasRight)
                {
                    return hasLeft.CompareTo(hasRight);
                }

                var order = left.Current.Value.CompareTo(right.Current.Value);
                if (order != 0)
                {
                    return order;
                }
            }
        }
    }
}
