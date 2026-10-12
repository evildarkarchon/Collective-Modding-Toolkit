using System.Collections.ObjectModel;
using System.Text;
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
    /// <c>game_settings.get(section, {}).get(key)</c>: a setting by its lowercased section and key, or null.
    /// </summary>
    public string? Setting(string section, string key)
        => Settings.TryGetValue(section, out var entries) && entries.TryGetValue(key, out var value) ? value : null;
}

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
        foreach (var name in IniNames)
        {
            var path = folder.Join(name);
            if (!HostPaths.IsFile(host, path))
            {
                continue;
            }

            Parse(ReadIniText(HostPaths.Resolve(host, path)), name == "Fallout4Prefs.ini" ? prefs : settings);
        }

        var language = Lower(Get(settings, "general", "slanguage") ?? English);
        if (!LanguageCodes.Contains(language, StringComparer.Ordinal))
        {
            language = English;
        }

        return new GameInis(Freeze(settings), Freeze(prefs), language);
    }

    /// <summary>The UTF-8 byte order mark, which Notepad and some INI tools write.</summary>
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Windows-1252, for INIs that aren't UTF-8: the game reads its INIs through the ANSI profile API, so editors save
    /// them in the ANSI code page. Fixed rather than the machine's code page, so a read never depends on the host's
    /// locale; the settings the toolkit reads are ASCII either way.
    /// </summary>
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
        ?? throw new InvalidOperationException("Code page 1252 isn't available.");

    /// <summary>
    /// Decodes one INI: a leading UTF-8 BOM is dropped, the rest is strict UTF-8, and bytes that aren't valid UTF-8 make
    /// the whole file Windows-1252 instead. The reference read strict UTF-8 only, keeping a BOM as U+FEFF (which broke
    /// the first header) and failing at startup on an ANSI file (B-14, bug #17, fixed).
    /// </summary>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be read.</exception>
    private static string ReadIniText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var body = bytes.AsSpan().StartsWith(Utf8Bom) ? bytes.AsSpan(Utf8Bom.Length) : bytes.AsSpan();
        try
        {
            return PythonText.DecodeUtf8Strict(body);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: an ANSI file, read as the game would read it.
            return Windows1252.GetString(body);
        }
    }

    /// <summary>
    /// The parse (INI-3): <c>splitlines()</c>, each line trimmed; a line that starts with <c>[</c> and ends with
    /// <c>]</c> is a lowercased section name; otherwise the line splits on its first <c>=</c> into a lowercased key and
    /// a value, both trimmed, and a line without one is skipped. A section exists only once a key lands in it, and each
    /// file starts under <see cref="NoSection"/>. The reference trimmed nothing, so <c>bNVFlexEnable = 1</c> was stored
    /// as <c>"bnvflexenable "</c> and missed, and <c>[Display] </c> wasn't a header (B-14, bug #17, fixed).
    /// </summary>
    private static void Parse(string text, OrderedDictionary<string, OrderedDictionary<string, string>> into)
    {
        var section = NoSection;
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
        }
    }

    /// <summary>Python's <c>str.lower()</c>; .NET's invariant lowering differs only for a few special-cased letters (see README residuals).</summary>
    private static string Lower(string value) => value.ToLowerInvariant();

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
