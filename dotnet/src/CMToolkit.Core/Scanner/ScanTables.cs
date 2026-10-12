namespace CMToolkit.Core.Scanner;

/// <summary>
/// The data walk's tables, ported verbatim from <c>scan_settings.py</c> and <c>globals.py</c> (SCN-C1..SCN-C8). Every
/// key and value is lowercase; extensions have no dot.
/// </summary>
public static class ScanTables
{
    /// <summary>
    /// <c>DATA_WHITELIST</c>: the top-level Data folders the walk enters, each with the extensions allowed under it, or
    /// null for any (<c>f4se</c>). A top-level folder not listed is pruned.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>?> DataWhitelist = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal)
    {
        ["f4se"] = null,
        ["materials"] = Set("bgem", "bgsm", "txt"),
        ["meshes"] = Set("bto", "btr", "hko", "hkx", "hkx_back", "hkx_backup", "lst", "max", "nif", "obj", "sclp", "ssf", "tri", "txt", "xml"),
        ["music"] = Set("wav", "xwm"),
        ["textures"] = Set("dds"),
        ["scripts"] = Set("pex", "psc", "txt", "zip"),
        ["sound"] = Set("cdf", "fuz", "lip", "wav", "xwm", "hkx"),
        ["vis"] = Set("uvd"),
    };

    /// <summary><c>JUNK_FILES</c>: file names that are junk wherever they are.</summary>
    public static readonly IReadOnlySet<string> JunkFiles = Set("thumbs.db", "desktop.ini", ".ds_store");

    /// <summary><c>JUNK_FILE_SUFFIXES</c>: file name endings that are junk.</summary>
    public static readonly IReadOnlyList<string> JunkFileSuffixes = [".tmp", ".bak"];

    /// <summary>
    /// <c>PROPER_FORMATS</c>: for an extension that needs converting, the extensions it should have been, in the order
    /// the walk looks for a sibling with each (SCN-C7).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ProperFormats = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        // Textures
        ["bmp"] = ["dds"],
        ["jpeg"] = ["dds"],
        ["jpg"] = ["dds"],
        ["png"] = ["dds"],
        ["psd"] = ["dds"],
        ["tga"] = ["dds"],
        // Sound
        ["mp3"] = ["wav", "xwm"],
    };

    /// <summary><c>ARCHIVE_NAME_WHITELIST</c>: the base game's archive names, which are never Invalid Archive Names (SCN-C8).</summary>
    public static readonly IReadOnlySet<string> ArchiveNameWhitelist = Set(
        "creationkit - shaders.ba2",
        "creationkit - textures.ba2",
        "fallout4 - animations.ba2",
        "fallout4 - interface.ba2",
        "fallout4 - materials.ba2",
        "fallout4 - meshes.ba2",
        "fallout4 - meshesextra.ba2",
        "fallout4 - misc.ba2",
        "fallout4 - nvflex.ba2",
        "fallout4 - shaders.ba2",
        "fallout4 - sounds.ba2",
        "fallout4 - startup.ba2",
        "fallout4 - textures1.ba2",
        "fallout4 - textures2.ba2",
        "fallout4 - textures3.ba2",
        "fallout4 - textures4.ba2",
        "fallout4 - textures5.ba2",
        "fallout4 - textures6.ba2",
        "fallout4 - textures7.ba2",
        "fallout4 - textures8.ba2",
        "fallout4 - textures9.ba2",
        "fallout4 - texturespatch.ba2",
        "fallout4 - voices.ba2",
        "dlcultrahighresolution - textures01.ba2",
        "dlcultrahighresolution - textures02.ba2",
        "dlcultrahighresolution - textures03.ba2",
        "dlcultrahighresolution - textures04.ba2",
        "dlcultrahighresolution - textures05.ba2",
        "dlcultrahighresolution - textures06.ba2",
        "dlcultrahighresolution - textures07.ba2",
        "dlcultrahighresolution - textures08.ba2",
        "dlcultrahighresolution - textures09.ba2",
        "dlcultrahighresolution - textures10.ba2",
        "dlcultrahighresolution - textures11.ba2",
        "dlcultrahighresolution - textures12.ba2",
        "dlcultrahighresolution - textures13.ba2",
        "dlcultrahighresolution - textures14.ba2",
        "dlcultrahighresolution - textures15.ba2",
        "dlcultrahighresolution - textures16.ba2");

    /// <summary>
    /// The keys of <c>F4SE_CRC</c>: F4SE's own script names. SCN-C6 tests only the name (the CRCs are never compared).
    /// </summary>
    public static readonly IReadOnlySet<string> F4seScripts = Set(
        "actor.pex",
        "actorbase.pex",
        "armor.pex",
        "armoraddon.pex",
        "cell.pex",
        "component.pex",
        "constructibleobject.pex",
        "defaultobject.pex",
        "encounterzone.pex",
        "equipslot.pex",
        "f4se.pex",
        "favoritesmanager.pex",
        "form.pex",
        "game.pex",
        "headpart.pex",
        "input.pex",
        "instancedata.pex",
        "location.pex",
        "math.pex",
        "matswap.pex",
        "miscobject.pex",
        "objectmod.pex",
        "objectreference.pex",
        "perk.pex",
        "scriptobject.pex",
        "ui.pex",
        "utility.pex",
        "watertype.pex",
        "weapon.pex");

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.Ordinal);
}

