using System.Runtime.InteropServices;

namespace CMToolkit.Core.Win32;

/// <summary>
/// Mapping a DLL as an image without running it, for the F4SE plugin inspection (F4SE-3, T-5): the same loader calls
/// <c>ctypes.WinDLL(path, winmode=DONT_RESOLVE_DLL_REFERENCES)</c> and its attribute lookups make.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// <c>DONT_RESOLVE_DLL_REFERENCES</c>: map the image without calling <c>DllMain</c> or loading its imports, so a
    /// plugin's code never runs.
    /// </summary>
    public const uint DontResolveDllReferences = 0x00000001;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint LoadLibraryEx(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial nint GetProcAddress(nint module, string procName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(nint module);
}
