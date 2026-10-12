using System.Runtime.CompilerServices;
using System.Text;
using PythonText = CMToolkit.Core.PythonSemantics.PythonText;
using CMToolkit.Core.Settings;

namespace CMToolkit.Core.Updates;

/// <summary>
/// How <c>requests</c> 2.32 turns a response's bytes into text, which decides what the update check reads (NET-1,
/// NET-2): the encoding taken from <c>Content-Type</c>, <c>iter_lines</c> over 512-byte chunks, and
/// <c>Response.json()</c>.
/// </summary>
internal static class RequestsText
{
    /// <summary><c>iter_lines</c>' default <c>chunk_size</c>; each chunk is one <c>raw.read(512)</c>.</summary>
    public const int ChunkSize = 512;

    /// <summary>
    /// <c>requests.utils.get_encoding_from_headers</c>: the <c>charset</c> parameter (quotes stripped); else
    /// <c>ISO-8859-1</c> when the media type contains <c>text</c> (case-sensitively); else <c>utf-8</c> for
    /// <c>application/json</c>; else <see langword="null"/>, as with no <c>Content-Type</c> at all.
    /// </summary>
    /// <exception cref="UpdateCheckCrashException">
    /// A bare <c>charset</c> parameter with no <c>=</c>: requests stores it as <c>True</c>, and <c>True.strip</c>
    /// raises <c>AttributeError</c>.
    /// </exception>
    public static string? EncodingFromHeaders(HttpResponseMessage response)
    {
        if (!response.Content.Headers.NonValidated.TryGetValues("Content-Type", out var values)
            && !response.Headers.NonValidated.TryGetValues("Content-Type", out values))
        {
            return null;
        }

        // A repeated header reaches requests' CaseInsensitiveDict joined with ", ", as urllib3 merges it.
        var header = string.Join(", ", values);
        if (header.Length == 0)
        {
            return null;
        }

        var tokens = header.Split(';');
        var contentType = PythonText.Strip(tokens[0]);
        string? charset = null;
        var bareCharset = false;
        foreach (var token in tokens.Skip(1))
        {
            var param = PythonText.Strip(token);
            if (param.Length == 0)
            {
                continue;
            }

            var equals = param.IndexOf('=', StringComparison.Ordinal);
            var key = equals == -1 ? param : param[..equals].Trim('"', '\'', ' ');
            if (!key.ToLowerInvariant().Equals("charset", StringComparison.Ordinal))
            {
                continue;
            }

            // A later charset parameter replaces an earlier one, as in the dict requests builds.
            bareCharset = equals == -1;
            charset = bareCharset ? null : param[(equals + 1)..].Trim('"', '\'', ' ');
        }

        if (bareCharset)
        {
            throw new UpdateCheckCrashException("AttributeError: 'bool' object has no attribute 'strip'");
        }

        if (charset is not null)
        {
            return charset.Trim('\'', '"');
        }

        if (contentType.Contains("text", StringComparison.Ordinal))
        {
            return "ISO-8859-1";
        }

        return contentType.Contains("application/json", StringComparison.Ordinal) ? "utf-8" : null;
    }

    /// <summary>
    /// <c>response.iter_lines(decode_unicode=True)</c> for a response with an <paramref name="encoding"/>: 512-byte
    /// chunks decoded with <c>errors="replace"</c>, each split with <c>str.splitlines()</c>, a chunk's unfinished last
    /// line carried into the next. Like requests, a <c>\r\n</c> split across two chunks yields an extra empty line, which
    /// can make the Nexus check read an empty "version line" (bug #82, reproduced).
    /// </summary>
    /// <param name="read">Reads one chunk: up to <see cref="ChunkSize"/> bytes, fewer only at the end.</param>
    /// <param name="decoder">The response encoding's decoder.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async IAsyncEnumerable<string> IterLinesAsync(
        Func<CancellationToken, ValueTask<byte[]>> read,
        PythonCodec.IncrementalDecoder decoder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? pending = null;
        while (true)
        {
            var bytes = await read(cancellationToken).ConfigureAwait(false);
            var final = bytes.Length == 0;
            var chunk = decoder.Decode(bytes, final);
            // stream_decode_response_unicode only yields non-empty decoded chunks.
            if (chunk.Length > 0)
            {
                if (pending is not null)
                {
                    chunk = pending + chunk;
                }

                var lines = PythonText.SplitLines(chunk).ToList();
                if (lines.Count > 0 && lines[^1].Length > 0 && lines[^1][^1] == chunk[^1])
                {
                    pending = lines[^1];
                    lines.RemoveAt(lines.Count - 1);
                }
                else
                {
                    pending = null;
                }

                foreach (var line in lines)
                {
                    yield return line;
                }
            }

            if (final)
            {
                break;
            }
        }

