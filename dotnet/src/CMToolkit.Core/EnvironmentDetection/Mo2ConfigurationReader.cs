using System.Security;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// Finds and reads MO2's <c>ModOrganizer.ini</c>: the mod manager part of <c>GameInfo.find_path</c> (MO2-1..MO2-3)
/// around a hand port of <c>ModManagerInfo.read_mo2_ini</c> (MO2-4..MO2-8). <c>[customExecutables]</c> is skipped,
/// since nothing reads what the reference collects from it (MO2-10).
/// </summary>
public static class Mo2ConfigurationReader
{
    private const string BaseDirPlaceholder = "%BASE_DIR%";
    private const string ByteArrayPrefix = "@ByteArray(";
    private const string General = "[General]";
    private const string SettingsSection = "[Settings]";
    private const string CustomExecutables = "[customExecutables]";

    /// <summary>The keys <c>read_mo2_ini</c> takes from each tracked section.</summary>
    private static readonly Dictionary<string, HashSet<string>> TrackedKeys = new(StringComparer.Ordinal)
    {
        [General] = new(StringComparer.Ordinal) { "gameName", "gamePath", "selected_profile" },
        [SettingsSection] = new(StringComparer.Ordinal)
        {
            "base_directory",
            "cache_directory",
            "download_directory",
            "mod_directory",
            "overwrite_directory",
            "profile_local_inis",
            "profile_local_saves",
            "profiles_directory",
            "skip_file_suffixes",
            "skip_directories",
        },
        [CustomExecutables] = [],
    };

    /// <summary>
    /// The MO2 configuration for <paramref name="manager"/>, or null for Vortex, which has none (MO2-11).
    /// <list type="number">
    /// <item><b>Portable</b> (MO2-1): <c>portable.txt</c> beside the exe means the INI beside it, which must exist.</item>
    /// <item><b>Instance</b> (MO2-2): otherwise <c>HKCU\Software\Mod Organizer Team\Mod Organizer\CurrentInstance</c>
    /// names a folder under <c>%LOCALAPPDATA%\ModOrganizer</c>, whose INI is read if it exists.</item>
    /// <item><b>Fallback</b> (MO2-3): if no read found a <c>gamePath</c>, the INI beside the exe is read (again, after
    /// a portable read) and the install marked portable; without it the lookup fails.</item>
    /// </list>
    /// </summary>
    /// <exception cref="FileNotFoundException">One of MO2-1's or MO2-3's messages, verbatim.</exception>
    /// <exception cref="Mo2ConfigurationException">One of MO2-8's messages, verbatim.</exception>
    /// <exception cref="System.Text.DecoderFallbackException">An INI isn't valid UTF-8 (T-1).</exception>
    /// <exception cref="IOException">An INI that exists can't be read.</exception>
    public static Mo2Configuration? Locate(ModManager manager, IHostEnvironment host)
    {
        if (manager.Kind != ModManagerKind.ModOrganizer)
        {
            return null;
        }

        var mo2Directory = manager.ExePath.Parent;
        var portableIni = mo2Directory.Join("ModOrganizer.ini");
        var portableIniExists = File.Exists(portableIni.ToString());
        var portableTxt = mo2Directory.Join("portable.txt");

        // ModManagerInfo's mutable state: each read_mo2_ini replaces everything but game_path, which it sets only when
        // the INI has gamePath, and portable_txt_path, which only the portable.txt branch sets.
        IniRead? read = null;
        PythonPath? gamePath = null;
        var portable = false;
        PythonPath? portableTxtPath = null;

        void Read(PythonPath iniPath)
        {
            read = ReadIni(iniPath);
            gamePath = read.GamePath ?? gamePath;
        }

        if (File.Exists(portableTxt.ToString()))
        {
            if (!portableIniExists)
            {
                throw new FileNotFoundException("portable.txt found but no ModOrganizer.ini found in MO2 install path");
            }

            Read(portableIni);
            portable = true;
            portableTxtPath = portableTxt;
        }
        else if (CurrentInstance(host) is { Length: > 0 } instance
                 && host.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } localAppData)
        {
            var instanceIni = new PythonPath(localAppData).Join("ModOrganizer").Join(instance).Join("ModOrganizer.ini");
            if (File.Exists(instanceIni.ToString()))
            {
                Read(instanceIni);
            }
        }

        if (gamePath is null)
        {
            if (!portableIniExists)
            {
                throw new FileNotFoundException(
                    "Unable to find ModOrganizer.ini. Please report this along with your MO2 instance details.");
            }

            Read(portableIni);
            portable = true;
        }

