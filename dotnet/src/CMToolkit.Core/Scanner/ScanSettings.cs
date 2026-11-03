using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Settings;

namespace CMToolkit.Core.Scanner;

/// <summary>
/// <c>scan_settings.ScanSetting</c>: the Scanner side pane's checkboxes, in the reference's enum order, which is the
/// order they are packed in (SCN-3).
/// </summary>
public enum ScanSetting
{
    /// <summary>Overview Issues: seeds the results with the Overview's problems (SCN-S3).</summary>
    OverviewIssues,

    /// <summary>Errors: has no check of its own; it only enables the data walk (SCN-C9).</summary>
    Errors,

    /// <summary>Wrong File Formats (SCN-C7, SCN-C8).</summary>
    WrongFormat,

    /// <summary>Loose Previs (SCN-C2, SCN-C3).</summary>
    LoosePrevis,

    /// <summary>Junk Files (SCN-C1, SCN-C5).</summary>
    JunkFiles,

    /// <summary>Problem Overrides (SCN-C4, SCN-C6).</summary>
    ProblemOverrides,

    /// <summary>Race Subgraphs (SCN-S5), counted on the worker without the data walk.</summary>
    RaceSubgraphs,
}

/// <summary>The texts each <see cref="ScanSetting"/> carries: <c>setting.value</c> and the settings key it is saved under.</summary>
public static class ScanSettingTexts
{
    /// <summary>The checkbox label (<c>setting.value[0]</c>).</summary>
    public static string Label(this ScanSetting setting) => setting switch
    {
        ScanSetting.OverviewIssues => "Overview Issues",
        ScanSetting.Errors => "Errors",
        ScanSetting.WrongFormat => "Wrong File Formats",
        ScanSetting.LoosePrevis => "Loose Previs",
        ScanSetting.JunkFiles => "Junk Files",
        ScanSetting.ProblemOverrides => "Problem Overrides",
        ScanSetting.RaceSubgraphs => "Race Subgraphs",
        _ => throw new ArgumentOutOfRangeException(nameof(setting)),
    };

    /// <summary>The checkbox tooltip (<c>setting.value[1]</c>: the <c>TOOLTIP_SCAN_*</c> constants), verbatim.</summary>
    public static string Tooltip(this ScanSetting setting) => setting switch
    {
        ScanSetting.OverviewIssues => "Report details of issues from Overview",
        ScanSetting.Errors => "Check for errors in mod configuration or files.",
        ScanSetting.WrongFormat => "Check file types against a whitelist per Data folder.\ne.g. MP3 instead of XWM/WAV in Data/Sound/.",
        ScanSetting.LoosePrevis => "Report loose Data/Vis/ and Data/Meshes/Precombined/ folders.",
        ScanSetting.JunkFiles => "Report junk files such as desktop.ini, Thumbs.db,\nand leftover fomod folders.",
        ScanSetting.ProblemOverrides => "Check for overrides that typically cause issues\nsuch as outdated F4SE script files or loose AnimTextData folders.",
        ScanSetting.RaceSubgraphs => RaceSubgraphsInfo + "\n\nBase Game Count: 37\nWarning Threshold: " + RaceSubgraphThreshold,
        _ => throw new ArgumentOutOfRangeException(nameof(setting)),
    };

    /// <summary>The <c>settings.json</c> key it is saved under: <c>scanner_</c> plus the enum member's name (SET-7).</summary>
    public static string SettingsKey(this ScanSetting setting) => "scanner_" + setting;

    /// <summary><c>INFO_SCAN_RACE_SUBGRAPHS</c>: the Race Subgraphs tooltip's first part, and that problem's summary.</summary>
    public const string RaceSubgraphsInfo =
        "Counts race animation subgraph records (RACE \\ SADD).\nDepending on your PC, adding too many of these may result in stutter when loading Cells.\nThis issue needs more investigation as this may be mere correlation and not causation.";

    /// <summary><c>RACE_SUBGRAPH_THRESHOLD</c>: a SADD total above it is a problem.</summary>
    public const int RaceSubgraphThreshold = 100;
}

