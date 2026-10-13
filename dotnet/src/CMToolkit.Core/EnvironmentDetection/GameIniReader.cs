using System.Collections.ObjectModel;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// The game INIs as <c>GameInfo.load_game_inis</c> leaves them (INI-2..INI-4): two section → key → value maps, in
/// Python dict order, and the language they select.
/// </summary>
/// <param name="Settings">
/// <c>game_settings</c>: <c>Fallout4.ini</c> merged with <c>Fallout4Custom.ini</c>, Custom overriding. Section names
/// and keys are lowercased; lines, keys and values are trimmed (the reference trimmed nothing, B-14, bug #17).
/// </param>
/// <param name="Prefs"><c>game_prefs</c>: <c>Fallout4Prefs.ini</c>, parsed the same way.</param>
/// <param name="Language">The language code, such as <c>en</c> or <c>ptbr</c> (<c>enums.Language</c>).</param>
public sealed record GameInis(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Settings,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Prefs,
    string Language)
{
    /// <summary>
    /// The language-dependent BA2 suffixes (INI-4): <c>main</c>, <c>textures</c>, <c>voices_en</c>, plus
    /// <c>voices_&lt;language&gt;</c> when the language isn't English.
    /// </summary>
    public IReadOnlyList<string> Ba2Suffixes => Language == GameIniReader.English
        ? ["main", "textures", "voices_en"]
        : ["main", "textures", "voices_en", "voices_" + Language];

    /// <summary>
    /// The INIs merged into <see cref="Settings"/> that were looked for, <c>Fallout4.ini</c> then
    /// <c>Fallout4Custom.ini</c>, read or not. The reference kept no record of them; the port names them in the
    /// problems for a missing <c>[Archive]</c> section (deviation, bug #31). Empty when not read from a machine.
    /// </summary>
    public IReadOnlyList<GameIniFile> SettingsFiles { get; init; } = [];
}

/// <summary>One game INI that <see cref="GameIniReader.Read"/> looked for.</summary>
/// <param name="Path">Where it was looked for, in <c>Documents\My Games\Fallout4</c>.</param>
/// <param name="Read">Whether it was a file, and so read.</param>
/// <param name="Sections">The named sections it set at least one key in (not <see cref="GameIniReader.NoSection"/>); 0 when not read.</param>
public sealed record GameIniFile(PythonPath Path, bool Read, int Sections);

/// <summary>Reads the game INIs from <c>Documents\My Games\Fallout4</c> (<c>GameInfo.load_game_inis</c>).</summary>
public static class GameIniReader
{
    /// <summary>The English language code, the default.</summary>
    public const string English = "en";

    /// <summary>The section that lines before any header go under. Not lowercased.</summary>
    public const string NoSection = "NO-SECTION";

    /// <summary>Every <c>enums.Language</c> code, in the enum's order.</summary>
    public static IReadOnlyList<string> LanguageCodes { get; } = ["cn", "de", "en", "es", "esmx", "fr", "it", "ja", "pl", "ptbr", "ru"];

    /// <summary>The INIs, in read order; Prefs goes to its own map.</summary>
    private static readonly string[] IniNames = ["Fallout4.ini", "Fallout4Prefs.ini", "Fallout4Custom.ini"];

    /// <summary>
    /// Reads <c>Fallout4.ini</c>, <c>Fallout4Prefs.ini</c> and <c>Fallout4Custom.ini</c>, in that order, skipping any that
    /// isn't a file (INI-2). Under MO2 these reads go through the VFS, which supplies the profile's INIs (T-12, issue #35).
    /// </summary>
    /// <exception cref="FileNotFoundException">The Documents folder doesn't exist (INI-1).</exception>
    /// <exception cref="IOException">An INI that exists can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">An INI that exists can't be read.</exception>
    public static GameInis Read(IHostEnvironment host)
    {
        var folder = KnownFolders.Get(host, KnownFolder.Documents).Join(@"My Games\Fallout4");
        var settings = new OrderedDictionary<string, OrderedDictionary<string, string>>(StringComparer.Ordinal);
        var prefs = new OrderedDictionary<string, OrderedDictionary<string, string>>(StringComparer.Ordinal);
        var settingsFiles = new List<GameIniFile>();
        foreach (var name in IniNames)
        {
            var path = folder.Join(name);
            var isPrefs = name == "Fallout4Prefs.ini";
            if (!HostPaths.IsFile(host, path))
            {
                if (!isPrefs)
                {
                    settingsFiles.Add(new GameIniFile(path, Read: false, Sections: 0));
                }

                continue;
            }

            var sections = Parse(ReadIniText(HostPaths.Resolve(host, path)), isPrefs ? prefs : settings);
            if (!isPrefs)
            {
                settingsFiles.Add(new GameIniFile(path, Read: true, sections));
            }
        }

        var language = Lower(Get(settings, "general", "slanguage") ?? English);
        if (!LanguageCodes.Contains(language, StringComparer.Ordinal))
        {
            language = English;
        }

        return new GameInis(Freeze(settings), Freeze(prefs), language) { SettingsFiles = settingsFiles };
    }

