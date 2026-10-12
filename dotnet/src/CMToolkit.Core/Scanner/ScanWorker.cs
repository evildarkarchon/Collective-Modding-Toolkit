using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMToolkit.Core.Scanner;

/// <summary>What the scan worker hands back: its problems, appended to the seeded results, and the mod file index.</summary>
/// <param name="Problems">The worker's problems, in the order it found them.</param>
/// <param name="Index">
/// The mod file index (SCN-S6), which only a data walk in MO2 stage mode builds; null otherwise. SCN-R2 looks the
/// Overview problems' mods up in it.
/// </param>
public sealed record ScanOutcome(IReadOnlyList<Problem> Problems, ModFiles? Index)
{
    /// <summary>No problems and no index.</summary>
    public static ScanOutcome Empty { get; } = new([], null);
}

/// <summary>
/// <c>ScannerTab.scan_data_files</c>: the work the reference ran on its scan thread, which the App runs as an
/// interactive operation (THR-1, ADR-0003).
/// </summary>
public static class ScanWorker
{
    /// <summary>The progress step the Race Subgraphs count reports, which is no folder, so the tab ignores it.</summary>
    public const string RaceSubgraphStep = "Race Subgraph Records";

    private const string LoosePrevisSummary =
        "Loose previs files should be archived so they only win conflicts according to their plugin's load order.\nLoose previs files are also not supported by PJM's Previs Scripts.";

    private const string RaceSubgraphSolution =
        "IF you are experiencing stutter when moving between cells, removing some of these mods could alleviate performance issues.\nMerging them may also reduce stutter.";

    private const string F4seOverrideSummary =
        "This is an override of an F4SE script. This could break F4SE if they aren't the same version or this mod isn't intended to override F4SE files.";

    private const string F4seOverrideSolution =
        "Check if this mod is supposed to override F4SE Scripts.\nIf this is a script extender/library or requires one, this is likely intentional but it must support your game version explicitly.\nOtherwise, this mod or file may need to be deleted.";

    // b"\x00\x53\x41\x44\x44": a NUL then the SADD record tag.
    private static readonly byte[] SaddBytes = [0x00, 0x53, 0x41, 0x44, 0x44];

    /// <summary>Runs the scan's worker part for <paramref name="settings"/>, reporting no progress and logging nothing.</summary>
    /// <param name="settings">The scan's settings snapshot.</param>
    public static ScanOutcome Run(ScanSettings settings) => Run(settings, null, NullLogger.Instance);

    /// <summary>
    /// Runs the scan's worker part: without a Data folder it finds nothing. Otherwise the Race Subgraphs count (SCN-S5)
    /// if ticked, then, unless no data check is ticked, the mod file index (SCN-S6) and the walk of Data (SCN-C1..C9),
    /// reporting each top-level folder it enters (SCN-S4).
    /// </summary>
    /// <param name="settings">The scan's settings snapshot.</param>
    /// <param name="progress">Where progress updates go, in order, from the calling thread; null for none.</param>
    /// <param name="logger">The app log, which gets the Race Subgraphs problem's <c>SimpleProblemInfo</c> line.</param>
    /// <exception cref="MissingMo2SettingsException">The index needs an MO2 profile that isn't set (B-3).</exception>
    /// <exception cref="FileNotFoundException">The MO2 profile has no <c>modlist.txt</c> (B-3).</exception>
    public static ScanOutcome Run(ScanSettings settings, IProgress<ScanUpdate>? progress, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        var data = settings.Game.DataPath;
        if (data is null)
        {
            return ScanOutcome.Empty;
        }

        var problems = new List<Problem>();
        if (settings[ScanSetting.RaceSubgraphs])
        {
            progress?.Report(new ScanUpdate.Folder(RaceSubgraphStep));
            CountRaceSubgraphs(settings, logger, problems);
        }

        if (settings.SkipDataScan)
        {
            return new ScanOutcome(problems, null);
        }

        var index = ModFileIndex.Build(settings);
        new DataWalk(settings, index ?? new ModFiles(), progress, problems).Run(data);
        return new ScanOutcome(problems, index);
    }

