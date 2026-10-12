using CMToolkit.Core;
using CMToolkit.Core.Downgrader;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Downgrader;

/// <summary>
/// <see cref="DeltaPatches.Decode"/> against the self-made Delta Patches in <c>parity/assets/delta-patches/out/</c>,
/// with the outcomes <c>pyxdelta.decode</c> 0.2.3 gave for the same inputs (probed by hand when the patches were built):
/// true and the target on success; false, never an exception, on any failure; no output file when the first window
/// fails, and the whole windows before a later failure kept.
/// </summary>
public sealed class DeltaPatchesTests
{
    private static readonly string Assets = Path.Combine(RepoPaths.DotnetRoot, "..", "parity", "assets");
    private static readonly string Patches = Path.Combine(Assets, "delta-patches", "out");
    private static readonly string StandIns = Path.Combine(Assets, "crc32-forge", "out", "downgrader");

    [Fact]
    [Trait("Parity", "DG-6")]
    public void A_patch_rebuilds_the_other_version_from_this_one()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "Fallout4.exe");

        var decoded = DeltaPatches.Decode(Path.Combine(StandIns, "og", "Fallout4.exe"), Path.Combine(Patches, "OG-to-NG-Fallout4.exe.xdelta"), output);

        Assert.True(decoded);
        Assert.Equal("C5965A2E", FileCrc32.Of(output));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "B-10")]
    public void Bug_15_an_error_page_saved_as_a_patch_fails_without_an_output_file()
    {
        using var temp = new TempDirectory();
        var patch = temp.WriteBytes("NG-to-OG-Fallout4.exe.xdelta", "Not Found"u8.ToArray());
        var output = Path.Combine(temp.Path, "Fallout4.exe");

        Assert.False(DeltaPatches.Decode(Path.Combine(StandIns, "ng", "Fallout4.exe"), patch, output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    public void An_empty_patch_fails_without_an_output_file()
    {
        using var temp = new TempDirectory();
        var patch = temp.WriteBytes("empty.xdelta", []);
        var output = Path.Combine(temp.Path, "out.bin");

        Assert.False(DeltaPatches.Decode(Path.Combine(StandIns, "og", "Fallout4.exe"), patch, output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    public void A_patch_applied_to_the_wrong_source_fails_its_checksum_and_writes_nothing()
    {
        // xdelta3 checks each window's Adler-32 before writing it; VCDiff writes first, so the port must hold it back.
        // The source is the right size but every byte inverted, so every COPY reads the wrong bytes.
        using var temp = new TempDirectory();
        var big = Path.Combine(Patches, "big");
        var source = temp.WriteBytes("inverted.exe", [.. File.ReadAllBytes(Path.Combine(big, "og", "Fallout4Launcher.exe")).Select(b => (byte)(b ^ 0xFF))]);
        var output = Path.Combine(temp.Path, "Fallout4Launcher.exe");

        Assert.False(DeltaPatches.Decode(source, Path.Combine(big, "OG-to-NG-Fallout4Launcher.exe.xdelta"), output));
        Assert.False(File.Exists(output));
    }

    [Theory]
    [Trait("Parity", "DG-6")]
    [InlineData("source")]
    [InlineData("patch")]
    public void A_missing_input_fails_without_an_output_file(string missing)
    {
        using var temp = new TempDirectory();
        var source = missing == "source" ? Path.Combine(temp.Path, "nope.exe") : Path.Combine(StandIns, "og", "Fallout4.exe");
        var patch = missing == "patch" ? Path.Combine(temp.Path, "nope.xdelta") : Path.Combine(Patches, "OG-to-NG-Fallout4.exe.xdelta");
        var output = Path.Combine(temp.Path, "out.bin");

        Assert.False(DeltaPatches.Decode(source, patch, output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    public void A_truncated_patch_keeps_the_windows_decoded_before_it_ran_out()
    {
        // 64 KiB in 16 KiB windows, the patch cut in half: pyxdelta returned False and left the first window.
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "Fallout4Launcher.exe");
        var big = Path.Combine(Patches, "big");

        var decoded = DeltaPatches.Decode(
            Path.Combine(big, "og", "Fallout4Launcher.exe"), Path.Combine(big, "OG-to-NG-Fallout4Launcher.exe.truncated.xdelta"), output);

        Assert.False(decoded);
        var target = File.ReadAllBytes(Path.Combine(big, "ng", "Fallout4Launcher.exe"));
        Assert.Equal(target[..16384], File.ReadAllBytes(output));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    public void A_multi_window_patch_rebuilds_the_whole_file_over_an_existing_one()
    {
        using var temp = new TempDirectory();
        var output = temp.WriteBytes("Fallout4Launcher.exe", "old"u8.ToArray());
        var big = Path.Combine(Patches, "big");

        Assert.True(DeltaPatches.Decode(Path.Combine(big, "og", "Fallout4Launcher.exe"), Path.Combine(big, "OG-to-NG-Fallout4Launcher.exe.xdelta"), output));
        Assert.Equal(File.ReadAllBytes(Path.Combine(big, "ng", "Fallout4Launcher.exe")), File.ReadAllBytes(output));
    }

    [Fact]
    [Trait("Parity", "NET-3")]
    public void A_patch_is_fetched_from_the_fork_mirror_under_its_reference_name()
    {
        // Only the base differs from the reference's wxMichael URL (issue #16); the asset names are the same.
        Assert.Equal(
            "https://github.com/evildarkarchon/Collective-Modding-Toolkit/releases/download/delta-patches/NG-to-OG-Archive2Interop.dll.xdelta",
            DeltaPatches.Url("NG-to-OG-", "Archive2Interop.dll"));
    }
}
