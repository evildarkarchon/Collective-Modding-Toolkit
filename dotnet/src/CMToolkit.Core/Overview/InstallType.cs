namespace CMToolkit.Core.Overview;

/// <summary>
/// What a game binary, and the game itself, was built for (<c>enums.InstallType</c>; GLOSSARY.md, Install Type). The
/// Python enum's <c>AE2</c> is an alias of <c>AE</c> (both "Anniversary") and its <c>Obsolete2</c> is never produced, so
/// neither is modelled (T-2; the Core seams resolution on issue #7).
/// </summary>
public enum InstallType
{
    /// <summary>The file doesn't exist: "Not Found".</summary>
    NotFound,

    /// <summary>A version or CRC that <c>BASE_FILES</c> doesn't list: "Unknown".</summary>
    Unknown,

    /// <summary>"Old-Gen" (1.10.163 and its tools).</summary>
    OldGen,

    /// <summary>"Down-Grade": an Old-Gen game with the Next-Gen <c>Startup.ba2</c> (OVW-B6). Never a binary's own type.</summary>
    DownGrade,

    /// <summary>"Next-Gen" (1.10.984).</summary>
    NextGen,

    /// <summary>"Anniversary" (1.11.240, the build with the raised archive limits).</summary>
    Anniversary,

    /// <summary>"Next-Gen &amp; Anniversary": a binary shared by both, which takes the game's type when it is one of them (OVW-B3).</summary>
    NextGenAndAnniversary,

    /// <summary>"Obsolete (OGNG)": a superseded build.</summary>
    Obsolete,
}

/// <summary>The Install Type's display strings and the reference's <c>GameInfo.is_*</c> groupings.</summary>
public static class InstallTypes
{
    /// <summary>
    /// <c>str(InstallType.X)</c>: the text the Overview shows and the problems quote, mapped in this one place.
    /// </summary>
    public static string DisplayName(this InstallType type) => type switch
    {
        InstallType.NotFound => "Not Found",
        InstallType.Unknown => "Unknown",
        InstallType.OldGen => "Old-Gen",
        InstallType.DownGrade => "Down-Grade",
        InstallType.NextGen => "Next-Gen",
        InstallType.Anniversary => "Anniversary",
        InstallType.NextGenAndAnniversary => "Next-Gen & Anniversary",
        InstallType.Obsolete => "Obsolete (OGNG)",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary><c>GameInfo.is_foog</c>: Old-Gen or Down-Grade.</summary>
    public static bool IsOldGenOrDownGrade(this InstallType type) => type is InstallType.OldGen or InstallType.DownGrade;

    /// <summary><c>GameInfo.is_fongae</c>: Next-Gen or Anniversary.</summary>
    public static bool IsNextGenOrAnniversary(this InstallType type) => type is InstallType.NextGen or InstallType.Anniversary;

    /// <summary>
    /// Whether the game takes the raised archive limits, 1024 General and 1023 Texture (OVW-A5). The reference tests
    /// <c>install_type in {InstallType.Obsolete2, InstallType.AE2}</c>, and since <c>Obsolete2</c> is never produced and
    /// <c>AE2</c> is <c>AE</c>, that is exactly Anniversary (T-2; the Core seams resolution on issue #7).
    /// </summary>
    public static bool UsesRaisedArchiveLimits(this InstallType type) => type == InstallType.Anniversary;
}