    /// <summary>
    /// SCN-S5: the SADD records in each enabled module's whole file, skipping a module that can't be read. A total over
    /// <see cref="ScanSettingTexts.RaceSubgraphThreshold"/> adds one simple problem listing the modules that have any.
    /// </summary>
    private static void CountRaceSubgraphs(ScanSettings settings, ILogger logger, List<Problem> problems)
    {
        var modules = new List<(int Count, PythonPath File)>();
        var total = 0;
        // Bug #90, fixed (ADR-0006): the reference read, totalled and listed a module twice when modules_enabled had it
        // twice (in Fallout4.ccc and plugins.txt, #33; scanner/details). The Overview now lists each module once, in
        // first-seen order, so each is counted once here.
        foreach (var module in settings.ModulesEnabled)
        {
            int count;
            try
            {
                count = CountSadd(HostPaths.Resolve(settings.Host, module));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // read_bytes raised OSError (a folder, a locked or vanished file): the module is skipped.
                continue;
            }

            if (count > 0)
            {
                modules.Add((count, module));
                total += count;
            }
        }

        if (total > ScanSettingTexts.RaceSubgraphThreshold)
        {
            OverviewReport.AddSimple(logger, problems, Problem.Simple(
                $"{total} SADD Records from {modules.Count} modules",
                ScanProblemTypes.RaceSubgraphRecordCount,
                ScanSettingTexts.RaceSubgraphsInfo,
                RaceSubgraphSolution) with { FileList = modules });
        }
    }

    /// <summary>
    /// <c>module_bytes.count(b"\x00SADD")</c>: non-overlapping occurrences in the whole file, read in chunks. The pattern
    /// can't overlap itself, so carrying its length minus one bytes between chunks finds each occurrence exactly once.
    /// </summary>
    internal static int CountSadd(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var buffer = new byte[(1 << 20) + SaddBytes.Length - 1];
        var carry = 0;
        var count = 0;
        while (true)
        {
            var read = stream.Read(buffer, carry, buffer.Length - carry);
            if (read == 0)
            {
                return count;
            }

            var span = buffer.AsSpan(0, carry + read);
            var searched = span;
            int at;
            var consumed = 0;
            while ((at = searched.IndexOf(SaddBytes)) >= 0)
            {
                count++;
                consumed += at + SaddBytes.Length;
                searched = span[consumed..];
            }

            // Keep the tail that could still start a match, but never bytes already counted in one.
            carry = Math.Min(SaddBytes.Length - 1, span.Length - consumed);
            span[^carry..].CopyTo(buffer);
        }
    }

    /// <summary>The walk of Data, top-down (SCN-C1..SCN-C9), with the state the reference kept in locals.</summary>
    private sealed class DataWalk(ScanSettings settings, ModFiles index, IProgress<ScanUpdate>? progress, List<Problem> problems)
    {
        // data_root_lower starts as "Data" (not lowercased), the pseudo-root of Data's own files, which has no whitelist.
        private string _dataRoot = "Data";

