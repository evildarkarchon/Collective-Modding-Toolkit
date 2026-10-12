using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Tests.Core.PythonSemantics;

/// <summary>
/// T-7: <c>pathlib.Path</c> on Windows collapses doubled separators, drops <c>.</c> segments and joins a rooted
/// right-hand side onto the left side's drive, unlike <see cref="Path.Combine(string, string)"/>. The expected strings
/// were produced by CPython 3.14's <c>str(PureWindowsPath(...))</c>.
/// </summary>
[Trait("Parity", "T-7")]
public sealed class PythonPathTests
{
    [Theory]
    [InlineData("", ".")]
    [InlineData(".", ".")]
    [InlineData("a", "a")]
    [InlineData("a/b", "a\\b")]
    [InlineData("a//b/", "a\\b")]
    [InlineData("./a/./b/.", "a\\b")]
    [InlineData("a/../b", "a\\..\\b")]
    [InlineData("C:", "C:")]
    [InlineData("C:foo", "C:foo")]
    [InlineData("C:/", "C:\\")]
    [InlineData("C:\\\\Games\\\\Fallout 4", "C:\\Games\\Fallout 4")]
    [InlineData("C://x//y//", "C:\\x\\y")]
    [InlineData("/a/b", "\\a\\b")]
    [InlineData("\\a", "\\a")]
    [InlineData("//server/share", "\\\\server\\share\\")]
    [InlineData("//server/share/dir//x", "\\\\server\\share\\dir\\x")]
    [InlineData("//server", "\\\\server")]
    [InlineData("\\\\?\\C:\\x", "\\\\?\\C:\\x")]
    [InlineData("//?/UNC/server/share/x", "\\\\?\\UNC\\server\\share\\x")]
    [InlineData("1:foo", "1:foo")]
    [InlineData("%BASE_DIR%/webcache", "%BASE_DIR%\\webcache")]
    [InlineData("X:\\..\\downloads", "X:\\..\\downloads")]
    [InlineData("a /b ", "a \\b ")]
    public void ToString_matches_str_of_a_path(string raw, string expected)
        => Assert.Equal(expected, new PythonPath(raw).ToString());

    [Theory]
    [InlineData("C:\\MO2", "mods", "C:\\MO2\\mods")]
    [InlineData("C:\\MO2", "/mods", "C:\\mods")]
    [InlineData("C:\\MO2", "\\mods", "C:\\mods")]
    [InlineData("C:\\MO2", "", "C:\\MO2")]
    [InlineData("C:\\MO2", "D:\\x", "D:\\x")]
    [InlineData("C:\\MO2", "D:x", "D:x")]
    [InlineData("C:\\MO2", "c:x", "c:\\MO2\\x")]
    [InlineData("C:\\MO2\\", "x", "C:\\MO2\\x")]
    [InlineData("C:", "x", "C:x")]
    [InlineData("C:", "/x", "C:\\x")]
    [InlineData("", "x", "x")]
    [InlineData("a", "b", "a\\b")]
    [InlineData("a", "/b", "\\b")]
    [InlineData("//server/share", "x", "\\\\server\\share\\x")]
    [InlineData("//server/share", "/x", "\\\\server\\share\\x")]
    [InlineData("X:\\\\Modding\\\\\\\\MO2\\", "/../downloads", "X:\\..\\downloads")]
    [InlineData("X:/Modding/MO2", "profiles", "X:\\Modding\\MO2\\profiles")]
    [InlineData("C:\\MO2", "/a=b", "C:\\a=b")]
    [InlineData("C:\\MO2", "//server/share/x", "\\\\server\\share\\x")]
    [InlineData(".", "x", "x")]
    [InlineData("C:\\x", ".", "C:\\x")]
    public void Join_matches_the_slash_operator(string left, string right, string expected)
        => Assert.Equal(expected, new PythonPath(left).Join(right).ToString());

    /// <summary>B-6: a <c>%BASE_DIR%/</c> value with its placeholder removed is rooted, so it lands on the drive root.</summary>
    [Fact]
    [Trait("Parity", "B-6")]
    public void A_rooted_value_joined_onto_the_base_lands_on_the_drive_root()
        => Assert.Equal("D:\\mods", new PythonPath("D:\\MO2").Join("/mods").ToString());

    [Theory]
    [InlineData("C:\\MO2\\ModOrganizer.exe", "C:\\MO2")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("a", ".")]
    [InlineData("a/b", "a")]
    [InlineData("", ".")]
    [InlineData("//server/share/x", "\\\\server\\share\\")]
    [InlineData("C:x", "C:")]
    public void Parent_matches_the_parent_property(string raw, string expected)
        => Assert.Equal(expected, new PythonPath(raw).Parent.ToString());
}
