using System.Text;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.Settings;

/// <summary>
/// <c>settings.json</c>: loading, validating and writing it exactly as <c>app_settings.AppSettings</c> does (SET-1,
/// SET-3..SET-6, T-10). Core owns the format; only the App calls <see cref="Save"/>, and no other Core operation touches
/// the file (the Core seams resolution on issue #7).
/// </summary>
/// <remarks>
/// Every message is logged with the reference's <c>Settings : </c> prefix and level (LOG-2). Which of them reach
/// <c>cm-toolkit.log</c> is the log file's business: during startup it still filters at INFO (SHELL-2).
/// </remarks>
public static class SettingsFile
{
    /// <summary>One key of <c>AppSettingsDict</c>, in file order, and the values a <c>Literal</c> key allows (null for a bool).</summary>
    private sealed record Key(string Name, string[]? Allowed);

    private static readonly string[] LogLevels = ["DEBUG", "INFO", "WARNING", "ERROR"];
    private static readonly string[] UpdateSources = ["nexus", "github", "both", "none"];

    /// <summary>The keys in <c>AppSettingsDict</c> order, which is also the file's order (SET-1).</summary>
    private static readonly Key[] Keys =
    [
        new("log_level", LogLevels),
        new("update_source", UpdateSources),
        new("scanner_OverviewIssues", null),
        new("scanner_Errors", null),
        new("scanner_WrongFormat", null),
        new("scanner_LoosePrevis", null),
        new("scanner_JunkFiles", null),
        new("scanner_ProblemOverrides", null),
        new("scanner_RaceSubgraphs", null),
        new("downgrader_keep_backups", null),
        new("downgrader_delete_deltas", null),
    ];

    /// <summary>UTF-8 that throws on bad bytes and keeps a BOM, like Python's <c>read_text("utf-8")</c> (T-1).</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The settings as <c>(key, value)</c> pairs in file order, each value a <see cref="string"/> (the two
    /// <c>Literal</c> keys) or a <see cref="bool"/>: the Python dict the reference holds and writes.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object>> Entries(AppSettings settings)
    {
        var values = ToValues(settings);
        return [.. Keys.Select((key, i) => new KeyValuePair<string, object>(key.Name, values[i]))];
    }

    /// <summary>
    /// Loads <paramref name="path"/> as <c>AppSettings.__init__</c> does, resaving it when it was missing (SET-3),
    /// unreadable or not a JSON object (SET-4: everything resets), or when any key was added, dropped or reset (SET-5).
    /// A valid value is kept; an invalid one keeps its default. Never throws.
    /// </summary>
    /// <param name="path">The settings file; the App passes <see cref="AppFolderFiles.Settings"/> of its own folder (bug #36, fixed).</param>
    /// <param name="downloadSource">The archive's Download Source, which seeds the default <c>update_source</c>.</param>
    /// <param name="logger">Receives the <c>Settings : …</c> lines, at the reference's levels.</param>
    /// <returns>The settings in effect.</returns>
    public static AppSettings Load(string path, DownloadSource downloadSource, ILogger logger)
    {
        var defaults = AppSettings.Defaults(downloadSource);
        var name = Path.GetFileName(path);

        // Path.is_file(): a folder of the same name counts as missing, and the save that follows then fails.
        if (!File.Exists(path))
        {
            logger.Write(LogLevel.Information, $"Settings : {name} not found; using defaults.");
            Save(path, defaults, logger);
            return defaults;
        }

        var values = ToValues(defaults);
        var resave = false;
        PythonDict? json = null;
        try
        {
            // Python reads in text mode, which turns CR and CRLF into LF first. That can't change the outcome: outside
            // strings both are whitespace, and inside strings both are refused as control characters.
            var text = StrictUtf8.GetString(File.ReadAllBytes(path));
            json = PythonJson.Loads(text) as PythonDict
                   ?? throw new InvalidDataException($"{name} doesn't contain a JSON object.");
        }
        catch (Exception ex)
        {
            // A bare except in the reference: any failure to read or parse the file resets every setting.
            logger.Write(LogLevel.Error, $"Settings : Failed to load {name}. Settings will be reset.", ex);
            resave = true;
        }

        if (json is not null)
        {
            resave |= Validate(json, values, logger);
        }

        var settings = FromValues(values);
        if (resave)
        {
            Save(path, settings, logger);
        }

        return settings;
    }

