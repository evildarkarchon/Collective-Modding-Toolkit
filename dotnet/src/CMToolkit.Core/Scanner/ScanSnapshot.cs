using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Scanner;

/// <summary>
/// The Scan's input snapshot: the side pane's boxes as they were when the Scan started, what the Scan reads from its
/// Overview refresh, the game and the machine, and what the data walk derives from them and from the mod manager.
/// Taken through <see cref="ScanSettings.Snapshot"/>. Immutable; the worker reads it off the UI thread.
/// </summary>
public sealed class ScanSnapshot
{
    private readonly IReadOnlyDictionary<ScanSetting, bool> _values;

    /// <summary>
    /// The snapshot: the boxes' values and what the scan reads from the Overview and the game, copied once when the scan
    /// starts. Build it through <see cref="ScanSettings.Snapshot"/>, which checks every setting is present; this
    /// constructor doesn't. It is internal, which keeps the App and other assemblies from skipping that check, but not
    /// the rest of Core or the tests (through <c>InternalsVisibleTo</c>): those must go through
    /// <see cref="ScanSettings.Snapshot"/> by convention.
    /// </summary>
    /// <param name="values">Every setting's box, ticked or not.</param>
    /// <param name="game">The installation; its mod manager decides the skip lists.</param>
    /// <param name="overview">
    /// The Overview's last report, whose enabled modules and archives the scan checks and whose problems it carries; null
    /// for none.
    /// </param>
    /// <param name="host">The machine the scan reads.</param>
    internal ScanSnapshot(IReadOnlyDictionary<ScanSetting, bool> values, GameInstall game, OverviewReport? overview, IHostEnvironment host)
    {
        _values = values;
        Host = host;
        ModulesEnabled = overview?.Modules.Enabled ?? [];
        ArchivesEnabled = new HashSet<PythonPath>(overview?.Archives.Enabled ?? [], PythonPath.Equality);
        OverviewProblems = overview?.Problems ?? [];
        // Overview Issues and Race Subgraphs don't need the walk; any other ticked box does.
        SkipDataScan = !ScanSettings.All.Any(s => values[s] && s is not (ScanSetting.OverviewIssues or ScanSetting.RaceSubgraphs));
        Game = game;
        Manager = game.Manager;
        if (Manager?.Kind == ModManagerKind.ModOrganizer && game.Mo2 is { } mo2)
        {
            SkipFileSuffixes = [.. mo2.SkipFileSuffixes, ".vortex_backup"];
            SkipDirectories = new HashSet<string>(ScanTables.IgnoreFolders.Concat(mo2.SkipDirectories), StringComparer.Ordinal);
        }
        else
        {
            SkipFileSuffixes = [".vortex_backup"];
            SkipDirectories = ScanTables.IgnoreFolders;
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
    /// The file suffixes the mod file index skips: MO2's <c>skip_file_suffixes</c> (MO2-7; <c>.mohidden</c> when unset,
    /// where the reference had B-5's corruption, bug #24)
    /// then <c>.vortex_backup</c>; just <c>.vortex_backup</c> without MO2.
    /// </summary>
    public IReadOnlyList<string> SkipFileSuffixes { get; }

    /// <summary>The folders the mod file index skips: <see cref="ScanTables.IgnoreFolders"/> joined with MO2's <c>skip_directories</c>.</summary>
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

    /// <summary>
    /// The Overview Report's problems as the scan's Overview refresh left them, in the report's order. Empty without an
    /// Overview Report.
    /// </summary>
    public IReadOnlyList<Problem> OverviewProblems { get; }

    /// <summary>The machine the scan reads: relative paths resolve against its working directory.</summary>
    public IHostEnvironment Host { get; }
}
