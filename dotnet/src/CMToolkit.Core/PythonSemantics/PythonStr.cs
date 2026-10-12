using System.Text;

namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// Python's <c>str()</c> of the values the reference puts into UI text (T-9): the App renders them, Core supplies the
/// exact Python forms.
/// </summary>
public static class PythonStr
{
    /// <summary><c>str(bool)</c>: <c>True</c> or <c>False</c>.</summary>
    public static string Of(bool value) => value ? "True" : "False";

    /// <summary><c>str(list[str])</c>: each item's <c>repr</c>, comma-separated in brackets, such as <c>['.mohidden']</c>.</summary>
    public static string Of(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(Repr)) + "]";

    /// <summary><c>repr(str)</c>, with Python's choice of quotes and escapes.</summary>
    public static string Repr(string value) => Settings.PythonText.ReprString(value);

    /// <summary>
    /// <c>text.rjust(width)</c>: padded on the left with spaces to <paramref name="width"/> code points (Python's
    /// <c>len</c>), not UTF-16 units.
    /// </summary>
    public static string RightJustify(string text, int width)
    {
        var length = Length(text);
        return length >= width ? text : new string(' ', width - length) + text;
    }

    /// <summary><c>len(text)</c>: the number of code points.</summary>
    public static int Length(string text) => text.EnumerateRunes().Count();
}
