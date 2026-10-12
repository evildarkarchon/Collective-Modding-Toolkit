using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// What the Overview scenarios can't show on their own: that the refresh re-reads a game that changed since the load,
/// rebuilds the frames and follows the new Install Type in the top block, with input blocked and no message box
/// (OVW-7, OVW-8); and the tab's layout (OVW-1).
/// </summary>
public sealed class OverviewTabTests
{
    [AvaloniaFact]
    [Trait("Parity", "OVW-8")]
    [Trait("Parity", "OVW-7")]
    public void Refresh_rereads_the_game_and_rebuilds_the_frames_without_message_boxes()
    {
        using var machine = Scenario("overview/og-steam");
        var (window, state, runtime) = Show(machine, machine.Host);
        var before = state.Report;
        Assert.Equal("Old-Gen", Text(window, "Version"));
        Assert.Equal("Installed", Text(window, "AddressLibrary"));

        // Since the load: the Address Library was removed and Startup.ba2 became the Next-Gen one (Down-Grade).
        File.Delete(machine.PathOf(@"Game\Data\F4SE\Plugins\version-1-10-163-0.bin"));
        File.Copy(Stub("dg", "Fallout4 - Startup.ba2"), machine.PathOf(@"Game\Data\Fallout4 - Startup.ba2"), overwrite: true);
        MouseClick.On(Named<Button>(window, "Refresh"));
        DispatcherWait.Until(() => !ReferenceEquals(state.Report, before) && !runtime.Input.IsBlocking);

        Assert.Equal(InstallType.DownGrade, state.InstallType);
        Assert.Equal("Down-Grade", Text(window, "Version"));
        Assert.Equal("Not Found", Text(window, "AddressLibrary"));
        // The problem list is a new one: No Mod Manager and the missing Address Library, not the load's appended to.
        Assert.Equal(["No Mod Manager", "File Not Found"], state.Report!.Problems.Select(p => p.Kind));
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaFact]
    [Trait("Parity", "OVW-8")]
    public void Refresh_gathers_with_input_blocked()
    {
        using var machine = Scenario("overview/og-steam");
        var host = new GatedHost(machine.Host);
        var (window, state, runtime) = Show(machine, host);
        var before = state.Report;

        host.Gate.Reset();
        MouseClick.On(Named<Button>(window, "Refresh"));
        // The worker is held inside the gathering, reading Fallout4.exe's version.
        DispatcherWait.Until(() => host.Waiting);
        Assert.True(runtime.Input.IsBlocking);
        Assert.Same(before, state.Report);

        host.Gate.Set();
        DispatcherWait.Until(() => !ReferenceEquals(state.Report, before));
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
    }

    [AvaloniaFact]
    [Trait("Parity", "OVW-1")]
    public void The_three_frames_sit_side_by_side_below_the_top_block()
    {
        using var machine = Scenario("overview/og-steam");
        var (window, _, _) = Show(machine, machine.Host);

        var frames = new[] { "Binaries", "Archives", "Modules" }.Select(n => Named<GroupBox>(window, n)).ToList();
        Assert.Equal(
            ["Binaries (EXE/DLL/BIN)", "Archives (BA2)", "Modules (ESM/ESL/ESP)"],
            frames.Select(f => ((TextBlock)f.Header!).Text));
        var boxes = frames.Select(f => f.Bounds).ToList();
        Assert.All(boxes, b => Assert.Equal(boxes[0].Y, b.Y));
        Assert.True(boxes[0].Right <= boxes[1].X && boxes[1].Right <= boxes[2].X);

        // The top block's refresh button is right-aligned above them.
        var refresh = Named<Button>(window, "Refresh");
        var refreshBottom = refresh.TranslatePoint(new Point(0, refresh.Bounds.Height), frames[0])!.Value.Y;
        Assert.True(refreshBottom <= 0);
        Assert.Equal("Refresh", ToolTip.GetTip(refresh));
    }

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    private static string Stub(string set, string name)
        => Path.Combine(RepoPaths.DotnetRoot, "..", "parity", "assets", "version-stubs", "out", set, name);

    private static (MainWindow Window, OverviewState State, AppRuntime Runtime) Show(MaterializedScenario machine, IHostEnvironment host)
    {
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult()!;
        var runtime = new AppRuntime(new CapturingLogger());
        var state = new OverviewState();
        var tab = new OverviewTab(runtime, host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
        {
            OpenFolder = _ => { },
        };
        return (TabHost.Show(runtime, tab), state, runtime);
    }

    private static string? Text(MainWindow window, string name) => Named<TextBlock>(window, name).Text;

    private static T Named<T>(MainWindow window, string name)
        where T : Control
        => TabHost.Content(window).GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);

    /// <summary>
    /// The scenario's host, except that reading a file version waits on <see cref="Gate"/>, so a test can look at the
    /// UI while the gathering is in progress.
    /// </summary>
    private sealed class GatedHost(FakeHostEnvironment inner) : IHostEnvironment
    {
        private volatile bool _waiting;

        public ManualResetEventSlim Gate { get; } = new(initialState: true);

        public bool Waiting => _waiting;

        public string AppDirectory => inner.AppDirectory;

        public bool IsWine => inner.IsWine;

        public OperatingSystemInfo OsInfo => inner.OsInfo;

        public long TotalPhysicalMemory => inner.TotalPhysicalMemory;

        public string CurrentDirectory => inner.CurrentDirectory;

        public string Argv0 => inner.Argv0;

        public RegistryData ReadRegistryValue(RegistryRoot root, string keyPath, string valueName)
            => inner.ReadRegistryValue(root, keyPath, valueName);

        public IReadOnlyList<AncestorProcess> GetAncestorProcesses(int maxCount) => inner.GetAncestorProcesses(maxCount);

        public IReadOnlyList<int>? GetFileVersion(string path)
        {
            if (!Gate.IsSet)
            {
                _waiting = true;
                Gate.Wait(TaskTimeouts.Default);
                _waiting = false;
            }

            return inner.GetFileVersion(path);
        }

        public string? GetEnvironmentVariable(string name) => inner.GetEnvironmentVariable(name);

        public string GetKnownFolderPath(KnownFolder folder) => inner.GetKnownFolderPath(folder);
    }
}