        public void Run(PythonPath data)
        {
            foreach (var step in PythonWalk.TopDown(data, p => HostPaths.Resolve(settings.Host, p)))
            {
                var (modName, modPath) = index.Folders.TryGetValue(step.Relative, out var folder) ? (folder.Mod, folder.Path) : (string.Empty, step.Path);

                // Bug #89, fixed (ADR-0006): the reference's `current_path is data_path` was never true on Python 3.12+
                // (Path.walk yields a new root object), so the tuple of top-level folders was never queued and no
                // folder name below was ever found in it. The root is the walk's first step: queue every top-level
                // folder (a copy, before any pruning below) so each one entered shows its place in the progress.
                if (step.Depth == 0)
                {
                    progress?.Report(new ScanUpdate.TopLevelFolders([.. step.Folders]));
                }

                if (step.Depth == 1)
                {
                    progress?.Report(new ScanUpdate.Folder(step.Path.Name));
                    _dataRoot = step.Path.Name.ToLowerInvariant();

                    if (settings[ScanSetting.JunkFiles] && _dataRoot == "fomod")
                    {
                        problems.Add(Problem.ForFile(
                            ScanProblemTypes.JunkFile, modPath, step.Relative, modName,
                            "This is a junk folder not used by the game or mod managers.", ScanSolutionTypes.DeleteOrIgnore));
                        step.Folders.Clear();
                        continue;
                    }

                    if (!ScanTables.DataWhitelist.ContainsKey(_dataRoot))
                    {
                        step.Folders.Clear();
                        continue;
                    }

                    if (settings[ScanSetting.LoosePrevis] && _dataRoot == "vis")
                    {
                        problems.Add(Problem.ForFile(
                            ScanProblemTypes.LoosePrevis, modPath, step.Relative, modName, LoosePrevisSummary, ScanSolutionTypes.ArchiveFolder));
                        step.Folders.Clear();
                        continue;
                    }
                }

                CheckFolders(step);
                CheckFiles(step);
            }
        }

        /// <summary>
        /// The sub-folders, from last to first as the reference deletes from <c>folders</c> (so its problems come in
        /// reverse order): skip folders pruned, then under Meshes, <c>precombined</c> (SCN-C3) and
        /// <c>animtextdata</c> (SCN-C4) reported and pruned.
        /// </summary>
        private void CheckFolders(WalkStep step)
        {
            for (var i = step.Folders.Count - 1; i >= 0; i--)
            {
                var name = step.Folders[i];
                var lower = name.ToLowerInvariant();
                if (settings.SkipDirectories.Contains(lower))
                {
                    step.Folders.RemoveAt(i);
                    continue;
                }

                if (_dataRoot != "meshes")
                {
                    continue;
                }

                var relative = step.Relative.Join(name);
                var (mod, path) = index.Folders.TryGetValue(relative, out var folder) ? (folder.Mod, folder.Path) : (string.Empty, step.Path.Join(name));
                if (settings[ScanSetting.LoosePrevis] && lower == "precombined")
                {
                    problems.Add(Problem.ForFile(ScanProblemTypes.LoosePrevis, path, relative, mod, LoosePrevisSummary, ScanSolutionTypes.ArchiveFolder));
                    step.Folders.RemoveAt(i);
                    continue;
                }

                if (settings[ScanSetting.ProblemOverrides] && lower == "animtextdata")
                {
                    problems.Add(Problem.ForFile(
                        ScanProblemTypes.AnimTextDataFolder, path, relative, mod,
                        "The existence of unpacked AnimTextData may cause the game to crash.", ScanSolutionTypes.ArchiveFolder));
                    step.Folders.RemoveAt(i);
                }
            }
        }

