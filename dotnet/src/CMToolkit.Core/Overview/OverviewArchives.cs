using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.Overview;

/// <summary>
/// What <c>get_info_archives</c> leaves in <c>GameInfo</c> (OVW-A1..OVW-A5), for the Archives frame, the Scanner and
/// the Archive Patcher. The reference holds each path list as a set, so each has one entry per archive regardless of
/// case (T-8), in no defined order (T-15).
/// </summary>
/// <param name="Enabled"><c>archives_enabled</c>: every archive the game would load.</param>
/// <param name="Unreadable"><c>archives_unreadable</c>.</param>
/// <param name="OldGen"><c>archives_og</c>: the readable v1 archives.</param>
/// <param name="NextGen"><c>archives_ng</c>: the readable v7 and v8 archives.</param>
/// <param name="General">The readable General (GNRL) archives counted (<c>ba2_count_gnrl</c>).</param>
/// <param name="Texture">The readable Texture (DX10) archives counted (<c>ba2_count_dx10</c>).</param>
public sealed record OverviewArchives(
    IReadOnlyList<PythonPath> Enabled,
    IReadOnlyList<PythonPath> Unreadable,
    IReadOnlyList<PythonPath> OldGen,
    IReadOnlyList<PythonPath> NextGen,
    int General,
    int Texture)
{
    /// <summary>The state before any gathering (<c>reset_archives</c>), and after one that found no Data folder.</summary>
    public static OverviewArchives None { get; } = new([], [], [], [], 0, 0);
}

