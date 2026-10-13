namespace CMToolkit.Core.Overview;

/// <summary>
/// <c>globals.BASE_FILES</c>, verbatim (OVW-B1, OVW-B2): the game binaries the Overview checks, in its order, each with
/// the file versions (<c>a.b.c.d</c>) and full-file CRC32s (8 uppercase hex digits) it recognises. A key is a path below
/// the game folder.
/// </summary>
public static class BaseFiles
{
    /// <summary>The binaries in check order, each with its version/CRC → Install Type table.</summary>
    public static IReadOnlyList<KeyValuePair<string, IReadOnlyDictionary<string, InstallType>>> All { get; } =
    [
        new("Fallout4.exe", new Dictionary<string, InstallType>
        {
            ["1.10.120.0"] = InstallType.Obsolete,
            ["1.10.130.0"] = InstallType.Obsolete,
            ["1.10.138.0"] = InstallType.Obsolete,
            ["1.10.162.0"] = InstallType.Obsolete,
            ["1.10.163.0"] = InstallType.OldGen,
            ["1.10.980.0"] = InstallType.Obsolete,
            ["1.10.984.0"] = InstallType.NextGen,
            ["1.11.137.0"] = InstallType.Obsolete,
            ["1.11.159.0"] = InstallType.Obsolete,
            ["1.11.169.0"] = InstallType.Obsolete,
            ["1.11.191.0"] = InstallType.Obsolete,
            ["1.11.221.0"] = InstallType.Obsolete,
            // The reference's own note: if we ever get another update, mark as Obsolete2 to keep the 1024 BA2 changes.
            ["1.11.240.0"] = InstallType.Anniversary,
        }),
        new("Fallout4Launcher.exe", new Dictionary<string, InstallType>
        {
            ["02445570"] = InstallType.OldGen, // 1.10.120 to 1.10.163
            ["F6A06FF5"] = InstallType.NextGen, // 1.10.980 to 1.10.984
            ["0E696744"] = InstallType.Obsolete, // 1.11.137
            ["D15C6A49"] = InstallType.Obsolete, // 1.11.159
            ["8C52BE93"] = InstallType.Obsolete, // 1.11.169a
            ["591009C9"] = InstallType.Obsolete, // 1.11.169b
            ["720BB9C3"] = InstallType.Obsolete, // 1.11.191
            ["CA61EDD7"] = InstallType.Obsolete, // 1.11.221
            ["57D1C794"] = InstallType.Anniversary, // 1.11.240
        }),
        new("steam_api64.dll", new Dictionary<string, InstallType>
        {
            ["2.89.45.4"] = InstallType.OldGen, // 1.10.120 to 1.10.163
            ["7.40.51.27"] = InstallType.NextGenAndAnniversary, // 1.10.980 to 1.11.240
            ["BD3AA35F"] = InstallType.OldGen, // GOG
        }),
        new("f4se_loader.exe", new Dictionary<string, InstallType>
        {
            ["0.0.6.23"] = InstallType.OldGen,
            ["0.0.7.2"] = InstallType.NextGen,
            ["0.0.7.4"] = InstallType.Obsolete,
            ["0.0.7.5"] = InstallType.Obsolete,
            ["0.0.7.6"] = InstallType.Obsolete,
            ["0.0.7.7"] = InstallType.Obsolete,
            ["0.0.7.8"] = InstallType.Obsolete,
            ["0.0.7.9"] = InstallType.Anniversary,
        }),
        new("f4se_steam_loader.dll", new Dictionary<string, InstallType>
        {
            ["0.0.6.23"] = InstallType.OldGen,
        }),
        new("CreationKit.exe", new Dictionary<string, InstallType>
        {
            ["1.10.162.0"] = InstallType.OldGen,
            ["1.10.943.1"] = InstallType.Obsolete,
            ["1.10.982.3"] = InstallType.NextGen,
            ["1.11.137.0"] = InstallType.Obsolete,
            ["1.11.240.0"] = InstallType.Anniversary,
        }),
        new(@"Tools\Archive2\Archive2.exe", new Dictionary<string, InstallType>
        {
            ["4CDFC7B5"] = InstallType.OldGen,
            ["71A5240B"] = InstallType.NextGen,
            ["C867674F"] = InstallType.Obsolete,
            ["9A059211"] = InstallType.Anniversary,
        }),
    ];

    /// <summary><c>globals.NG_STARTUP_BA2_CRC</c>: the Next-Gen <c>Fallout4 - Startup.ba2</c>'s CRC32 past its header (OVW-B6).</summary>
    public const string NextGenStartupBa2Crc = "A5808F5F";
}
