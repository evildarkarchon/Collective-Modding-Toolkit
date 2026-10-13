using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Tests.Core.PythonSemantics;

/// <summary>
/// MO2-7: <c>skip_*</c> values go through <c>next(csv.reader((value,), doublequote=False, escapechar="\\",
/// skipinitialspace=True))</c>. The expected rows were produced by CPython 3.14's <c>csv</c> module.
/// </summary>
[Trait("Parity", "MO2-7")]
public sealed class PythonCsvTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a,b", new[] { "a", "b" })]
    [InlineData("a, b", new[] { "a", "b" })]
    [InlineData("  a ,  b  ", new[] { "a ", "b  " })]
    [InlineData(",", new[] { "", "" })]
    [InlineData(" ", new[] { "" })]
    [InlineData("   ", new[] { "" })]
    [InlineData(" , ", new[] { "", "" })]
    [InlineData("\"a,b\"", new[] { "a,b" })]
    [InlineData(" \"a,b\" ,c", new[] { "a,b ", "c" })]
    [InlineData("\"a\"b,c", new[] { "ab", "c" })]
    [InlineData("\"open", new[] { "open" })]
    [InlineData("\"open\\", new[] { "open\u000A" })]
    [InlineData("a\\,b", new[] { "a,b" })]
    [InlineData("a\\", new[] { "a\u000A" })]
    [InlineData("a\\\\b", new[] { "a\\b" })]
    [InlineData("\"q\\\"x\"", new[] { "q\"x" })]
    [InlineData("\"q\"\"x\"", new[] { "q\"x\"" })]
    [InlineData("('.mohidden',)", new[] { "('.mohidden'", ")" })]
    [InlineData("set()", new[] { "set()" })]
    [InlineData(".mohidden, .BAK", new[] { ".mohidden", ".BAK" })]
    [InlineData("x,\"\",y", new[] { "x", "", "y" })]
    [InlineData("a,\"b", new[] { "a", "b" })]
    [InlineData("\"", new[] { "" })]
    [InlineData("a,,b", new[] { "a", "", "b" })]
    [InlineData("a ,b", new[] { "a ", "b" })]
    [InlineData("\u0000", new[] { "\u0000" })]
    [InlineData("a\u0009b, c", new[] { "a\u0009b", "c" })]
    [InlineData(" \"x\"", new[] { "x" })]
    public void ReadRow_matches_the_csv_module(string line, string[] expected)
        => Assert.Equal(expected, PythonCsv.ReadMo2Row(line));

    /// <summary>
    /// The <c>csv</c> module on the <c>str()</c> of a tuple and a set (MO2-7). This is how the reference corrupted MO2's
    /// default skip lists (B-5); the port no longer parses the defaults at all (bug #24, fixed), so this is now only the
    /// CSV rule.
    /// </summary>
    [Fact]
    [Trait("Parity", "MO2-7")]
    public void The_python_default_reprs_parse_to_garbage()
    {
        Assert.Equal(["('.mohidden'", ")"], PythonCsv.ReadMo2Row("('.mohidden',)"));
        Assert.Equal(["set()"], PythonCsv.ReadMo2Row("set()"));
    }
}
