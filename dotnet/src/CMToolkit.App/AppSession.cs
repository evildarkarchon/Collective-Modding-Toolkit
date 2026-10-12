using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
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

    /// <summary>The machine startup ran on, which the tabs keep reading (file versions, the exe name).</summary>
    public IHostEnvironment Host { get; init; } = SystemHostEnvironment.Instance;

    /// <summary>
    /// The app's own folder (<see cref="global::CMToolkit.App.AppFolder"/>), where the Downgrader saves and looks for
    /// Delta Patches. The exe's folder by default; the reference used the working directory (bug #36, fixed as a
    /// Deviation, ADR-0006).
    /// </summary>
    public string AppFolder { get; init; } = AppContext.BaseDirectory;

    /// <summary>
    /// The current Overview report (the reference's <c>cmc.game</c> Overview state and <c>cmc.overview_problems</c>),
    /// which the Overview tab sets and the F4SE, Scanner and Downgrader read.
    /// </summary>
    public OverviewState Overview { get; } = new();

    /// <summary>
    /// The main window built over this session (the reference's <c>cmc.root</c>), which owns the windows the tabs open,
    /// such as the Downgrader. Set once it is built, after the tabs are composed; null before.
    /// </summary>
    public Views.MainWindow? MainWindow { get; set; }

    /// <summary>
    /// <c>cmc.refresh_tab(Tab.Overview)</c>, which the Archive Patcher and the Downgrader call: logs
    /// <c>Refresh Tab : Overview</c> at DEBUG, then refreshes the Overview tab as its button does (OVW-8).
    /// </summary>
    /// <remarks>
    /// There is one refresh registration: the Overview tab sets <see cref="ViewModels.OverviewState.Refresher"/> on
    /// <see cref="Overview"/> when it is built. The Scanner (#56) calls <see cref="ViewModels.OverviewState.RefreshAsync"/>
    /// itself and logs the DEBUG line there; both paths end in <see cref="Tabs.OverviewTab.RefreshAsync"/>.
    /// </remarks>
    /// <returns>
    /// False when the refresh failed (already reported), which in the reference raised out of the caller and skipped the
    /// rest of it; true otherwise, including when there is no Overview tab to refresh.
    /// </returns>
    public async Task<bool> RefreshOverviewAsync()
    {
        Runtime.Logger.Write(Microsoft.Extensions.Logging.LogLevel.Debug, "Refresh Tab : Overview");
        return Overview.Refresher is null || await Overview.RefreshAsync();
    }

    /// <summary>
    /// What the Downgrader downloads Delta Patches through (NET-3): <see cref="Core.Downgrader.DeltaPatches.CreateHandler"/>
    /// at startup, a scenario's canned responses when the exe runs on one, a fake in tests. Null, as in tests that set
    /// none, makes every download fail rather than reach the network.
    /// </summary>
    public HttpMessageHandler? DeltaPatchHttp { get; set; }
}