        /// <summary>The files, in order: skip suffixes, then junk (SCN-C5), F4SE scripts (SCN-C6), formats (SCN-C7, SCN-C8).</summary>
        private void CheckFiles(WalkStep step)
        {
            var whitelist = ScanTables.DataWhitelist.GetValueOrDefault(_dataRoot);
            foreach (var name in step.Files)
            {
                var lower = name.ToLowerInvariant();
                if (settings.SkipFileSuffixes.Count > 0 && ModFileIndex.HasSuffix(lower, settings.SkipFileSuffixes))
                {
                    continue;
                }

                var full = step.Path.Join(name);
                var relative = step.Relative.Join(name);
                var (mod, path) = index.Files.TryGetValue(relative, out var file) ? (file.Mod, file.Path) : (string.Empty, full);

                if (settings[ScanSetting.JunkFiles] && (ScanTables.JunkFiles.Contains(lower) || ModFileIndex.HasSuffix(lower, ScanTables.JunkFileSuffixes)))
                {
                    problems.Add(Problem.ForFile(
                        ScanProblemTypes.JunkFile, path, relative, mod,
                        "This is a junk file not used by the game or mod managers.", ScanSolutionTypes.DeleteOrIgnore));
                    continue;
                }

                // Directly in Data/Scripts, and only for a file the index gives a mod: without MO2 this never fires.
                if (_dataRoot == "scripts" && step.Depth == 1
                    && mod.Length > 0 && settings[ScanSetting.ProblemOverrides] && ScanTables.F4seScripts.Contains(lower))
                {
                    problems.Add(Problem.ForFile(ScanProblemTypes.F4seOverride, path, relative, mod, F4seOverrideSummary, F4seOverrideSolution));
                    continue;
                }

                // file_lower.rsplit(".", maxsplit=1): no dot, no extension, no more checks.
                var dot = lower.LastIndexOf('.');
                if (dot < 0)
                {
                    continue;
                }

                var stemLower = lower[..dot];
                var extension = lower[(dot + 1)..];
                if (!settings[ScanSetting.WrongFormat])
                {
                    continue;
                }

                // A DLL is checked by a string prefix of the folder: "f4se\pluginsextra" passes, Data's own "." doesn't.
                if ((whitelist is not null && !whitelist.Contains(extension))
                    || (extension == "dll" && !step.Relative.ToString().ToLowerInvariant().StartsWith(@"f4se\plugins", StringComparison.Ordinal)))
                {
                    var (summary, solution) = WrongFormat(full, extension);
                    problems.Add(Problem.ForFile(ScanProblemTypes.UnexpectedFormat, path, relative, mod, summary, solution));
                    continue;
                }

                if (extension == "ba2" && !ScanTables.ArchiveNameWhitelist.Contains(lower) && !settings.ArchivesEnabled.Contains(full))
                {
                    // ba2_name_split = file_split[0].rsplit(" - ", 1)
                    var split = stemLower.LastIndexOf(" - ", StringComparison.Ordinal);
                    var prefix = split < 0 ? stemLower : stemLower[..split];
                    var suffixes = settings.Game.Inis.Ba2Suffixes;
                    if (split < 0 || !suffixes.Contains(stemLower[(split + 3)..]))
                    {
                        problems.Add(Problem.ForFile(
                            ScanProblemTypes.InvalidArchiveName, path, relative, mod,
                            "This is not a valid archive name and won't be loaded by the game.",
                            ScanSolutionTypes.RenameArchive,
                            [$"\nValid Suffixes: {string.Join(", ", suffixes)}", $"Example: {prefix} - Main.ba2"]));
                    }
                }
            }
        }

        /// <summary>
        /// SCN-C7's texts: for a format with proper alternatives, whether a sibling with one of them exists (looked up
        /// by <c>with_suffix</c>, so the names listed are the constructed ones); otherwise an unknown format.
        /// </summary>
        private (string Summary, string Solution) WrongFormat(PythonPath full, string extension)
        {
            var head = $"Format not in whitelist for {_dataRoot}.\n";
            if (!ScanTables.ProperFormats.TryGetValue(extension, out var proper))
            {
                return (head + "Unable to determine whether the game will use this file.", ScanSolutionTypes.UnknownFormat);
            }

            var found = proper
                .Select(e => full.WithName(full.Stem + "." + e))
                .Where(p => HostPaths.IsFile(settings.Host, p))
                .Select(p => p.Name)
                .ToList();
            return found.Count > 0
                ? (head + $"A file with the expected format was found ({string.Join(", ", found)}).", ScanSolutionTypes.DeleteOrIgnore)
                : (head + $"A file with the expected format was NOT found ({string.Join(", ", proper)}).", ScanSolutionTypes.ConvertDeleteOrIgnoreFile);
        }
    }
}
