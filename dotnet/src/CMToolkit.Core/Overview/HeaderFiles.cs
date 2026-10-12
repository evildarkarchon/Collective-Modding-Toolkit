using System.Text;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Overview;

/// <summary>
/// Reading the start of a module or archive the way the reference does: <c>path.open("rb")</c> then <c>f.read(n)</c>,
/// with <c>except PermissionError, FileNotFoundError</c> around the open (OVW-M6, OVW-A4).
/// </summary>
internal static class HeaderFiles
{
    // Win32 errors that CPython's open() maps to EACCES, so PermissionError, beyond the access denial .NET reports as
    // UnauthorizedAccessException: another process holding the file without sharing it, or a locked byte range.
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>UTF-8 that throws on bad bytes, as <c>bytes.decode("utf-8")</c> does (T-1).</summary>
    public static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Up to <paramref name="count"/> bytes from the start of the file, fewer if it is shorter, or null when opening it
    /// raised what Python reports as <c>PermissionError</c> or <c>FileNotFoundError</c>. A folder is a
    /// <c>PermissionError</c> on Windows, so it is null too. The file is opened with read and write sharing, as the C
    /// runtime's <c>_wopen</c> under Python's <c>open</c> shares it.
    /// </summary>
    /// <exception cref="IOException">Any other read failure, which the reference doesn't catch either.</exception>
    public static byte[]? ReadHead(IHostEnvironment host, PythonPath path, int count)
    {
        try
        {
            using var stream = new FileStream(HostPaths.Resolve(host, path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[count];
            var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
            return buffer[..read];
        }
        catch (Exception ex) when (IsPermissionOrNotFound(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// <c>path.read_text("utf-8")</c> (T-1): strict UTF-8, a BOM kept as U+FEFF, opened with read and write sharing.
    /// </summary>
    /// <exception cref="DecoderFallbackException">The file isn't valid UTF-8.</exception>
    public static string ReadText(IHostEnvironment host, PythonPath path)
    {
        using var stream = new FileStream(HostPaths.Resolve(host, path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return StrictUtf8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is what Python raises as <c>PermissionError</c> (access denied, a folder, a
    /// sharing or lock violation) or <c>FileNotFoundError</c> (no such file or folder) from an open.
    /// </summary>
    public static bool IsPermissionOrNotFound(Exception exception) => exception switch
    {
        UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException => true,
        IOException io => io.HResult is SharingViolation or LockViolation,
        _ => false,
    };
}
