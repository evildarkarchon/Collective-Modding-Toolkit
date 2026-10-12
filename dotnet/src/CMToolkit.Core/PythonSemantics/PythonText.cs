using System.Text;

namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// Python's text handling where the Reference Implementation relies on it (T-1): strict UTF-8 decoding that keeps a
/// BOM, <c>str.splitlines()</c> and <c>str.strip()</c>. Shared by every hand-ported text parser (the MO2 INI, MO2-4,
/// and the game INIs, INI-3).
/// </summary>
public static class PythonText
{
    /// <summary>
    /// UTF-8 that throws on undecodable bytes, like Python's <c>"utf-8"</c> codec with <c>errors="strict"</c>. It also
    /// rejects encoded surrogates and overlong forms, as CPython does. No BOM handling: <see cref="Encoding.GetString(byte[])"/>
    /// never strips one, matching <c>"utf-8"</c> (not <c>"utf-8-sig"</c>).
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// <c>Path.read_text(encoding="utf-8")</c>: the whole file decoded as strict UTF-8, with a leading BOM kept as
    /// U+FEFF. Line endings are left alone; <see cref="SplitLines"/> treats <c>\r\n</c> and <c>\r</c> as Python's
    /// universal-newline translation plus <c>splitlines()</c> would, so the result is the same.
    /// </summary>
    /// <exception cref="DecoderFallbackException">The file isn't valid UTF-8 (Python's <c>UnicodeDecodeError</c>).</exception>
    /// <exception cref="IOException">The file can't be read.</exception>
    public static string ReadUtf8Strict(string path) => StrictUtf8.GetString(File.ReadAllBytes(path));

    /// <summary>
    /// <c>bytes.decode("utf-8")</c>: <paramref name="bytes"/> as strict UTF-8, a BOM kept as U+FEFF, like
    /// <see cref="ReadUtf8Strict"/> on bytes already read.
    /// </summary>
    /// <exception cref="DecoderFallbackException">The bytes aren't valid UTF-8 (Python's <c>UnicodeDecodeError</c>).</exception>
    public static string DecodeUtf8Strict(ReadOnlySpan<byte> bytes) => StrictUtf8.GetString(bytes);

    /// <summary>
    /// <c>str.splitlines()</c>: splits on <c>\n</c>, <c>\r</c>, <c>\r\n</c>, <c>\v</c>, <c>\f</c>, U+001C..U+001E,
    /// U+0085, U+2028 and U+2029, drops the separators, and yields no trailing empty line for a final separator.
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (!IsLineBreak(c))
            {
                i++;
                continue;
            }

            lines.Add(text[start..i]);
            // \r\n is one separator; every other break character is one on its own.
            i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            start = i;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>Whether <paramref name="c"/> is one of the characters <c>str.splitlines()</c> breaks on.</summary>
    private static bool IsLineBreak(char c)
        => c is '\n' or '\r' or '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029';

    /// <summary>
    /// <c>str.isspace()</c> for one character. Python's whitespace set is .NET's <see cref="char.IsWhiteSpace(char)"/>
    /// plus U+001C..U+001F (the file, group, record and unit separators), which Python counts and .NET doesn't. Python's
    /// <c>re</c> <c>\s</c> uses the same set for <c>str</c> patterns.
    /// </summary>
    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';

    /// <summary>The no-argument <c>str.strip()</c>: <see cref="IsSpace"/> characters removed from both ends.</summary>
    public static string Strip(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }
}
