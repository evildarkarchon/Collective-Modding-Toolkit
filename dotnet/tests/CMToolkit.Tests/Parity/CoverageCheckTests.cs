using System.Text;
using CMToolkit.ParityCoverage;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// The coverage check (ADR-0004, issue #46), against throwaway repo layouts. It collects proofs from
/// <c>scenario.json</c>, <c>[Trait("Parity", …)]</c> and <c>coverage-manual.json</c>, and runs as a ratchet over
/// <c>covered.txt</c>, or strictly.
/// </summary>
public sealed class CoverageCheckTests : IDisposable
{
    private readonly TempDirectory _repo = new();

    public void Dispose() => _repo.Dispose();

    private void Write(string path, string text) => _repo.WriteBytes(path, Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Writes a C# source fixture. The check scans sources as text, so a literal parity trait in this file's string
    /// literals would count as a proof (of IDs that don't exist); fixtures spell it TRAIT and it is fixed up here.
    /// </summary>
    private void WriteSource(string path, string text) => Write(path, text.Replace("TRAIT", "Trait", StringComparison.Ordinal));

    private const string Inventory = """
        # Inventory

        | ID | Behaviour | Source |
        |---|---|---|
        | SET-1 | Keys. | `a.py:1` |
        | SET-2 | Download source. | `a.py:2` |
        | — | Not an item. | |

        | ID | Type | Path |
        |---|---|---|
        | OVW-P5, P6 | Wrong Version | x |
        | OVW-P15–P18 | Invalid Archive | y |

        | ID | Bug | Where |
        |---|---|---|
        | B-1 · [#20](https://example.invalid/20) | A bug. | SET-8 |

        | Name | Not an ID table |
        |---|---|
        | SET-99 | ignored |
        """;

    private void Layout(string covered = "", string manual = "[]")
    {
        Write("docs/parity-inventory.md", Inventory);
        Write("parity/covered.txt", covered);
        Write("parity/coverage-manual.json", manual);
        Directory.CreateDirectory(Path.Combine(_repo.Path, "parity", "scenarios"));
        Directory.CreateDirectory(Path.Combine(_repo.Path, "dotnet", "tests"));
    }

    private void Scenario(string id, string parityJson, bool recorded = true)
    {
        Write($"parity/scenarios/{id}/scenario.json", $$$"""{"operation": "op", "parity": {{{parityJson}}}, "host": {}}""");
        if (recorded)
        {
            Write($"parity/scenarios/{id}/golden.json", "{}");
        }
    }

    private CoverageReport Run(bool strict = false) => CoverageCheck.Run(RepoLayout.At(_repo.Path), strict);

    [Fact]
    public void Inventory_ids_come_from_the_first_column_of_id_tables_with_shorthand_and_ranges_expanded()
    {
        var ids = ParityInventory.ParseIds(Inventory);

        Assert.Equal(
            ["SET-1", "SET-2", "OVW-P5", "OVW-P6", "OVW-P15", "OVW-P16", "OVW-P17", "OVW-P18", "B-1"],
            ids);
    }

    [Fact]
    public void A_malformed_id_cell_is_an_error_rather_than_silently_skipped()
    {
        var ex = Assert.Throws<FormatException>(() => ParityInventory.ParseIds("""
            | ID | Behaviour |
            |---|---|
            | set 2 | lowercase |
            """));

        Assert.Contains("set 2", ex.Message);
    }

    [Fact]
    public void A_duplicate_id_is_an_error()
    {
        Assert.Throws<FormatException>(() => ParityInventory.ParseIds("""
            | ID | Behaviour |
            |---|---|
            | SET-1 | a |
            | SET-1 | b |
            """));
    }

    [Fact]
    public void The_real_inventory_parses()
    {
        var ids = ParityInventory.ParseIds(File.ReadAllText(Path.Combine(ParityPaths.RepoRoot, "docs", "parity-inventory.md")));

        Assert.Contains("SHELL-1", ids);
        Assert.Contains("OVW-P17", ids);
        Assert.Contains("T-15", ids);
        Assert.Contains("B-18", ids);
        // 194 ID rows at promotion, 203 IDs once the §5.6 shorthand and range rows are expanded.
        Assert.InRange(ids.Count, 203, 1000);
    }

    [Fact]
    public void Proofs_are_collected_from_scenarios_traits_and_the_manual_list()
    {
        Layout(manual: """[{"id": "B-1", "proof": "exempt", "reason": "Not reproducible."}]""");
        Scenario("settings/a", """["SET-2"]""");
        WriteSource("dotnet/tests/X/FooTests.cs", """
            [Fact, TRAIT("Parity", "SET-1")]
            public void A() { }
            [TRAIT( "Parity" , "OVW-P5" )]
            public void B() { }
            [TRAIT("Category", "SET-99")]
            public void C() { }
            """);

        var report = Run();

        Assert.Empty(report.Errors);
        Assert.Equal(["SET-2"], report.Proofs.Where(p => p.Kind == ProofKind.Scenario).Select(p => p.Id));
        Assert.Equal(["OVW-P5", "SET-1"], report.Proofs.Where(p => p.Kind == ProofKind.Trait).Select(p => p.Id).Order());
        Assert.Equal(["B-1"], report.Proofs.Where(p => p.Kind == ProofKind.Manual).Select(p => p.Id));
        Assert.Equal(["OVW-P6", "OVW-P15", "OVW-P16", "OVW-P17", "OVW-P18"], report.Unproven);
    }

    [Fact]
    public void A_ratcheted_id_that_loses_its_proof_fails()
    {
        Layout(covered: "# Proven so far\nSET-1\n\nSET-2\n");
        Scenario("settings/a", """["SET-2"]""");

        var report = Run();

        Assert.Equal(["covered.txt lists SET-1, but nothing proves it."], report.Errors);
    }

    [Fact]
    public void A_proof_citing_an_id_that_is_not_in_the_inventory_fails()
    {
        Layout(manual: """[{"id": "SET-77", "proof": "manual", "reason": "x"}]""");
        Scenario("settings/a", """["SET-2", "OVW-P99"]""");
        WriteSource("dotnet/tests/FooTests.cs", """[TRAIT("Parity", "SHELL-404")]""");

        var errors = Run().Errors;

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("OVW-P99", StringComparison.Ordinal) && e.Contains("scenario settings/a", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("SHELL-404", StringComparison.Ordinal) && e.Contains("FooTests.cs", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("SET-77", StringComparison.Ordinal) && e.Contains("coverage-manual.json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_covered_id_that_is_not_in_the_inventory_fails()
    {
        Layout(covered: "SET-404\n");

        Assert.Contains("covered.txt lists SET-404, which isn't in the Parity Inventory.", Run().Errors);
    }

    [Fact]
    public void An_unrecorded_scenario_proves_nothing_and_fails()
    {
        Layout(covered: "SET-2\n");
        Scenario("settings/a", """["SET-2"]""", recorded: false);

        var errors = Run().Errors;

        Assert.Contains("scenario settings/a has no golden.json; record it or remove it.", errors);
        Assert.Contains("covered.txt lists SET-2, but nothing proves it.", errors);
    }

    [Theory]
    [InlineData("""[{"id": "SET-1", "proof": "vibes", "reason": "x"}]""", "proof must be one of")]
    [InlineData("""[{"id": "SET-1", "proof": "exempt", "reason": ""}]""", "needs a reason")]
    [InlineData("""[{"id": "SET-1", "proof": "exempt"}]""", "needs a reason")]
    [InlineData("""{"id": "SET-1"}""", "must be a JSON array")]
    public void Malformed_manual_entries_fail(string manual, string expected)
    {
        Layout(manual: manual);

        Assert.Contains(Run().Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Ratchet_mode_only_reports_unproven_ids()
    {
        Layout(covered: "SET-2\n");
        Scenario("settings/a", """["SET-2"]""");

        var report = Run();

        Assert.Empty(report.Errors);
        Assert.Contains("SET-1", report.Unproven);
        Assert.True(report.Passed);
    }

    [Fact]
    public void Strict_mode_fails_on_every_id_that_is_unproven_or_not_yet_ratcheted()
    {
        Layout(covered: "SET-2\n");
        Scenario("settings/a", """["SET-2", "B-1"]""");

        var report = Run(strict: true);

        Assert.False(report.Passed);
        Assert.Contains("--strict: SET-1 has no proof.", report.Errors);
        Assert.Contains("--strict: B-1 is proven but not in covered.txt.", report.Errors);
        Assert.DoesNotContain(report.Errors, e => e.Contains("SET-2", StringComparison.Ordinal));
    }

    [Fact]
    public void Strict_mode_passes_when_every_id_is_ratcheted_and_proven()
    {
        Write("docs/parity-inventory.md", """
            | ID | Behaviour |
            |---|---|
            | SET-1 | a |
            """);
        Write("parity/covered.txt", "SET-1\n");
        Write("parity/coverage-manual.json", """[{"id": "SET-1", "proof": "screenshot", "reason": "Approved in the PR."}]""");

        Assert.True(Run(strict: true).Passed);
    }

    [Fact]
    public void The_layout_is_found_by_walking_up_from_a_folder_inside_the_repo()
    {
        Layout();
        var nested = Path.Combine(_repo.Path, "dotnet", "tests");

        Assert.Equal(_repo.Path, RepoLayout.Find(nested).Root);
        Assert.Throws<DirectoryNotFoundException>(() => RepoLayout.Find(Path.GetPathRoot(_repo.Path)!));
    }

    [Fact]
    public void The_committed_repo_passes_the_ratchet()
    {
        var report = CoverageCheck.Run(RepoLayout.At(ParityPaths.RepoRoot), strict: false);

        Assert.Empty(report.Errors);
    }
}
