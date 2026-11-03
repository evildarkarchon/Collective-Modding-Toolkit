using System.Diagnostics;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The Scan's rules over a materialized scenario: the seed from the Overview Report, the order of the problem list
/// and the groups on the outcome. Then the pieces the scenarios can't reach cheaply: the SADD count across read chunks
/// (SCN-S5) and the <c>Path.walk</c> emulation's order, pruning and link handling that the data walk and the mod file
/// index stand on.
/// </summary>
public sealed class ScanChecksTests
{
    private const int Chunk = 1 << 20;

    private static readonly byte[] Sadd = [0x00, 0x53, 0x41, 0x44, 0x44];

    // scanner/data-walk-mo2's Overview Report problems, in the report's order.
    private static readonly (string Kind, string Item)[] OverviewProblems =
    [
        ("Invalid Module", "BadMagic.esp"),
        ("File Not Found", "Fallout4 - TexturesPatch.ba2"),
        ("Invalid Archive", "BadMagic - main.ba2"),
        ("Wrong Version", "f4se_loader.exe"),
    ];

    [Fact]
    public async Task With_overview_issues_ticked_the_overview_problems_come_first_then_what_the_checks_found()
    {
        using var materialized = Materialize("scanner/data-walk-mo2");
        var machine = await ScanScenarioMachine.LocateAsync(materialized);
        var checksOnly = ScanChecks.Run(machine.Snapshot(ScanSettings.All.Where(s => s != ScanSetting.OverviewIssues)));

        var outcome = ScanChecks.Run(machine.Snapshot(ScanSettings.All));

        Assert.Equal(OverviewProblems, outcome.Problems.Take(OverviewProblems.Length).Select(KindAndItem));
        // Then the checks' problems, in the order found: what the same scan finds without the seed.
        Assert.NotEmpty(checksOnly.Problems);
        Assert.Equal(checksOnly.Problems.Select(KindAndItem), outcome.Problems.Skip(OverviewProblems.Length).Select(KindAndItem));
    }

    [Fact]
    public async Task With_overview_issues_unticked_no_overview_problem_is_listed()
    {
        using var materialized = Materialize("scanner/data-walk-mo2");
        var machine = await ScanScenarioMachine.LocateAsync(materialized);

        var outcome = ScanChecks.Run(machine.Snapshot([ScanSetting.JunkFiles]));

        Assert.Equal([("Junk File", "Thumbs.db")], outcome.Problems.Select(KindAndItem));
    }

    [Fact]
    public async Task Overview_issues_alone_lists_the_overview_problems_in_their_order()
    {
        using var materialized = Materialize("scanner/data-walk-mo2");
        var machine = await ScanScenarioMachine.LocateAsync(materialized);

        var outcome = ScanChecks.Run(machine.Snapshot([ScanSetting.OverviewIssues]));

        Assert.Equal(OverviewProblems, outcome.Problems.Select(KindAndItem));
        Assert.Null(outcome.Error);
        Assert.Null(outcome.Index);
    }

    [Fact]
    public async Task The_outcome_groups_its_problems_by_type_naming_the_mod_that_supplies_an_overview_problems_file()
    {
        using var materialized = Materialize("scanner/data-walk-mo2");
        var machine = await ScanScenarioMachine.LocateAsync(materialized);

        var outcome = ScanChecks.Run(machine.Snapshot(ScanSettings.All));

        Assert.Equal(
            [
                "Invalid Module", "File Not Found", "Invalid Archive", "Wrong Version", "Junk File", "Loose Previs",
                "Loose AnimTextData", "F4SE Script Override", "Unexpected Format",
            ],
            outcome.Groups.Select(g => g.Kind));
        // The Overview's module and archive problems name the mod that has the file at its root, through the index.
        Assert.Equal("LowMod", Assert.Single(outcome.Groups[0].Items).Mod);
        Assert.Equal("HighMod", Assert.Single(outcome.Groups[2].Items).Mod);
        // Sorted by type + mod within a group.
        Assert.Equal(["<Unmanaged>", "HighMod", "LowMod"], outcome.Groups[^1].Items.Select(p => p.Mod));
        Assert.Equal(outcome.Problems.Count, outcome.Groups.Sum(g => g.Items.Count));
    }

