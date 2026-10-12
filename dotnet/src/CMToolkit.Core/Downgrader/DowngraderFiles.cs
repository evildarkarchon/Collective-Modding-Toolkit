using CMToolkit.Core.Overview;

namespace CMToolkit.Core.Downgrader;

/// <summary>
/// The Downgrader's six files and the CRC32s it knows them by (<c>Downgrader.CRCs_game</c> / <c>CRCs_ck</c>, DG-1),
/// in the reference's order: the game's three, then the Creation Kit's three. These tables are the Downgrader's own,
/// separate from the Overview's <see cref="BaseFiles"/>, and a file is only ever recognised by its full-file CRC.
/// </summary>
public static class DowngraderFiles
{
    /// <summary>
    /// <c>CRCs_game</c>: <c>Fallout4.exe</c>, <c>Fallout4Launcher.exe</c> and <c>steam_api64.dll</c>, each with its
    /// CRC → Install Type table. The comments are the reference's game versions.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, IReadOnlyDictionary<string, InstallType>>> Game { get; } =
    [
        new("Fallout4.exe", new Dictionary<string, InstallType>
        {
            ["97DA3E03"] = InstallType.Obsolete, // 1.10.120
            ["2ED2A242"] = InstallType.Obsolete, // 1.10.130
            ["A0100017"] = InstallType.Obsolete, // 1.10.138
            ["9ABC94F0"] = InstallType.Obsolete, // 1.10.162
            ["C6053902"] = InstallType.OldGen, // 1.10.163
            ["B61675B1"] = InstallType.Obsolete, // 1.10.980
            ["C5965A2E"] = InstallType.NextGen, // 1.10.984
            ["0AEB19A7"] = InstallType.Obsolete, // 1.11.137
            ["1E90BE57"] = InstallType.Obsolete, // 1.11.159
            ["0481725D"] = InstallType.Obsolete, // 1.11.169a
            ["0E176ABC"] = InstallType.Obsolete, // 1.11.169b
            ["CF47788D"] = InstallType.Anniversary, // 1.11.191
        }),
        new("Fallout4Launcher.exe", new Dictionary<string, InstallType>
        {
            ["02445570"] = InstallType.OldGen, // 1.10.120 to 1.10.163
            ["F6A06FF5"] = InstallType.NextGen, // 1.10.980 to 1.10.984
            ["0E696744"] = InstallType.Obsolete, // 1.11.137
            ["D15C6A49"] = InstallType.Obsolete, // 1.11.159
            ["8C52BE93"] = InstallType.Obsolete, // 1.11.169a
            ["591009C9"] = InstallType.Obsolete, // 1.11.169b
            ["720BB9C3"] = InstallType.Anniversary, // 1.11.191
        }),
        new("steam_api64.dll", new Dictionary<string, InstallType>
        {
            ["BBD912FC"] = InstallType.OldGen,
            ["E36E7B4D"] = InstallType.NextGenAndAnniversary,
        }),
    ];

    /// <summary>
    /// <c>CRCs_ck</c>: <c>CreationKit.exe</c> and the two Archive2 files, which sit in <c>Tools\Archive2</c> under the
    /// game folder (the names are the reference's relative paths).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, IReadOnlyDictionary<string, InstallType>>> CreationKit { get; } =
    [
        new("CreationKit.exe", new Dictionary<string, InstallType>
        {
            ["0F5C065B"] = InstallType.OldGen,
            ["481CCE95"] = InstallType.NextGen,
            ["49E45284"] = InstallType.Anniversary,
        }),
        new(@"Tools\Archive2\Archive2.exe", new Dictionary<string, InstallType>
        {
            ["4CDFC7B5"] = InstallType.OldGen,
            ["71A5240B"] = InstallType.NextGen,
            ["C867674F"] = InstallType.Anniversary,
        }),
        new(@"Tools\Archive2\Archive2Interop.dll", new Dictionary<string, InstallType>
        {
            ["850D36A9"] = InstallType.OldGen,
            ["EFBE3622"] = InstallType.NextGen,
            ["7B893B0D"] = InstallType.Anniversary,
        }),
    ];

    /// <summary>
    /// <c>CRCs_by_type</c>: every known CRC grouped by its Install Type, in table order, for the Patch All run's
    /// "is this the opposite version" check (DG-5). A Next-Gen &amp; Anniversary CRC counts for both Next-Gen and
    /// Anniversary; Old-Gen, Next-Gen, Anniversary and Obsolete always have an entry.
    /// </summary>
    public static IReadOnlyDictionary<InstallType, IReadOnlyList<string>> CrcsByType { get; } = GroupByType();

    private static Dictionary<InstallType, IReadOnlyList<string>> GroupByType()
    {
        // The reference's MappingProxyType has exactly these four keys; its derivation loop appends to them in place.
        var groups = new Dictionary<InstallType, List<string>>
        {
            [InstallType.Obsolete] = [],
            [InstallType.OldGen] = [],
            [InstallType.NextGen] = [],
            [InstallType.Anniversary] = [],
        };
        foreach (var (_, crcs) in Game.Concat(CreationKit))
        {
            foreach (var (crc, type) in crcs)
            {
                if (type == InstallType.NextGenAndAnniversary)
                {
                    groups[InstallType.NextGen].Add(crc);
                    groups[InstallType.Anniversary].Add(crc);
                }
                else
                {
                    groups[type].Add(crc);
                }
            }
        }

        return groups.ToDictionary(g => g.Key, IReadOnlyList<string> (g) => g.Value);
    }
}
