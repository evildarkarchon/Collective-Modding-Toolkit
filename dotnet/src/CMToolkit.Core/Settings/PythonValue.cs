using System.Globalization;
using System.Numerics;
using System.Text;

namespace CMToolkit.Core.Settings;

/// <summary>
/// A value as Python's <c>json.loads</c> returns it, kept with its Python type so that the reference's log messages can
/// be rendered exactly: <c>type(v).__name__</c> and <c>str(v)</c> (SET-5, T-9).
/// </summary>
internal abstract record PythonValue
{
    /// <summary><c>type(v).__name__</c>: <c>str</c>, <c>int</c>, <c>float</c>, <c>bool</c>, <c>NoneType</c>, <c>list</c> or <c>dict</c>.</summary>
    public abstract string TypeName { get; }

    /// <summary>Python's <c>str(v)</c>, which is what <c>%s</c> in a log message renders.</summary>
    public abstract string Str();

    /// <summary>Python's <c>repr(v)</c>, which containers use for their items. Only strings differ from <see cref="Str"/>.</summary>
    public virtual string Repr() => Str();
}

/// <summary>A JSON string.</summary>
internal sealed record PythonStr(string Value) : PythonValue
{
    public override string TypeName => "str";

    public override string Str() => Value;

    public override string Repr() => PythonText.ReprString(Value);
}

/// <summary>A JSON number without a fraction or exponent, which Python reads as an arbitrary-precision <c>int</c>.</summary>
internal sealed record PythonInt(BigInteger Value) : PythonValue
{
    public override string TypeName => "int";

    public override string Str() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A JSON number with a fraction or exponent, or one of the <c>NaN</c>/<c>Infinity</c> tokens.</summary>
internal sealed record PythonFloat(double Value) : PythonValue
{
    public override string TypeName => "float";

    public override string Str() => PythonText.FloatRepr(Value);
}

/// <summary><c>true</c> or <c>false</c>.</summary>
internal sealed record PythonBool(bool Value) : PythonValue
{
    public override string TypeName => "bool";

    public override string Str() => Value ? "True" : "False";
}

/// <summary><c>null</c>.</summary>
internal sealed record PythonNone : PythonValue
{
    public static readonly PythonNone Instance = new();

    public override string TypeName => "NoneType";

    public override string Str() => "None";
}

/// <summary>A JSON array.</summary>
internal sealed record PythonList(IReadOnlyList<PythonValue> Items) : PythonValue
{
    public override string TypeName => "list";

    public override string Str() => "[" + string.Join(", ", Items.Select(i => i.Repr())) + "]";
}

/// <summary>
/// A JSON object as a Python <c>dict</c>: insertion-ordered, and a repeated key keeps its first position but takes its
/// last value.
/// </summary>
internal sealed record PythonDict(IReadOnlyList<KeyValuePair<string, PythonValue>> Items) : PythonValue
{
    public override string TypeName => "dict";

    public override string Str()
    {
        var builder = new StringBuilder("{");
        for (var i = 0; i < Items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(PythonText.ReprString(Items[i].Key)).Append(": ").Append(Items[i].Value.Repr());
        }

        return builder.Append('}').ToString();
    }
}
