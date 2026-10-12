using System.ComponentModel;
using CMToolkit.Core.Win32;

namespace CMToolkit.Core.F4se;

/// <summary>
/// What inspecting one DLL in <c>F4SE\Plugins</c> found (<c>helpers.DLLInfo</c>; F4SE-3). The NG/AE facts are null
/// rather than false when the DLL doesn't say, which the verdicts tell apart (F4SE-5).
/// </summary>
/// <param name="IsF4se">It exports <c>F4SEPlugin_Load</c> or <c>F4SEPlugin_Preload</c>.</param>
/// <param name="SupportsOg">It exports <c>F4SEPlugin_Query</c>, the Old-Gen plugin API.</param>
/// <param name="SupportsNgAe">It exports <c>F4SEPlugin_Version</c>, the Next-Gen/Anniversary plugin API.</param>
/// <param name="SupportsNg">True when <c>compatibleVersions</c> lists 1.10.980 or 1.10.984; otherwise null, never false.</param>
/// <param name="SupportsAe">True when <c>compatibleVersions</c> lists a version above 1.11.137; otherwise null, never false.</param>
/// <param name="AddressIndependent"><c>addressIndependence &amp; 0b110</c>, or null without <c>F4SEPlugin_Version</c>.</param>
/// <param name="StructureIndependent"><c>structureIndependence &amp; 0b110</c>, or null without <c>F4SEPlugin_Version</c>.</param>
public sealed record F4sePluginInfo(
    bool IsF4se,
    bool SupportsOg,
    bool SupportsNgAe,
    bool? SupportsNg,
    bool? SupportsAe,
    bool? AddressIndependent,
    bool? StructureIndependent);

/// <summary>
/// A DLL in <c>F4SE\Plugins</c> that the loader refused to map (the reference's <c>OSError</c> out of
/// <c>ctypes.WinDLL</c>): not a PE, a PE for another architecture, a folder, or otherwise unloadable (B-18, T-5).
/// </summary>
public sealed class F4sePluginLoadException : IOException
{
    /// <param name="path">The DLL's full path.</param>
    /// <param name="error">The Win32 error <c>LoadLibraryExW</c> failed with.</param>
    public F4sePluginLoadException(string path, int error)
        // Worded as Python words a Windows OSError (trailing dots and whitespace stripped); the path isn't part of it.
        : base($"[WinError {error}] {new Win32Exception(error).Message.TrimEnd(' ', '.', '\r', '\n')}")
    {
        Path = path;
        NativeErrorCode = error;
        HResult = unchecked((int)(0x80070000u | (uint)error));
    }

    /// <summary>The DLL's full path.</summary>
    public string Path { get; }

    /// <summary>The Win32 error code, such as 193 (<c>ERROR_BAD_EXE_FORMAT</c>) for a 32-bit or non-PE file.</summary>
    public int NativeErrorCode { get; }
}

/// <summary>
/// <c>utils.parse_dll</c> (F4SE-3): maps a DLL with <c>LoadLibraryExW(DONT_RESOLVE_DLL_REFERENCES)</c>, checks its F4SE
/// exports and reads its <c>F4SEPlugin_Version</c> struct.
/// </summary>
/// <remarks>
/// T-5: this is the real Windows loader, not a managed PE reader, so whatever the loader refuses (a 32-bit DLL in this
/// 64-bit process, a file that isn't a PE, a folder) fails here exactly as it failed the reference, rather than being
/// read anyway. Unlike the reference, which never freed a <c>WinDLL</c>, the image is unmapped once read, so the port
/// doesn't keep every plugin locked against deletion for the rest of the session.
/// </remarks>
public static class F4sePluginInspector
{
    /// <summary>F4SE's packed 1.10.980 (<c>RUNTIME_VERSION_1_10_980</c>), the first Next-Gen runtime.</summary>
    public const uint NextGen980 = 0x010A3D40;

    /// <summary>F4SE's packed 1.10.984 (<c>RUNTIME_VERSION_1_10_984</c>).</summary>
    public const uint NextGen984 = 0x010A3D80;

    /// <summary>
    /// F4SE's packed 1.11.137 (<c>RUNTIME_VERSION_1_11_137</c>), the first Anniversary runtime. Anniversary support is a
    /// compatible version strictly above it, so a plugin listing only 1.11.137 doesn't count.
    /// </summary>
    public const uint Anniversary137 = 0x010B0890;

    /// <summary>The independence bits that count: Address Library (1 &lt;&lt; 1, 1 &lt;&lt; 2), not signatures (1 &lt;&lt; 0).</summary>
    private const uint IndependenceMask = 0b110;

    // Offsets into F4SE's PluginVersionData (utils.F4SEPluginVersionData): two uint32s, then name[256] and author[256].
    private const int AddressIndependenceOffset = 4 + 4 + 256 + 256;
    private const int StructureIndependenceOffset = AddressIndependenceOffset + 4;
    private const int CompatibleVersionsOffset = StructureIndependenceOffset + 4;
    private const int CompatibleVersionsCount = 16;

    /// <summary>Inspects the DLL at <paramref name="path"/>.</summary>
    /// <param name="path">A full path: the loader would search for a relative one.</param>
    /// <returns>What its exports and version struct say.</returns>
    /// <exception cref="F4sePluginLoadException">The loader refused to map it (B-18).</exception>
    public static unsafe F4sePluginInfo Inspect(string path)
    {
        var module = NativeMethods.LoadLibraryEx(path, 0, NativeMethods.DontResolveDllReferences);
        if (module == 0)
        {
            throw new F4sePluginLoadException(path, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        try
        {
            // hasattr(dll, name): GetProcAddress, with a miss as false.
            bool Has(string name) => NativeMethods.GetProcAddress(module, name) != 0;

            var isF4se = Has("F4SEPlugin_Load") || Has("F4SEPlugin_Preload");
            var supportsOg = Has("F4SEPlugin_Query");
            var version = NativeMethods.GetProcAddress(module, "F4SEPlugin_Version");
            if (version == 0)
            {
                return new F4sePluginInfo(isF4se, supportsOg, false, null, null, null, null);
            }

            // The export's address is the struct itself (ctypes.cast(dll.F4SEPlugin_Version, POINTER(...))).
            var data = (byte*)version;
            var address = *(uint*)(data + AddressIndependenceOffset);
            var structure = *(uint*)(data + StructureIndependenceOffset);
            var compatible = new ReadOnlySpan<uint>(data + CompatibleVersionsOffset, CompatibleVersionsCount);

            // Every one of the 16 entries is read, the zeros after the last real one included; the compare is
            // unsigned, as Python compares the c_uint32 values (T-4).
            bool? supportsNg = compatible.Contains(NextGen980) || compatible.Contains(NextGen984) ? true : null;
            bool? supportsAe = null;
            foreach (var entry in compatible)
            {
                if (entry > Anniversary137)
                {
                    supportsAe = true;
                    break;
                }
            }

            return new F4sePluginInfo(
                isF4se, supportsOg, true, supportsNg, supportsAe, (address & IndependenceMask) != 0, (structure & IndependenceMask) != 0);
        }
        finally
        {
            NativeMethods.FreeLibrary(module);
        }
    }
}
