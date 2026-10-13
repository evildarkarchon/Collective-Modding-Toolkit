using CMToolkit.Core.Overview;

namespace CMToolkit.Core.F4se;

/// <summary>
/// What one cell of the F4SE tree says about a plugin (F4SE-4..F4SE-6). The App draws each as its glyph: an empty
/// OG/NG/AE cell or a cross for "Your Game" (<see cref="Unsupported"/>), a check mark, a warning sign, a heavy asterisk,
/// or a question mark.
/// </summary>
public enum F4seSupport
{
    /// <summary>Not an F4SE plugin, so nothing can be said (<c>EMOJI_DLL_UNKNOWN</c>).</summary>
    Unknown,

    /// <summary>Not supported (<c>EMOJI_DLL_BAD</c>, empty; "Your Game" shows <c>CROSS MARK</c>).</summary>
    Unsupported,

    /// <summary>Supported (<c>EMOJI_DLL_GOOD</c>).</summary>
    Supported,

    /// <summary>It has the NG/AE API but doesn't say which versions: check the mod page (<c>EMOJI_DLL_NOTE</c>).</summary>
    Note,

    /// <summary>Address- and struct-independent, so any version (<c>EMOJI_DLL_INDIE</c>).</summary>
    Independent,
}

/// <summary>
/// The F4SE tree's verdicts for one plugin (<c>F4SETab._build_gui</c>'s row values and tag; F4SE-4..F4SE-6), as
/// semantic values.
/// </summary>
/// <param name="Og">The OG cell: <see cref="F4seSupport.Supported"/> or <see cref="F4seSupport.Unsupported"/>.</param>
/// <param name="Ng">The NG cell.</param>
/// <param name="Ae">The AE cell.</param>
/// <param name="YourGame">
/// The "Your Game" cell for the game's Install Type, or null for a DLL that isn't an F4SE plugin, whose row leaves it
/// blank (F4SE-4).
/// </param>
public sealed record F4seVerdict(F4seSupport Og, F4seSupport Ng, F4seSupport Ae, F4seSupport? YourGame)
{
    /// <summary>A DLL that isn't an F4SE plugin: question marks in the first three cells, "Your Game" blank.</summary>
    private static readonly F4seVerdict NotAPlugin = new(F4seSupport.Unknown, F4seSupport.Unknown, F4seSupport.Unknown, null);

    /// <summary>
    /// The row's tag, which colours it (F4SE-6): the "Your Game" verdict's, with anything else (unsupported, or not a
    /// plugin) as bad, except that a non-plugin row is neutral.
    /// </summary>
    public F4seRowTag Tag => YourGame switch
    {
        null => F4seRowTag.Neutral,
        F4seSupport.Independent => F4seRowTag.Independent,
        F4seSupport.Note => F4seRowTag.Note,
        F4seSupport.Supported => F4seRowTag.Good,
        _ => F4seRowTag.Bad,
    };

    /// <summary>The verdicts for <paramref name="info"/> on a game of <paramref name="installType"/> (F4SE-5, F4SE-6).</summary>
    /// <param name="info">
    /// The plugin's inspection, or null for a DLL the loader refused, which is treated as not a plugin (the reference's
    /// <c>info is None</c> branch, reached since bug #34 was fixed).
    /// </param>
    /// <param name="installType">The Install Type the Overview found, read when the tab is built.</param>
    public static F4seVerdict Of(F4sePluginInfo? info, InstallType installType)
    {
        if (info is null || !info.IsF4se)
        {
            return NotAPlugin;
        }

        var independent = info.StructureIndependent == true && info.AddressIndependent == true;
        var og = info.SupportsOg ? F4seSupport.Supported : F4seSupport.Unsupported;
        var ng = info.SupportsNgAe ? NgAeCell(info.SupportsNg, independent) : F4seSupport.Unsupported;
        var ae = info.SupportsNgAe ? NgAeCell(info.SupportsAe, independent) : F4seSupport.Unsupported;

        // is_foae() is Anniversary only, is_fong() Next-Gen only, is_foog() Old-Gen or Down-Grade; every other Install
        // Type (Next-Gen & Anniversary included) keeps the default cross.
        var yourGame = installType switch
        {
            InstallType.Anniversary => NewGenCell(info.SupportsAe, info, independent),
            InstallType.NextGen => NewGenCell(info.SupportsNg, info, independent),
            InstallType.OldGen or InstallType.DownGrade => independent ? F4seSupport.Independent
                : info.SupportsOg ? F4seSupport.Supported : F4seSupport.Unsupported,
            _ => F4seSupport.Unsupported,
        };
        return new F4seVerdict(og, ng, ae, yourGame);
    }

    /// <summary>An NG or AE cell of a plugin with the NG/AE API: independent, else a note while unknown, else supported.</summary>
    private static F4seSupport NgAeCell(bool? supports, bool independent)
        => independent ? F4seSupport.Independent
            : supports is null ? F4seSupport.Note
            : supports.Value ? F4seSupport.Supported : F4seSupport.Unsupported;

    /// <summary>"Your Game" on Next-Gen or Anniversary: independent, else supported, else a note if it has the NG/AE API.</summary>
    private static F4seSupport NewGenCell(bool? supports, F4sePluginInfo info, bool independent)
        => independent ? F4seSupport.Independent
            : supports == true ? F4seSupport.Supported
            : info.SupportsNgAe ? F4seSupport.Note : F4seSupport.Unsupported;
}

/// <summary>The F4SE tree's row tags (<c>TAG_*</c>), each with its own colour.</summary>
public enum F4seRowTag
{
    /// <summary><c>neutral</c>: not an F4SE plugin (<c>COLOR_NEUTRAL_1</c>).</summary>
    Neutral,

    /// <summary><c>good</c>: supported on this game (<c>COLOR_GOOD</c>).</summary>
    Good,

    /// <summary><c>bad</c>: not supported on this game (<c>COLOR_BAD</c>).</summary>
    Bad,

    /// <summary><c>note</c>: may be supported; check the mod page (<c>COLOR_NOTE</c>).</summary>
    Note,

    /// <summary><c>independent</c>: version-independent (<c>COLOR_INDIE</c>).</summary>
    Independent,
}
