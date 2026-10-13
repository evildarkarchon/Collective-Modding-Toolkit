using System.Text.Json;
using System.Text.Json.Nodes;
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
            var candidate = new RepoLayout(dir.FullName);
            if (File.Exists(candidate.Inventory))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"No docs/parity-inventory.md at or above {start}.");
    }
}

/// <summary>The kinds of evidence the check accepts.</summary>
public enum ProofKind
{
    /// <summary>A recorded scenario's <c>parity</c> list.</summary>
    Scenario,

    /// <summary>A <c>[Trait("Parity", …)]</c> on a test.</summary>
    Trait,

    /// <summary>A <c>coverage-manual.json</c> entry.</summary>
    Manual,
}

/// <summary>One piece of evidence that an ID is proven.</summary>
/// <param name="Id">The Parity Inventory ID.</param>
/// <param name="Kind">What kind of evidence it is.</param>
/// <param name="Location">
/// Where it is: the scenario ID, the test file (repo-relative), or the manual proof kind (<c>exempt</c>, …).
/// </param>
public sealed record Proof(string Id, ProofKind Kind, string Location)
{
    /// <summary>How error messages name the proof, for example <c>scenario settings/download-source-bom</c>.</summary>
    public string Source => Kind switch
    {
        ProofKind.Scenario => $"scenario {Location}",
        ProofKind.Trait => $"trait {Location}",
        _ => $"manual {Location} (coverage-manual.json)",
    };
}

/// <summary>
/// One documented Deviation from Behaviour Parity (ADR-0006): a place where the port fixes a reference bug instead of
/// reproducing it.
/// </summary>
/// <param name="Bug">The <c>bug</c> issue number on the fork.</param>
/// <param name="Reason">
/// What the port does instead. For a test it is the string literal's text as written in the source, escapes included.
/// </param>
/// <param name="Source">Where it is declared: <c>scenario &lt;id&gt;</c> or <c>test &lt;repo-relative file&gt;</c>.</param>
public sealed record Deviation(int Bug, string Reason, string Source);

