using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.Overview;

/// <summary>How a binary's row is coloured (OVW-B7): <c>COLOR_GOOD</c>, <c>COLOR_NEUTRAL_1</c> or <c>COLOR_BAD</c>.</summary>
public enum BinaryStatus
{
    /// <summary>It matches the game (green).</summary>
    Good,

    /// <summary>It is missing, which is fine for it (grey).</summary>
    Neutral,

    /// <summary>It is the wrong version, or missing when it shouldn't be (red); each adds a problem.</summary>
    Bad,
}

/// <summary>
/// One <c>BASE_FILES</c> binary as the Overview found it (<c>helpers.FileInfo</c>), plus its row's colour (OVW-B2, OVW-B7).
/// </summary>
/// <param name="Name">The file name, the <c>file_info</c> key: <c>Archive2.exe</c> for <c>Tools\Archive2\Archive2.exe</c>.</param>
/// <param name="File">The file under the game folder, or null when it isn't there.</param>
/// <param name="Version">Its version resource's four parts, or null when it has none (T-4) or isn't there.</param>
/// <param name="VersionString">The version as <c>a.b.c.d</c>, or the CRC when it has no version, or null when it isn't there.</param>
/// <param name="Hash">The full-file CRC32, or null when it isn't there.</param>
/// <param name="InstallType">What it was built for, after OVW-B3's Next-Gen &amp; Anniversary substitution.</param>
/// <param name="Status">Its row's colour against the game's Install Type.</param>
public sealed record BinaryFile(
    string Name,
    PythonPath? File,
    IReadOnlyList<int>? Version,
    string? VersionString,
    string? Hash,
    InstallType InstallType,
    BinaryStatus Status);

