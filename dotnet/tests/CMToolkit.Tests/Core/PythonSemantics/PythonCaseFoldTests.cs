using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Tests.Core.PythonSemantics;

/// <summary>
/// <see cref="PythonCaseFold"/> against <c>str.casefold()</c> of CPython 3.14.5 (Unicode 16.0.0), the Archive Patcher's
/// name filter (AP-2). Expected values were printed by that interpreter; each case is one where .NET's lowercasing
/// gives something else.
/// </summary>
public sealed class PythonCaseFoldTests
{
    [Theory]
    [Trait("Parity", "AP-2")]
    [InlineData("ABC", "abc")]
    [InlineData("BOSS", "boss")]
    [InlineData("\u00DF", "ss")]
    [InlineData("\u1E9E", "ss")]
    [InlineData("Stra\u00DFe - Main.ba2", "strasse - main.ba2")]
    [InlineData("\u03C2", "\u03C3")]
    [InlineData("\u03A3", "\u03C3")]
    [InlineData("\uFB01", "fi")]
    [InlineData("\uAB70", "\u13A0")]
    [InlineData("\u0130", "i\u0307")]
    [InlineData("\u00B5", "\u03BC")]
    [InlineData("\U00010400", "\U00010428")]
    [InlineData("\u1F88", "\u1F00\u03B9")]
    [InlineData("", "")]
    [InlineData("already folded", "already folded")]
    public void Folds_like_python(string text, string expected)
    {
        Assert.Equal(expected, PythonCaseFold.Of(text));
    }

    [Fact]
    public void A_lone_surrogate_is_kept_as_it_is()
    {
        // Python keeps a lone surrogate (from surrogateescape) unchanged; a .NET string can hold one too.
        Assert.Equal("a\uD800b", PythonCaseFold.Of("A\uD800B"));
    }
}