/// <summary>What the check found.</summary>
/// <param name="InventoryIds">Every inventory ID, in document order.</param>
/// <param name="Proofs">Every proof collected, valid or not.</param>
/// <param name="Covered">The IDs in <c>covered.txt</c>.</param>
/// <param name="Unproven">Inventory IDs with no proof, in document order.</param>
/// <param name="NotRatcheted">Proven inventory IDs missing from <c>covered.txt</c> (for example <i>part</i> IDs).</param>
/// <param name="Errors">Everything that fails the check.</param>
/// <param name="Deviations">
/// Every well-formed deviation, scenarios first (by scenario ID) and then tests (by file and position). Malformed ones
/// are in <paramref name="Errors"/> instead.
/// </param>
public sealed record CoverageReport(
    IReadOnlyList<string> InventoryIds,
    IReadOnlyList<Proof> Proofs,
    IReadOnlyList<string> Covered,
    IReadOnlyList<string> Unproven,
    IReadOnlyList<string> NotRatcheted,
    IReadOnlyList<string> Errors,
    IReadOnlyList<Deviation> Deviations)
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
        var deviations = new List<Deviation>();
        proofs.AddRange(ScenarioProofs(layout, errors, deviations));
        proofs.AddRange(TraitProofs(layout));
        proofs.AddRange(ManualProofs(layout, errors));
        deviations.AddRange(TestDeviations(layout, errors));

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

        return new CoverageReport(inventory, proofs, covered, unproven, notRatcheted, errors, deviations);
    }

    /// <summary>
    /// The <c>parity</c> IDs of every scenario. A scenario without a <c>golden.json</c> proves nothing: it was never
    /// recorded, so no C# test can have passed it. A deviating scenario still proves its IDs (the port's behaviour there
    /// is the documented fix); its deviations are collected into <paramref name="deviations"/>.
    /// </summary>
    private static IEnumerable<Proof> ScenarioProofs(RepoLayout layout, List<string> errors, List<Deviation> deviations)
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
                // Duplicate keys are an error, as in the C# replay and the Python driver: with the last one winning, a
                // second "parity" or "deviations" would hide the first from every check.
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest), new JsonDocumentOptions { AllowDuplicateProperties = false });
                cited = doc.RootElement.GetProperty("parity").EnumerateArray().Select(e => e.GetString()!).ToArray();
                CollectScenarioDeviations(id, dir, doc.RootElement, errors, deviations);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                errors.Add($"scenario {id}: scenario.json has no readable \"parity\" array ({ex.Message}).");
                continue;
            }

            foreach (var parityId in cited)
            {
                yield return new Proof(parityId, ProofKind.Scenario, id);
            }
        }
    }

    /// <summary>
    /// Reads a scenario's <c>deviations</c> (ADR-0006) and checks that they come together with a <c>golden.port.json</c>
    /// that actually differs from the reference golden. Each deviation needs a positive integer <c>bug</c>, listed once,
    /// and a non-blank <c>reason</c>; a malformed one is an error and isn't listed. It never throws for a malformed shape, so
    /// the caller's "no readable parity array" message stays about the <c>parity</c> array.
    /// </summary>
    private static void CollectScenarioDeviations(string id, string dir, JsonElement manifest, List<string> errors, List<Deviation> deviations)
    {
        var source = $"scenario {id}";
        var portGolden = Path.Combine(dir, PortGoldenFileName);
        var declared = manifest.TryGetProperty("deviations", out var list);
        var hasPortGolden = File.Exists(portGolden);
        if (!declared)
        {
            if (hasPortGolden)
            {
                errors.Add($"{source} has a {PortGoldenFileName} but declares no deviations.");
            }

            return;
        }

        if (!hasPortGolden)
        {
            errors.Add($"{source} declares deviations but has no {PortGoldenFileName}.");
        }
        else if (GoldensAreIdentical(Path.Combine(dir, "golden.json"), portGolden))
        {
            errors.Add($"{source}: {PortGoldenFileName} is identical to golden.json, so it deviates from nothing.");
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{source}: \"deviations\" must be an array of {{bug, reason}} objects.");
            return;
        }

        if (list.GetArrayLength() == 0)
        {
            errors.Add($"{source}: \"deviations\" names no bug; remove it or name the bug the port fixes.");
            return;
        }

        var seen = new HashSet<int>();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{source}: a deviation must be a {{bug, reason}} object: {entry}");
                continue;
            }

            if (!entry.TryGetProperty("bug", out var bugElement)
                || bugElement.ValueKind != JsonValueKind.Number
                || !bugElement.TryGetInt32(out var bug)
                || bug <= 0)
            {
                errors.Add($"{source}: deviation bug number {(entry.TryGetProperty("bug", out var raw) ? raw.GetRawText() : "(missing)")} isn't a positive issue number.");
                continue;
            }

            var reason = entry.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(reason))
            {
                errors.Add($"{source}: the deviation for bug #{bug} needs a reason.");
                continue;
            }

            // ScenarioManifest.ValidateDeviations rejects this too; one bug, one entry, one reason.
            if (!seen.Add(bug))
            {
                errors.Add($"{source}: the deviation for bug #{bug} is listed twice.");
                continue;
            }

            deviations.Add(new Deviation(bug, reason, source));
        }
    }

    /// <summary>
    /// Whether the two goldens hold the same JSON. Compared as parsed JSON rather than bytes, so a port golden that only
    /// differs in formatting or line endings still counts as no deviation at all. An unreadable file counts as different;
    /// the C# replay reports that one.
    /// </summary>
    private static bool GoldensAreIdentical(string reference, string port)
    {
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(reference)), JsonNode.Parse(File.ReadAllText(port)));
        }
        catch (JsonException)
        {
            // A golden that doesn't parse fails its replaying test with a clearer message than this check could give.
            return false;
        }
    }

    /// <summary>
    /// <c>Deviation(&lt;bug&gt;, "&lt;reason&gt;")</c> attributes in the test sources (ADR-0006), found textually like the
    /// parity traits. The bug must be a positive integer literal and the reason a non-blank regular string literal; a
    /// constant, a verbatim or raw string, or a missing argument can't be read without a build, so it is an error. The
    /// test must also carry a <c>Trait("Parity", …)</c>, on its method or on an enclosing class (see
    /// <see cref="HasParityTrait"/>): a deviation is a fix to a Parity behaviour, and one tied to no ID proves nothing.
    /// </summary>
    private static IEnumerable<Deviation> TestDeviations(RepoLayout layout, List<string> errors)
    {
        if (!Directory.Exists(layout.Tests))
        {
            return [];
        }

        var found = new List<Deviation>();
        var sources = Directory.EnumerateFiles(layout.Tests, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(layout.Tests, f))
            .Order(StringComparer.Ordinal);
        foreach (var file in sources)
        {
            var source = $"test {Path.GetRelativePath(layout.Root, file).Replace('\\', '/')}";
            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            foreach (Match head in DeviationHead().Matches(text))
            {
                var call = DeviationArguments().Match(text, head.Index + head.Length);
                var args = call.Success ? SplitDeviationArguments().Match(call.Groups["args"].Value) : Match.Empty;
                var bugText = args.Success ? args.Groups["bug"].Value : string.Empty;
                if (!PositiveInteger().IsMatch(bugText) || !int.TryParse(bugText, out var bug))
                {
                    errors.Add($"{source}: Deviation bug number '{bugText}' isn't a positive integer literal.");
                    continue;
                }

                var reason = DeviationReason().Match(args.Groups["rest"].Value);
                if (!args.Groups["rest"].Success || !reason.Success || string.IsNullOrWhiteSpace(reason.Groups["reason"].Value))
                {
                    errors.Add($"{source}: the Deviation for bug #{bug} needs a reason (a non-blank regular string literal).");
                    continue;
                }

                var line = text.AsSpan(0, head.Index).Count('\n');
                if (!HasParityTrait(lines, line))
                {
                    errors.Add($"{source}:{line + 1}: the Deviation for bug #{bug} has no Trait(\"Parity\", …) on its test or its class.");
                    continue;
                }

                found.Add(new Deviation(bug, reason.Groups["reason"].Value, source));
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the attribute on line <paramref name="line"/> sits with a parity trait: in the same attribute list (the
    /// lines around it up to a blank line, a comment or a line ending a statement or block), or in the attribute list
    /// of a class, record or struct that encloses it, found as a declaration above it with less indentation. Textual,
    /// like the rest of the check, so it needs no build.
    /// </summary>
    /// <param name="lines">The source file's lines.</param>
    /// <param name="line">The zero-based line the attribute starts on.</param>
    private static bool HasParityTrait(string[] lines, int line)
    {
        if (AttributeLines(lines, line, upOnly: false).Any(l => ParityTrait().IsMatch(l)))
        {
            return true;
        }

        var indent = Indentation(lines[line]);
        for (var i = line - 1; i >= 0 && indent > 0; i--)
        {
            if (Indentation(lines[i]) >= indent || !TypeDeclaration().IsMatch(lines[i]))
            {
                continue;
            }

            if (AttributeLines(lines, i - 1, upOnly: true).Any(l => ParityTrait().IsMatch(l)))
            {
                return true;
            }

            // An outer type may still carry it.
            indent = Indentation(lines[i]);
        }

        return false;
    }

    /// <summary>
    /// The attribute lines around <paramref name="line"/>: the lines next to it that are neither blank nor comments and
    /// don't end a statement or a block, which keeps a multi-line <c>InlineData</c> inside the list.
    /// </summary>
    private static IEnumerable<string> AttributeLines(string[] lines, int line, bool upOnly)
    {
        static bool InList(string text)
        {
            var trimmed = text.Trim();
            return trimmed.Length > 0
                   && !trimmed.StartsWith("//", StringComparison.Ordinal)
                   && !trimmed.EndsWith('{') && !trimmed.EndsWith('}') && !trimmed.EndsWith(';');
        }

        for (var i = line; i >= 0 && InList(lines[i]); i--)
        {
            yield return lines[i];
        }

        for (var i = line + 1; !upOnly && i < lines.Length && InList(lines[i]); i++)
        {
            yield return lines[i];
        }
    }

    private static int Indentation(string text) => text.Length - text.TrimStart(' ', '\t').Length;

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
                yield return new Proof(match.Groups["id"].Value, ProofKind.Trait, relative);
            }
        }
    }

    private static bool IsBuildOutput(string root, string file)
        => Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    /// <summary>
    /// The entries of <c>coverage-manual.json</c>. Each must have an <c>id</c>, a <c>proof</c> from
    /// <see cref="ManualProofKinds"/> and a non-empty <c>reason</c>; a malformed entry is an error and proves nothing.
    /// A missing file means no manual proofs.
    /// </summary>
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
                proofs.Add(new Proof(id, ProofKind.Manual, kind));
            }
        }

        return proofs;
    }

    /// <summary>The IDs in <c>covered.txt</c>: one per line, with <c>#</c> starting a comment and blank lines ignored.</summary>
    private static List<string> ReadCovered(RepoLayout layout)
        => File.Exists(layout.Covered)
            ? File.ReadAllLines(layout.Covered)
                .Select(line => line.Split('#', 2)[0].Trim())
                .Where(line => line.Length > 0)
                .ToList()
            : [];

    [GeneratedRegex("""Trait\s*\(\s*"Parity"\s*,\s*"(?<id>[^"]+)"\s*\)""")]
    private static partial Regex ParityTrait();

    /// <summary>
    /// The start of a <c>Deviation(</c> attribute: only right after <c>[</c> or a <c>,</c> in an attribute list, so the
    /// attribute's own class declaration, a <c>new</c> expression and names such as <c>ScenarioDeviation</c> don't count.
    /// </summary>
    [GeneratedRegex("""(?<=[\[,]\s*)Deviation(?:Attribute)?\s*\(""")]
    private static partial Regex DeviationHead();

    /// <summary>The argument text up to the closing parenthesis, skipping over parentheses inside string literals.</summary>
    [GeneratedRegex("""\G(?<args>(?:"(?:[^"\\\r\n]|\\.)*"|[^"()])*)\)""")]
    private static partial Regex DeviationArguments();

    /// <summary>The first argument (the bug number) and, after the first comma, the rest.</summary>
    [GeneratedRegex("""^\s*(?<bug>[^,"]*?)\s*(?:,(?<rest>.*))?$""", RegexOptions.Singleline)]
    private static partial Regex SplitDeviationArguments();

    [GeneratedRegex("""^\s*"(?<reason>(?:[^"\\\r\n]|\\.)*)"\s*$""")]
    private static partial Regex DeviationReason();

    [GeneratedRegex("^[1-9][0-9]*$")]
    private static partial Regex PositiveInteger();

    /// <summary>A line declaring a class, record or struct, after any modifiers.</summary>
    [GeneratedRegex("""^\s*(?:(?:public|internal|private|protected|file|sealed|static|abstract|partial|readonly)\s+)*(?:class|record|struct)\s+\w""")]
    private static partial Regex TypeDeclaration();

    /// <summary>The port-expected golden beside a deviating scenario's <c>golden.json</c> (ADR-0006).</summary>
    public const string PortGoldenFileName = "golden.port.json";
}
