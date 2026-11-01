using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// Writes a golden in the format every committed golden uses: keys sorted ordinally, two-space indent, LF line
/// endings, a closing newline, and every character outside printable ASCII as a lowercase <c>\u</c> escape. That is
/// what Python's <c>json.dumps(indent=2, sort_keys=True)</c> wrote for the original recordings, so a re-record's diff
/// shows only the values that changed.
/// </summary>
internal static class GoldenWriter
{
    /// <summary>
    /// Formats <paramref name="node"/> as golden text. Arrays keep their order, <c>$unordered</c> markers included;
    /// numbers keep the JSON text their node holds, so a number read from a golden is written back as it was read.
    /// </summary>
    public static string Write(JsonNode? node)
    {
        var text = new StringBuilder();
        WriteNode(text, node, 0);
        text.Append('\n');
        return text.ToString();
    }

    private static void WriteNode(StringBuilder text, JsonNode? node, int depth)
    {
        switch (node)
        {
            case null:
                text.Append("null");
                break;
            case JsonObject obj:
                WriteContainer(text, '{', '}', depth, obj.OrderBy(p => p.Key, StringComparer.Ordinal), (property, inner) =>
                {
                    WriteString(text, property.Key);
                    text.Append(": ");
                    WriteNode(text, property.Value, inner);
                });
                break;
            case JsonArray array:
                WriteContainer(text, '[', ']', depth, array, (item, inner) => WriteNode(text, item, inner));
                break;
            default:
                if (node.GetValueKind() is JsonValueKind.String)
                {
                    // Round-trip through JSON text so any CLR type a constructed JsonValue wraps (string, char, enum
                    // via a converter, ...) yields the same string the comparer sees.
                    WriteString(text, JsonSerializer.Deserialize<string>(node.ToJsonString())!);
                }
                else
                {
                    text.Append(node.ToJsonString());
                }

                break;
        }
    }

    /// <summary>
    /// Writes one object or array: empty as <c>{}</c>/<c>[]</c> on one line, otherwise each item on its own line one
    /// level deeper, comma-separated, with the closing bracket back at <paramref name="depth"/>.
    /// </summary>
    private static void WriteContainer<T>(StringBuilder text, char open, char close, int depth, IEnumerable<T> items, Action<T, int> writeItem)
    {
        text.Append(open);
        var any = false;
        foreach (var item in items)
        {
            text.Append(any ? ",\n" : "\n");
            text.Append(' ', 2 * (depth + 1));
            writeItem(item, depth + 1);
            any = true;
        }

        if (any)
        {
            text.Append('\n');
            text.Append(' ', 2 * depth);
        }

        text.Append(close);
    }

    /// <summary>Writes a quoted string with Python's <c>ensure_ascii</c> escaping.</summary>
    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                // Everything outside space..tilde is escaped, DEL included. A character above U+FFFF is already two
                // UTF-16 surrogates here, so it comes out as an escaped pair, as Python writes it.
                case < ' ' or > '~': text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: text.Append(c); break;
            }
        }

        text.Append('"');
    }
}
