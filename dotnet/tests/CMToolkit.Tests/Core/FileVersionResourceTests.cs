using CMToolkit.Core.Win32;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core;

/// <summary>
/// <see cref="FileVersionResource"/> against the committed version stubs (<c>parity/assets/version-stubs/out</c>). The
/// expected values are what pywin32's <c>GetFileVersionInfo(path, "\\")</c> read from the same files when they were
/// built: a version, <see langword="None"/> for no resource, and a real <c>(0, 0, 0, 0)</c>.
/// </summary>
public sealed class FileVersionResourceTests
{
    private static string Stub(string set, string name)
        => Path.Combine(RepoPaths.DotnetRoot, "..", "parity", "assets", "version-stubs", "out", set, name);

    [Fact]
    public void A_version_resource_reads_as_its_four_file_version_parts()
    {
        Assert.Equal([1, 10, 163, 0], FileVersionResource.Read(Stub("og", "Fallout4.exe")));
        Assert.Equal([0, 0, 6, 23], FileVersionResource.Read(Stub("og", "f4se_steam_loader.dll")));
    }

    [Fact]
    [Trait("Parity", "T-4")]
    public void A_file_without_a_version_resource_has_no_version()
    {
        // FileVersionInfo would report 0.0.0.0 here; Python gets None.
        Assert.Null(FileVersionResource.Read(Stub("no-version", "Fallout4.exe")));
        Assert.Null(FileVersionResource.Read(Stub("og", "Fallout4 - Startup.ba2")));
    }

    [Fact]
    [Trait("Parity", "T-4")]
    public void A_resource_that_says_zero_is_a_version_of_zeros()
    {
        Assert.Equal([0, 0, 0, 0], FileVersionResource.Read(Stub("zero-version", "Fallout4.exe")));
    }

    [Fact]
    public void A_missing_file_has_no_version()
    {
        Assert.Null(FileVersionResource.Read(Stub("og", "missing.exe")));
    }
}