/// <summary>
/// <c>OverviewTab.get_info_archives</c>: builds the enabled archive set from the INI lists, the enabled modules, NVFlex
/// and the Anniversary TexturesPatch, then reads each archive's header (OVW-A1..OVW-A4).
/// </summary>
internal sealed class ArchivesGathering(
    GameInstall game, IHostEnvironment host, ILogger logger, List<Problem> problems, IReadOnlyList<PythonPath> modules, InstallType installType)
{
    /// <summary>The <c>[Archive]</c> settings whose comma-separated archives the game loads, in the reference's order.</summary>
    private static readonly string[] ArchiveLists =
        ["sresourceindexfilelist", "sresourcestartuparchivelist", "sresourcearchivelist", "sresourcearchivelist2"];

    private readonly PathSet _enabled = new();
    private readonly PathSet _unreadable = new();
    private readonly PathSet _oldGen = new();
    private readonly PathSet _nextGen = new();
    private int _general;
    private int _texture;

    /// <summary>What has been gathered so far, as a report part.</summary>
    public OverviewArchives Snapshot()
        => new(_enabled.Snapshot(), _unreadable.Snapshot(), _oldGen.Snapshot(), _nextGen.Snapshot(), _general, _texture);

    /// <summary>
    /// Runs <c>get_info_archives</c>. INIs without an <c>[Archive]</c> section are a problem rather than the
    /// reference's <c>ValueError</c> (OVW-A2, B-15; deviation, bug #31).
    /// </summary>
    public void Run()
    {
        logger.Write(LogLevel.Debug, "Gathering Info: Archives");
        logger.Write(LogLevel.Debug, "Reset Info: Archives");

        var data = game.DataPath;
        if (data is null)
        {
            // Already reported by the modules (OVW-A1).
            logger.Write(LogLevel.Debug, "Archives: No Data Path");
            return;
        }

        if (game.Inis.Settings.TryGetValue("archive", out var section))
        {
            foreach (var list in ArchiveLists)
            {
                foreach (var name in (section.TryGetValue(list, out var value) ? value : string.Empty).Split(','))
                {
                    AddIfFile(data.Join(PythonText.Strip(name)));
                }
            }
        }
        else
        {
            // The reference raised ValueError("Archive section missing from INIs") here, which failed the Overview and
            // every Scanner run's refresh (OVW-A2, B-15). The port reports it and goes on without the INI lists
            // (deviation, bug #31).
            OverviewReport.AddSimple(logger, problems, Problem.Simple(
                "[Archive]",
                ProblemTypes.FileNotFound,
                "The [Archive] section was not found in your game INIs (Fallout4.ini may not have been generated yet).\nArchives listed only in the INIs can't be detected, so archive counts may be lower than your actual counts.",
                "Launch the game once with its launcher to generate Fallout4.ini.\nOtherwise, verify files with Steam or reinstall the game."));
        }

        // Each enabled module's "<stem> - <suffix>.ba2" beside it, for every language suffix (INI-4).
        foreach (var module in modules)
        {
            foreach (var suffix in game.Inis.Ba2Suffixes)
            {
                AddIfFile(module.WithName($"{module.Stem} - {suffix}.ba2"));
            }
        }

        var nvflex = game.Inis.Prefs.TryGetValue("nvflex", out var prefs) && prefs.TryGetValue("bnvflexenable", out var flag) ? flag : "0";
        if (nvflex == "1")
        {
            AddRequired(
                data,
                "Fallout4 - Nvflex.ba2",
                "Nvidia Flex is enabled in your game INIs (bNVFlexEnable=1) but the Nvflex BA2 is missing.\nThe exe is hardcoded to load this BA2.");
        }

        if (installType == InstallType.Anniversary)
        {
            AddRequired(
                data,
                "Fallout4 - TexturesPatch.ba2",
                "Your game is Anniversary Edition but the TexturesPatch BA2 is missing.\nThe exe is hardcoded to load this BA2.");
        }

        foreach (var archive in _enabled.Items)
        {
            logger.Write(LogLevel.Debug, $"Archives: Checking {archive.Name}");
            Check(archive);
        }
    }

    private void AddIfFile(PythonPath path)
    {
        if (HostPaths.IsFile(host, path))
        {
            _enabled.Add(path);
        }
    }

    /// <summary>An archive the exe always loads: enabled when present, otherwise a problem (OVW-P13, OVW-P14).</summary>
    private void AddRequired(PythonPath data, string name, string summary)
    {
        var path = data.Join(name);
        if (HostPaths.IsFile(host, path))
        {
            _enabled.Add(path);
            return;
        }

        OverviewReport.AddSimple(logger, problems, Problem.Simple(name, ProblemTypes.FileNotFound, summary, SolutionTypes.VerifyFiles));
    }

    /// <summary>One archive's first 12 bytes: readable, BTDX, its version and its format (OVW-A4).</summary>
    private void Check(PythonPath archive)
    {
        var head = HeaderFiles.ReadHead(host, archive, 12);
        if (head is null)
        {
            Unreadable(archive, "Failed to read archive due to permissions or the file is missing.");
            return;
        }

        if (head.Length != 12 || !head.AsSpan(0, 4).SequenceEqual("BTDX"u8))
        {
            Unreadable(archive, "Archive is either corrupt or not in Bethesda Archive 2 format.");
            return;
        }

        bool nextGen;
        switch (head[4])
        {
            case 1:
                nextGen = false;
                break;
            case 7 or 8:
                nextGen = true;
                break;
            default:
                Unreadable(archive, $"Archive version ({head[4]}) is not valid for Fallout 4.");
                return;
        }

        var format = head.AsSpan(8, 4);
        if (format.SequenceEqual("GNRL"u8))
        {
            _general++;
        }
        else if (format.SequenceEqual("DX10"u8))
        {
            _texture++;
        }
        else
        {
            // The reference decoded the format strictly, so bytes that aren't UTF-8 raised and failed the Overview
            // (T-1). The port replaces them with U+FFFD, as errors="replace" would (deviation, bug #86).
            Unreadable(archive, $"Archive format ({HeaderFiles.LenientUtf8.GetString(format)}) is not valid for Fallout 4.");
            return;
        }

        (nextGen ? _nextGen : _oldGen).Add(archive);
    }

    /// <summary>Records <paramref name="archive"/> as unreadable and adds its Invalid Archive problem with <paramref name="summary"/>.</summary>
    private void Unreadable(PythonPath archive, string summary)
    {
        _unreadable.Add(archive);
        problems.Add(Problem.ForOverviewFile(ProblemTypes.InvalidArchive, archive, summary, null));
    }
}