/// <summary>The <c>enums.ProblemType</c> values the data walk adds: its result tree groups.</summary>
public static class ScanProblemTypes
{
    /// <summary><c>ProblemType.JunkFile</c> (SCN-C1, SCN-C5).</summary>
    public const string JunkFile = "Junk File";

    /// <summary><c>ProblemType.UnexpectedFormat</c> (SCN-C7).</summary>
    public const string UnexpectedFormat = "Unexpected Format";

    /// <summary><c>ProblemType.LoosePrevis</c> (SCN-C2, SCN-C3).</summary>
    public const string LoosePrevis = "Loose Previs";

    /// <summary><c>ProblemType.AnimTextDataFolder</c> (SCN-C4).</summary>
    public const string AnimTextDataFolder = "Loose AnimTextData";

    /// <summary><c>ProblemType.InvalidArchiveName</c> (SCN-C8).</summary>
    public const string InvalidArchiveName = "Invalid Archive Name";

    /// <summary><c>ProblemType.F4SEOverride</c> (SCN-C6).</summary>
    public const string F4seOverride = "F4SE Script Override";

    /// <summary>The Race Subgraphs problem's group (SCN-S5), a <c>SimpleProblemInfo</c>'s problem text.</summary>
    public const string RaceSubgraphRecordCount = "Race Subgraph Record Count";
}

/// <summary>The <c>enums.SolutionType</c> texts the data walk uses.</summary>
public static class ScanSolutionTypes
{
    /// <summary><c>SolutionType.ArchiveFolder</c>.</summary>
    public const string ArchiveFolder = "These folders should be packed into BA2 archives.";

    /// <summary><c>SolutionType.ConvertDeleteOrIgnoreFile</c>.</summary>
    public const string ConvertDeleteOrIgnoreFile =
        "This file may need to be converted and relevant files updated for the new name.\nOtherwise it can likely be deleted or ignored.";

    /// <summary><c>SolutionType.DeleteOrIgnoreFile</c>, which has the same text as <c>DeleteOrIgnoreFolder</c>.</summary>
    public const string DeleteOrIgnore = "It can either be deleted or ignored.";

    /// <summary><c>SolutionType.RenameArchive</c>.</summary>
    public const string RenameArchive = "Archives must be named the same as a plugin with an added suffix or added to an INI.";

    /// <summary><c>SolutionType.UnknownFormat</c>.</summary>
    public const string UnknownFormat = "If this file type is expected here, please report it.";
}
