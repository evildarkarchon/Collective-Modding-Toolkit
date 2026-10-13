using System.Text;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// Decoding for the game's own text files, which editors may save in the ANSI code page: strict UTF-8, falling back to
/// Windows-1252 where the bytes aren't UTF-8. The reference decoded strict UTF-8 only and failed on such files (T-1).
/// The port decodes each game INI whole this way (bug #17), and <c>Fallout4.ccc</c> and <c>plugins.txt</c> line by
/// line (bug #86).
/// </summary>
public static class AnsiFallbackText
{
    /// <summary>
    /// Windows-1252, for text that isn't UTF-8: the game reads its INIs through the ANSI profile API, so editors save
    /// them in the ANSI code page. Fixed rather than the machine's code page, so a read never depends on the host's
    /// locale; the settings the toolkit reads are ASCII either way. A module name saved in another ANSI code page decodes
    /// differently from what the game sees (README known residuals).
    /// </summary>
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
        ?? throw new InvalidOperationException("Code page 1252 isn't available.");

    /// <summary>
    /// <paramref name="bytes"/> as strict UTF-8, or, when they aren't valid UTF-8, all of them as Windows-1252. A BOM is
    /// not stripped: under UTF-8 it stays U+FEFF, as Python's <c>"utf-8"</c> codec keeps it.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return PythonText.DecodeUtf8Strict(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: an ANSI file, read as the game would read it.
            return Windows1252.GetString(bytes);
        }
    }

    /// <summary>
    /// <see cref="Decode"/> one line at a time: the bytes are cut after each CR and LF and every piece is decoded on its
    /// own, so one ANSI line (a hand-edited <c>plugins.txt</c>, say) doesn't change how the UTF-8 lines around it read.
    /// The line breaks are kept, so <see cref="PythonText.SplitLines"/> splits the result as it would the file's text.
    /// Valid UTF-8 decodes exactly as <see cref="PythonText.DecodeUtf8Strict"/> would, since a CR or LF byte is never
    /// part of a multi-byte UTF-8 sequence.
    /// </summary>
    public static string DecodeLines(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        while (!bytes.IsEmpty)
        {
            var end = bytes.IndexOfAny((byte)'\r', (byte)'\n');
            var length = end < 0 ? bytes.Length : end + 1;
            text.Append(Decode(bytes[..length]));
            bytes = bytes[length..];
        }

        return text.ToString();
    }
}