/// <summary>
/// The Scan Settings themselves: every setting, the boxes as they were saved, what saving the boxes writes, and the
/// <see cref="ScanSnapshot"/> a Scan takes of them when it starts.
/// </summary>
public static class ScanSettings
{
    /// <summary>Every setting, in enum (and side pane) order.</summary>
    public static readonly IReadOnlyList<ScanSetting> All = Enum.GetValues<ScanSetting>();

    /// <summary>
    /// Takes the snapshot with what the scan reads from the Overview refresh it just ran (the enabled modules and
    /// archives, and the problems) and the machine it reads. It doesn't save the boxes (see
    /// <see cref="ChangedSettings(AppSettings, IReadOnlyDictionary{ScanSetting, bool})"/>).
    /// </summary>
    /// <param name="values">Each box's state; every <see cref="ScanSetting"/> must be present.</param>
    /// <param name="game">The installation being scanned.</param>
    /// <param name="overview">
    /// The current Overview report, or null when there is none (no modules or archives enabled, no Overview problems).
    /// </param>
    /// <param name="host">
    /// The machine the scan reads. There is deliberately no default: a snapshot that fell back to the real machine would
    /// let a test read it, relative to its working directory, instead of the scenario it set up.
    /// </param>
    /// <exception cref="ArgumentException">A setting is missing from <paramref name="values"/>.</exception>
    public static ScanSnapshot Snapshot(
        IReadOnlyDictionary<ScanSetting, bool> values, GameInstall game, OverviewReport? overview, IHostEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(host);
        var missing = All.Where(s => !values.ContainsKey(s)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"No value for {string.Join(", ", missing)}.", nameof(values));
        }

        return new ScanSnapshot(All.ToDictionary(s => s, s => values[s]), game, overview, host);
    }

    /// <summary>
    /// The boxes as <paramref name="stored"/> saved them: each setting's <c>scanner_*</c> value. The side pane starts
    /// from these (SET-8). The reference never read them and ticked every box (B-1); the port fixes that (bug #20).
    /// </summary>
    /// <param name="stored">The settings in effect.</param>
    /// <returns>A value for every <see cref="ScanSetting"/>.</returns>
    public static IReadOnlyDictionary<ScanSetting, bool> Stored(AppSettings stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return All.ToDictionary(s => s, s => s switch
        {
            ScanSetting.OverviewIssues => stored.ScannerOverviewIssues,
            ScanSetting.Errors => stored.ScannerErrors,
            ScanSetting.WrongFormat => stored.ScannerWrongFormat,
            ScanSetting.LoosePrevis => stored.ScannerLoosePrevis,
            ScanSetting.JunkFiles => stored.ScannerJunkFiles,
            ScanSetting.ProblemOverrides => stored.ScannerProblemOverrides,
            ScanSetting.RaceSubgraphs => stored.ScannerRaceSubgraphs,
            _ => throw new ArgumentOutOfRangeException(nameof(stored), s, "Unknown scan setting."),
        });
    }

    /// <summary>
    /// What saving <paramref name="boxes"/> writes (SET-7): <paramref name="stored"/> with every <c>scanner_*</c> value
    /// set to its box, when at least one box differs from its stored value; null when none does. The side pane saves
    /// this on each box change (bug #20).
    /// </summary>
    /// <param name="stored">The settings in effect.</param>
    /// <param name="boxes">Each box's state; every <see cref="ScanSetting"/> must be present.</param>
    /// <exception cref="KeyNotFoundException">A setting is missing from <paramref name="boxes"/>.</exception>
    public static AppSettings? ChangedSettings(AppSettings stored, IReadOnlyDictionary<ScanSetting, bool> boxes)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(boxes);
        var updated = stored with
        {
            ScannerOverviewIssues = boxes[ScanSetting.OverviewIssues],
            ScannerErrors = boxes[ScanSetting.Errors],
            ScannerWrongFormat = boxes[ScanSetting.WrongFormat],
            ScannerLoosePrevis = boxes[ScanSetting.LoosePrevis],
            ScannerJunkFiles = boxes[ScanSetting.JunkFiles],
            ScannerProblemOverrides = boxes[ScanSetting.ProblemOverrides],
            ScannerRaceSubgraphs = boxes[ScanSetting.RaceSubgraphs],
        };
        return updated == stored ? null : updated;
    }
}
