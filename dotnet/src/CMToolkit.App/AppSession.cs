using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core.EnvironmentDetection;

namespace CMToolkit.App;

/// <summary>
/// Everything startup produced that the tabs and the update check build on, in place of the reference's
/// <c>CMChecker</c> (<c>cmc</c>): the runtime, the main window's model (settings and Download Source), the PC info and
/// the immutable game installation. Built once, after the game INIs and before the main window (SHELL-5).
/// </summary>
/// <param name="Runtime">The app runtime: log, error reporting, input block and background operations.</param>
/// <param name="Model">The main window's model, which holds the settings store (SET-7).</param>
/// <param name="Pc">The PC info (<c>cmc.pc</c>, PC-1..PC-5).</param>
/// <param name="Game">The game installation (<c>cmc.game</c>'s startup part).</param>
public sealed record AppSession(AppRuntime Runtime, MainWindowViewModel Model, PcInfo Pc, GameInstall Game)
{
    /// <summary>The settings in effect, which the Settings tab, the Scanner and the Downgrader read and save.</summary>
    public SettingsStore Settings => Model.Settings;
}
