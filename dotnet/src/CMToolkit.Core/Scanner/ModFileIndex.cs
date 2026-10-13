using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Scanner;

/// <summary>
/// <c>ScannerTab.build_mod_file_list</c> and <c>get_stage_paths</c> (SCN-S6): the mod file index of MO2's enabled mods
/// and its overwrite folder, built on the scan worker.
/// </summary>
public static class ModFileIndex
{
    // The texts of the problem rows that replace get_stage_paths' exceptions (bug #22). The index is what tells the
    // data checks which mod each file belongs to, so without it the worker stops and they are skipped.
    private const string SkippedChecks = "so the Scanner can't tell which mods are enabled.\nThe data checks were skipped.";

    private const string NoProfileSummary =
        "Mod Organizer 2 has no selected profile (selected_profile in ModOrganizer.ini is empty), " + SkippedChecks;

    private const string NoProfileSolution =
        "Open Mod Organizer 2 and select a profile, then relaunch the toolkit from MO2 and scan again.";

    private const string NoModlistSummary = "Mod Organizer 2's selected profile has no modlist.txt, " + SkippedChecks;

    private const string NoModlistSolution =
        "Open Mod Organizer 2 and select a working profile so it writes modlist.txt, then relaunch the toolkit from MO2 and scan again.";

