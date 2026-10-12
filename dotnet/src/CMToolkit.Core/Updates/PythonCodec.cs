using System.Text;

namespace CMToolkit.Core.Updates;

/// <summary>
/// The Python codecs a response's charset can name, as far as the update check needs them: <c>codecs.lookup</c>'s
/// name normalisation and aliases for the UTF-8, UTF-16, UTF-32, Latin-1 and ASCII codecs, including the BOM handling
/// of <c>utf-8-sig</c>, <c>utf-16</c> and <c>utf-32</c> (which consume a BOM and otherwise assume little-endian).
/// </summary>
/// <remarks>
/// Any other codec Python knows (<c>cp1252</c>, <c>iso8859-15</c>, <c>shift_jis</c>, ...) is treated as unknown,
/// which is Python's <c>LookupError</c> (dotnet/README.md, Known residuals). Servers the update check talks to send
/// UTF-8.
/// </remarks>
internal sealed class PythonCodec
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        // encodings.aliases, for the codecs ported here.
        ["utf8"] = "utf_8", ["u8"] = "utf_8", ["utf"] = "utf_8", ["utf8_ucs2"] = "utf_8", ["utf8_ucs4"] = "utf_8",
        ["cp65001"] = "utf_8",
        ["latin"] = "latin_1", ["latin1"] = "latin_1", ["l1"] = "latin_1", ["iso_8859_1"] = "latin_1",
        ["iso8859_1"] = "latin_1", ["8859"] = "latin_1", ["cp819"] = "latin_1", ["csisolatin1"] = "latin_1",
        ["ibm819"] = "latin_1", ["iso_ir_100"] = "latin_1", ["iso_8859_1_1987"] = "latin_1",
        ["646"] = "ascii", ["ansi_x3.4_1968"] = "ascii", ["ansi_x3_4_1968"] = "ascii", ["ansi_x3.4_1986"] = "ascii",
        ["cp367"] = "ascii", ["csascii"] = "ascii", ["ibm367"] = "ascii", ["iso646_us"] = "ascii",
        ["iso_646.irv_1991"] = "ascii", ["iso_ir_6"] = "ascii", ["us"] = "ascii", ["us_ascii"] = "ascii",
        ["u16"] = "utf_16", ["utf16"] = "utf_16", ["unicodebigunmarked"] = "utf_16_be", ["utf_16be"] = "utf_16_be",
        ["unicodelittleunmarked"] = "utf_16_le", ["utf_16le"] = "utf_16_le",
        ["u32"] = "utf_32", ["utf32"] = "utf_32", ["utf_32be"] = "utf_32_be", ["utf_32le"] = "utf_32_le",
    };

    private readonly string _name;

    private PythonCodec(string name)
    {
        _name = name;
    }

    /// <summary>
    /// <c>codecs.lookup(name)</c>, or <see langword="null"/> where Python raises <c>LookupError</c> (or the codec isn't
    /// ported).
    /// </summary>
    public static PythonCodec? Lookup(string name)
    {
        var normalized = Normalize(name);
        normalized = Aliases.GetValueOrDefault(normalized, normalized);
        return normalized is "utf_8" or "utf_8_sig" or "latin_1" or "ascii" or "utf_16" or "utf_16_le" or "utf_16_be"
            or "utf_32" or "utf_32_le" or "utf_32_be"
            ? new PythonCodec(normalized)
            : null;
    }

    /// <summary>
    /// A decoder for this codec: <c>errors="replace"</c> (each undecodable sequence becomes U+FFFD) or strict (throws
    /// <see cref="DecoderFallbackException"/>, Python's <c>UnicodeDecodeError</c>).
    /// </summary>
    public IncrementalDecoder CreateDecoder(bool strict) => new(_name, strict);

    /// <summary><c>bytes.decode(codec, errors)</c> in one go.</summary>
    /// <exception cref="DecoderFallbackException"><paramref name="strict"/> and the bytes don't decode.</exception>
    public string Decode(ReadOnlySpan<byte> bytes, bool strict) => CreateDecoder(strict).Decode(bytes, final: true);

    /// <summary>
    /// <c>codecs.lookup</c>'s normalisation: lowercased, spaces as hyphens, then <c>encodings.normalize_encoding</c>,
    /// which turns every run of characters other than ASCII letters, digits and <c>.</c> into one <c>_</c> and drops
    /// leading and trailing ones.
    /// </summary>
    private static string Normalize(string name)
    {
        var builder = new StringBuilder();
        var pendingSeparator = false;
        foreach (var c in name.ToLowerInvariant().Replace(' ', '-'))
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.')
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                }

                pendingSeparator = false;
                builder.Append(c);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// <c>codecs.getincrementaldecoder(codec)(errors)</c>: decodes a byte stream chunk by chunk, holding back an
    /// incomplete sequence at a chunk's end. For <c>utf-8-sig</c>, <c>utf-16</c> and <c>utf-32</c> it first waits for
    /// enough bytes to tell whether the stream starts with a BOM, which it consumes and (for UTF-16/32) takes its byte
    /// order from.
    /// </summary>
    internal sealed class IncrementalDecoder
    {
        private readonly string _codec;
        private readonly bool _strict;
        private Decoder? _decoder;
        private byte[] _head = [];

        /// <summary>A decoder for <paramref name="codec"/>, ready for the stream's first chunk.</summary>
        /// <param name="codec">
        /// A normalised codec name (<c>utf_8</c>, <c>latin_1</c>, <c>ascii</c>, <c>utf_16_le</c>, ...). The BOM-sniffing
        /// <c>utf_8_sig</c>, <c>utf_16</c> and <c>utf_32</c> pick their decoder in <see cref="Decode"/> once the head is in.
        /// </param>
        /// <param name="strict">
        /// <c>errors="strict"</c>: bytes that don't decode throw; otherwise each becomes U+FFFD (<c>"replace"</c>).
        /// </param>
        public IncrementalDecoder(string codec, bool strict)
        {
            _codec = codec;
            _strict = strict;
            _decoder = codec switch
            {
                "utf_8" => Utf8(strict),
                "latin_1" => Encoding.Latin1.GetDecoder(),
                "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, Fallback(strict)).GetDecoder(),
                "utf_16_le" => Utf16(bigEndian: false, strict),
                "utf_16_be" => Utf16(bigEndian: true, strict),
                "utf_32_le" => Utf32(bigEndian: false, strict),
                "utf_32_be" => Utf32(bigEndian: true, strict),
                // The BOM-sniffing codecs pick their decoder once the head is in.
                _ => null,
            };
        }

        /// <summary><c>decoder.decode(bytes, final)</c>.</summary>
        /// <exception cref="DecoderFallbackException">A strict decoder met bytes that don't decode.</exception>
        public string Decode(ReadOnlySpan<byte> bytes, bool final)
        {
            if (_decoder is null)
            {
                _head = [.. _head, .. bytes];
                var needed = _codec == "utf_32" ? 4 : _codec == "utf_16" ? 2 : 3;
                if (_head.Length < needed && !final)
                {
                    return string.Empty;
                }

                var (decoder, bomLength) = Sniff(_head);
                _decoder = decoder;
                bytes = _head.AsSpan(bomLength);
            }

            var chars = new char[_decoder.GetCharCount(bytes, final)];
            var count = _decoder.GetChars(bytes, chars, final);
            return new string(chars, 0, count);
        }

        /// <summary>The decoder a BOM (or its absence) selects, and the BOM's length.</summary>
        private (Decoder Decoder, int BomLength) Sniff(ReadOnlySpan<byte> head)
        {
            switch (_codec)
            {
                case "utf_8_sig":
                    return (Utf8(_strict), head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0);
                case "utf_16":
                    if (head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
                    {
                        return (Utf16(bigEndian: true, _strict), 2);
                    }

                    return (Utf16(bigEndian: false, _strict), head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ? 2 : 0);
                default:
                    if (head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
                    {
                        return (Utf32(bigEndian: true, _strict), 4);
                    }

                    return (Utf32(bigEndian: false, _strict), head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]) ? 4 : 0);
            }
        }

        private static DecoderFallback Fallback(bool strict) =>
            strict ? DecoderFallback.ExceptionFallback : new DecoderReplacementFallback("\uFFFD");

        private static Decoder Utf8(bool strict) =>
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: strict).GetDecoder();

        private static Decoder Utf16(bool bigEndian, bool strict) =>
            new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: strict).GetDecoder();

        private static Decoder Utf32(bool bigEndian, bool strict) =>
            new UTF32Encoding(bigEndian, byteOrderMark: false, throwOnInvalidCharacters: strict).GetDecoder();
    }
}
