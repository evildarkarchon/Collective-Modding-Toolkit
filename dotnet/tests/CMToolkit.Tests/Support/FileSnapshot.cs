using System.Text;
using System.Text.Json.Nodes;

namespace CMToolkit.Tests.Support;

/// <summary>The driver's <c>file_snapshot</c>, for goldens that record a file an operation left behind.</summary>
public static class FileSnapshot
{
    /// <summary>
    /// <see langword="null"/> if <paramref name="path"/> isn't a file, <c>{"text"}</c> if its bytes are strict UTF-8,
    /// else <c>{"base64"}</c>. Line endings and every other byte are kept, so a writer is compared byte-exact.
    /// </summary>
    public static JsonObject? Of(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        try
        {
            return new JsonObject { ["text"] = new UTF8Encoding(false, true).GetString(bytes) };
        }
        catch (DecoderFallbackException)
        {
            return new JsonObject { ["base64"] = Convert.ToBase64String(bytes) };
        }
    }
}
