using Avalonia.Media;

namespace CmtShellPrototype;

/// <summary>One row of the Binaries box: file stem, Install Type text, version shown on hover, colour.</summary>
public sealed record BinaryRow(string Name, string InstallType, string Version, IBrush Brush);

/// <summary>One row of a count block (General/Texture/Total, Full/Light/Total): label, count, limit, colour.</summary>
public sealed record CountRow(string Label, string Count, string Limit, IBrush Brush);

/// <summary>One row in the TreeWindow sample.</summary>
public sealed record HedrRow(string Hedr, string Module);

/// <summary>
/// PROTOTYPE canned, read-only Overview data. No Core, no detection - the question is purely how it looks.
/// Colours follow tabs/_overview.py: counts are good below 95% of the limit, warning up to it, bad above.
/// </summary>
public sealed class OverviewData
{
    public static readonly IBrush Default = new SolidColorBrush(Color.Parse("#CACACA"));
    public static readonly IBrush Good = new SolidColorBrush(Color.Parse("#619267"));
    public static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#AF5A66"));
    public static readonly IBrush Neutral1 = new SolidColorBrush(Color.Parse("#808080"));
    public static readonly IBrush Neutral2 = new SolidColorBrush(Color.Parse("#FFE4C4"));
    public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#FFA500"));

    public bool ShowUpdateBanner { get; init; }
    public string ModManager { get; init; } = "Not Found";
    public IBrush ModManagerBrush { get; init; } = Bad;
    public bool ShowModManagerInfoIcon { get; init; }
    public bool ShowOsWarningIcon { get; init; }
    public string GamePath { get; init; } = @"E:\SteamLibrary\steamapps\common\Fallout 4";
    public string InstallType { get; init; } = "Anniversary";
    public string Specs1 { get; init; } = "Windows 11\n64GB RAM";
    public string Specs2 { get; init; } = "AMD Ryzen 7 7800X3D\nNVIDIA GeForce RTX 4070 12GB";

    public required IReadOnlyList<BinaryRow> Binaries { get; init; }
    public string AddressLibrary { get; init; } = "Installed";
    public IBrush AddressLibraryBrush { get; init; } = Good;

    public required IReadOnlyList<CountRow> Archives { get; init; }
    public string ArchivesUnreadable { get; init; } = "0";
    public IBrush ArchivesUnreadableBrush { get; init; } = Neutral1;
    public string ArchivesOg { get; init; } = "468";
    public string ArchivesNg { get; init; } = "435";

    public required IReadOnlyList<CountRow> Modules { get; init; }
    public string ModulesUnreadable { get; init; } = "0";
    public IBrush ModulesUnreadableBrush { get; init; } = Neutral1;
    public string Hedr100 { get; init; } = "940";
    public string Hedr95 { get; init; } = "194";
    public string HedrUnknown { get; init; } = "0";
    public IBrush HedrUnknownBrush { get; init; } = Neutral1;
    public bool ShowHedrInfoIcon { get; init; }

    /// <summary>The exact values in python-overview.png (this machine, no mod manager).</summary>
    public static OverviewData Baseline() => new()
    {
        Binaries =
        [
            new("Fallout4", "Anniversary", "1.11.191.0", Good),
            new("Fallout4Launcher", "Anniversary", "1.11.191.0", Good),
            new("steam_api64", "Anniversary", "7.40.51.27", Good),
            new("f4se_loader", "Anniversary", "0.7.7.0", Good),
            new("f4se_steam_loader", "Old-Gen", "0.6.23.0", Bad),
            new("CreationKit", "Anniversary", "1.11.191.0", Good),
            new("Archive2", "Anniversary", "1.1.0.4", Good),
        ],
        Archives =
        [
            new("General:", "510", " / 1024", Good),
            new("Texture:", "393", " / 1023", Good),
            new("Total:", "903", " / 2047", Good),
        ],
        Modules =
        [
            new("Full:", "210", " /  254", Good),
            new("Light:", "924", " / 4096", Good),
            new("Total:", "1134", " / 4350", Good),
        ],
    };

    /// <summary>Every icon, colour and the update banner at once.</summary>
    public static OverviewData Problems() => new()
    {
        ShowUpdateBanner = true,
        ModManager = "Mod Organizer v2.5.2 [Profile: Default]",
        ModManagerBrush = Neutral2,
        ShowModManagerInfoIcon = true,
        ShowOsWarningIcon = true,
        InstallType = "Next-Gen",
        Specs1 = "Windows 11 24H2\n32GB RAM",
        Binaries =
        [
            new("Fallout4", "Next-Gen", "1.10.984.0", Good),
            new("Fallout4Launcher", "Next-Gen", "1.10.984.0", Good),
            new("steam_api64", "Old-Gen", "2.89.45.4", Bad),
            new("f4se_loader", "Not Found", "", Bad),
            new("f4se_steam_loader", "Not Found", "", Bad),
            new("CreationKit", "Not Found", "", Neutral1),
            new("Archive2", "Unknown", "1.1.0.9", Bad),
        ],
        AddressLibrary = "Not Found",
        AddressLibraryBrush = Bad,
        Archives =
        [
            new("General:", "1001", " / 1024", Warning),
            new("Texture:", "1100", " / 1023", Bad),
            new("Total:", "2101", " / 2047", Bad),
        ],
        ArchivesUnreadable = "2",
        ArchivesUnreadableBrush = Bad,
        Modules =
        [
            new("Full:", "260", " /  254", Bad),
            new("Light:", "3000", " / 4096", Good),
            new("Total:", "3260", " / 4350", Good),
        ],
        ModulesUnreadable = "1",
        ModulesUnreadableBrush = Bad,
        HedrUnknown = "3",
        HedrUnknownBrush = Bad,
        ShowHedrInfoIcon = true,
    };

    public static IReadOnlyList<HedrRow> HedrRows { get; } =
    [
        new("1.71", "SkyrimPort.esp"),
        new("1.7", "OldSkyrimThing.esm"),
        new("0.94", "Fallout3Leftover.esp"),
    ];
}
