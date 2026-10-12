using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;

namespace CMToolkit.App.Startup;

/// <summary>
/// <c>GameInfo.__init__</c> as the App runs it: the Core calls for the mod manager, MO2's configuration, the game path
/// and the game INIs, with the dialogs Core never shows itself in between (GP-2, GP-3).
/// </summary>
public static class GameInstallLocator
{
    /// <summary>
    /// Finds the mod manager (MM-1) and MO2's configuration (MO2-1..MO2-3), then the game path (GP-1). If nothing finds
    /// it, asks whether to locate it by hand, then for <c>Fallout4.exe</c> (GP-2); an invalid location, or a registry
    /// path that isn't one, shows <c>Game not found</c> (GP-3). Then derives Data and F4SE Plugins (GP-4) and reads the
    /// game INIs (INI-1..INI-4).
    /// </summary>
    /// <param name="host">The machine.</param>
    /// <param name="dialogs">The dialogs, shown from the calling (UI) thread.</param>
    /// <returns>
    /// The installation; or null where the reference calls <c>sys.exit()</c> (No, a cancelled file dialog, or the error
    /// box's dismissal), which ends the process silently with exit code 0.
    /// </returns>
    /// <remarks>
    /// The Core calls run with <see cref="Task.Run(Action)"/> rather than through the runner: there is no window yet to
    /// block, and a failure belongs in the startup failure dialog (SHELL-13), so exceptions propagate to the caller.
    /// </remarks>
    public static async Task<GameInstall?> LocateAsync(IHostEnvironment host, IStartupDialogs dialogs)
    {
        var (manager, mo2, found) = await Task.Run(() =>
        {
            var manager = ModManagerDetection.Find(host);
            var mo2 = manager is null ? null : Mo2ConfigurationReader.Locate(manager, host);
            return (manager, mo2, GamePathDetection.Detect(mo2, host));
        });

        if (found is GamePathResult.NeedsUserLocation)
        {
            if (!await dialogs.AskYesNoAsync(GamePathDetection.AskTitle, GamePathDetection.AskMessage))
            {
                return null;
            }

            var picked = await dialogs.AskOpenFileNameAsync(
                GamePathDetection.PickTitle, GamePathDetection.PickFileTypeName, GamePathDetection.PickFileTypePattern);
            if (picked.Length == 0)
            {
                await dialogs.ShowErrorAsync(GamePathDetection.NotFoundTitle, GamePathDetection.NotFoundMessage);
                return null;
            }

            // Asking happens only when no registry value existed, so the error never names one.
            found = await Task.Run(() => GamePathDetection.ValidateUserLocation(picked, registryPath: null, host));
        }

        switch (found)
        {
            case GamePathResult.Invalid invalid:
                await dialogs.ShowErrorAsync(GamePathDetection.NotFoundTitle, invalid.Message);
                return null;
            case GamePathResult.Found { GamePath: var gamePath }:
                return await Task.Run(() => new GameInstall(manager, mo2, GameDirectories.Of(gamePath, host), GameIniReader.Read(host)));
            default:
                throw new InvalidOperationException($"Unexpected game path result {found}.");
        }
    }
}