    /// <summary>
    /// Writes <paramref name="settings"/> to <paramref name="path"/> (SET-6): <see cref="Format"/> with CRLF line
    /// endings, as Python's text mode writes it on Windows. A failure is logged and swallowed, leaving the file as it
    /// was if it couldn't be opened.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <param name="settings">What to write.</param>
    /// <param name="logger">Receives <c>Settings : Saving …</c> (DEBUG) and any failure (ERROR, with the exception).</param>
    public static void Save(string path, AppSettings settings, ILogger logger)
    {
        var name = Path.GetFileName(path);
        logger.Write(LogLevel.Debug, $"Settings : Saving {name}");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(Format(settings).Replace("\n", "\r\n", StringComparison.Ordinal));

            // FileMode.Create is CREATE_ALWAYS, as the CRT's open(..., "w") is, so it is refused for a read-only file
            // and for a hidden or system one too, as the reference's save is.
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes);
        }
        catch (Exception ex)
        {
            logger.Write(LogLevel.Error, $"Settings : Failed to save {name}", ex);
        }
    }

    /// <summary>
    /// The text <c>json.dump(settings, f, indent="\t")</c> plus <c>f.write("\n")</c> produces, with LF line endings
    /// (T-10): one tab-indented key per line, <c>": "</c> after each key, no trailing comma, every non-ASCII or control
    /// character escaped as <c>\uXXXX</c>, and a final newline. <see cref="Save"/> writes it with CRLF.
    /// </summary>
    public static string Format(AppSettings settings)
    {
        var builder = new StringBuilder("{\n");
        var entries = Entries(settings);
        for (var i = 0; i < entries.Count; i++)
        {
            builder.Append('\t');
            AppendAsciiJsonString(builder, entries[i].Key);
            builder.Append(": ");
            switch (entries[i].Value)
            {
                case bool b:
                    builder.Append(b ? "true" : "false");
                    break;
                case string s:
                    AppendAsciiJsonString(builder, s);
                    break;
            }

            builder.Append(i < entries.Count - 1 ? ",\n" : "\n");
        }

        return builder.Append("}\n").ToString();
    }

    /// <summary>
    /// The per-key half of <c>AppSettings.__init__</c>: reports keys the file lacks, then walks the file's keys in
    /// order, keeping valid values in <paramref name="values"/> and logging the rest exactly as the reference does.
    /// </summary>
    /// <returns>Whether the file needs resaving.</returns>
    private static bool Validate(PythonDict json, object[] values, ILogger logger)
    {
        var resave = false;
        var present = json.Items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var added = Keys.Where(key => !present.Contains(key.Name)).Select(key => key.Name).ToList();
        if (added.Count > 0)
        {
            logger.Write(LogLevel.Information, $"Settings : Adding new settings to JSON: {string.Join(", ", added)}");
            resave = true;
        }

        foreach (var (name, value) in json.Items)
        {
            var index = Array.FindIndex(Keys, key => key.Name == name);
            if (index < 0)
            {
                logger.Write(LogLevel.Error, $"Settings : Unknown setting '{name}' will be removed.");
                resave = true;
                continue;
            }

            var key = Keys[index];
            if (key.Allowed is not null)
            {
                // `v in get_args(Literal[...])`: only a string equal to one of the allowed values matches.
                if (value is PythonStr text && key.Allowed.Contains(text.Value, StringComparer.Ordinal))
                {
                    logger.Write(LogLevel.Debug, $"Settings : '{name}' is correct type ({value.TypeName})");
                    values[index] = text.Value;
                }
                else
                {
                    logger.Write(
                        LogLevel.Error,
                        $"Settings : '{name}' has invalid value '{value.Str()}'. Reset to '{PythonStrOf(values[index])}'");
                    resave = true;
                }
            }
            else if (value is PythonBool flag)
            {
                // `type(v) is bool`: 1 and 0 are ints, not bools, and are reset.
                logger.Write(LogLevel.Debug, $"Settings : '{name}' is correct type ({value.TypeName})");
                values[index] = flag.Value;
            }
            else
            {
                logger.Write(
                    LogLevel.Error,
                    $"Settings : '{name}' has invalid type ({value.TypeName}) '{value.Str()}'. Reset to '{PythonStrOf(values[index])}'");
                resave = true;
            }
        }

        return resave;
    }

    /// <summary><c>str()</c> of a stored value: the string itself, or <c>True</c>/<c>False</c>.</summary>
    private static string PythonStrOf(object value) => value switch
    {
        bool b => b ? "True" : "False",
        _ => (string)value,
    };

    private static object[] ToValues(AppSettings settings) =>
    [
        LogLevels[(int)settings.LogLevel],
        UpdateSources[(int)settings.UpdateSource],
        settings.ScannerOverviewIssues,
        settings.ScannerErrors,
        settings.ScannerWrongFormat,
        settings.ScannerLoosePrevis,
        settings.ScannerJunkFiles,
        settings.ScannerProblemOverrides,
        settings.ScannerRaceSubgraphs,
        settings.DowngraderKeepBackups,
        settings.DowngraderDeleteDeltas,
    ];

    private static AppSettings FromValues(object[] values) => new()
    {
        LogLevel = (SettingsLogLevel)Array.IndexOf(LogLevels, (string)values[0]),
        UpdateSource = (UpdateSource)Array.IndexOf(UpdateSources, (string)values[1]),
        ScannerOverviewIssues = (bool)values[2],
        ScannerErrors = (bool)values[3],
        ScannerWrongFormat = (bool)values[4],
        ScannerLoosePrevis = (bool)values[5],
        ScannerJunkFiles = (bool)values[6],
        ScannerProblemOverrides = (bool)values[7],
        ScannerRaceSubgraphs = (bool)values[8],
        DowngraderKeepBackups = (bool)values[9],
        DowngraderDeleteDeltas = (bool)values[10],
    };

    /// <summary>
    /// Python's <c>py_encode_basestring_ascii</c> (<c>ensure_ascii=True</c>): <c>"</c>, <c>\</c>, <c>\n</c>,
    /// <c>\r</c>, <c>\t</c>, <c>\b</c> and <c>\f</c> get short escapes; anything else outside space..<c>~</c> becomes a
    /// lowercase <c>\uXXXX</c>, a character beyond the BMP as its surrogate pair. Unlike <c>System.Text.Json</c>, it
    /// leaves <c>+</c>, <c>&lt;</c>, <c>'</c> and friends alone.
    /// </summary>
    private static void AppendAsciiJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                case '\b':
                    builder.Append(@"\b");
                    break;
                case '\f':
                    builder.Append(@"\f");
                    break;
                case >= ' ' and <= '~':
                    builder.Append(c);
                    break;
                default:
                    builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    break;
            }
        }

        builder.Append('"');
    }
}
