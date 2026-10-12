using System.Diagnostics;

namespace CMToolkit.Core.Win32;

/// <summary>
/// A file's version resource, as <c>utils.get_file_version</c> reads it with <c>win32api.GetFileVersionInfo(path, "\\")</c>:
/// the four parts of <c>VS_FIXEDFILEINFO</c>'s <c>FileVersionMS</c>/<c>FileVersionLS</c>.
/// </summary>
public static class FileVersionResource
{
    /// <summary>
    /// The four file-version parts, or null when the file can't be read. A file without a version resource reports
    /// <c>0.0.0.0</c> here where Python gets <see langword="None"/> (T-4), so callers treat all zeros as missing.
    /// </summary>
    /// <remarks>
    /// Needs the exe's Windows 10 <c>supportedOS</c> manifest entry: without it Windows reports its own files as 6.2
    /// (issue #4). <see cref="FileVersionInfo"/> reads the fixed-info parts, not the version strings.
    /// </remarks>
    public static IReadOnlyList<int>? Read(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return [info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart];
        }
        catch (Exception)
        {
            // Python's bare except around GetFileVersionInfo turns every failure into None.
            return null;
        }
    }
}
