using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;

namespace CMToolkit.App;

/// <summary>
/// Everything startup produced that the tabs and the update check build on, in place of the reference's
/// <c>CMChecker</c> (<c>cmc</c>): the runtime, the main window's model (settings and Download Source), the PC info and
/// the immutable game installation. Built once, after the game INIs and before the main window (SHELL-5).
/// </summary>
/// <param name="Runtime">The app runtime: log, error reporting, input block and UI Action runner.</param>
/// <param name="Model">The main window's model, which holds the settings store (SET-7).</param>
/// <param name="Pc">The PC info (<c>cmc.pc</c>, PC-1..PC-5).</param>
/// <param name="Game">The game installation (<c>cmc.game</c>'s startup part).</param>
/// <param name="Host">
/// The machine startup ran on, which the tabs keep reading (file versions, the exe name) and the Overview Report is
/// gathered from: <see cref="SystemHostEnvironment.Instance"/> in the app, a scenario's machine in tests.
/// </param>
public sealed record AppSession(AppRuntime Runtime, MainWindowViewModel Model, PcInfo Pc, GameInstall Game, IHostEnvironment Host)
{
    private ScanState? _scan;

    /// <summary>The settings in effect, which the Settings tab, the Scanner and the Downgrader read and save.</summary>
    public SettingsStore Settings => Model.Settings;

    /// <summary>
    /// The app's own folder (<see cref="global::CMToolkit.App.AppFolder"/>), where the Downgrader saves and looks for
    /// Delta Patches. The exe's folder by default; the reference used the working directory (bug #36, fixed as a
    /// Deviation, ADR-0006).
    /// </summary>
    public string AppFolder { get; init; } = AppContext.BaseDirectory;

    /// <summary>
    /// The Overview Report module, which gathers the report from the session's game and machine into its log. The
    /// Overview tab loads it; the Overview tab, the Scan module (<see cref="Scan"/>), the Archive Patcher and the
    /// Downgrader refresh it; the F4SE tab, the Scan module, the Archive Patcher and the Downgrader read it.
    /// </summary>
    public OverviewState Overview { get; } =
        new(Runtime, previous => OverviewReport.Gather(Game, Host, Runtime.Logger, previous));

    /// <summary>
    /// The Scan module, over the session's game, Overview Report module and machine: the one answer to whether a Scan is
    /// running. The Scanner tab starts Scans through it and draws its events; the Downgrader and the Archive Patcher ask
    /// it whether a Scan is running when Patch All is clicked. The same module on every read. UI thread only.
    /// </summary>
    /// <remarks>
    /// Built on first read rather than in an initializer like <see cref="Overview"/>'s: an initializer can't read another
    /// instance member, and the module needs <see cref="Overview"/>. Every read is on the UI thread, so the lazy build
    /// needs no lock.
    /// </remarks>
    public ScanState Scan => _scan ??= new(Runtime, Game, Overview, Host);

    /// <summary>
    /// The main window built over this session (the reference's <c>cmc.root</c>), which owns the windows the tabs open,
    /// such as the Downgrader. Set once it is built, after the tabs are composed; null before.
    /// </summary>
    public Views.MainWindow? MainWindow { get; set; }

    /// <summary>
    /// What the Downgrader downloads Delta Patches through (NET-3): <see cref="Core.Downgrader.DeltaPatches.CreateHandler"/>
    /// at startup, a scenario's canned responses when the exe runs on one, a fake in tests. Null, as in tests that set
    /// none, makes every download fail rather than reach the network.
    /// </summary>
    public HttpMessageHandler? DeltaPatchHttp { get; set; }
}
