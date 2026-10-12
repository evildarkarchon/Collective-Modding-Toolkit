using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Tests.Core.PythonSemantics;

/// <summary>
/// <see cref="PythonFloat"/> against values CPython 3.14 printed for <c>str(round(x, 2))</c> (T-3): the HEDR versions
/// the Overview formats, exact ties (which round half to even), and doubles just either side of a tie, where scaling
/// by 100 first would round the wrong way.
/// </summary>
public sealed class PythonFloatTests
{
    [Theory]
    [Trait("Parity", "T-3")]
    [InlineData(0.94, "0.94")]
    [InlineData(1.0, "1.0")]
    [InlineData(68.0, "68.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(0.125, "0.12")]
    [InlineData(0.375, "0.38")]
    [InlineData(2.675, "2.67")]
    [InlineData(1.005, "1.0")]
    [InlineData(123456.785, "123456.79")]
    [InlineData(-0.001, "-0.0")]
    [InlineData(1e20, "1e+20")]
    public void Rounds_to_two_places_and_prints_like_Python(double value, string expected)
        => Assert.Equal(expected, PythonFloat.Repr(PythonFloat.Round(value, 2)));

    [Theory]
    [Trait("Parity", "T-3")]
    [InlineData(new byte[] { 0x33, 0x33, 0x73, 0x3f }, "0.95")]
    [InlineData(new byte[] { 0xd7, 0xa3, 0x70, 0x3f }, "0.94")]
    [InlineData(new byte[] { 0x9a, 0x99, 0xd9, 0x3f }, "1.7")]
    [InlineData(new byte[] { 0xec, 0x78, 0xad, 0x60 }, "1.0000000200408773e+20")]
    public void A_float32_header_version_rounds_as_struct_unpack_then_round_does(byte[] bytes, string expected)
        => Assert.Equal(expected, PythonFloat.Repr(PythonFloat.Round(BitConverter.ToSingle(bytes), 2)));

    [Fact]
    [Trait("Parity", "T-3")]
    public void Infinities_and_nan_pass_through()
    {
        Assert.Equal("inf", PythonFloat.Repr(PythonFloat.Round(double.PositiveInfinity, 2)));
        Assert.Equal("nan", PythonFloat.Repr(PythonFloat.Round(double.NaN, 2)));
    }
}
