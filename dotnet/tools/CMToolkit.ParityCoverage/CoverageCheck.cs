using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMToolkit.ParityCoverage;

/// <summary>Where the check's inputs live, relative to the repo root.</summary>
/// <param name="Root">The repo root.</param>
public sealed record RepoLayout(string Root)
{
    /// <summary><c>docs/parity-inventory.md</c>.</summary>
    public string Inventory => Path.Combine(Root, "docs", "parity-inventory.md");

    /// <summary><c>parity/covered.txt</c>: the ratchet, one proven ID per line, <c>#</c> comments allowed.</summary>
    public string Covered => Path.Combine(Root, "parity", "covered.txt");

    /// <summary><c>parity/coverage-manual.json</c>: proofs that aren't a scenario or a test.</summary>
    public string Manual => Path.Combine(Root, "parity", "coverage-manual.json");

    /// <summary><c>parity/scenarios/</c>.</summary>
    public string Scenarios => Path.Combine(Root, "parity", "scenarios");

    /// <summary><c>dotnet/tests/</c>, searched for <c>[Trait("Parity", …)]</c>.</summary>
    public string Tests => Path.Combine(Root, "dotnet", "tests");

    /// <summary>The layout rooted at <paramref name="root"/>.</summary>
    public static RepoLayout At(string root) => new(Path.GetFullPath(root));

    /// <summary>The layout of the repo containing <paramref name="start"/>: the nearest folder holding the inventory.</summary>
    /// <exception cref="DirectoryNotFoundException">No folder at or above <paramref name="start"/> holds it.</exception>
    public static RepoLayout Find(string start)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "parity-inventory.md")))
            {
                return new RepoLayout(dir.FullName);
            }
        }

        throw new DirectoryNotFoundException($"No docs/parity-inventory.md at or above {start}.");
    }
}

/// <summary>One piece of evidence that an ID is proven.</summary>
/// <param name="Id">The Parity Inventory ID.</param>
/// <param name="Source">Where it comes from: <c>scenario &lt;id&gt;</c>, <c>trait &lt;file&gt;</c> or <c>manual &lt;kind&gt;</c>.</param>
public sealed record Proof(string Id, string Source);

/// <summary>What the check found.</summary>
/// <param name="InventoryIds">Every inventory ID, in document order.</param>
/// <param name="Proofs">Every proof collected, valid or not.</param>
/// <param name="Covered">The IDs in <c>covered.txt</c>.</param>
/// <param name="Unproven">Inventory IDs with no proof, in document order.</param>
/// <param name="NotRatcheted">Proven inventory IDs missing from <c>covered.txt</c> (for example <i>part</i> IDs).</param>
/// <param name="Errors">Everything that fails the check.</param>
public sealed record CoverageReport(
    IReadOnlyList<string> InventoryIds,
    IReadOnlyList<Proof> Proofs,
    IReadOnlyList<string> Covered,
    IReadOnlyList<string> Unproven,
    IReadOnlyList<string> NotRatcheted,
    IReadOnlyList<string> Errors)
{
    /// <summary>Whether the check passed.</summary>
    public bool Passed => Errors.Count == 0;
}

/// <summary>
/// The Parity Inventory coverage check (ADR-0004). It collects proofs from <c>scenario.json</c> files,
/// <c>[Trait("Parity", …)]</c> attributes and <c>coverage-manual.json</c>, then fails if an ID in <c>covered.txt</c>
/// has no proof, or if any proof (or <c>covered.txt</c>) cites an ID that isn't in the inventory. With
/// <c>strict</c>, it also fails on every inventory ID that isn't in <c>covered.txt</c> or has no proof, which is what
/// "full Behaviour Parity" means.
/// </summary>
public static partial class CoverageCheck
{
    /// <summary>The kinds of proof <c>coverage-manual.json</c> accepts.</summary>
    public static readonly IReadOnlySet<string> ManualProofKinds = new HashSet<string>(StringComparer.Ordinal) { "exempt", "manual", "screenshot" };

