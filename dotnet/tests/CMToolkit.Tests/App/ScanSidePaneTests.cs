using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// Scan Game's rule on the Scanner's side pane, read from a real <see cref="ScanState"/> with no main window: the pane is
/// shown in a plain window, and the module's scan after its Overview refresh is held until the test ends it.
/// </summary>
public sealed class ScanSidePaneTests
{
    [AvaloniaFact]
    public void Ticking_or_unticking_a_box_mid_scan_leaves_scan_game_disabled_on_scanning()
    {
        using var module = HeldScanModule.Create();
        var pane = new ScanSidePane(AllTicked, module.Scan);
        using var shown = Shown.In(pane);
        module.Start();

        pane.Checks[ScanSetting.JunkFiles].IsChecked = false;
        AssertScanning(pane);

        pane.Checks[ScanSetting.JunkFiles].IsChecked = true;
        AssertScanning(pane);

        module.End();
        AssertScanGame(pane, enabled: true);
    }

    [AvaloniaFact]
    public void A_pane_built_mid_scan_starts_on_scanning_and_shows_scan_game_once_the_scan_ends()
    {
        using var module = HeldScanModule.Create();
        module.Start();

        var pane = new ScanSidePane(AllTicked, module.Scan);
        AssertScanning(pane);
        using var shown = Shown.In(pane);
        AssertScanning(pane);

        module.End();
        AssertScanGame(pane, enabled: true);
    }

    [AvaloniaFact]
    public void A_scan_that_ends_with_every_box_unticked_leaves_scan_game_disabled()
    {
        using var module = HeldScanModule.Create();
        var pane = new ScanSidePane(AllTicked, module.Scan);
        using var shown = Shown.In(pane);
        module.Start();

        foreach (var check in pane.Checks.Values)
        {
            check.IsChecked = false;
            AssertScanning(pane);
        }

        module.End();
        AssertScanGame(pane, enabled: false);
    }

    [AvaloniaFact]
    public void A_pane_taken_out_mid_scan_and_put_back_after_the_scan_ended_shows_scan_game_enabled()
    {
        using var module = HeldScanModule.Create();
        var pane = new ScanSidePane(AllTicked, module.Scan);
        using var shown = Shown.In(pane);
        module.Start();
        AssertScanning(pane);

        shown.Detach();
        module.End();
        shown.Attach();

        AssertScanGame(pane, enabled: true);
    }

    /// <summary>Every box ticked.</summary>
    private static IReadOnlyDictionary<ScanSetting, bool> AllTicked { get; } = ScanSettings.All.ToDictionary(s => s, _ => true);

    /// <summary>Scan Game as it is while a Scan runs: disabled, showing <c>Scanning...</c>.</summary>
    private static void AssertScanning(ScanSidePane pane)
    {
        Assert.Equal("Scanning...", (string?)pane.ScanButton.Content);
        Assert.False(pane.ScanButton.IsEnabled);
    }

    /// <summary>Scan Game as it is while no Scan runs: showing <c>Scan Game</c>, enabled or not as <paramref name="enabled"/> says.</summary>
    private static void AssertScanGame(ScanSidePane pane, bool enabled)
    {
        Assert.Equal("Scan Game", (string?)pane.ScanButton.Content);
        Assert.Equal(enabled, pane.ScanButton.IsEnabled);
    }

    /// <summary>A plain window showing a pane, closed on dispose; the pane can be taken out and put back.</summary>
    private sealed class Shown : IDisposable
    {
        private readonly Window _window;
        private readonly Control _content;

        private Shown(Window window, Control content)
        {
            _window = window;
            _content = content;
        }

        /// <summary>Shows <paramref name="content"/> in a new window.</summary>
        public static Shown In(Control content)
        {
            var window = new Window { Width = 200, Height = 400, Content = content };
            window.Show();
            return new Shown(window, content);
        }

        /// <summary>Takes the content out of the window, detaching it from the visual tree.</summary>
        public void Detach() => _window.Content = null;

        /// <summary>Puts the content back in the window, attaching it to the visual tree again.</summary>
        public void Attach() => _window.Content = _content;

        public void Dispose() => _window.Close();
    }

    /// <summary>
    /// A <see cref="ScanState"/> over a loaded Overview Report module with fixed empty reports, on a game that is never
    /// read, whose scan after the refresh holds until <see cref="End"/>.
    /// </summary>
    private sealed class HeldScanModule : IDisposable
    {
        private readonly ManualResetEventSlim _finish = new();
        private bool _entered;
        private Task? _running;

        private HeldScanModule()
        {
            var runtime = new AppRuntime(new CapturingLogger());
            Overview = new OverviewState(runtime, _ => new OverviewReport(InstallType.NextGen, [], null, []));
            var empty = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            var game = new GameInstall(
                null, null, new GameDirectories(new PythonPath(@"C:\Nowhere\Fallout 4"), null, null), new GameInis(empty, empty, "en"));
            Scan = new ScanState(runtime, game, Overview, new FakeHostEnvironment(new HostBlock(), @"C:\Nowhere"))
            {
                RunScan = (_, _) =>
                {
                    Volatile.Write(ref _entered, true);
                    _finish.Wait(TaskTimeouts.Default);
                    return ScanOutcome.Empty;
                },
            };
        }

        public OverviewState Overview { get; }

        public ScanState Scan { get; }

        /// <summary>Builds the module and loads its Overview Report module, which each Scan refreshes first.</summary>
        public static HeldScanModule Create()
        {
            var module = new HeldScanModule();
            var load = module.Overview.LoadAsync();
            DispatcherWait.Until(() => load.IsCompleted);
            load.GetAwaiter().GetResult();
            return module;
        }

        /// <summary>Starts a Scan with every box ticked and runs the dispatcher until its scan is held, past the refresh.</summary>
        public void Start()
        {
            _running = Scan.StartAsync(AllTicked);
            DispatcherWait.Until(() => Volatile.Read(ref _entered));
        }

        /// <summary>Lets the held scan finish and runs the dispatcher until the Scan has ended.</summary>
        public void End()
        {
            _finish.Set();
            DispatcherWait.Until(() => _running!.IsCompleted);
            _running!.GetAwaiter().GetResult();
        }

        /// <summary>Releases the scan, so a failed test doesn't leave a worker waiting.</summary>
        /// <remarks>
        /// The event is set, not disposed: a worker still inside <see cref="ManualResetEventSlim.Wait(TimeSpan)"/> would
        /// throw on a disposed one, and a slim event that never handed out its wait handle holds nothing to free.
        /// </remarks>
        public void Dispose() => _finish.Set();
    }
}
