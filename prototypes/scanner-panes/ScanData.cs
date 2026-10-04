namespace CmtPanesPrototype;

/// <summary>One result row (ProblemInfo / SimpleProblemInfo, reduced to what the details pane shows).</summary>
public sealed record ProblemItem(string Text, string Summary, string Solution, bool HasFileList = false, bool HasAutoFix = false, bool LocationExists = true);

/// <summary>A results-tree group: the problem type and its rows.</summary>
public sealed record ProblemGroup(string Title, IReadOnlyList<ProblemItem> Items);

/// <summary>
/// Canned results matching reference/python-scanner-*.png (a real scan of this machine's install), so the
/// prototype's panes can be put next to the Python ones. Strings come from src/ (enums.SolutionType,
/// globals.INFO_SCAN_RACE_SUBGRAPHS, _overview/_scanner summaries).
/// </summary>
public static class ScanData
{
    private const string NoSolution = "No solution suggestion.";

    public static IReadOnlyList<ProblemGroup> Results { get; } =
    [
        new("Wrong Version",
        [
            new("f4se_steam_loader.dll", "The version of this binary does not match your installed game version.", NoSolution),
        ]),
        new("Invalid Archive Name",
        [
            new("CompanionIvy_quest - Meshes.ba2", "This is not a valid archive name and won't be loaded by the game.",
                "Archives must be named the same as a plugin with an added suffix or added to an INI.\n\nValid Suffixes: main, textures, voices_en\nExample: companionivy_quest - Main.ba2"),
            new("CompanionIvy_quest - Sounds.ba2", "This is not a valid archive name and won't be loaded by the game.",
                "Archives must be named the same as a plugin with an added suffix or added to an INI.\n\nValid Suffixes: main, textures, voices_en\nExample: companionivy_quest - Main.ba2"),
        ]),
        new("No Mod Manager",
        [
            new("drive_python.py", "No mod manager was detected.", NoSolution, LocationExists: false),
        ]),
        new("Unexpected Format",
        [
            new("avdynrojc.webp", "Format not in whitelist for this folder.", "If this file type is expected here, please report it.", HasAutoFix: true),
            new("wrongscaleus.jpg", "Format not in whitelist for this folder.", "If this file type is expected here, please report it."),
            new("TexGen_FO4.ini", "Format not in whitelist for this folder.", "If this file type is expected here, please report it."),
            new("version.ini", "Format not in whitelist for this folder.", "If this file type is expected here, please report it."),
            new("big bed.psd", "Format not in whitelist for this folder.", "If this file type is expected here, please report it."),
            new("smallbed.psd", "Format not in whitelist for this folder.", "If this file type is expected here, please report it."),
            new("RGB.png", "Format not in whitelist for this folder.", "If this file type is expected here, please report it."),
        ]),
        new("Race Subgraph Record Count",
        [
            new("137 SADD Records from 50 modules",
                "Counts race animation subgraph records (RACE \\ SADD).\nDepending on your PC, adding too many of these may result in stutter when loading Cells.\nThis issue needs more investigation as this may be mere correlation and not causation.",
                "IF you are experiencing stutter when moving between cells, removing some of these mods could alleviate performance issues.\nMerging them may also reduce stutter.",
                HasFileList: true),
        ]),
        new("Junk File",
        [
            new("desktop.ini", "This is a junk file not used by the game or mod managers.", "It can either be deleted or ignored.", HasAutoFix: true),
            new("DLCCoast - Textures.ba2.bak", "This is a junk file not used by the game or mod managers.", "It can either be deleted or ignored."),
        ]),
    ];

    public static int Count => Results.Sum(g => g.Items.Count);
}
