using System.Text;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.PythonSemantics;

/// <summary>
/// T-1: the Reference Implementation reads INIs with <c>read_text("utf-8")</c> (strict, BOM kept) and splits them with
/// <c>str.splitlines()</c>. The expected values were produced by CPython 3.14 itself, not derived from the port.
/// </summary>
[Trait("Parity", "T-1")]
public sealed class PythonTextTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a\u000A", new[] { "a" })]
    [InlineData("a\u000D\u000Ab", new[] { "a", "b" })]
    [InlineData("a\u000D\u000Db", new[] { "a", "", "b" })]
    [InlineData("a\u000A\u000Ab\u000A", new[] { "a", "", "b" })]
    [InlineData("\u000A", new[] { "" })]
    [InlineData("a\u000Bb\u000Cc\u001Cd\u001De\u001Ef\u0085g\u2028h\u2029i", new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" })]
    [InlineData("a\u001Fb\u0000c\u0009d", new[] { "a\u001Fb\u0000c\u0009d" })]
    [InlineData("\uFEFF[General]\u000D\u000Ax=1", new[] { "\uFEFF[General]", "x=1" })]
    [InlineData("a\u000D", new[] { "a" })]
    [InlineData("\u000D\u000A\u000D\u000A", new[] { "", "" })]
    public void SplitLines_matches_str_splitlines(string text, string[] expected)
        => Assert.Equal(expected, PythonText.SplitLines(text));

    [Theory]
    [InlineData("  a  ", "a")]
    [InlineData("\u001C a \u001F", "a")]
    [InlineData("\u0085a\u00A0", "a")]
    [InlineData("\u2028a\u3000", "a")]
    [InlineData("\u200Ba\u200B", "\u200Ba\u200B")]
    [InlineData("\u0000a", "\u0000a")]
    [InlineData("\u0009a\u000A\u000D\u000B\u000C", "a")]
    [InlineData("\u180Ea", "\u180Ea")]
    public void Strip_matches_str_strip(string text, string expected)
        => Assert.Equal(expected, PythonText.Strip(text));

    [Fact]
    public void A_utf8_bom_is_kept_as_U_FEFF()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteBytes("a.ini", [0xEF, 0xBB, 0xBF, (byte)'[', (byte)'x', (byte)']']);

        Assert.Equal("\uFEFF[x]", PythonText.ReadUtf8Strict(path));
    }

    [Theory]
    [InlineData(new byte[] { 0x43, 0x61, 0x66, 0xE9 })] // Latin-1 "Café"
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })] // an encoded surrogate, which Python's codec rejects too
    [InlineData(new byte[] { 0xC0, 0xAF })] // an overlong encoding
    public void Undecodable_bytes_throw(byte[] bytes)
    {
        using var dir = new TempDirectory();
        var path = dir.WriteBytes("a.ini", bytes);

        Assert.Throws<DecoderFallbackException>(() => PythonText.ReadUtf8Strict(path));
    }
}