/// <summary>
/// What the Overview tab shows and reports (the Core seams resolution on issue #7): one immutable result per load or
/// refresh, which the App holds as the current one. This slice covers the binaries; the modules and archives come next.
/// </summary>
/// <param name="InstallType">The game's Install Type (<c>game.install_type</c>), which the top block and other tabs read.</param>
/// <param name="Binaries">The <c>BASE_FILES</c> binaries in check order (OVW-B1).</param>
/// <param name="AddressLibrary">The Address Library's file when it is installed, else null (OVW-B5).</param>
/// <param name="Problems">
/// The Overview problems in the order the reference's shared list holds them (§5.6): the ones gathered with the
/// binaries, then the ones the Binaries frame appends while it is built (OVW-P5, OVW-P6).
/// </param>
public sealed record OverviewReport(
    InstallType InstallType,
    IReadOnlyList<BinaryFile> Binaries,
    PythonPath? AddressLibrary,
    IReadOnlyList<Problem> Problems)
{
    /// <summary><c>TOOLTIP_NO_MOD_MANAGER</c>: the Mod Manager value's tooltip, and OVW-P1's solution.</summary>
    public const string NoModManagerText = "Your mod manager must launch the app to be detected.";

    /// <summary><c>TOOLTIP_ADDRESS_LIBRARY_MISSING</c>.</summary>
    public const string AddressLibraryMissingText = "Address Library is required for many F4SE mods.";

    private const string AddressLibraryLink = "https://www.nexusmods.com/fallout4/mods/47327";

    /// <summary>
    /// Gathers a new report: <c>OverviewTab._load</c> / <c>refresh</c>'s <c>get_info_binaries</c>, then the problems
    /// <c>build_gui_binaries</c> adds. Synchronous and blocking (the reference ran it on the UI thread): the App runs it
    /// as a <see cref="OperationPhase.Blocking"/> operation and never shows message boxes from it (OVW-8).
    /// </summary>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="host">The machine: file versions, the exe name and the working directory relative paths resolve against.</param>
    /// <param name="logger">The app log, which gets the reference's DEBUG lines.</param>
    /// <exception cref="IOException">A binary or <c>Startup.ba2</c> exists but can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">A binary or <c>Startup.ba2</c> can't be opened.</exception>
    public static OverviewReport Gather(GameInstall game, IHostEnvironment host, ILogger logger)
    {
        var gathered = new BinariesGathering(game, host, logger);
        gathered.Run();

        // The modules and the archives are gathered here, after the binaries (§5.6; the next Overview slice).
        var problems = new List<Problem>(gathered.Problems);

        // build_gui_binaries: the row colours, and their problems, come after everything gathered.
        var binaries = new List<BinaryFile>();
        foreach (var file in gathered.Files)
        {
            var (status, problem) = Judge(file, gathered.InstallType);
            binaries.Add(file with { Status = status });
            if (problem is not null)
            {
                problems.Add(problem);
            }
        }

        return new OverviewReport(gathered.InstallType, binaries, gathered.AddressLibrary, problems);
    }

    /// <summary>
    /// A row's colour and problem, decided in OVW-B7's order: equal to the game's type (even <c>Unknown</c> or
    /// <c>Not Found</c>, B-13) is good; Old-Gen is good only on a Down-Grade game; a missing binary is expected for the
    /// Creation Kit, Archive2 and, on Next-Gen/Anniversary, f4se_steam_loader.dll; anything else is the wrong version.
    /// </summary>
    private static (BinaryStatus Status, Problem? Problem) Judge(BinaryFile file, InstallType game)
    {
        // file_info[name]["File"] or Path(name): a missing file's problem names it without a folder.
        var path = file.File ?? new PythonPath(file.Name);
        var relative = new PythonPath(path.Name);
        if (file.InstallType == game)
        {
            return (BinaryStatus.Good, null);
        }

        switch (file.InstallType)
        {
            case InstallType.OldGen when game == InstallType.DownGrade:
                return (BinaryStatus.Good, null);
            case InstallType.NotFound:
                if (file.Name is "CreationKit.exe" or "Archive2.exe"
                    || (game.IsNextGenOrAnniversary() && file.Name == "f4se_steam_loader.dll"))
                {
                    return (BinaryStatus.Neutral, null);
                }

                return (BinaryStatus.Bad, Problem.ForFile(
                    ProblemTypes.FileNotFound, path, relative, null, "This file is missing from your game installation.", null));
            default:
                return (BinaryStatus.Bad, Problem.ForFile(
                    ProblemTypes.WrongVersion, path, relative, null, "The version of this binary does not match your installed game version.", null));
        }
    }

    /// <summary><c>get_info_binaries</c>: the per-file lookups, the Install Type, the Address Library and Down-Grade checks.</summary>
    private sealed class BinariesGathering(GameInstall game, IHostEnvironment host, ILogger logger)
    {
        public List<Problem> Problems { get; } = [];

        public List<BinaryFile> Files { get; } = [];

        // reset_binaries sets Unknown before the loop; get_file_info's OVW-B3 substitution reads it mid-loop.
        public InstallType InstallType { get; private set; } = InstallType.Unknown;

        public PythonPath? AddressLibrary { get; private set; }

        public void Run()
        {
            logger.Write(LogLevel.Debug, "Gathering Info: Binaries");
            logger.Write(LogLevel.Debug, "Reset Info: Binaries");

            if (game.Manager is null)
            {
                AddSimple(Problem.Simple(new PythonPath(host.Argv0).Name, "No Mod Manager", "No Mod Manager Detected", NoModManagerText));
            }

            foreach (var (key, table) in BaseFiles.All)
            {
                var file = GetFileInfo(game.GamePath.Join(key), table);
                Files.Add(file);
                if (key == "Fallout4.exe")
                {
                    CheckGame(file);
                }
            }
        }

        /// <summary><c>get_file_info</c>: the version resource, then the CRC, looked up in that order (OVW-B2).</summary>
        private BinaryFile GetFileInfo(PythonPath path, IReadOnlyDictionary<string, InstallType> table)
        {
            logger.Write(LogLevel.Debug, $"Get File Info: {path}");
            if (!HostPaths.IsFile(host, path))
            {
                return new BinaryFile(path.Name, null, null, null, null, InstallType.NotFound, default);
            }

            var resolved = HostPaths.Resolve(host, path);
            var version = host.GetFileVersion(resolved);
            var hash = FileCrc32.Of(resolved);
            string versionString;
            InstallType? type;
            if (version is not null)
            {
                versionString = string.Join('.', version);
                type = Lookup(table, versionString) ?? Lookup(table, hash);
            }
            else
            {
                versionString = hash;
                type = Lookup(table, hash);
            }

            if (type == InstallType.NextGenAndAnniversary && InstallType.IsNextGenOrAnniversary())
            {
                type = InstallType;
            }

            return new BinaryFile(path.Name, path, version, versionString, hash, type ?? InstallType.Unknown, default);
        }

        /// <summary>Fallout4.exe's part: it sets the Install Type (OVW-B4), then the Address Library (OVW-B5) and Down-Grade (OVW-B6) checks run.</summary>
        private void CheckGame(BinaryFile exe)
        {
            InstallType = exe.InstallType;
            if (InstallType == InstallType.Unknown)
            {
                AddSimple(Problem.Simple(
                    "Fallout4.exe",
                    "Unknown Game Version",
                    $"{exe.VersionString ?? exe.Hash} is an unknown version.\nPossible causes:\n1. The game is an old version and should be updated.\n2. The exe file may be corrupted.\n3. The game is a new version and the Toolkit needs to be updated.",
                    "Either update the game/verify files in Steam, or report this issue."));
            }

            var data = game.DataPath;
            if (data is not null && !string.IsNullOrEmpty(exe.VersionString))
            {
                // Without a version resource the "version" is the CRC, so the name becomes version-<CRC>.bin.
                var name = $"version-{exe.VersionString.Replace('.', '-')}.bin";
                var relative = new PythonPath("F4SE/Plugins").Join(name);
                var path = data.Join(relative.Raw);
                if (HostPaths.IsFile(host, path))
                {
                    AddressLibrary = path;
                }
                else
                {
                    Problems.Add(Problem.ForFile(
                        ProblemTypes.FileNotFound,
                        path,
                        relative,
                        null,
                        "Address Library is a requirement for many F4SE mods and playing downgraded,\nand likely needs to be installed.",
                        SolutionTypes.DownloadMod,
                        [AddressLibraryLink]));
                }
            }

            if (data is not null && InstallType.IsOldGenOrDownGrade())
            {
                var name = new PythonPath("Fallout4 - Startup.ba2");
                var startup = data.Join(name.Raw);
                if (HostPaths.IsFile(host, startup))
                {
                    if (FileCrc32.Of(HostPaths.Resolve(host, startup), skipBa2Header: true) == BaseFiles.NextGenStartupBa2Crc)
                    {
                        InstallType = InstallType.DownGrade;
                    }
                }
                else
                {
                    Problems.Add(Problem.ForFile(
                        ProblemTypes.FileNotFound,
                        startup,
                        name,
                        null,
                        "This is a base game file, and is used by CM Toolkit to differentiate between\nOld-Gen and Down-Grade.",
                        SolutionTypes.VerifyFiles));
                }
            }
        }

        /// <summary>Adds a simple problem, logging it as <c>SimpleProblemInfo.__init__</c> does.</summary>
        private void AddSimple(Problem problem)
        {
            logger.Write(LogLevel.Debug, $"Added SimpleProblemInfo: Path: {problem.ItemText} | Problem: {problem.Kind}");
            Problems.Add(problem);
        }

        /// <summary><c>BASE_FILES[name].get(key)</c>, which is falsy (so the next lookup runs) when absent.</summary>
        private static InstallType? Lookup(IReadOnlyDictionary<string, InstallType> table, string key)
            => table.TryGetValue(key, out var type) ? type : null;
    }
}
