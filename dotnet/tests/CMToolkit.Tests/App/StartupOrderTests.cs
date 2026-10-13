using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The startup sequence (SHELL-5): log, settings, PC info, mod manager, game path, game INIs, main window, the update
/// check hook, and only then the window is shown. Each step is observed through the machine facts it reads.
/// </summary>
public sealed class StartupOrderTests
{
    /// <summary>
    /// A host that names the startup step behind each fact it is asked for, in order. Everything else comes from the
    /// wrapped scenario host; the exe folder is the test output's, which carries a valid Download Source file.
    /// </summary>
    private sealed class StepRecordingHost(FakeHostEnvironment inner) : IHostEnvironment
    {
        private readonly List<string> _steps = [];

        /// <summary>The steps, consecutive repeats collapsed.</summary>
        public IReadOnlyList<string> Steps => _steps;

        public void Record(string step)
        {
            lock (_steps)
            {
                if (_steps.Count == 0 || _steps[^1] != step)
                {
                    _steps.Add(step);
                }
            }
        }

        public string AppDirectory
        {
            get
            {
                Record("settings");
                return AppContext.BaseDirectory;
            }
        }

        public RegistryData ReadRegistryValue(RegistryRoot root, string keyPath, string valueName)
        {
            Record(keyPath.Contains("Bethesda Softworks", StringComparison.Ordinal) || keyPath.Contains("GOG.com", StringComparison.Ordinal)
                ? "game path"
                : keyPath.Contains("Mod Organizer", StringComparison.Ordinal) ? "mod manager" : "pc info");
            return inner.ReadRegistryValue(root, keyPath, valueName);
        }

        public bool IsWine
        {
            get
            {
                Record("pc info");
                return inner.IsWine;
            }
        }

        public OperatingSystemInfo OsInfo
        {
            get
            {
                Record("pc info");
                return inner.OsInfo;
            }
        }

        public long TotalPhysicalMemory
        {
            get
            {
                Record("pc info");
                return inner.TotalPhysicalMemory;
            }
        }

        public IReadOnlyList<AncestorProcess> GetAncestorProcesses(int maxCount)
        {
            Record("mod manager");
            return inner.GetAncestorProcesses(maxCount);
        }

        public IReadOnlyList<int>? GetFileVersion(string path)
        {
            Record("mod manager");
            return inner.GetFileVersion(path);
        }

        public string? GetEnvironmentVariable(string name) => inner.GetEnvironmentVariable(name);

        // Not a step marker: every relative-path check reads it, in several steps.
        public string CurrentDirectory => inner.CurrentDirectory;

        public string GetKnownFolderPath(KnownFolder folder)
        {
            Record("game inis");
            return inner.GetKnownFolderPath(folder);
        }

        // Not a startup step: only the Overview's No Mod Manager problem reads it, once the window is shown.
        public string Argv0 => inner.Argv0;
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-5")]
    public async Task Startup_runs_in_the_reference_order_and_checks_for_updates_before_the_window_is_shown()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "shell/main-window").Materialize();
        using var dir = new TempDirectory();
        var host = new StepRecordingHost(machine.Host);
        // The log is open before the sequence starts: it takes the open log (SHELL-1).
        using var log = AppStartup.OpenLog(Path.Combine(dir.Path, "cm-toolkit.log"), DateTime.Now);
        var runtime = new AppRuntime(log);
        MainWindow? checkedWindow = null;
        AppSession? checkedSession = null;
        var sequence = new StartupSequence(runtime, host, log, new WindowStartupDialogs())
        {
            SettingsPath = Path.Combine(dir.Path, "settings.json"),
            CheckForUpdatesAsync = (window, session) =>
            {
                host.Record("update check");
                checkedWindow = window;
                checkedSession = session;
                // The window is built, tabs and all, but not yet shown.
                Assert.False(window.IsVisible);
                Assert.Equal(6, window.GetLogicalDescendants().OfType<TabItem>().Count());
                return Task.CompletedTask;
            },
        };

        var window = await sequence.BuildMainWindowAsync().WithTimeout();