    [Fact]
    public async Task Without_a_mod_file_index_the_overview_problems_name_no_mod()
    {
        using var materialized = Materialize("scanner/data-walk-mo2");
        var machine = await ScanScenarioMachine.LocateAsync(materialized);

        // No data check is ticked, so no index is built to name the mods.
        var outcome = ScanChecks.Run(machine.Snapshot([ScanSetting.OverviewIssues]));

        Assert.Equal(["", "", "", "<Unmanaged>"], outcome.Groups.SelectMany(g => g.Items).Select(p => p.Mod));
    }

    [Theory]
    [Trait("Parity", "SCN-S5")]
    [InlineData(Chunk - 5)]
    [InlineData(Chunk - 3)]
    [InlineData(Chunk - 1)]
    [InlineData(Chunk)]
    [InlineData(Chunk + 2)]
    public void A_record_split_across_read_chunks_is_counted_once(int offset)
    {
        using var temp = new TempDirectory();
        var bytes = new byte[Chunk * 2];
        Sadd.CopyTo(bytes, offset);
        Sadd.CopyTo(bytes, 10);
        var path = temp.WriteBytes("module.esp", bytes);

        Assert.Equal(2, ScanChecks.CountSadd(path));
    }

    [Fact]
    [Trait("Parity", "SCN-S5")]
    public void Adjacent_records_count_as_python_bytes_count_does()
    {
        using var temp = new TempDirectory();
        // b"\x00SADD\x00SADDSADD\x00\x00SADD".count(b"\x00SADD") == 3.
        var path = temp.WriteBytes("module.esp", [.. Sadd, .. Sadd, 0x53, 0x41, 0x44, 0x44, 0x00, .. Sadd]);

        Assert.Equal(3, ScanChecks.CountSadd(path));
    }

    [Fact]
    public void The_walk_is_depth_first_in_listing_order_and_honours_pruning()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes(@"b\x\1.txt", []);
        temp.WriteBytes(@"b\y\2.txt", []);
        temp.WriteBytes(@"a\3.txt", []);
        temp.WriteBytes(@"c\4.txt", []);
        temp.WriteBytes("root.txt", []);

        var visited = new List<string>();
        foreach (var step in PythonWalk.TopDown(new PythonPath(temp.Path), p => p.ToString()))
        {
            visited.Add($"{step.Relative}|{step.Depth}|{string.Join(",", step.Folders)}|{string.Join(",", step.Files)}");
            if (step.Relative.ToString() == "b")
            {
                step.Folders.Remove("x");
            }
        }

        Assert.Equal([".|0|a,b,c|root.txt", "a|1||3.txt", "b|1|x,y|", @"b\y|2||2.txt", "c|1||4.txt"], visited);
    }

    [Fact]
    public void A_junction_is_listed_as_a_file_and_not_entered()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes(@"real\inner\1.txt", []);
        var link = Path.Combine(temp.Path, "junc");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, Path.Combine(temp.Path, "real")])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!)
        {
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }

        var steps = PythonWalk.TopDown(new PythonPath(temp.Path), p => p.ToString()).ToList();

        // Python 3.14: top ['real'] ['junc'], then real, then real\inner.
        Assert.Equal([".", "real", @"real\inner"], steps.Select(s => s.Relative.ToString()));
        Assert.Equal(["junc"], steps[0].Files);
        Assert.Equal(["real"], steps[0].Folders);
    }

    [Fact]
    public void A_top_that_cant_be_listed_yields_nothing()
    {
        using var temp = new TempDirectory();

        Assert.Empty(PythonWalk.TopDown(new PythonPath(Path.Combine(temp.Path, "missing")), p => p.ToString()));
    }

    private static MaterializedScenario Materialize(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    private static (string Kind, string Item) KindAndItem(CMToolkit.Core.Problems.Problem problem) => (problem.Kind, problem.ItemText);
}
