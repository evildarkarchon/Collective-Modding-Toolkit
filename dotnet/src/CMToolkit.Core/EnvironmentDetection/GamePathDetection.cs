using System.Security;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// What the game path search found (GP-1). Core never prompts: the App asks the user when it gets
/// <see cref="NeedsUserLocation"/>, and shows the error and exits on <see cref="Invalid"/> (GP-2, GP-3).
/// </summary>
public abstract record GamePathResult
{
    private GamePathResult()
    {
    }

    /// <summary>The game folder, as the reference's <c>Path</c> (kept as given; only <c>str()</c> normalises it).</summary>
    public sealed record Found(PythonPath GamePath) : GamePathResult;

    /// <summary>
    /// Nothing found the game, and no registry value named a folder: the reference asks the user (GP-2). A location they
    /// give is checked with <see cref="GamePathDetection.ValidateUserLocation"/>, with no registry path.
    /// </summary>
    public sealed record NeedsUserLocation : GamePathResult;

    /// <summary>
    /// A location that isn't a Fallout 4 folder. The reference shows <see cref="GamePathDetection.NotFoundTitle"/> with
    /// <paramref name="Message"/>, then exits (GP-3).
    /// </summary>
    public sealed record Invalid(string Message) : GamePathResult;
}

/// <summary>
/// The game path part of <c>GameInfo.find_path</c> after the mod manager part (<see cref="Mo2ConfigurationReader"/>):
/// MO2's <c>gamePath</c>, the CWD, the Steam and GOG registry values, then the user (GP-1..GP-3).
/// </summary>
public static class GamePathDetection
{
    /// <summary>The title of the "Game not found" error box (GP-2, GP-3).</summary>
    public const string NotFoundTitle = "Game not found";

    /// <summary>The plain "Game not found" message: after a cancelled file dialog, or a bad pick with no registry path.</summary>
    public const string NotFoundMessage = "A Fallout 4 installation could not be found.";

    /// <summary>The title of the yes/no box that asks for the location (GP-2).</summary>
    public const string AskTitle = "Fallout 4 Not Found";

    /// <summary>The text of the yes/no box that asks for the location (GP-2).</summary>
    public const string AskMessage =
        "Your Fallout 4 installation could not be detected.\n"
        + "This is usually due to the game being moved or the launcher not being run once from its current location.\n\n"
        + "Manually specify a location?\n"
        + "CM Toolkit will close otherwise.";

    /// <summary>The file dialog's title (GP-2).</summary>
    public const string PickTitle = "Select Fallout4.exe";

    /// <summary>The file dialog's one file type: its name...</summary>
    public const string PickFileTypeName = "Fallout 4";

    /// <summary>...and its pattern.</summary>
    public const string PickFileTypePattern = "Fallout4.exe";

    private const string SteamKey = @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4";
    private const string GogKey = @"SOFTWARE\WOW6432Node\GOG.com\Games\1998527297";

    /// <summary>
    /// The search, in the reference's order (GP-1):
    /// <list type="number">
    /// <item>MO2's <c>gamePath</c>, when MO2 has one. It is <b>not validated</b>; an empty value is <c>.</c>, the CWD (T-7).</item>
    /// <item>The CWD, if it is an FO4 dir (a folder holding a <c>Fallout4.exe</c> file).</item>
    /// <item>The Steam <c>Installed Path</c>, else the GOG <c>path</c>: the first that is a non-empty <c>REG_SZ</c>. It is
    /// validated as a user location would be, but an invalid one names itself in the error (GP-3), and is never
    /// followed by the GOG value or by asking (bug #80).</item>
    /// <item>Otherwise <see cref="GamePathResult.NeedsUserLocation"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="mo2">The MO2 configuration, or null without MO2 (Vortex, or no manager).</param>
    /// <param name="host">The machine: the CWD and the registry.</param>
    public static GamePathResult Detect(Mo2Configuration? mo2, IHostEnvironment host)
    {
        if (mo2?.GamePath is { } mo2GamePath)
        {
            return new GamePathResult.Found(mo2GamePath);
        }

        var cwd = new PythonPath(host.CurrentDirectory);
        if (IsFo4Directory(host, cwd))
        {
            return new GamePathResult.Found(cwd);
        }

        var registryPath = RegistryString(host, SteamKey, "Installed Path") ?? RegistryString(host, GogKey, "path");
        return registryPath is null ? new GamePathResult.NeedsUserLocation() : ValidateUserLocation(registryPath, registryPath, host);
    }

    /// <summary>
    /// Checks a location (GP-3): a file is reduced to its folder, which must be an FO4 dir.
    /// </summary>
    /// <param name="path">The location as given: a registry value, or the file the user picked.</param>
    /// <param name="registryPath">The registry value the location came from, which the error names; null otherwise.</param>
    /// <param name="host">The machine, for resolving a relative path against the CWD.</param>
    /// <returns><see cref="GamePathResult.Found"/> or <see cref="GamePathResult.Invalid"/>.</returns>
    public static GamePathResult ValidateUserLocation(string path, string? registryPath, IHostEnvironment host)
    {
        var location = new PythonPath(path);
        if (HostPaths.IsFile(host, location))
        {
            location = location.Parent;
        }

        if (IsFo4Directory(host, location))
        {
            return new GamePathResult.Found(location);
        }

        return new GamePathResult.Invalid(registryPath is null
            ? NotFoundMessage
            : NotFoundMessage + "\n\n"
              + "The path set in your registry is:\n"
              + registryPath + "\n\n"
              + "If this is not correct, please run the Fallout 4 Launcher to correct it.");
    }

    /// <summary><c>utils.is_fo4_dir</c>: a folder holding a <c>Fallout4.exe</c> file.</summary>
    private static bool IsFo4Directory(IHostEnvironment host, PythonPath path)
        => HostPaths.IsDirectory(host, path) && HostPaths.IsFile(host, path.Join("Fallout4.exe"));

    /// <summary>
    /// <c>utils.get_registry_value</c>: the value if it is a non-empty <c>REG_SZ</c>, else null. Any registry failure
    /// (Python's <c>OSError</c>) means null.
    /// </summary>
    private static string? RegistryString(IHostEnvironment host, string keyPath, string valueName)
    {
        try
        {
            return host.ReadRegistryValue(RegistryRoot.LocalMachine, keyPath, valueName).AsRegSz is { Length: > 0 } value ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}

/// <summary>The game folder and the folders derived from it (GP-4).</summary>
/// <param name="GamePath">The game folder.</param>
/// <param name="DataPath"><c>&lt;game&gt;\Data</c> if it is a folder, else null.</param>
/// <param name="F4sePath"><c>&lt;game&gt;\Data\F4SE\Plugins</c> if Data is a folder and it is too, else null.</param>
public sealed record GameDirectories(PythonPath GamePath, PythonPath? DataPath, PythonPath? F4sePath)
{
    /// <summary>
    /// The <c>GameInfo.game_path</c> setter: derives Data and F4SE Plugins from <paramref name="gamePath"/>. The derived
    /// paths are joined, not resolved, so a relative game path gives relative ones (T-7); existence is checked against
    /// the CWD.
    /// </summary>
    public static GameDirectories Of(PythonPath gamePath, IHostEnvironment host)
    {
        var data = gamePath.Join("Data");
        if (!HostPaths.IsDirectory(host, data))
        {
            return new GameDirectories(gamePath, null, null);
        }

        var f4se = data.Join("F4SE/Plugins");
        return new GameDirectories(gamePath, data, HostPaths.IsDirectory(host, f4se) ? f4se : null);
    }
}