        Assert.Equal(["settings", "pc info", "mod manager", "game path", "game inis", "update check"], host.Steps);
        Assert.NotNull(window);
        Assert.Same(window, checkedWindow);
        Assert.False(window.IsVisible);
        Assert.Equal(machine.PathOf("Game"), checkedSession!.Game.GamePath.ToString());
        // PC-4's cleanup of the scenario's "AMD Ryzen 7 7800X3D 8-Core Processor           ".
        Assert.Equal("AMD Ryzen 7 7800X3D", checkedSession.Pc.Cpu);
        Assert.Same(runtime, checkedSession.Runtime);

        // The single composition point: the reference's tab order, with the tabs whose slices have landed real.
        var pages = MainTabs.Compose(checkedSession);
        Assert.Equal(["Overview", "F4SE", "Scanner", "Tools", "Settings", "About"], pages.Select(p => p.Header));
        Assert.IsType<ToolsTab>(pages[3]);
        Assert.IsType<SettingsTab>(pages[4]);
        Assert.IsType<AboutTab>(pages[5]);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-5")]
    [Trait("Parity", "GP-2")]
    public async Task Declining_to_locate_the_game_ends_startup_with_no_window_after_pc_info()
    {
        using var dir = new TempDirectory();
        var block = new HostBlock(
            Cwd: "<ROOT>",
            KnownFolders: new Dictionary<string, string> { ["Documents"] = "<ROOT>" },
            Os: new HostOs("Windows", "11", 26100),
            Pc: new HostPc(17179869184),
            Dialogs: [new DialogAnswer("askyesno", JsonSerializer.SerializeToElement(false))]);
        var host = new StepRecordingHost(new FakeHostEnvironment(block, dir.Path));
        using var log = AppStartup.OpenLog(Path.Combine(dir.Path, "cm-toolkit.log"), DateTime.Now);
        var dialogs = new ScriptedStartupDialogs(block.Dialogs!, s => s);
        var updateChecked = false;
        var sequence = new StartupSequence(new AppRuntime(log), host, log, dialogs)
        {
            SettingsPath = Path.Combine(dir.Path, "settings.json"),
            CheckForUpdatesAsync = (_, _) =>
            {
                updateChecked = true;
                return Task.CompletedTask;
            },
        };

        var window = await sequence.BuildMainWindowAsync().WithTimeout();

        Assert.Null(window);
        Assert.False(updateChecked);
        Assert.Equal(["settings", "pc info", "mod manager", "game path"], host.Steps);
        Assert.Equal("Fallout 4 Not Found", Assert.Single(dialogs.Calls)!["args"]![0]!.GetValue<string>());
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-13")]
    [Trait("Parity", "INI-1")]
    public async Task A_missing_documents_folder_fails_startup_into_the_startup_failure_dialog()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Game"));
        File.WriteAllBytes(Path.Combine(dir.Path, "Game", "Fallout4.exe"), []);
        var block = new HostBlock(
            AppDir: AppContext.BaseDirectory,
            Cwd: "<ROOT>/Game",
            KnownFolders: new Dictionary<string, string> { ["Documents"] = "<ROOT>\\No Documents" },
            Os: new HostOs("Windows", "11", 26100),
            Pc: new HostPc(17179869184));
        var host = new FakeHostEnvironment(block, dir.Path);
        var logger = new CapturingLogger();
        using var log = AppStartup.OpenLog(Path.Combine(dir.Path, "cm-toolkit.log"), DateTime.Now);
        var runtime = new AppRuntime(logger);
        var sequence = new StartupSequence(runtime, host, log, new ScriptedStartupDialogs([], s => s))
        {
            SettingsPath = Path.Combine(dir.Path, "settings.json"),
        };

        var result = await AppStartup.BuildAsync(runtime, async () => await sequence.BuildMainWindowAsync());

        Assert.True(result.Failed);
        Assert.Equal(1, result.ExitCode);
        var dialog = Assert.IsType<StartupFailureDialog>(result.Window);
        Assert.Equal(
            $"Failed to execute script 'main' due to unhandled exception: Folder does not exist:\n{dir.Path}\\No Documents",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
    }

    [AvaloniaFact]
    public async Task A_deliberate_exit_hands_back_no_window_and_exit_code_0()
    {
        var runtime = new AppRuntime(new CapturingLogger());

        var result = await AppStartup.BuildAsync(runtime, () => Task.FromResult<Window?>(null));

        Assert.Null(result.Window);
        Assert.False(result.Failed);
        Assert.Equal(0, result.ExitCode);
    }
}