    /// <summary>The UTF-8 byte order mark, which Notepad and some INI tools write.</summary>
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Decodes one INI: a leading UTF-8 BOM is dropped, the rest is strict UTF-8, and bytes that aren't valid UTF-8 make
    /// the whole file Windows-1252 instead (<see cref="AnsiFallbackText.Decode"/>). The reference read strict UTF-8
    /// only, keeping a BOM as U+FEFF (which broke the first header) and failing at startup on an ANSI file (B-14, bug
    /// #17, fixed).
    /// </summary>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be read.</exception>
    private static string ReadIniText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var body = bytes.AsSpan().StartsWith(Utf8Bom) ? bytes.AsSpan(Utf8Bom.Length) : bytes.AsSpan();
        return AnsiFallbackText.Decode(body);
    }

    /// <summary>
    /// The parse (INI-3): <c>splitlines()</c>, each line trimmed; a line that starts with <c>[</c> and ends with
    /// <c>]</c> is a lowercased section name; otherwise the line splits on its first <c>=</c> into a lowercased key and
    /// a value, both trimmed, and a line without one is skipped. A section exists only once a key lands in it, and each
    /// file starts under <see cref="NoSection"/>. The reference trimmed nothing, so <c>bNVFlexEnable = 1</c> was stored
    /// as <c>"bnvflexenable "</c> and missed, and <c>[Display] </c> wasn't a header (B-14, bug #17, fixed).
    /// </summary>
    /// <returns>
    /// How many named sections (not <see cref="NoSection"/>) this file set a key in, whether or not an earlier file had
    /// them: the count <see cref="GameIniFile.Sections"/> reports.
    /// </returns>
    private static int Parse(string text, OrderedDictionary<string, OrderedDictionary<string, string>> into)
    {
        var section = NoSection;
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in PythonText.SplitLines(text))
        {
            var line = PythonText.Strip(rawLine);
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                // line[1:-1]: a bare "[]" gives the empty section name. A single "[" never gets here, since it doesn't
                // end with "]".
                section = Lower(line[1..^1]);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                continue;
            }

            if (!into.TryGetValue(section, out var entries))
            {
                entries = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                into.Add(section, entries);
            }

            // A repeated key keeps its first position and takes the new value, as a dict assignment does.
            entries[Lower(PythonText.Strip(line[..equals]))] = PythonText.Strip(line[(equals + 1)..]);
            if (section != NoSection)
            {
                named.Add(section);
            }
        }

        return named.Count;
    }

    /// <summary>Python's <c>str.lower()</c>; .NET's invariant lowering differs only for a few special-cased letters (see README residuals).</summary>
    private static string Lower(string value) => value.ToLowerInvariant();

    /// <summary>
    /// <c>game_settings.get(section, {}).get(key)</c> on the maps being built: a setting by its lowercased section and
    /// key, or null.
    /// </summary>
    private static string? Get(OrderedDictionary<string, OrderedDictionary<string, string>> sections, string section, string key)
        => sections.TryGetValue(section, out var entries) && entries.TryGetValue(key, out var value) ? value : null;

    /// <summary>The parsed maps as read-only views, in the same order.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Freeze(
        OrderedDictionary<string, OrderedDictionary<string, string>> sections)
    {
        var frozen = new OrderedDictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (name, entries) in sections)
        {
            frozen.Add(name, new ReadOnlyDictionary<string, string>(entries));
        }

        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(frozen);
    }
}
