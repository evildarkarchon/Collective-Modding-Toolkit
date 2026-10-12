using System.Buffers.Binary;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.Overview;

/// <summary>A module whose HEDR version Fallout 4 doesn't support, with that version (<c>modules_hedr_unknown</c>).</summary>
/// <param name="Module">The module file.</param>
/// <param name="Version">The version, <c>round(float32, 2)</c> (T-3): shown with <see cref="PythonFloat.Repr"/>.</param>
public sealed record UnknownHedrModule(PythonPath Module, double Version);

/// <summary>
/// What <c>get_info_modules</c> leaves in <c>GameInfo</c> (OVW-M1..OVW-M9), for the Modules frame and the Scanner.
/// </summary>
/// <param name="Enabled">
/// <c>modules_enabled</c>: the game masters, the CC modules, then plugins.txt's (or every module in Data), in that
/// order. The reference didn't de-duplicate it, so a CC module also in plugins.txt was listed, and counted, twice
/// (B-17). The port keeps each module once, in first-listed order, compared regardless of case (T-8) as Python's
/// <c>Path</c> equality does (deviation, bugs #33 and #90).
/// </param>
/// <param name="Unreadable"><c>modules_unreadable</c>, a set: one entry per module however often it is listed.</param>
/// <param name="Hedr95"><c>modules_hedr_95</c>, a set.</param>
/// <param name="HedrUnknown"><c>modules_hedr_unknown</c>, a dict: first-listed order, the last version read.</param>
/// <param name="Full">The Full modules counted, once per <paramref name="Enabled"/> entry.</param>
/// <param name="Light">The Light modules counted, once per entry.</param>
/// <param name="Hedr100">The HEDR v1.00 modules counted, once per entry (<c>module_count_v1</c>).</param>
public sealed record OverviewModules(
    IReadOnlyList<PythonPath> Enabled,
    IReadOnlyList<PythonPath> Unreadable,
    IReadOnlyList<PythonPath> Hedr95,
    IReadOnlyList<UnknownHedrModule> HedrUnknown,
    int Full,
    int Light,
    int Hedr100)
{
    /// <summary>The state before any gathering (<c>reset_modules</c>), and after one that found no Data folder.</summary>
    public static OverviewModules None { get; } = new([], [], [], [], 0, 0, 0);
}

