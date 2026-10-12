using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// How a scan's results become the tree (<c>ScannerTab.populate_results</c>): the Overview problems seeded first
/// (SCN-S3), their <c>OVERVIEW</c> mods looked up in the mod file index (SCN-R2), then one group per problem type with
/// its items stable-sorted by type + mod (SCN-R3).
/// </summary>
public sealed class ScanResultsTests
{
    [Fact]
    [Trait("Parity", "SCN-S3")]
    public void Overview_issues_seed_the_results_with_the_overview_problems_in_order()
    {
        Problem[] overview = [Simple("a", "No Mod Manager"), Simple("b", "File Not Found")];

        Assert.Equal(overview, ScanResults.Seed(overviewIssues: true, overview));
        Assert.Empty(ScanResults.Seed(overviewIssues: false, overview));
    }

    [Fact]
    [Trait("Parity", "SCN-R3")]
    public void Each_problem_type_is_one_group_in_first_seen_order_with_items_sorted_by_type_and_mod()
    {
        var z = File("Wrong Version", "z.exe", "Zeta");
        var a = File("Wrong Version", "a.exe", "Alpha");
        var unmanaged = File("Wrong Version", "u.exe", null);
        var missing = Simple("Data", "File Not Found");
        var a2 = File("Wrong Version", "a2.exe", "Alpha");

        var groups = ScanResults.Group([z, missing, a, unmanaged, a2], index: null);

        Assert.Equal(["Wrong Version", "File Not Found"], groups.Select(g => g.Kind));
        // "<Unmanaged>" sorts before letters ('<' is U+003C); equal keys keep their order (a before a2).
        Assert.Equal([unmanaged, a, a2, z], groups[0].Items);
        Assert.Equal([missing], groups[1].Items);
    }

    [Fact]
    [Trait("Parity", "SCN-R3")]
    public void Items_sort_by_code_point_like_python_strings()
    {
        // U+FF21 (fullwidth A) sorts before U+1F600 by code point, but after it by UTF-16 code unit (0xFF21 > 0xD83D).
        var emoji = File("T", "e.esp", "\U0001F600");
        var fullwidth = File("T", "f.esp", "\uFF21");

        var groups = ScanResults.Group([emoji, fullwidth], index: null);

        Assert.Equal([fullwidth, emoji], groups[0].Items);
    }

    [Fact]
    [Trait("Parity", "SCN-R2")]
    public void Without_a_mod_file_index_overview_problems_get_an_empty_mod()
    {
        var module = Problem.ForOverviewFile(ProblemTypes.InvalidModule, new PythonPath(@"C:\Game\Data\Bad.esp"), "Module is either corrupt or not in TES4 format.", null);

        var item = Assert.Single(Assert.Single(ScanResults.Group([module], index: null)).Items);

        Assert.Equal("", item.Mod);
        Assert.Equal(ModSource.Given, item.ModSource);
        Assert.Equal(ModSource.Overview, module.ModSource);
    }

    [Fact]
    [Trait("Parity", "SCN-R2")]
    public void With_an_index_overview_problems_get_the_mod_of_their_file_name_at_a_mod_root()
    {
        var index = new ModFiles();
        index.Files[new PythonPath("bad.ESP")] = new ModFile("Bad Mod", new PythonPath(@"C:\MO2\mods\Bad Mod\Bad.esp"));
        index.Files[new PythonPath(@"Sub\Other.esp")] = new ModFile("Nested", new PythonPath(@"C:\MO2\mods\Nested\Sub\Other.esp"));
        var bad = Problem.ForOverviewFile(ProblemTypes.InvalidModule, new PythonPath(@"C:\Game\Data\Bad.esp"), "s", null);
        var other = Problem.ForOverviewFile(ProblemTypes.InvalidModule, new PythonPath(@"C:\Game\Data\Other.esp"), "s", null);
        var given = File(ProblemTypes.InvalidModule, "Given.esp", null);

        var items = Assert.Single(ScanResults.Group([bad, other, given], index)).Items;

        // Matched case-insensitively (T-8); only mod-root files match, as the key is just the file name.
        Assert.Equal(["", "<Unmanaged>", "Bad Mod"], items.Select(p => p.Mod));
        Assert.Equal(["Other.esp", "Given.esp", "Bad.esp"], items.Select(p => p.ItemText));
    }

    [Fact]
    [Trait("Parity", "SCN-R1")]
    public void The_info_label_counts_every_result()
        => Assert.Equal("3 Results ~ Select an item for details", ScanResults.InfoText(3));

    private static Problem File(string kind, string name, string? mod)
        => Problem.ForFile(kind, new PythonPath(@"C:\Game\" + name), new PythonPath(name), mod, "summary", null);

    private static Problem Simple(string path, string kind) => Problem.Simple(path, kind, "summary", "solution");
}
