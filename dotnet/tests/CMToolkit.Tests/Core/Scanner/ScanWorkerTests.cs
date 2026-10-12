using System.Diagnostics;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The scan worker's pieces the scenarios can't reach cheaply: the SADD count across read chunks (SCN-S5) and the
/// <c>Path.walk</c> emulation's order, pruning and link handling that the data walk and the mod file index stand on.
/// </summary>
public sealed class ScanWorkerTests
{
    private const int Chunk = 1 << 20;

    private static readonly byte[] Sadd = [0x00, 0x53, 0x41, 0x44, 0x44];

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

        Assert.Equal(2, ScanWorker.CountSadd(path));
    }

    [Fact]
    [Trait("Parity", "SCN-S5")]
    public void Adjacent_records_count_as_python_bytes_count_does()
    {
        using var temp = new TempDirectory();
        // b"\x00SADD\x00SADDSADD\x00\x00SADD".count(b"\x00SADD") == 3.
        var path = temp.WriteBytes("module.esp", [.. Sadd, .. Sadd, 0x53, 0x41, 0x44, 0x44, 0x00, .. Sadd]);

        Assert.Equal(3, ScanWorker.CountSadd(path));
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
}
