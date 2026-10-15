using CMToolkit.Core.Updates;

namespace CMToolkit.Tests.Core.Updates;

/// <summary>
/// UPD-3 and T-4: <see cref="Pep440Version"/> against <c>packaging.version.Version</c> 25.0, the comparer the
/// reference's update check uses. Every expected verdict below was printed by packaging itself, not derived by hand.
/// </summary>
public sealed class Pep440VersionTests
{
    [Theory]
    [Trait("Parity", "UPD-3")]
    [Trait("Parity", "T-4")]
    [InlineData("0.7.0", "0.6.2-dev", 1)]
    [InlineData("0.6.2", "0.6.2-dev", 1)]
    [InlineData("0.6.2.dev0", "0.6.2-dev", 0)]
    [InlineData("0.6.1", "0.6.2-dev", -1)]
    [InlineData("v0.7.0", "0.6.2-dev", 1)]
    [InlineData(" V0.7.0-PRE.2 ", "0.6.2-dev", 1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1.0a1", "1.0.dev5", 1)]
    [InlineData("1.0rc1", "1.0c1", 0)]
    [InlineData("1.0alpha", "1.0a0", 0)]
    [InlineData("1.0b2", "1.0rc1", -1)]
    [InlineData("1.0.post1", "1.0", 1)]
    [InlineData("1.0-1", "1.0.post1", 0)]
    [InlineData("1.0r", "1.0.post0", 0)]
    [InlineData("1.0.post1.dev1", "1.0.post1", -1)]
    [InlineData("1.0a1.post1", "1.0a1", 1)]
    [InlineData("1!0.1", "2.0", 1)]
    [InlineData("1.0+local", "1.0", 1)]
    [InlineData("1.0+abc.2", "1.0+abc.10", -1)]
    [InlineData("1.0+2", "1.0+abc", 1)]
    [InlineData("1.0+abc", "1.0+abc.0", -1)]
    [InlineData("1.0+ABC", "1.0+abc", 0)]
    [InlineData("1.0_dev_3", "1.0.dev3", 0)]
    [InlineData("1.0-dev-1", "1.0.dev1", 0)]
    [InlineData("01.002", "1.2", 0)]
    [InlineData("1.0.0.0.0", "1", 0)]
    [InlineData("0", "0.0.0", 0)]
    [InlineData("100000000000000000000000.1", "99999999999999999999999.9", 1)]
    [InlineData("1.0dev", "1.0.dev0", 0)]
    [InlineData("1.0.preview3", "1.0rc3", 0)]
    [InlineData("1.0-rev2", "1.0.post2", 0)]
    [InlineData("1.0beta", "1.0b", 0)]
    [InlineData("1.0a1.dev2", "1.0a1", -1)]
    [InlineData("1.0.post1", "1.0a1", 1)]
    [InlineData("\t1.0\n", "1.0", 0)]
    [InlineData("1.0\u3000", "1.0", 0)]
    public void Versions_compare_as_packaging_compares_them(string left, string right, int expected)
    {
        var comparison = Pep440Version.Parse(left).CompareTo(Pep440Version.Parse(right));

        Assert.Equal(expected, Math.Sign(comparison));
        Assert.Equal(expected, -Math.Sign(Pep440Version.Parse(right).CompareTo(Pep440Version.Parse(left))));
    }

    [Theory]
    [Trait("Parity", "T-4")]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.0.")]
    [InlineData(".1")]
    [InlineData("1..0")]
    [InlineData("1.0+")]
    [InlineData("1.0+a..b")]
    [InlineData("v")]
    [InlineData("vv1.0")]
    [InlineData("1.0 beta")]
    [InlineData("None")]
    [InlineData("True")]
    [InlineData("1.0.post1.post2")]
    [InlineData("1e+20")]
    [InlineData("\u0661.\u0660")]
    [InlineData("1.0a1b2")]
    [InlineData("1!2!3")]
    [InlineData("+local")]
    [InlineData("1.0-")]
    [InlineData("1.0--1")]
    public void Text_packaging_rejects_is_an_invalid_version(string text)
    {
        Assert.Throws<InvalidVersionException>(() => Pep440Version.Parse(text));
    }

    [Fact]
    public void Python_case_folds_are_matched_but_lowered_as_python_lowers_them()
    {
        // packaging 25.0: re.IGNORECASE lets these match [a-z], i and s; str.lower() then leaves U+0130 as "i" plus a
        // combining dot (so not "i"), turns the Kelvin sign into "k", and keeps a spelling like "previ\u0307ew" as its
        // own pre-release letter rather than "rc".
        Assert.NotEqual(0, Pep440Version.Parse("1.0+\u0130").CompareTo(Pep440Version.Parse("1.0+i")));
        Assert.Equal(0, Pep440Version.Parse("1.0+\u212A").CompareTo(Pep440Version.Parse("1.0+k")));
        Assert.NotEqual(0, Pep440Version.Parse("1.0prev\u0130ew1").CompareTo(Pep440Version.Parse("1.0rc1")));
        Assert.True(Pep440Version.Parse("1.0po\u017Ft1").CompareTo(Pep440Version.Parse("1.0")) > 0);
    }

    [Fact]
    public void A_number_past_pythons_int_digit_limit_is_not_an_invalid_version()
    {
        // int() raises ValueError past sys.get_int_max_str_digits() (4300), which except InvalidVersion doesn't catch.
        var ex = Assert.ThrowsAny<FormatException>(() => Pep440Version.Parse("1." + new string('9', 4301)));
        Assert.IsNotType<InvalidVersionException>(ex);
        Assert.Equal(1, Pep440Version.Parse("1." + new string('9', 4300)).CompareTo(Pep440Version.Parse("1")));
    }
}
