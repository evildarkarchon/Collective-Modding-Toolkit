using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
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
/// <c>scan_settings.ScanSettings</c> (SCN-S2): the snapshot a scan takes of the side pane's boxes when it starts, with
/// what the data walk derives from them and from the mod manager. Immutable; the worker reads it off the UI thread.
/// </summary>
public sealed class ScanSettings
{
    /// <summary><c>IGNORE_FOLDERS</c>: the folders the mod file index always skips, lowercase.</summary>
    public static readonly IReadOnlySet<string> IgnoreFolders = new HashSet<string>(StringComparer.Ordinal)
    {
        "bodyslide",
        "fo4edit",
        "robco_patcher",
        "source",
    };

    /// <summary>Every setting, in enum (and side pane) order.</summary>
    public static readonly IReadOnlyList<ScanSetting> All = Enum.GetValues<ScanSetting>();

    private readonly IReadOnlyDictionary<ScanSetting, bool> _values;

    private ScanSettings(IReadOnlyDictionary<ScanSetting, bool> values, GameInstall game, OverviewReport? overview, IHostEnvironment host)
    {
        _values = values;
        Host = host;
        ModulesEnabled = overview?.Modules.Enabled ?? [];
        ArchivesEnabled = new HashSet<PythonPath>(overview?.Archives.Enabled ?? [], PythonPath.Equality);
        // Overview Issues and Race Subgraphs don't need the walk; any other ticked box does.
        SkipDataScan = !All.Any(s => values[s] && s is not (ScanSetting.OverviewIssues or ScanSetting.RaceSubgraphs));
        Game = game;
        Manager = game.Manager;
        if (Manager?.Kind == ModManagerKind.ModOrganizer && game.Mo2 is { } mo2)
        {
            SkipFileSuffixes = [.. mo2.SkipFileSuffixes, ".vortex_backup"];
            SkipDirectories = new HashSet<string>(IgnoreFolders.Concat(mo2.SkipDirectories), StringComparer.Ordinal);
        }
        else
        {
            SkipFileSuffixes = [".vortex_backup"];
            SkipDirectories = IgnoreFolders;
        }
    }

    /// <summary>Whether <paramref name="setting"/>'s box was ticked when the scan started.</summary>
    public bool this[ScanSetting setting] => _values[setting];

    /// <summary><c>skip_data_scan</c>: true unless some ticked box is a data check.</summary>
    public bool SkipDataScan { get; }

    /// <summary>The installation being scanned.</summary>
    public GameInstall Game { get; }

    /// <summary>The mod manager, or null (<c>scan_settings.manager</c>).</summary>
    public ModManager? Manager { get; }

    /// <summary>
    /// Stage mode (<c>using_stage</c>): the manager has a staging folder, which only MO2 has. The results then get the
    /// mod column (SCN-2) and the details pane its Mod row.
    /// </summary>
    public bool UsingStage => Game.Mo2 is not null;

    /// <summary>
    /// The file suffixes the mod file index skips: MO2's <c>skip_file_suffixes</c> (MO2-7, B-5's corruption included)
    /// then <c>.vortex_backup</c>; just <c>.vortex_backup</c> without MO2.
    /// </summary>
    public IReadOnlyList<string> SkipFileSuffixes { get; }

    /// <summary>The folders the mod file index skips: <see cref="IgnoreFolders"/> joined with MO2's <c>skip_directories</c>.</summary>
    public IReadOnlySet<string> SkipDirectories { get; }

    /// <summary>
    /// <c>game.modules_enabled</c> as the scan's Overview refresh left it: the modules the Race Subgraphs count reads
    /// (SCN-S5), in order. Empty without an Overview report.
    /// </summary>
    public IReadOnlyList<PythonPath> ModulesEnabled { get; }

    /// <summary>
    /// <c>game.archives_enabled</c> as the scan's Overview refresh left it, compared case-insensitively (T-8): an
    /// enabled archive is never an Invalid Archive Name (SCN-C8).
    /// </summary>
    public IReadOnlySet<PythonPath> ArchivesEnabled { get; }

    /// <summary>The machine the scan reads: relative paths resolve against its working directory.</summary>
    public IHostEnvironment Host { get; }

    /// <summary>Takes the snapshot: <c>ScanSettings(side_pane)</c>, without its save (see <see cref="ChangedSettings"/>).</summary>
    /// <param name="values">Each box's state; every <see cref="ScanSetting"/> must be present.</param>
    /// <param name="game">The installation being scanned.</param>
    /// <exception cref="ArgumentException">A setting is missing from <paramref name="values"/>.</exception>
    public static ScanSettings Snapshot(IReadOnlyDictionary<ScanSetting, bool> values, GameInstall game)
        => Snapshot(values, game, null, SystemHostEnvironment.Instance);

    /// <summary>
    /// Takes the snapshot with what the worker reads from the Overview refresh the scan just ran (the reference's shared
    /// <c>cmc.game</c> state) and the machine it reads.
    /// </summary>
    /// <param name="values">Each box's state; every <see cref="ScanSetting"/> must be present.</param>
    /// <param name="game">The installation being scanned.</param>
    /// <param name="overview">The current Overview report, or null when there is none (no modules or archives enabled).</param>
    /// <param name="host">The machine the scan reads.</param>
    /// <exception cref="ArgumentException">A setting is missing from <paramref name="values"/>.</exception>
    public static ScanSettings Snapshot(
        IReadOnlyDictionary<ScanSetting, bool> values, GameInstall game, OverviewReport? overview, IHostEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(host);
        var missing = All.Where(s => !values.ContainsKey(s)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"No value for {string.Join(", ", missing)}.", nameof(values));
        }

        return new ScanSettings(All.ToDictionary(s => s, s => values[s]), game, overview, host);
    }

    /// <summary>
    /// What the snapshot saves (SET-7): <paramref name="stored"/> with every <c>scanner_*</c> value set to its box, when
    /// at least one box differs from its stored value; null when none does, and then nothing is written.
    /// </summary>
    /// <param name="stored">The settings in effect.</param>
    public AppSettings? ChangedSettings(AppSettings stored)
    {
        var updated = stored with
        {
            ScannerOverviewIssues = this[ScanSetting.OverviewIssues],
            ScannerErrors = this[ScanSetting.Errors],
            ScannerWrongFormat = this[ScanSetting.WrongFormat],
            ScannerLoosePrevis = this[ScanSetting.LoosePrevis],
            ScannerJunkFiles = this[ScanSetting.JunkFiles],
            ScannerProblemOverrides = this[ScanSetting.ProblemOverrides],
            ScannerRaceSubgraphs = this[ScanSetting.RaceSubgraphs],
        };
        return updated == stored ? null : updated;
    }
}
