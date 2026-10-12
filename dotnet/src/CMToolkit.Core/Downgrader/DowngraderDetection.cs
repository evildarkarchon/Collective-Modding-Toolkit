using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;

namespace CMToolkit.Core.Downgrader;

/// <summary>One of the Downgrader's six files and the version it was detected as (an entry of <c>current_versions</c>).</summary>
/// <param name="FileName">The table's name for it, relative to the game folder (<c>Tools\Archive2\Archive2.exe</c>).</param>
/// <param name="InstallType">
/// What its CRC maps to in its table; <see cref="InstallType.Unknown"/> for a CRC the table doesn't list, and
/// <see cref="InstallType.NotFound"/> when it isn't a file.
/// </param>
/// <param name="IsCreationKit">Whether it is one of the Creation Kit's three (shown in the Current Creation Kit frame).</param>
public sealed record DowngraderFileVersion(string FileName, InstallType InstallType, bool IsCreationKit);

/// <summary>
/// <c>Downgrader.get_info</c> (DG-1): which version each of the Downgrader's six files is, by full-file CRC32.
/// </summary>
public static class DowngraderDetection
{
    /// <summary>
    /// Detects the six files under the game folder, in table order: the game's three, then the Creation Kit's three.
    /// </summary>
    /// <param name="game">The installation; its game path may be relative to the host's working directory (T-7).</param>
    /// <param name="host">The machine the files are read on.</param>
    /// <exception cref="IOException">A file exists but can't be read; the reference's <c>open()</c> raised too.</exception>
    /// <exception cref="UnauthorizedAccessException">A file exists but can't be opened for reading.</exception>
    /// <remarks>Reads every present file in full, so it runs as a blocking operation, as the reference ran it on the UI thread.</remarks>
    public static IReadOnlyList<DowngraderFileVersion> Detect(GameInstall game, IHostEnvironment host)
    {
        var versions = new List<DowngraderFileVersion>(6);
        foreach (var (files, isCreationKit) in new[] { (DowngraderFiles.Game, false), (DowngraderFiles.CreationKit, true) })
        {
            foreach (var (name, crcs) in files)
            {
                var path = game.GamePath.Join(name);
                var type = HostPaths.IsFile(host, path)
                    ? crcs.GetValueOrDefault(FileCrc32.Of(HostPaths.Resolve(host, path)), InstallType.Unknown)
                    : InstallType.NotFound;
                versions.Add(new DowngraderFileVersion(name, type, isCreationKit));
            }
        }

        return versions;
    }
}