/// <summary>
/// <c>OverviewTab.get_info_modules</c>: builds the enabled module list, then reads each module's header (OVW-M1..OVW-M8).
/// </summary>
internal sealed class ModulesGathering(
    GameInstall game, IHostEnvironment host, ILogger logger, List<Problem> problems, List<OverviewWarning> warnings)
{
    /// <summary><c>GAME_MASTERS</c> (<c>globals.py</c>), in order, lowercase as the reference spells them.</summary>
    private static readonly string[] GameMasters =
    [
        "fallout4.esm",
        "fallout4_vr.esm",
        "dlcrobot.esm",
        "dlcworkshop01.esm",
        "dlcworkshop02.esm",
        "dlcworkshop03.esm",
        "dlccoast.esm",
        "dlcnukaworld.esm",
        "dlcultrahighresolution.esm",
    ];

    /// <summary>
    /// <c>MODULE_VERSION_SUPPORT</c> (<c>globals.py</c>), verbatim and in order: the HEDR versions each game accepts. The
    /// reference matched them as text against <c>str(round(version, 2))</c>, so <c>1.70</c> never matched, since Python
    /// prints 1.7 (B-7). The port matches them by value (deviation, bug #26).
    /// </summary>
    private static readonly (string Game, string[] Versions)[] VersionSupport =
    [
        ("Morrowind", ["1.2", "1.3"]),
        ("Oblivion", ["0.8", "1.0"]),
        ("Skyrim LE", ["0.94", "1.70"]),
        ("Skyrim VR", ["0.94", "1.70"]),
        ("Skyrim SE", ["0.94", "1.70", "1.71"]),
        ("Fallout 3", ["0.85", "0.94"]),
        ("Fallout 4 VR", ["0.95"]),
        ("Fallout 4", ["0.95", "1.00"]),
        ("Fallout NV", ["0.94", "1.32", "1.33", "1.34"]),
        ("Fallout 76", ["68.0", "216.0", "223.0"]),
        ("Starfield", ["0.96"]),
    ];

    private static readonly HashSet<string> ModuleSuffixes = [".esp", ".esl", ".esm"];

    private readonly PathSet _unreadable = new();
    private readonly PathSet _hedr95 = new();
    private readonly List<UnknownHedrModule> _hedrUnknown = [];
    private readonly Dictionary<PythonPath, int> _hedrUnknownIndex = new(PythonPath.Equality);
    private int _full;
    private int _light;
    private int _hedr100;

    // modules_enabled, de-duplicated (bugs #33, #90): the game loads a module once however often it is listed.
    private readonly PathSet _enabled = new();

    /// <summary><c>modules_enabled</c> as gathered so far, each module once; the archives read it next.</summary>
    public IReadOnlyList<PythonPath> Enabled => _enabled.Items;

    /// <summary>What has been gathered so far, as a report part.</summary>
    public OverviewModules Snapshot()
        => new([.. Enabled], _unreadable.Snapshot(), _hedr95.Snapshot(), [.. _hedrUnknown], _full, _light, _hedr100);

    /// <summary>
    /// Runs <c>get_info_modules</c>. Its warnings (OVW-M3, OVW-M5) are collected rather than shown; the App shows them
    /// on the first load only.
    /// </summary>
    /// <exception cref="FileNotFoundException">The local app data folder doesn't exist (as at startup, INI-1).</exception>
    public void Run()
    {
        logger.Write(LogLevel.Debug, "Gathering Info: Modules");
        logger.Write(LogLevel.Debug, "Reset Info: Modules");

        var data = game.DataPath;
        if (data is null)
        {
            OverviewReport.AddSimple(logger, problems, Problem.Simple(
                "Data", ProblemTypes.FileNotFound, "The Data folder was not found in your game install path.", SolutionTypes.VerifyFiles));
            return;
        }

        // exists(), not is_file(): a folder named like a master is listed, and then can't be read (OVW-M2).
        AddEnabled(GameMasters.Select(data.Join).Where(path => HostPaths.Exists(host, path)));
        AddCreationClub(data);
        AddPlugins(data);

        foreach (var module in Enabled)
        {
            Check(module);
        }
    }

    /// <summary>Lists each module not already listed (deviation, bugs #33 and #90), keeping the first spelling.</summary>
    private void AddEnabled(IEnumerable<PythonPath> modules)
    {
        foreach (var module in modules)
        {
            _enabled.Add(module);
        }
    }

    /// <summary><c>Fallout4.ccc</c>: each line that is a file in Data, or a problem and a warning without it (OVW-M3).</summary>
    private void AddCreationClub(PythonPath data)
    {
        var ccc = game.GamePath.Join("Fallout4.ccc");
        if (HostPaths.IsFile(host, ccc))
        {
            var content = HeaderFiles.ReadTextLeniently(host, ccc, out var valid);
            if (!valid)
            {
                AddNotUtf8(ccc.Name, "CC modules on lines that aren't UTF-8 can't be detected.", SolutionTypes.VerifyFiles);
            }

            AddEnabled(PythonText.SplitLines(content)
                .Select(data.Join)
                .Where(path => HostPaths.IsFile(host, path)));
            return;
        }

        OverviewReport.AddSimple(logger, problems, Problem.Simple(
            "Fallout4.ccc",
            ProblemTypes.FileNotFound,
            "The CC list file was not found in your game install path.\nThis is used to detect which CC modules/archives may be enabled.",
            SolutionTypes.VerifyFiles));
        warnings.Add(new OverviewWarning(
            "Warning", $"{ccc.Name} not found.\nCC files may not be detected. Verifying Steam files or reinstalling should fix this."));
    }

    /// <summary>
    /// <c>%LOCALAPPDATA%\Fallout4\plugins.txt</c>: every <c>*</c> line that is a file in Data (OVW-M4). Without it, a
    /// problem, a warning, and every module in Data not already listed, compared regardless of case (OVW-M5, T-8).
    /// </summary>
    private void AddPlugins(PythonPath data)
    {
        var plugins = KnownFolders.Get(host, KnownFolder.LocalAppData).Join("Fallout4\\plugins.txt");
        string content;
        bool valid;
        try
        {
            content = HeaderFiles.ReadTextLeniently(host, plugins, out valid);
        }
        catch (Exception ex) when (HeaderFiles.IsPermissionOrNotFound(ex))
        {
            OverviewReport.AddSimple(logger, problems, Problem.Simple(
                plugins.Name,
                ProblemTypes.FileNotFound,
                "plugins.txt was not found.\nThis is used to detect which modules/archives are enabled.",
                game.Manager is not null ? "N/A" : "Launch this app with your mod manager."));
            warnings.Add(new OverviewWarning(
                "Warning",
                "plugins.txt not found.\nEnable state of plugins can't be detected.\nCounts will reflect all modules/archives in Data, which is likely higher than your actual counts."));

            var current = new HashSet<PythonPath>(Enabled, PythonPath.Equality);
            // data_path.iterdir(): files and folders alike, in the file system's order (FindFirstFile's on NTFS).
            foreach (var entry in Directory.EnumerateFileSystemEntries(HostPaths.Resolve(host, data)))
            {
                var path = data.Join(Path.GetFileName(entry));
                if (ModuleSuffixes.Contains(path.Suffix.ToLowerInvariant()) && !current.Contains(path))
                {
                    _enabled.Add(path);
                }
            }

            return;
        }

        if (!valid)
        {
            AddNotUtf8(
                plugins.Name,
                "Modules on lines that aren't UTF-8 can't be detected, so counts may be lower than your actual counts.",
                "Save plugins.txt as UTF-8, or have your mod manager write it again.");
        }

        AddEnabled(PythonText.SplitLines(content)
            .Where(line => line.StartsWith('*'))
            .Select(line => data.Join(line[1..]))
            .Where(path => HostPaths.IsFile(host, path)));
    }

    /// <summary>
    /// The problem for a list file (Fallout4.ccc or plugins.txt) that isn't valid UTF-8. The reference's strict read
    /// raised and failed the Overview (T-1); the port reads its UTF-8 lines and reports the file instead (deviation,
    /// bug #86). No warning box is shown, unlike a missing file.
    /// </summary>
    /// <param name="name">The file's name, the problem's path text.</param>
    /// <param name="consequence">The summary's second line: what can't be detected.</param>
    /// <param name="solution">The solution text.</param>
    private void AddNotUtf8(string name, string consequence, string solution)
        => OverviewReport.AddSimple(logger, problems, Problem.Simple(
            name, ScanProblemTypes.UnexpectedFormat, $"{name} is not valid UTF-8.\n{consequence}", solution));

    /// <summary>One module's first 34 bytes: readable, TES4, HEDR, its version (OVW-M6, OVW-M7) and type (OVW-M8).</summary>
    private void Check(PythonPath module)
    {
        var head = HeaderFiles.ReadHead(host, module, 34);
        if (head is null)
        {
            _unreadable.Add(module);
            problems.Add(Problem.ForOverviewFile(
                ProblemTypes.InvalidModule, module, "Failed to read module due to permissions or the file is missing.", null));
            return;
        }

        if (head.Length != 34 || !head.AsSpan(0, 4).SequenceEqual("TES4"u8))
        {
            _unreadable.Add(module);
            problems.Add(Problem.ForOverviewFile(ProblemTypes.InvalidModule, module, "Module is either corrupt or not in TES4 format.", null));
            return;
        }

        if (!head.AsSpan(24, 4).SequenceEqual("HEDR"u8))
        {
            // Unreadable, but with no problem entry, and not counted (OVW-M6).
            _unreadable.Add(module);
            return;
        }

        var version = head.AsSpan(30, 4);
        if (version.SequenceEqual((ReadOnlySpan<byte>)[0x33, 0x33, 0x73, 0x3f]))
        {
            _hedr95.Add(module);
        }
        else if (version.SequenceEqual((ReadOnlySpan<byte>)[0x00, 0x00, 0x80, 0x3f]))
        {
            _hedr100++;
        }
        else
        {
            AddUnknown(module, PythonFloat.Round(BinaryPrimitives.ReadSingleLittleEndian(version), 2));
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8, 4));
        if ((flags & 0x200) != 0 || module.Suffix.ToLowerInvariant() == ".esl")
        {
            _light++;
        }
        else
        {
            _full++;
        }
    }

    /// <summary>
    /// An unsupported HEDR version: recorded against the module (a dict, so a module listed twice keeps its first place)
    /// and reported, naming the games whose versions include its value (OVW-P12, T-3; by value since bug #26, B-7).
    /// </summary>
    private void AddUnknown(PythonPath module, double hedr)
    {
        var text = PythonFloat.Repr(hedr);
        // By value, not by text: "1.70" is 1.7 (bug #26). Both sides are the nearest double to a two-decimal number,
        // so equal values compare exactly.
        var games = VersionSupport
            .Where(s => s.Versions.Any(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) == hedr))
            .Select(s => s.Game)
            .ToList();
        var validGames = games.Count > 0 ? $"\nGames supporting v{text}: " + string.Join(", ", games) : string.Empty;
        if (_hedrUnknownIndex.TryGetValue(module, out var index))
        {
            _hedrUnknown[index] = _hedrUnknown[index] with { Version = hedr };
        }
        else
        {
            _hedrUnknownIndex.Add(module, _hedrUnknown.Count);
            _hedrUnknown.Add(new UnknownHedrModule(module, hedr));
        }

        problems.Add(Problem.ForOverviewFile(
            ProblemTypes.InvalidModule,
            module,
            $"Module version ({text}) is not valid for Fallout 4.{validGames}",
            "It may be possible to open/resave this file with Creation Kit to update its format for Fallout 4.\nYou should compare the original and resaved files with xEdit to verify no undesired changes were made."));
    }
}
