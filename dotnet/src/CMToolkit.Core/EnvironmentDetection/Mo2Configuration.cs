using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// What the toolkit takes from MO2's <c>ModOrganizer.ini</c> (MO2-9): the fields <c>ModManagerInfo</c> exposes after
/// <c>read_mo2_ini</c> and <c>GameInfo.find_path</c>'s lookup.
/// </summary>
/// <param name="IniPath">The INI read last.</param>
/// <param name="Portable">Whether the portable INI (beside the exe) was used, by portable.txt or by the fallback.</param>
/// <param name="PortableTxtPath"><c>portable.txt</c>, when it was what selected the portable INI (MO2-1).</param>
/// <param name="GamePath"><c>[General] gamePath</c>, or null when no INI read had one (MO2-3).</param>
/// <param name="SelectedProfile"><c>[General] selected_profile</c>, never trimmed.</param>
/// <param name="StagePath"><c>mod_directory</c>, resolved (MO2-6).</param>
/// <param name="OverwritePath"><c>overwrite_directory</c>, resolved.</param>
/// <param name="ProfilesPath"><c>profiles_directory</c>, resolved.</param>
/// <param name="SkipFileSuffixes">The CSV-split <c>skip_file_suffixes</c> (MO2's <c>.mohidden</c> when unset, bug #24), lowercased, in order (MO2-7, B-5).</param>
/// <param name="SkipDirectories">The CSV-split <c>skip_directories</c> (MO2's <c>.git</c> when unset, bug #24), lowercased; a set, so unordered (T-15).</param>
/// <param name="Settings">
/// Every <c>mo2_settings</c> entry in dict order (defaults first, then keys first seen in the INI), shown verbatim in
/// OVW-4.
/// </param>
public sealed record Mo2Configuration(
    PythonPath IniPath,
    bool Portable,
    PythonPath? PortableTxtPath,
    PythonPath? GamePath,
    string SelectedProfile,
    PythonPath StagePath,
    PythonPath OverwritePath,
    PythonPath ProfilesPath,
    IReadOnlyList<string> SkipFileSuffixes,
    IReadOnlySet<string> SkipDirectories,
    IReadOnlyList<Mo2Setting> Settings);

/// <summary>One <c>mo2_settings</c> entry.</summary>
public sealed record Mo2Setting(string Key, Mo2SettingValue Value);

/// <summary>
/// A <c>mo2_settings</c> value, typed as the Python value is, because OVW-4 renders it with <c>str()</c> (T-9).
/// </summary>
public abstract record Mo2SettingValue
{
    private Mo2SettingValue()
    {
    }

    /// <summary>A <c>Path</c>: the resolved directories, <c>base_directory</c> and <c>gamePath</c>.</summary>
    public sealed record PathValue(PythonPath Path) : Mo2SettingValue;

    /// <summary>A <c>str</c> read from the INI and kept as text (<c>gameName</c>, <c>selected_profile</c>, a <c>profile_local_*</c> that was set).</summary>
    public sealed record TextValue(string Text) : Mo2SettingValue;

    /// <summary>A <c>bool</c>: an unset <c>profile_local_inis</c>/<c>profile_local_saves</c> keeps its <c>False</c> default.</summary>
    public sealed record FlagValue(bool Flag) : Mo2SettingValue;

    /// <summary>A <c>list[str]</c>: a CSV-split <c>skip_*</c> value, not yet lowercased.</summary>
    public sealed record ListValue(IReadOnlyList<string> Items) : Mo2SettingValue;
}

/// <summary>
/// A fatal problem with <c>ModOrganizer.ini</c> whose message the reference wrote for the user (Python's
/// <c>ValueError</c> in <c>read_mo2_ini</c>, MO2-8). Startup fails with it (SHELL-13).
/// </summary>
public sealed class Mo2ConfigurationException : Exception
{
    /// <summary>An exception carrying the reference's message verbatim.</summary>
    public Mo2ConfigurationException(string message)
        : base(message)
    {
    }
}
