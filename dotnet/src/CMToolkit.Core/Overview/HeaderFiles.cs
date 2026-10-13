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
    /// <c>path.read_text("utf-8")</c> (T-1) for a list file (<c>Fallout4.ccc</c>, <c>plugins.txt</c>): UTF-8, a BOM
    /// kept as U+FEFF, opened with read and write sharing. The reference's strict decode raised on a file that isn't
    /// valid UTF-8 and failed the Overview; the port decodes each line that isn't UTF-8 as Windows-1252 instead, as the
    /// game INIs are read (<see cref="AnsiFallbackText.DecodeLines"/>; deviation, bug #86).
    /// </summary>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be read.</exception>
    public static string ReadListText(IHostEnvironment host, PythonPath path) => AnsiFallbackText.DecodeLines(ReadAll(host, path));

    /// <summary>The whole file, opened with read and write sharing as Python's <c>open</c> shares it.</summary>
    private static byte[] ReadAll(IHostEnvironment host, PythonPath path)
    {
        using var stream = new FileStream(HostPaths.Resolve(host, path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
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
