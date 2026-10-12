using System.Runtime.CompilerServices;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Scanner;

/// <summary>
/// MO2 is missing a setting the mod file index needs (<c>get_stage_paths</c>'s <c>ValueError</c>). Its message is the
/// reference's dump of the manager and its paths, verbatim. The scan worker dies with it (B-3).
/// </summary>
public sealed class MissingMo2SettingsException : Exception
{
    /// <summary>An exception carrying the reference's message.</summary>
    public MissingMo2SettingsException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// <c>ScannerTab.build_mod_file_list</c> and <c>get_stage_paths</c> (SCN-S6): the mod file index of MO2's enabled mods
/// and its overwrite folder, built on the scan worker.
/// </summary>
public static class ModFileIndex
{
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
    /// <exception cref="MissingMo2SettingsException">MO2 has no selected profile (the only setting that can be empty).</exception>
    /// <exception cref="FileNotFoundException">The profile has no <c>modlist.txt</c>.</exception>
    /// <exception cref="System.Text.DecoderFallbackException"><c>modlist.txt</c> isn't UTF-8 (T-1).</exception>
    public static ModFiles? Build(ScanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.UsingStage || settings.Manager is not { Kind: ModManagerKind.ModOrganizer })
        {
            return null;
        }

        var index = new ModFiles();
        foreach (var modPath in StagePaths(settings))
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
    /// overwrite folder if it exists.
    /// </summary>
    private static List<PythonPath> StagePaths(ScanSettings settings)
    {
        var mo2 = settings.Game.Mo2;
        var manager = settings.Manager;
        // stage_path, profiles_path and overwrite_path are Path objects, always truthy; selected_profile is a str.
        if (manager is null || mo2 is null || mo2.SelectedProfile.Length == 0)
        {
            throw new MissingMo2SettingsException(manager is null
                ? "Manager: None"
                : $"Missing MO2 settings\nManager: {Repr(manager)}\nmods: {mo2?.StagePath}\nprofiles: {mo2?.ProfilesPath}\nprofile: {mo2?.SelectedProfile}\noverwrite: {mo2?.OverwritePath}");
        }

        var modlist = mo2.ProfilesPath.Join(mo2.SelectedProfile).Join("modlist.txt");
        if (!HostPaths.IsFile(settings.Host, modlist))
        {
            throw new FileNotFoundException($"File doesn't exist: {modlist}");
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
    /// <c>str(manager)</c>: <c>ModManagerInfo</c> has no <c>__str__</c>, so Python prints its default repr with the
    /// object's address. This object's identity hash stands in for the address (the goldens mask it).
    /// </summary>
    private static string Repr(ModManager manager)
        => $"<mod_manager_info.ModManagerInfo object at 0x{RuntimeHelpers.GetHashCode(manager):X16}>";

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
