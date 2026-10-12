namespace CMToolkit.Core.Overview;

/// <summary>How close a count is to its limit (OVW-C1): green, orange or red.</summary>
public enum CountLevel
{
    /// <summary>Below 95 % of the limit (<c>COLOR_GOOD</c>).</summary>
    Good,

    /// <summary>From 95 % up to the limit itself (<c>COLOR_WARNING</c>, orange).</summary>
    Warning,

    /// <summary>Over the limit (<c>COLOR_BAD</c>); every row but the totals also raises a Limit Exceeded problem.</summary>
    Exceeded,
}

/// <summary>A count the Overview shows against its limit (<c>add_count_label</c>).</summary>
/// <param name="Count">The count.</param>
/// <param name="Limit">The limit it is shown against.</param>
public readonly record struct LimitedCount(int Count, int Limit)
{
    /// <summary>
    /// <c>num &lt; int(0.95 * limit)</c> is good, <c>num &lt;= limit</c> a warning, anything more exceeded (OVW-C1). The
    /// 95 % mark is the double product truncated, as Python's <c>int()</c> truncates it: 241 of 254, 972 of 1024.
    /// </summary>
    public CountLevel Level => Count < (int)(0.95 * Limit) ? CountLevel.Good : Count <= Limit ? CountLevel.Warning : CountLevel.Exceeded;
}

/// <summary>The engine limits the Overview counts against (<c>globals.MAX_*</c>).</summary>
public static class OverviewLimits
{
    /// <summary><c>MAX_MODULES_FULL</c>.</summary>
    public const int FullModules = 254;

    /// <summary><c>MAX_MODULES_LIGHT</c>.</summary>
    public const int LightModules = 4096;

    /// <summary><c>MAX_ARCHIVES_GNRL</c>.</summary>
    public const int GeneralArchives = 256;

    /// <summary><c>MAX_ARCHIVES_DX10</c>.</summary>
    public const int TextureArchives = 255;

    /// <summary><c>MAX_ARCHIVES_GNRL_AE</c>: Anniversary's General limit.</summary>
    public const int GeneralArchivesRaised = 1024;

    /// <summary><c>MAX_ARCHIVES_DX10_AE</c>: Anniversary's Texture limit.</summary>
    public const int TextureArchivesRaised = 1023;

    /// <summary>The General archive limit for a game of this Install Type (OVW-A5, T-2).</summary>
    public static int GeneralArchiveLimit(InstallType type) => type.UsesRaisedArchiveLimits() ? GeneralArchivesRaised : GeneralArchives;

    /// <summary>The Texture archive limit for a game of this Install Type (OVW-A5, T-2).</summary>
    public static int TextureArchiveLimit(InstallType type) => type.UsesRaisedArchiveLimits() ? TextureArchivesRaised : TextureArchives;
}

/// <summary>
/// A warning box the Overview's first load shows (OVW-M3, OVW-M5): <c>messagebox.showwarning(title, message)</c>. Core
/// only collects them; the App shows them, in order, and never on a refresh.
/// </summary>
/// <param name="Title">The box's title.</param>
/// <param name="Message">Its text.</param>
public sealed record OverviewWarning(string Title, string Message);

/// <summary>
/// The Overview's gathering died part-way, as the reference's did when one of its steps raised (OVW-A2, B-15, T-1). It
/// carries what the reference's shared state held at that moment, which the other tabs go on reading, and the
/// warnings shown before it died.
/// </summary>
public sealed class OverviewIncompleteException : Exception
{
    /// <summary>The gathering's death, with what it had gathered; the message is the inner exception's.</summary>
    /// <param name="partial">What had been gathered.</param>
    /// <param name="innerException">What the gathering threw.</param>
    public OverviewIncompleteException(OverviewReport partial, Exception innerException)
        : base(innerException.Message, innerException)
    {
        Partial = partial;
    }

    /// <summary>
    /// The state the reference was left in: its problem list as far as it got (none of the problems appended while the
    /// frames are built), the binaries, Install Type, modules and archives as gathered so far, and for a step that never
    /// started, what the previous report had (or nothing on the first load). Its warnings are those raised.
    /// </summary>
    public OverviewReport Partial { get; }
}
