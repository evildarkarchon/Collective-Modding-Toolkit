using System.Collections.ObjectModel;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// The game INIs as <c>GameInfo.load_game_inis</c> leaves them (INI-2..INI-4): two section → key → value maps, in
/// Python dict order, and the language they select.
/// </summary>
/// <param name="Settings">
/// <c>game_settings</c>: <c>Fallout4.ini</c> merged with <c>Fallout4Custom.ini</c>, Custom overriding. Section names
/// and keys are lowercased; nothing is trimmed (B-14).
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
    /// <exception cref="System.Text.DecoderFallbackException">An INI isn't valid UTF-8 (INI-3, B-14, T-1).</exception>
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

            Parse(PythonText.ReadUtf8Strict(HostPaths.Resolve(host, path)), name == "Fallout4Prefs.ini" ? prefs : settings);
        }

        var language = Lower(Get(settings, "general", "slanguage") ?? English);
        if (!LanguageCodes.Contains(language, StringComparer.Ordinal))
        {
            language = English;
        }

        return new GameInis(Freeze(settings), Freeze(prefs), language);
    }

    /// <summary>
    /// The hand-rolled parse (INI-3): <c>splitlines()</c>; a line that starts with <c>[</c> and ends with <c>]</c> is a
    /// lowercased section name (a BOM before it makes it a plain line, B-14); otherwise the line splits on its first
    /// <c>=</c> into a lowercased key and a value, neither trimmed, and a line without one is skipped. A section exists
    /// only once a key lands in it, and each file starts under <see cref="NoSection"/>.
    /// </summary>
    private static void Parse(string text, OrderedDictionary<string, OrderedDictionary<string, string>> into)
    {
        var section = NoSection;
        foreach (var line in PythonText.SplitLines(text))
        {
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
            entries[Lower(line[..equals])] = line[(equals + 1)..];
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
