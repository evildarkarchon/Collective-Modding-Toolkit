using System.Runtime.InteropServices;

namespace CMToolkit.Core.Win32;

/// <summary>
/// A file's version resource, as <c>utils.get_file_version</c> reads it with <c>win32api.GetFileVersionInfo(path, "\\")</c>:
/// the four parts of <c>VS_FIXEDFILEINFO</c>'s <c>FileVersionMS</c>/<c>FileVersionLS</c>.
/// </summary>
public static unsafe partial class FileVersionResource
{
    /// <summary>The size of <c>VS_FIXEDFILEINFO</c>.</summary>
    private const int FixedInfoSize = 52;

    /// <summary>
    /// The four file-version parts, or null when the file has no version resource or can't be read, exactly where
    /// pywin32 raises and the reference's bare <c>except</c> returns <see langword="None"/> (T-4). A resource that really
    /// says <c>0.0.0.0</c> is returned as such, as Python returns <c>(0, 0, 0, 0)</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Diagnostics.FileVersionInfo"/> can't tell those two cases apart (both read as zeros), so this
    /// makes pywin32's own calls: <c>GetFileVersionInfoSizeW</c>, <c>GetFileVersionInfoW</c>, then
    /// <c>VerQueryValueW(buf, "\\")</c>. Needs the exe's Windows 10 <c>supportedOS</c> manifest entry: without it
    /// Windows reports its own files as 6.2 (issue #4).
    /// </remarks>
    public static IReadOnlyList<int>? Read(string path)
    {
        var size = GetFileVersionInfoSize(path, out _);
        if (size == 0)
        {
            // No resource, not a PE, missing or unreadable: pywin32 raises for every one of them.
            return null;
        }

        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (!GetFileVersionInfo(path, 0, size, data)
                || !VerQueryValue(data, @"\", out var fixedInfo, out var length)
                || length < FixedInfoSize)
            {
                return null;
            }

            // VS_FIXEDFILEINFO: dwSignature, dwStrucVersion, dwFileVersionMS, dwFileVersionLS, ...; the parts are the
            // HIWORD and LOWORD of each, as win32api.HIWORD/LOWORD split them. The pointer is into the pinned buffer.
            var info = (uint*)fixedInfo;
            uint ms = info[2], ls = info[3];
            return [(int)(ms >> 16), (int)(ms & 0xFFFF), (int)(ls >> 16), (int)(ls & 0xFFFF)];
        }
    }

    [LibraryImport("version.dll", EntryPoint = "GetFileVersionInfoSizeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFileVersionInfoSize(string fileName, out uint handle);

    [LibraryImport("version.dll", EntryPoint = "GetFileVersionInfoW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileVersionInfo(string fileName, uint handle, uint length, byte* data);

    [LibraryImport("version.dll", EntryPoint = "VerQueryValueW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VerQueryValue(byte* block, string subBlock, out nint value, out uint length);
}
