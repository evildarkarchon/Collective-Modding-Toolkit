using System.Globalization;
using System.IO.Hashing;

namespace CMToolkit.Core;

/// <summary>
/// <c>utils.get_crc32</c>: a file's CRC32 (zlib's, the IEEE polynomial), as 8 uppercase hex digits. Used for the
/// binaries known only by CRC (OVW-B2) and the Down-Grade check (OVW-B6); the Downgrader reuses it.
/// </summary>
public static class FileCrc32
{
    /// <summary>The CRC32 of the whole file at <paramref name="path"/>, or of everything past its first 12 bytes.</summary>
    /// <param name="path">The file, absolute (relative paths would resolve against the process's working directory).</param>
    /// <param name="skipBa2Header">
    /// <c>skip_ba2_header=True</c>: start at offset 12, past a BA2 header (<c>f.seek(12)</c>; a shorter file hashes as empty).
    /// </param>
    /// <exception cref="IOException">The file can't be read; the reference lets <c>open()</c>'s error escape too.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be opened for reading.</exception>
    public static string Of(string path, bool skipBa2Header = false)
    {
        // FileShare.ReadWrite: Python's open() shares like the CRT's _SH_DENYNO, so a file another process has open for
        // writing is still read.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
        if (skipBa2Header)
        {
            stream.Seek(12, SeekOrigin.Begin);
        }

        var crc = new Crc32();
        // The reference reads 64 KiB chunks; the result doesn't depend on the chunking.
        crc.Append(stream);
        return crc.GetCurrentHashAsUInt32().ToString("X8", CultureInfo.InvariantCulture);
    }
}
