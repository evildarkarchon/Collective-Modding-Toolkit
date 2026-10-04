using System.Text;

namespace CMToolkit.Core;

/// <summary>The site an archive was published to, baked into it by <c>assets\download-source.txt</c>.</summary>
public enum DownloadSource
{
    Nexus,
    GitHub,
}

/// <summary>
/// How a <see cref="DownloadSourceFile.Read"/> went. Each outcome matches one log line in the Reference
/// Implementation, which the logging slice writes from it.
/// </summary>
public enum DownloadSourceOutcome
{
    /// <summary>The file named a known source. Logged at DEBUG: <c>Settings : Download source: '&lt;value&gt;'</c>.</summary>
    Valid,

    /// <summary>The file was read but named something else. Logged at ERROR: <c>Settings : Invalid download source: '&lt;value&gt;'</c>.</summary>
    Invalid,

    /// <summary>The file couldn't be read. Logged with the exception: <c>Settings : Failed to detect download source.</c></summary>
    ReadFailed,
}

/// <summary>The result of looking up the Download Source.</summary>
/// <param name="Source">The source to use. <see cref="DownloadSource.Nexus"/> unless the file validly said otherwise.</param>
/// <param name="Outcome">Whether the file was valid, invalid or unreadable.</param>
/// <param name="RawValue">The decoded and stripped file contents, or <see langword="null"/> if the read failed.</param>
/// <param name="Error">The exception that stopped the read, for <see cref="DownloadSourceOutcome.ReadFailed"/> only.</param>
public sealed record DownloadSourceLookup(
    DownloadSource Source,
    DownloadSourceOutcome Outcome,
    string? RawValue,
    Exception? Error);

/// <summary>
/// Reads the Download Source from <c>assets\download-source.txt</c> beside the exe (SET-2; <c>app_settings.py:31-42</c>
/// at the Parity Baseline). The two release archives differ only in this file (ADR-0002).
/// </summary>
public static class DownloadSourceFile
{
    /// <summary>
    /// UTF-8 that drops undecodable bytes, like Python's <c>read_text("utf-8", "ignore")</c>. The default UTF-8
    /// decoder would substitute U+FFFD instead, which turns a stray byte into an invalid value.
    /// </summary>
    private static readonly Encoding Utf8IgnoringErrors =
        Encoding.GetEncoding("utf-8", EncoderFallback.ReplacementFallback, new DecoderReplacementFallback(string.Empty));

    /// <summary>The file's location inside <paramref name="appDirectory"/>, the folder holding the exe.</summary>
    public static string PathIn(string appDirectory) => Path.Combine(appDirectory, "assets", "download-source.txt");

    /// <summary>
    /// Reads and validates the file. It must say <c>nexus</c> or <c>github</c> (exactly, after stripping whitespace);
    /// anything else, or any failure to read it, falls back to <see cref="DownloadSource.Nexus"/>. Never throws.
    /// </summary>
    /// <param name="appDirectory">The folder holding the exe, normally <see cref="AppContext.BaseDirectory"/>.</param>
    public static DownloadSourceLookup Read(string appDirectory)
    {
        string raw;
        try
        {
            // ReadAllBytes + GetString, not ReadAllText: ReadAllText silently drops a UTF-8 BOM, but the reference
            // decodes with "utf-8" (not "utf-8-sig"), so a BOM survives into the value and makes it invalid.
            raw = PythonStrip(Utf8IgnoringErrors.GetString(File.ReadAllBytes(PathIn(appDirectory))));
        }
        catch (Exception ex)
        {
            // The reference uses a bare except: any failure to read the file means "nexus".
            return new DownloadSourceLookup(DownloadSource.Nexus, DownloadSourceOutcome.ReadFailed, null, ex);
        }

        return raw switch
        {
            "nexus" => new DownloadSourceLookup(DownloadSource.Nexus, DownloadSourceOutcome.Valid, raw, null),
            "github" => new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, raw, null),
            _ => new DownloadSourceLookup(DownloadSource.Nexus, DownloadSourceOutcome.Invalid, raw, null),
        };
    }

    /// <summary>
    /// Python's no-argument <c>str.strip()</c>. Its whitespace set is .NET's <see cref="char.IsWhiteSpace(char)"/> plus
    /// U+001C..U+001F (the file, group, record and unit separators), which Python treats as whitespace and .NET doesn't.
    /// </summary>
    private static string PythonStrip(string value)
    {
        static bool IsPythonWhiteSpace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';

        var start = 0;
        var end = value.Length;
        while (start < end && IsPythonWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsPythonWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }
}