        // Every path to here has read an INI, or thrown.
        var ini = read!;
        return new Mo2Configuration(
            ini.IniPath,
            portable,
            portableTxtPath,
            gamePath,
            ini.SelectedProfile,
            ini.StagePath,
            ini.OverwritePath,
            ini.ProfilesPath,
            ini.SkipFileSuffixes,
            ini.SkipDirectories,
            ini.Settings);
    }

    /// <summary>
    /// <c>utils.get_registry_value</c> for <c>CurrentInstance</c>: the value if it is a non-empty <c>REG_SZ</c>, else
    /// null. Any registry failure (Python's <c>OSError</c>) means null.
    /// </summary>
    private static string? CurrentInstance(IHostEnvironment host)
    {
        try
        {
            return host.ReadRegistryValue(RegistryRoot.CurrentUser, @"Software\Mod Organizer Team\Mod Organizer", "CurrentInstance").AsRegSz;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>read_mo2_ini</c>: strict UTF-8, <c>splitlines</c> (T-1). A line starting with <c>[</c> switches section, and
    /// only an exact <c>[General]</c>, <c>[Settings]</c> or <c>[customExecutables]</c> is tracked. Lines split on the
    /// first <c>=</c>, keys and values untrimmed, and <c>@ByteArray(...)</c> unwrapped by dropping the prefix and the
    /// last character. Then directories are resolved against <c>base_directory</c> and <c>skip_*</c> CSV-parsed.
    /// </summary>
    /// <exception cref="Mo2ConfigurationException">MO2-8: a game other than Fallout 4, or no <c>selected_profile</c>.</exception>
    private static IniRead ReadIni(PythonPath iniPath)
    {
        var text = PythonText.ReadUtf8Strict(iniPath.ToString());

        // mo2_settings is a dict: insertion order, with an overwritten key keeping its place.
        var settings = new OrderedSettings();
        settings["base_directory"] = new Mo2SettingValue.PathValue(iniPath.Parent);
        settings["cache_directory"] = new DefaultDirectory("webcache");
        settings["download_directory"] = new DefaultDirectory("downloads");
        settings["mod_directory"] = new DefaultDirectory("mods");
        settings["overwrite_directory"] = new DefaultDirectory("overwrite");
        settings["profile_local_inis"] = new Mo2SettingValue.FlagValue(false);
        settings["profile_local_saves"] = new Mo2SettingValue.FlagValue(false);
        settings["profiles_directory"] = new DefaultDirectory("profiles");
        // MO2's own defaults (Settings::skipFileSuffixes / skipDirectories), already as lists. The reference held a tuple
        // and a set, whose str() the CSV pass below then parsed into garbage such as "('.mohidden'" (B-5, bug #24,
        // fixed). Its set() default was also not MO2's: MO2 skips .git folders unless told otherwise.
        settings["skip_file_suffixes"] = new Mo2SettingValue.ListValue([".mohidden"]);
        settings["skip_directories"] = new Mo2SettingValue.ListValue([".git"]);

        string? section = null;
        foreach (var line in PythonText.SplitLines(text))
        {
            if (line.StartsWith('['))
            {
                section = TrackedKeys.ContainsKey(line) ? line : null;
                continue;
            }

            if (section is null)
            {
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                continue;
            }

            if (section == CustomExecutables)
            {
                // MO2-10 (not ported): the reference collects xEdit/BSArch paths here, and nothing reads them.
                continue;
            }

            var key = line[..equals];
            var value = line[(equals + 1)..];
            if (!TrackedKeys[section].Contains(key))
            {
                continue;
            }

            if (value.StartsWith(ByteArrayPrefix, StringComparison.Ordinal))
            {
                // value[11:-1]: drops the last character whatever it is, and is empty when nothing is left.
                value = value.Length > ByteArrayPrefix.Length + 1 ? value[ByteArrayPrefix.Length..^1] : string.Empty;
            }

            settings[key] = key == "base_directory"
                ? new Mo2SettingValue.PathValue(new PythonPath(value))
                : new Mo2SettingValue.TextValue(value);
        }

        var baseDirectory = ((Mo2SettingValue.PathValue)settings["base_directory"]).Path;
        foreach (var name in settings.Keys.ToList())
        {
            if (name == "base_directory")
            {
                continue;
            }

            var current = settings[name];
            if (name.EndsWith("directory", StringComparison.Ordinal) || name.EndsWith("Path", StringComparison.Ordinal))
            {
                settings[name] = current switch
                {
                    // MO2's PathSettings::resolve: the placeholder is replaced by the base directory's text, so
                    // "%BASE_DIR%/mods" is <base>\mods. The reference removed it and joined the now-rooted "/mods" onto
                    // the base, landing on the drive root (B-6, bug #25, fixed). The base's raw text is used, as MO2
                    // uses the stored string; a value without the placeholder is Path(value).
                    Mo2SettingValue.TextValue { Text: var raw } when raw.Contains(BaseDirPlaceholder, StringComparison.Ordinal)
                        => new Mo2SettingValue.PathValue(new PythonPath(raw.Replace(BaseDirPlaceholder, baseDirectory.Raw, StringComparison.Ordinal))),
                    Mo2SettingValue.TextValue { Text: var raw } => new Mo2SettingValue.PathValue(new PythonPath(raw)),
                    DefaultDirectory { Name: var sub } => new Mo2SettingValue.PathValue(baseDirectory.Join(sub)),
                    _ => current,
                };
            }
            else if (name.StartsWith("skip_", StringComparison.Ordinal))
            {
                // A value read from the INI is CSV-split; an unset key keeps its default list as is.
                settings[name] = current switch
                {
                    Mo2SettingValue.TextValue t => new Mo2SettingValue.ListValue(PythonCsv.ReadMo2Row(t.Text)),
                    Mo2SettingValue.ListValue => current,
                    _ => throw new InvalidOperationException($"Unexpected {name} value {current}."),
                };
            }
        }

        var gameName = settings.TryGetValue("gameName", out var game) ? ((Mo2SettingValue.TextValue)game).Text : "Fallout 4";
        if (gameName != "Fallout 4")
        {
            throw new Mo2ConfigurationException($"Only Fallout 4 is supported.\ngameName is '{gameName}' in INI: \n{iniPath}");
        }

        if (!settings.TryGetValue("selected_profile", out var profile))
        {
            throw new Mo2ConfigurationException("Profile is not set in ModOrganizer.ini.");
        }

        // Python's str.lower(); .NET's invariant lowering differs only for a few special-cased letters (see README residuals).
        static string Lower(string s) => s.ToLowerInvariant();
        var suffixes = ((Mo2SettingValue.ListValue)settings["skip_file_suffixes"]).Items;
        var directories = ((Mo2SettingValue.ListValue)settings["skip_directories"]).Items;

        return new IniRead(
            iniPath,
            settings.TryGetValue("gamePath", out var gamePath) ? ((Mo2SettingValue.PathValue)gamePath).Path : null,
            ((Mo2SettingValue.TextValue)profile).Text,
            PathOf(settings, "mod_directory"),
            PathOf(settings, "overwrite_directory"),
            PathOf(settings, "profiles_directory"),
            [.. suffixes.Select(Lower)],
            new HashSet<string>(directories.Select(Lower), StringComparer.Ordinal),
            [.. settings.Keys.Select(k => new Mo2Setting(k, (Mo2SettingValue)settings[k]))]);
    }

    /// <summary>A resolved directory setting.</summary>
    private static PythonPath PathOf(OrderedSettings settings, string key) => ((Mo2SettingValue.PathValue)settings[key]).Path;

    /// <summary>One <c>read_mo2_ini</c> call's results.</summary>
    private sealed record IniRead(
        PythonPath IniPath,
        PythonPath? GamePath,
        string SelectedProfile,
        PythonPath StagePath,
        PythonPath OverwritePath,
        PythonPath ProfilesPath,
        IReadOnlyList<string> SkipFileSuffixes,
        IReadOnlySet<string> SkipDirectories,
        IReadOnlyList<Mo2Setting> Settings);

    /// <summary>
    /// A default directory before resolution: Python's <c>Path("%BASE_DIR%/&lt;name&gt;")</c>, which resolves to
    /// <c>base / name</c>. Never left in a finished configuration.
    /// </summary>
    private sealed record DefaultDirectory(string Name);

    /// <summary>
    /// A string-keyed map that remembers first-insertion order, like a Python dict. Values are <see cref="Mo2SettingValue"/>s,
    /// or a <see cref="DefaultDirectory"/> until the resolution pass replaces it.
    /// </summary>
    private sealed class OrderedSettings
    {
        private readonly List<string> _order = [];
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Keys => _order;

        public object this[string key]
        {
            get => _values[key];
            set
            {
                if (_values.TryAdd(key, value))
                {
                    _order.Add(key);
                }
                else
                {
                    _values[key] = value;
                }
            }
        }

        public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);
    }
}