        if (pending is not null)
        {
            yield return pending;
        }
    }

    /// <summary>
    /// <c>Response.json()</c>: with no encoding from the headers, the bytes' UTF flavour is guessed
    /// (<c>guess_json_utf</c>) and decoded strictly; otherwise, or if that decode fails, <c>Response.text</c> is
    /// parsed.
    /// </summary>
    /// <exception cref="PythonJsonException">Python's <c>json.loads</c> raises.</exception>
    public static PythonValue Json(byte[] content, string? encoding)
    {
        // `not self.encoding`: an empty charset parameter counts as no encoding here, though not in Response.text.
        if (string.IsNullOrEmpty(encoding) && content.Length > 3 && GuessJsonUtf(content) is { } guessed)
        {
            try
            {
                return PythonJson.Loads(PythonCodec.Lookup(guessed)!.Decode(content, strict: true));
            }
            catch (DecoderFallbackException)
            {
                // Python's UnicodeDecodeError: requests falls through to Response.text below.
            }
        }

        return PythonJson.Loads(Text(content, encoding));
    }

    /// <summary>
    /// <c>Response.text</c>: the bytes decoded with <c>errors="replace"</c> in the response's encoding, or in UTF-8
    /// when that codec is unknown. With no encoding requests asks chardet (<c>apparent_encoding</c>); that guess isn't
    /// ported, and UTF-8 stands in for it (dotnet/README.md, Known residuals).
    /// </summary>
    public static string Text(byte[] content, string? encoding)
    {
        if (content.Length == 0)
        {
            return string.Empty;
        }

        var codec = (encoding is null ? null : PythonCodec.Lookup(encoding)) ?? PythonCodec.Lookup("utf-8")!;
        return codec.Decode(content, strict: false);
    }

    /// <summary>
    /// <c>requests.utils.guess_json_utf</c>: a BOM names its codec, otherwise the pattern of NUL bytes in the first
    /// four bytes (JSON starts with two ASCII characters) does; <see langword="null"/> when nothing fits.
    /// </summary>
    public static string? GuessJsonUtf(ReadOnlySpan<byte> data)
    {
        var sample = data[..Math.Min(4, data.Length)];
        if (sample.SequenceEqual((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00])
            || sample.SequenceEqual((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            return "utf-32";
        }

        if (sample.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return "utf-8-sig";
        }

        if (sample.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || sample.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return "utf-16";
        }

        var nulls = sample.Count((byte)0);
        if (nulls == 0)
        {
            return "utf-8";
        }

        if (nulls == 2 && sample.Length == 4)
        {
            if (sample[0] == 0 && sample[2] == 0)
            {
                return "utf-16-be";
            }

            if (sample[1] == 0 && sample[3] == 0)
            {
                return "utf-16-le";
            }
        }

        if (nulls == 3 && sample.Length == 4)
        {
            if (sample[..3].SequenceEqual((ReadOnlySpan<byte>)[0, 0, 0]))
            {
                return "utf-32-be";
            }

            if (sample[1..].SequenceEqual((ReadOnlySpan<byte>)[0, 0, 0]))
            {
                return "utf-32-le";
            }
        }

        return null;
    }
}
