using System.Reflection.PortableExecutable;
using CMToolkit.Core.F4se;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.F4se;

/// <summary>
/// <see cref="F4sePluginInspector"/> on the committed plugin stubs (<c>parity/assets/f4se-plugins/out</c>) and on a real
/// Windows DLL: the loader, not a managed PE reader, decides what can be inspected (T-5), and the compatible versions
/// compare as the reference's unsigned 32-bit values (T-4).
/// </summary>
public sealed class F4sePluginInspectorTests
{
    private static readonly string Stubs = Path.Combine(Path.GetDirectoryName(RepoPaths.DotnetRoot)!, "parity", "assets", "f4se-plugins", "out");

    [Fact]
    [Trait("Parity", "T-5")]
    public void A_32_bit_plugin_is_refused_by_the_loader_though_a_managed_PE_reader_reads_it()
    {
        var path = Path.Combine(Stubs, "x86", "OldGenOnly.dll");

        // A managed reader would happily read it: it is a well-formed PE, just for x86...
        using (var stream = File.OpenRead(path))
        using (var reader = new PEReader(stream))
        {
            Assert.Equal(Machine.I386, reader.PEHeaders.CoffHeader.Machine);
        }

        // ...but LoadLibraryExW in this 64-bit process fails, as ctypes.WinDLL raised OSError [WinError 193].
        var error = Assert.Throws<F4sePluginLoadException>(() => F4sePluginInspector.Inspect(path));
        Assert.Equal(193, error.NativeErrorCode);
        Assert.StartsWith("[WinError 193] ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Parity", "T-5")]
    public void A_file_that_is_not_a_PE_and_a_folder_are_refused()
    {
        using var temp = new TempDirectory();
        var text = temp.WriteBytes("Broken.dll", "This is not a DLL.\r\n"u8.ToArray());
        var folder = Directory.CreateDirectory(Path.Combine(temp.Path, "Folder.dll")).FullName;

        Assert.Throws<F4sePluginLoadException>(() => F4sePluginInspector.Inspect(text));
        Assert.Throws<F4sePluginLoadException>(() => F4sePluginInspector.Inspect(folder));
    }

    [Fact]
    [Trait("Parity", "T-5")]
    public void A_real_Windows_DLL_is_mapped_and_is_not_an_F4SE_plugin()
    {
        // kernel32.dll is on every Windows, CI's server runners included, and is already loaded in this process.
        var kernel32 = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Assert.Equal(new F4sePluginInfo(false, false, false, null, null, null, null), F4sePluginInspector.Inspect(kernel32));
    }

    [Fact]
    [Trait("Parity", "T-5")]
    public void Inspecting_a_plugin_leaves_it_unmapped_so_it_can_be_deleted()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteBytes("AddressLibrary.dll", File.ReadAllBytes(Path.Combine(Stubs, "x64", "AddressLibrary.dll")));

        Assert.True(F4sePluginInspector.Inspect(path).IsF4se);

        // The reference kept every WinDLL mapped for the session, which locks the file; the port frees it.
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Parity", "T-4")]
    public void Compatible_versions_compare_unsigned_and_the_first_Anniversary_build_does_not_count()
    {
        // 0xFF000000 is above 1.11.137 only as an unsigned value.
        var high = F4sePluginInspector.Inspect(Path.Combine(Stubs, "x64", "HighVersion.dll"));
        // Only 1.11.137 itself (0x010B0890): AE support needs a version strictly above it.
        var first = F4sePluginInspector.Inspect(Path.Combine(Stubs, "x64", "AnniversaryFirstRelease.dll"));
        // 1.10.980, then two zeros, then 1.11.191: every one of the 16 entries is read, past the zeros.
        var gap = F4sePluginInspector.Inspect(Path.Combine(Stubs, "x64", "CompatibleAfterGap.dll"));

        Assert.True(high.SupportsAe);
        Assert.Null(high.SupportsNg);
        Assert.Null(first.SupportsAe);
        Assert.Null(first.SupportsNg);
        Assert.True(gap.SupportsNg);
        Assert.True(gap.SupportsAe);
    }
}
