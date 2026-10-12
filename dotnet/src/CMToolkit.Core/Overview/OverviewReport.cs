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
/// refresh, which the App holds as the current one.
/// </summary>
/// <param name="InstallType">The game's Install Type (<c>game.install_type</c>), which the top block and other tabs read.</param>
/// <param name="Binaries">The <c>BASE_FILES</c> binaries in check order (OVW-B1).</param>
/// <param name="AddressLibrary">The Address Library's file when it is installed, else null (OVW-B5).</param>
/// <param name="Problems">
/// The Overview problems in the order the reference's shared list holds them (§5.6): the ones gathered with the
/// binaries, the modules and the archives, then the ones the frames append while they are built: per binary
/// (OVW-P5, OVW-P6), then the archive limits and the module limits (OVW-C2).
/// </param>
public sealed record OverviewReport(
    InstallType InstallType,
    IReadOnlyList<BinaryFile> Binaries,
    PythonPath? AddressLibrary,
    IReadOnlyList<Problem> Problems)
{
    /// <summary>The modules (OVW-M1..OVW-M9); none until gathered.</summary>
    public OverviewModules Modules { get; init; } = OverviewModules.None;

    /// <summary>The archives (OVW-A1..OVW-A5); none until gathered.</summary>
    public OverviewArchives Archives { get; init; } = OverviewArchives.None;

    /// <summary>The warning boxes the gathering raised, in order (OVW-M3, OVW-M5). The App shows them on the first load only.</summary>
    public IReadOnlyList<OverviewWarning> Warnings { get; init; } = [];

    /// <summary>The General archives against their limit, raised for Anniversary (OVW-A5, T-2).</summary>
    public LimitedCount GeneralArchives => new(Archives.General, OverviewLimits.GeneralArchiveLimit(InstallType));

    /// <summary>The Texture archives against their limit, raised for Anniversary (OVW-A5, T-2).</summary>
    public LimitedCount TextureArchives => new(Archives.Texture, OverviewLimits.TextureArchiveLimit(InstallType));

    /// <summary>Both kinds of archive against both limits.</summary>
    public LimitedCount TotalArchives => new(GeneralArchives.Count + TextureArchives.Count, GeneralArchives.Limit + TextureArchives.Limit);

    /// <summary>The Full modules against 254.</summary>
    public LimitedCount FullModules => new(Modules.Full, OverviewLimits.FullModules);

    /// <summary>The Light modules against 4096.</summary>
    public LimitedCount LightModules => new(Modules.Light, OverviewLimits.LightModules);

    /// <summary>All modules against 4350.</summary>
    public LimitedCount TotalModules => new(FullModules.Count + LightModules.Count, FullModules.Limit + LightModules.Limit);

    /// <summary><c>TOOLTIP_NO_MOD_MANAGER</c>: the Mod Manager value's tooltip, and OVW-P1's solution.</summary>
    public const string NoModManagerText = "Your mod manager must launch the app to be detected.";

    /// <summary><c>TOOLTIP_ADDRESS_LIBRARY_MISSING</c>.</summary>
    public const string AddressLibraryMissingText = "Address Library is required for many F4SE mods.";

    private const string AddressLibraryLink = "https://www.nexusmods.com/fallout4/mods/47327";

    /// <summary>
    /// Gathers a new report: <c>OverviewTab._load</c> / <c>refresh</c>, which gather the binaries, the modules and the
    /// archives, then the problems the three frames append while they are built. Synchronous and blocking (the reference
    /// ran it on the UI thread): the App runs it as a <see cref="OperationPhase.Blocking"/> operation. It shows nothing;
    /// its warnings are in <see cref="Warnings"/> for the App to show on the first load (OVW-8).
    /// </summary>
    /// <param name="game">The installation found at startup.</param>
    /// <param name="host">The machine: file versions, the exe name and the working directory relative paths resolve against.</param>
    /// <param name="logger">The app log, which gets the reference's DEBUG lines.</param>
    /// <param name="previous">
    /// The report this one replaces, if any. A gathering that dies before a step starts leaves that step's part as it was,
    /// as the reference's shared state did.
    /// </param>
    /// <exception cref="OverviewIncompleteException">
    /// A step raised: a binary, module or archive that exists but can't be read, no local app data folder, or no
    /// <c>[Archive]</c> section (OVW-A2, B-15). It carries what had been gathered. (Text that isn't UTF-8 failed the
    /// reference here too, T-1; the port reports it instead, bug #86.)
    /// </exception>
    public static OverviewReport Gather(GameInstall game, IHostEnvironment host, ILogger logger, OverviewReport? previous = null)
        => Gather(game, host, logger, previous, modulesAndArchives: true);

    /// <summary>
    /// <see cref="Gather(GameInstall, IHostEnvironment, ILogger, OverviewReport?)"/>, optionally without the modules and
    /// archives. The <c>overview-tab</c> parity scenarios were recorded with those two steps stubbed out (their frames
    /// built from empty state), so their replays skip them the same way.
    /// </summary>
    internal static OverviewReport Gather(
        GameInstall game, IHostEnvironment host, ILogger logger, OverviewReport? previous, bool modulesAndArchives)
    {
        var problems = new List<Problem>();
        var warnings = new List<OverviewWarning>();
        var gathered = new BinariesGathering(game, host, logger, problems);
        ModulesGathering? modules = null;
        ArchivesGathering? archives = null;
        try
        {
            gathered.Run();
            if (modulesAndArchives)
            {
                modules = new ModulesGathering(game, host, logger, problems, warnings);
                modules.Run();
                archives = new ArchivesGathering(game, host, logger, problems, modules.Enabled, gathered.InstallType);
                archives.Run();
            }
        }
        catch (Exception ex)
        {
            // The frames are never built, so none of their problems are appended, and the rows stay unjudged.
            throw new OverviewIncompleteException(
                new OverviewReport(gathered.InstallType, [.. gathered.Files], gathered.AddressLibrary, [.. problems])
                {
                    Modules = modules?.Snapshot() ?? (modulesAndArchives ? previous?.Modules : null) ?? OverviewModules.None,
                    Archives = archives?.Snapshot() ?? (modulesAndArchives ? previous?.Archives : null) ?? OverviewArchives.None,
                    Warnings = [.. warnings],
                },
                ex);
        }

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

        var counted = new OverviewReport(gathered.InstallType, binaries, gathered.AddressLibrary, [])
        {
            Modules = modules?.Snapshot() ?? OverviewModules.None,
            Archives = archives?.Snapshot() ?? OverviewArchives.None,
            Warnings = warnings,
        };

        // build_gui_archives, then build_gui_modules: add_count_label's Limit Exceeded problems, Totals excepted (OVW-C2).
        AddLimitProblem(logger, problems, counted.GeneralArchives, "General", "Archive");
        AddLimitProblem(logger, problems, counted.TextureArchives, "Texture", "Archive");
        AddLimitProblem(logger, problems, counted.FullModules, "Full", "Module");
        AddLimitProblem(logger, problems, counted.LightModules, "Light", "Module");
        return counted with { Problems = problems };
    }

    /// <summary>Adds a simple problem, logging it as <c>SimpleProblemInfo.__init__</c> does.</summary>
    internal static void AddSimple(ILogger logger, List<Problem> problems, Problem problem)
    {
        logger.Write(LogLevel.Debug, $"Added SimpleProblemInfo: Path: {problem.ItemText} | Problem: {problem.Kind}");
        problems.Add(problem);
    }

    /// <summary>
    /// <c>add_count_label</c>'s problem for a count over its limit: <c>"{n} {format} {type}s"</c> / <c>Limit Exceeded</c>,
    /// with the reference's solution and link for each type (OVW-C2).
    /// </summary>
    private static void AddLimitProblem(ILogger logger, List<Problem> problems, LimitedCount count, string format, string type)
    {
        if (count.Level != CountLevel.Exceeded)
        {
            return;
        }

        var (solution, extraData) = (type, format) switch
        {
            ("Archive", _) => (
                "Archives can be unpacked or merged to reduce your total.\nNote: Do not mix texture and non-texture archives when merging.\nUnpacking is only suggested for small non-texture archives for performance reasons.\nYou can use Unpackrr to quickly unpack small archives:",
                (IReadOnlyList<string>?)["https://www.nexusmods.com/fallout4/mods/82082"]),
            (_, "Full") => (
                "Many Full modules are eligible to be flagged as Light (ESL).\n\nThis guide walks you through the process in xEdit:",
                ["https://themidnightride.moddinglinked.com/esl.html"]),
            _ => (
                "Some plugins will need to be removed or manually merged.\nWarning: Do not use old/outdated tools like zMerge with Fallout 4 unless\nyou understand their issues and how to fix the merged plugins afterward.",
                null),
        };

        AddSimple(logger, problems, Problem.Simple(
            $"{count.Count} {format} {type}s",
            "Limit Exceeded",
            $"You have {count.Count} {format} {type}s enabled. The limit is {count.Limit}.",
            solution,
            extraData));
    }

    /// <summary>
    /// A row's colour and problem, decided in OVW-B7's order: equal to the game's type is good; Old-Gen is good only on
    /// a Down-Grade game; a missing binary is expected for the Creation Kit, Archive2 and, on Next-Gen/Anniversary,
    /// f4se_steam_loader.dll; anything else is the wrong version.
    /// </summary>
    /// <remarks>
    /// Deviation (bug #84, B-13): the reference matched <c>case self.cmc.game.install_type</c> first, so with no
    /// Fallout4.exe (the game <c>Not Found</c>) every missing binary equalled the game and was green with no problem. A
    /// <c>Not Found</c> binary never matches the game here, so each missing one is judged as missing.
    /// </remarks>
    private static (BinaryStatus Status, Problem? Problem) Judge(BinaryFile file, InstallType game)
    {
        // file_info[name]["File"] or Path(name): a missing file's problem names it without a folder.
        var path = file.File ?? new PythonPath(file.Name);
        var relative = new PythonPath(path.Name);
        if (file.InstallType == game && game != InstallType.NotFound)
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
    private sealed class BinariesGathering(GameInstall game, IHostEnvironment host, ILogger logger, List<Problem> problems)
    {
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
                    problems.Add(Problem.ForFile(
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
                    problems.Add(Problem.ForFile(
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
        private void AddSimple(Problem problem) => OverviewReport.AddSimple(logger, problems, problem);

        /// <summary><c>BASE_FILES[name].get(key)</c>, which is falsy (so the next lookup runs) when absent.</summary>
        private static InstallType? Lookup(IReadOnlyDictionary<string, InstallType> table, string key)
            => table.TryGetValue(key, out var type) ? type : null;
    }
}