    /// <summary>Runs the check.</summary>
    /// <exception cref="FormatException">The inventory itself is malformed.</exception>
    public static CoverageReport Run(RepoLayout layout, bool strict)
    {
        var errors = new List<string>();
        var inventory = ParityInventory.ParseIds(File.ReadAllText(layout.Inventory));
        var known = inventory.ToHashSet(StringComparer.Ordinal);

        var proofs = new List<Proof>();
        proofs.AddRange(ScenarioProofs(layout, errors));
        proofs.AddRange(TraitProofs(layout));
        proofs.AddRange(ManualProofs(layout, errors));

        foreach (var proof in proofs.Where(p => !known.Contains(p.Id)))
        {
            errors.Add($"{proof.Source} cites {proof.Id}, which isn't in the Parity Inventory.");
        }

        var proven = proofs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var covered = ReadCovered(layout);
        foreach (var id in covered)
        {
            if (!known.Contains(id))
            {
                errors.Add($"covered.txt lists {id}, which isn't in the Parity Inventory.");
            }
            else if (!proven.Contains(id))
            {
                errors.Add($"covered.txt lists {id}, but nothing proves it.");
            }
        }

        var coveredSet = covered.ToHashSet(StringComparer.Ordinal);
        var unproven = inventory.Where(id => !proven.Contains(id)).ToList();
        var notRatcheted = inventory.Where(id => proven.Contains(id) && !coveredSet.Contains(id)).ToList();
        if (strict)
        {
            errors.AddRange(unproven.Select(id => $"--strict: {id} has no proof."));
            errors.AddRange(notRatcheted.Select(id => $"--strict: {id} is proven but not in covered.txt."));
        }

        return new CoverageReport(inventory, proofs, covered, unproven, notRatcheted, errors);
    }

    /// <summary>
    /// The <c>parity</c> IDs of every scenario. A scenario without a <c>golden.json</c> proves nothing: it was never
    /// recorded, so no C# test can have passed it.
    /// </summary>
    private static IEnumerable<Proof> ScenarioProofs(RepoLayout layout, List<string> errors)
    {
        if (!Directory.Exists(layout.Scenarios))
        {
            yield break;
        }

        var manifests = Directory.EnumerateFiles(layout.Scenarios, "scenario.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            var dir = Path.GetDirectoryName(manifest)!;
            var id = Path.GetRelativePath(layout.Scenarios, dir).Replace('\\', '/');
            if (!File.Exists(Path.Combine(dir, "golden.json")))
            {
                errors.Add($"scenario {id} has no golden.json; record it or remove it.");
                continue;
            }

            string[] cited;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                cited = doc.RootElement.GetProperty("parity").EnumerateArray().Select(e => e.GetString()!).ToArray();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                errors.Add($"scenario {id}: scenario.json has no readable \"parity\" array ({ex.Message}).");
                continue;
            }

            foreach (var parityId in cited)
            {
                yield return new Proof(parityId, $"scenario {id}");
            }
        }
    }

    /// <summary>
    /// <c>[Trait("Parity", "&lt;ID&gt;")]</c> in the test sources, found textually so the check needs no build. That
    /// also catches traits in attribute lists such as <c>[Fact, Trait("Parity", "SET-2")]</c>.
    /// </summary>
    private static IEnumerable<Proof> TraitProofs(RepoLayout layout)
    {
        if (!Directory.Exists(layout.Tests))
        {
            yield break;
        }

        var sources = Directory.EnumerateFiles(layout.Tests, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(layout.Tests, f))
            .Order(StringComparer.Ordinal);
        foreach (var file in sources)
        {
            var relative = Path.GetRelativePath(layout.Root, file).Replace('\\', '/');
            foreach (Match match in ParityTrait().Matches(File.ReadAllText(file)))
            {
                yield return new Proof(match.Groups["id"].Value, $"trait {relative}");
            }
        }
    }

    private static bool IsBuildOutput(string root, string file)
        => Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static IEnumerable<Proof> ManualProofs(RepoLayout layout, List<string> errors)
    {
        if (!File.Exists(layout.Manual))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(layout.Manual), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("coverage-manual.json must be a JSON array of {id, proof, reason} objects.");
            return [];
        }

        var proofs = new List<Proof>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var kind = entry.TryGetProperty("proof", out var kindElement) ? kindElement.GetString() : null;
            var reason = entry.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"coverage-manual.json: an entry has no id: {entry}");
            }
            else if (kind is null || !ManualProofKinds.Contains(kind))
            {
                errors.Add($"coverage-manual.json: {id}: proof must be one of {string.Join(", ", ManualProofKinds)}.");
            }
            else if (string.IsNullOrWhiteSpace(reason))
            {
                errors.Add($"coverage-manual.json: {id} needs a reason.");
            }
            else
            {
                proofs.Add(new Proof(id, $"manual {kind} (coverage-manual.json)"));
            }
        }

        return proofs;
    }

    private static List<string> ReadCovered(RepoLayout layout)
        => File.Exists(layout.Covered)
            ? File.ReadAllLines(layout.Covered)
                .Select(line => line.Split('#', 2)[0].Trim())
                .Where(line => line.Length > 0)
                .ToList()
            : [];

    [GeneratedRegex("""Trait\s*\(\s*"Parity"\s*,\s*"(?<id>[^"]+)"\s*\)""")]
    private static partial Regex ParityTrait();
}
