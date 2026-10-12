using System.Text;

namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// A hand port of CPython's <c>_csv</c> reader state machine, for the one dialect the Reference Implementation uses:
/// MO2's <c>skip_*</c> values, read with <c>csv.reader((value,), doublequote=False, escapechar="\\",
/// skipinitialspace=True)</c> (MO2-7, <c>mod_manager_info.py</c>). Delimiter <c>,</c>, quote <c>"</c>,
/// <c>QUOTE_MINIMAL</c>, not strict.
/// </summary>
public static class PythonCsv
{
    private const char Delimiter = ',';
    private const char Quote = '"';
    private const char Escape = '\\';

    private enum State
    {
        StartRecord,
        StartField,
        EscapedChar,
        InField,
        InQuotedField,
        EscapeInQuotedField,
    }

    /// <summary>
    /// <c>next(reader)</c> over the single line <paramref name="line"/>: the fields of its one record. An empty line is
    /// an empty record. With <c>doublequote=False</c> a closing quote just ends the quoted part, so <c>"a"b</c> is
    /// <c>ab</c>; an escape at the very end, or an unclosed quote, keep what was read (the escape adds a newline),
    /// because the reader isn't strict.
    /// </summary>
    /// <param name="line">The value, which never contains <c>\r</c> or <c>\n</c> (it comes from one INI line).</param>
    public static IReadOnlyList<string> ReadMo2Row(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var state = State.StartRecord;

        void SaveField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        // Each character, then the end-of-line marker (null) the C reader feeds after every line.
        for (var i = 0; i <= line.Length; i++)
        {
            char? c = i < line.Length ? line[i] : null;
            switch (state)
            {
                case State.StartRecord when c is null:
                    // An empty line is an empty record.
                    break;
                case State.StartRecord:
                case State.StartField:
                    state = State.StartField;
                    if (c is null)
                    {
                        SaveField();
                        state = State.StartRecord;
                    }
                    else if (c == Quote)
                    {
                        state = State.InQuotedField;
                    }
                    else if (c == Escape)
                    {
                        state = State.EscapedChar;
                    }
                    else if (c == ' ')
                    {
                        // skipinitialspace: spaces before a field's first character are dropped.
                    }
                    else if (c == Delimiter)
                    {
                        SaveField();
                    }
                    else
                    {
                        field.Append(c.Value);
                        state = State.InField;
                    }

                    break;
                case State.EscapedChar:
                    // An escape at the end of the line escapes the line break, which the C reader adds as '\n'.
                    field.Append(c ?? '\n');
                    state = State.InField;
                    break;
                case State.InField:
                    if (c is null)
                    {
                        SaveField();
                        state = State.StartRecord;
                    }
                    else if (c == Escape)
                    {
                        state = State.EscapedChar;
                    }
                    else if (c == Delimiter)
                    {
                        SaveField();
                        state = State.StartField;
                    }
                    else
                    {
                        field.Append(c.Value);
                    }

                    break;
                case State.InQuotedField:
                    if (c is null)
                    {
                        // A line break inside quotes continues the field on the next line; there is none.
                    }
                    else if (c == Escape)
                    {
                        state = State.EscapeInQuotedField;
                    }
                    else if (c == Quote)
                    {
                        // doublequote=False: the quote ends the quoted part, and the field carries on unquoted.
                        state = State.InField;
                    }
                    else
                    {
                        field.Append(c.Value);
                    }

                    break;
                case State.EscapeInQuotedField:
                    field.Append(c ?? '\n');
                    state = State.InQuotedField;
                    break;
            }
        }

        // The record is still open only inside quotes or just after an escape, and both cases have read something (an
        // escape always appends a character), so the non-strict reader's end-of-input rule saves the field. CPython's
        // other end-of-input branch, a StopIteration, needs a state this single-line input can't reach.
        if (state != State.StartRecord)
        {
            SaveField();
        }

        return fields;
    }
}
