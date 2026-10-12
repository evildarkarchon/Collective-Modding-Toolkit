using CMToolkit.Core.EnvironmentDetection;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.F4se;

/// <summary>One inspected DLL: its file name (the tree's first column) and what it is.</summary>
/// <param name="Name">The file name, as listed.</param>
/// <param name="Info">Its inspection.</param>
public sealed record F4seDll(string Name, F4sePluginInfo Info);

/// <summary>What the F4SE tab's load found (<c>F4SETab._load</c>).</summary>
/// <param name="LoadingError">
/// Why the tab can't load (F4SE-1), shown in place of the tab and logged; null when it loaded.
/// </param>
/// <param name="Dlls">The inspected DLLs in directory order (<c>dll_info</c>); empty when it didn't load.</param>
public sealed record F4seScanResult(string? LoadingError, IReadOnlyList<F4seDll> Dlls);

/// <summary>
/// <c>F4SETab._load</c>: checks that <c>Data</c> and <c>Data\F4SE\Plugins</c> were found, then inspects every DLL in the
/// plugins folder (F4SE-1..F4SE-3).
/// </summary>
public static class F4seScan
{
    /// <summary>The load error without a <c>Data</c> folder (F4SE-1).</summary>
    public const string NoDataFolder = "Data folder not found";

    /// <summary>The load error without a <c>Data\F4SE\Plugins</c> folder (F4SE-1).</summary>
    public const string NoPluginsFolder = "Data/F4SE/Plugins folder not found";

    /// <summary>Appended to <see cref="NoPluginsFolder"/> when no mod manager launched the app (F4SE-1).</summary>
    public const string LaunchViaManager = "\nTry launching via your mod manager.";

    /// <summary>
    /// Inspects the plugins folder of <paramref name="game"/>, logging <c>Scanning &lt;name&gt;</c> at debug before each
    /// DLL (F4SE-2). Every entry is listed in directory order, folders included; an entry counts when its suffix is
    /// <c>.dll</c> in any case and its name doesn't start with <c>msdia</c> in lower case.
    /// </summary>
    /// <param name="game">The installation found at startup; its <c>Data</c> and plugins folders were checked then (GP-4).</param>
    /// <param name="host">The machine, whose working directory a relative game path is relative to.</param>
    /// <param name="logger">The app log.</param>
    /// <returns>The DLLs, or the load error.</returns>
    /// <exception cref="F4sePluginLoadException">
    /// A DLL couldn't be mapped. The DLLs before it were inspected and those after it never are: the whole load fails
    /// (B-18).
    /// </exception>
    /// <exception cref="IOException">The plugins folder can't be listed any more.</exception>
    public static F4seScanResult Run(GameInstall game, IHostEnvironment host, ILogger logger)
    {
        if (game.DataPath is null)
        {
            return new F4seScanResult(NoDataFolder, []);
        }

        if (game.F4sePath is not { } plugins)
        {
            return new F4seScanResult(game.Manager is null ? NoPluginsFolder + LaunchViaManager : NoPluginsFolder, []);
        }

        var dlls = new List<F4seDll>();
        // iterdir(): os.scandir, the file system's own order, hidden and system entries and folders included.
        foreach (var entry in new DirectoryInfo(HostPaths.Resolve(host, plugins)).EnumerateFileSystemInfos())
        {
            var name = entry.Name;
            if (!Suffix(name).Equals(".dll", StringComparison.OrdinalIgnoreCase) || name.StartsWith("msdia", StringComparison.Ordinal))
            {
                continue;
            }

            logger.Write(LogLevel.Debug, $"Scanning {name}");
            dlls.Add(new F4seDll(name, F4sePluginInspector.Inspect(entry.FullName)));
        }

        return new F4seScanResult(null, dlls);
    }

    /// <summary>
    /// pathlib's <c>suffix</c>: from the last dot, unless the name starts with its only dot or ends with it, which
    /// gives none (<c>.dll</c> alone has no suffix).
    /// </summary>
    private static string Suffix(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[dot..] : "";
    }
}