    /// <summary>
    /// Builds the index, or returns null outside MO2 stage mode (the reference then hands the walk an empty
    /// <c>ModFiles</c> and keeps none). Mods are walked lowest priority first, so each path ends up with the
    /// highest-priority mod that has it; skip folders are pruned case-insensitively and files with a skip suffix
    /// skipped.
    /// </summary>
    /// <remarks>
    /// Bug #89, fixed (ADR-0006): the reference's <c>root is mod_path</c> was never true on Python 3.12+
    /// (<c>Path.walk</c> yields a new root object), so each mod's root was indexed as the folder <c>.</c> (the last mod
    /// walked winning it) and <see cref="ModFiles.Modules"/> and <see cref="ModFiles.Archives"/> were never filled. The
    /// port recognises the root as the walk's first step: it gets no folder entry, and its modules and archives are
    /// indexed by file name.
    /// </remarks>
    /// <param name="settings">The scan's settings snapshot.</param>
    /// <param name="failure">
    /// Set when MO2's configuration stops the index (bug #22, fixed): its selected profile is empty, or the profile has
    /// no <c>modlist.txt</c>. The reference's <c>get_stage_paths</c> raised <c>ValueError</c> or
    /// <c>FileNotFoundError</c> there, which killed its scan thread (B-3); the port returns the problem row for the
    /// results instead, and no index. Null otherwise.
    /// </param>
    /// <returns>The index; null outside MO2 stage mode, or when <paramref name="failure"/> is set.</returns>
    /// <exception cref="System.Text.DecoderFallbackException"><c>modlist.txt</c> isn't UTF-8 (T-1).</exception>
    public static ModFiles? Build(ScanSettings settings, out Problem? failure)
    {
        ArgumentNullException.ThrowIfNull(settings);
        failure = null;
        if (!settings.UsingStage || settings.Manager is not { Kind: ModManagerKind.ModOrganizer } || settings.Game.Mo2 is not { } mo2)
        {
            return null;
        }

        var stagePaths = StagePaths(settings, mo2, out failure);
        if (stagePaths is null)
        {
            return null;
        }

        var index = new ModFiles();
        foreach (var modPath in stagePaths)
        {
            var modName = modPath.Name;
            foreach (var step in PythonWalk.TopDown(modPath, p => HostPaths.Resolve(settings.Host, p)))
            {
                PruneSkipFolders(step.Folders, settings.SkipDirectories);

                // Bug #89, fixed (ADR-0006): the reference's `root is mod_path` was never true, so the mod's root got a
                // folder entry keyed "." too. The root is the walk's first step, and it isn't a folder of the mod.
                var isRoot = step.Depth == 0;
                if (!isRoot)
                {
                    index.Folders[step.Relative] = new ModFile(modName, step.Path);
                }

                foreach (var file in step.Files)
                {
                    var lower = file.ToLowerInvariant();
                    if (HasSuffix(lower, settings.SkipFileSuffixes))
                    {
                        continue;
                    }

                    // A dict update keeps the first key's spelling and replaces the value, as Dictionary's indexer does.
                    var entry = new ModFile(modName, step.Path.Join(file));
                    index.Files[step.Relative.Join(file)] = entry;

                    // The mod's own modules and archives, by file name (case-sensitive str keys), the later (higher
                    // priority) mod winning; the bug left these empty.
                    if (isRoot)
                    {
                        if (lower.EndsWith(".esp", StringComparison.Ordinal) || lower.EndsWith(".esl", StringComparison.Ordinal) || lower.EndsWith(".esm", StringComparison.Ordinal))
                        {
                            index.Modules[file] = entry;
                        }
                        else if (lower.EndsWith(".ba2", StringComparison.Ordinal))
                        {
                            index.Archives[file] = entry;
                        }
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// <c>get_stage_paths</c>: the enabled mods' folders from the selected profile's <c>modlist.txt</c>, read bottom-up
    /// (lowest priority first), keeping <c>+name</c> lines whose folder exists under the staging folder, then the
    /// overwrite folder if it exists. Null, with <paramref name="failure"/> set, when MO2 has no selected profile or
    /// the profile has no <c>modlist.txt</c>.
    /// </summary>
    private static List<PythonPath>? StagePaths(ScanSettings settings, Mo2Configuration mo2, out Problem? failure)
    {
        failure = null;

        // stage_path, profiles_path and overwrite_path are Path objects, always truthy; selected_profile is a str, the
        // only setting get_stage_paths checked that can be empty here (without a manager or an MO2 configuration
        // there is no stage mode, so Build never gets this far).
        if (mo2.SelectedProfile.Length == 0)
        {
            // Not a file of any mod, so no <Unmanaged> in the mod column.
            failure = Problem.ForFile(
                ScanProblemTypes.MissingMo2Settings, mo2.IniPath, mo2.IniPath, null, NoProfileSummary, NoProfileSolution)
                with { Mod = string.Empty };
            return null;
        }

        var modlist = mo2.ProfilesPath.Join(mo2.SelectedProfile).Join("modlist.txt");
        if (!HostPaths.IsFile(settings.Host, modlist))
        {
            failure = Problem.ForFile(ProblemTypes.FileNotFound, modlist, modlist, null, NoModlistSummary, NoModlistSolution);
            return null;
        }

        var lines = PythonText.SplitLines(PythonText.ReadUtf8Strict(HostPaths.Resolve(settings.Host, modlist)));
        var paths = new List<PythonPath>();
        foreach (var line in lines.Reverse())
        {
            if (line.StartsWith('+') && mo2.StagePath.Join(line[1..]) is var modPath && HostPaths.IsDirectory(settings.Host, modPath))
            {
                paths.Add(modPath);
            }
        }

        if (HostPaths.IsDirectory(settings.Host, mo2.OverwritePath))
        {
            paths.Add(mo2.OverwritePath);
        }

        return paths;
    }

    /// <summary>
    /// Deletes the folders whose lowercased name is a skip folder, iterating from the end as the reference does (which
    /// leaves the survivors in order).
    /// </summary>
    internal static void PruneSkipFolders(List<string> folders, IReadOnlySet<string> skip)
    {
        for (var i = folders.Count - 1; i >= 0; i--)
        {
            if (skip.Contains(folders[i].ToLowerInvariant()))
            {
                folders.RemoveAt(i);
            }
        }
    }

    /// <summary><c>name.endswith(suffixes)</c> for a tuple of suffixes: an empty suffix matches every name.</summary>
    internal static bool HasSuffix(string name, IEnumerable<string> suffixes)
        => suffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal));
}
