using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.Downgrader;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Core.ArchivePatcher;
using CMToolkit.Tests.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Downgrader's Patch All run against the <c>downgrader/run-*</c> goldens (<c>downgrader-run</c>), in the app as
/// startup composes it (settings loaded from the scenario's working directory): the Downgrader opened from the
/// Overview's button, set up by the scenario's <c>Script/downgrader-run.json</c>, Patch All clicked. As the driver does,
/// every download is held until the click is over (<c>afterClick</c>), then released, and the window read again once
/// the run is done or stuck (<c>final</c>): the button, the close guard, the bar, the versions and their labels, the log
/// pane and the log lines. Then what died, the Game and Cwd folders, <c>settings.json</c> byte for byte and the
/// requests made. Compared in full.
/// </summary>
public sealed class DowngraderRunScenarioTests
{
    /// <summary>The Tk colour names the reference's widgets use, by the colour the port draws them in.</summary>
    private static readonly Dictionary<Color, string> TkColours = new()
    {
        [Color.Parse("#AF5A66")] = "#AF5A66",
        [Color.Parse("#808080")] = "gray",
        [Color.Parse("#1E90FF")] = "dodger blue",
        [Color.Parse("#836FFF")] = "SlateBlue1",
        [Color.Parse("#FA8072")] = "salmon",
        [Color.Parse("#619267")] = "#619267",
    };

    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("downgrader", "downgrader-run");

    /// <summary>
    /// Patch bodies for downloads only the port makes, by scenario id and logical resource. A Deviation can make the
    /// port fetch a patch the reference never reached, and the scenario's <c>http/</c> folder can't script it: it is the
    /// reference's recording input. Without one, the port golden would record the harness's "Unscripted HTTP request"
    /// failure instead of the app's behaviour.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> PortOnlyResponses = new(StringComparer.Ordinal)
    {
        // Bug #15: the hung Fallout4.exe download now fails and the queue moves on to the launcher's patch, which the
        // reference's stuck poll never fetched. The self-made patch turns the scenario's Old-Gen stand-in into Next-Gen.
        ["downgrader/run-download-hangs"] = new(StringComparer.Ordinal)
        {
            ["delta/OG-to-NG-Fallout4Launcher.exe.xdelta"] = Path.Combine(
                RepoPaths.DotnetRoot, "..", "parity", "assets", "delta-patches", "out", "OG-to-NG-Fallout4Launcher.exe.xdelta"),
        },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Scenarios))]
    [Trait("Parity", "DG-4")]
    [Trait("Parity", "DG-5")]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "NET-3")]
    [Trait("Parity", "THR-2")]
    [Trait("Parity", "SET-7")]
    public void The_patch_all_run_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var logger = new CapturingLogger();
        var http = new HeldHandler(machine.HasHttp ? machine.CreateHttpHandler() : null, PortOnlyResponses.GetValueOrDefault(scenario.Id));
        var (main, session) = Compose(machine, logger, http);
        var runtime = session.Runtime;

        MouseClick.On(ComposedSession.Named<Button>(main, "DowngradeManager"));
        DispatcherWait.Until(() => main.OwnedWindows.OfType<DowngraderWindow>().Any(w => w.Versions is not null && w.Content is not null));
        var window = main.OwnedWindows.OfType<DowngraderWindow>().Single();
        window.UpdateLayout();

        var scriptPath = Path.Combine(machine.Root, "Script", "downgrader-run.json");
        var script = File.Exists(scriptPath) ? JsonNode.Parse(File.ReadAllText(scriptPath))!.AsObject() : [];
        if ((string?)script["desiredVersion"] is { } desired)
        {
            MouseClick.On(window.GetLogicalDescendants().OfType<RadioButton>().Single(r => (string?)r.Content == desired));
        }

        foreach (var (key, name) in new[] { ("keepBackups", "KeepBackups"), ("deletePatches", "DeletePatches") })
        {
            var check = ComposedSession.Named<CheckBox>(window, name);
            if (script[key] is { } wanted && (check.IsChecked == true) != (bool)wanted)
            {
                MouseClick.On(check);
            }
        }

        var logged = logger.Entries.Count;
        JsonObject State()
        {
            var lines = logger.Entries.Skip(logged).Where(e => !IsErrorReport(e)).ToList();
            logged = logger.Entries.Count;
            window.UpdateLayout();
            return new JsonObject
            {
                ["patchEnabled"] = ComposedSession.Named<Button>(window, "PatchAll").IsEnabled,
                ["processingData"] = window.ProcessingData,
                ["progress"] = ComposedSession.Named<ProgressBar>(window, "Progress").Value,
                ["versions"] = new JsonArray([.. window.Versions!.Select(v => (JsonNode)new JsonObject
                {
                    ["file"] = v.FileName,
                    ["installType"] = v.InstallType.DisplayName(),
                })]),
                ["display"] = new JsonObject
                {
                    ["game"] = Frame(window, "Game"),
                    ["creationKit"] = Frame(window, "CreationKit"),
                    ["log"] = new JsonArray([.. ArchivePatcherWindowScenarioTests.PaneLines(window.Log!)]),
                    ["logLines"] = OverviewFullScenarioTests.UnorderedRuns(
                        [.. lines.Select(e => (JsonNode)new JsonObject
                        {
                            ["level"] = LogFile.LevelName(e.Level),
                            ["message"] = e.Message.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal),
                        })],
                        line => ((string)line["message"]!).StartsWith("Archives: Checking ", StringComparison.Ordinal)),
                },
            };
        }

        MouseClick.On(ComposedSession.Named<Button>(window, "PatchAll"));
        // The click is one blocking operation; once input is live again it is over, and its download (if any) is held.
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        var afterClick = State();
        http.Release();
        DispatcherWait.Until(() => !window.IsRunning);
        var final = State();

        var reports = logger.Entries.Where(IsErrorReport).Select(e => e.Message).ToList();
        var actual = new JsonObject
        {
            ["downgraderRun"] = new JsonObject
            {
                ["script"] = script.DeepClone(),
                ["afterClick"] = afterClick,
                ["final"] = final,
                ["errors"] = new JsonObject
                {
                    ["callback"] = reports.Count(r => r.Contains(ErrorReporter.OperationHeader("Downgrader Patch All"), StringComparison.Ordinal)),
                    ["thread"] = reports.Count(r => r.Contains(ErrorReporter.OperationHeader("Downgrader download"), StringComparison.Ordinal)),
                },
                ["files"] = Snapshot(machine, "Game", "Cwd"),
                ["settings"] = FileText(Path.Combine(machine.Root, "Cwd", AppFolderFiles.SettingsFileName)),
            },
        };
        if (http.Requests.Count > 0)
        {
            actual["httpRequests"] = new JsonArray([.. http.Requests.Select(resource => (JsonNode)new JsonObject
            {
                ["resource"] = resource,
                ["kwargs"] = new JsonObject { ["timeout"] = (int)DeltaPatches.ReadTimeout.TotalSeconds, ["stream"] = true },
            })]);
        }

        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Full);
        Assert.Equal(final["processingData"]!.GetValue<bool>(), window.ProcessingData);
        window.ProcessingData = false;
        window.Close();
        main.Close();
    }

    /// <summary>
    /// The app as startup composes it on the scenario's machine, with its settings loaded from the scenario's working
    /// directory (so Patch All saves there) and its patches downloaded through <paramref name="http"/>; shown, the
    /// Overview loaded.
    /// </summary>
    internal static (MainWindow Main, AppSession Session) Compose(MaterializedScenario machine, CapturingLogger logger, HttpMessageHandler http)
    {
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var settingsPath = Path.Combine(machine.Host.Expand(machine.Host.Block.Cwd!), AppFolderFiles.SettingsFileName);
        var runtime = new AppRuntime(logger);
        var settings = SettingsStore.Load(settingsPath, DownloadSource.Nexus, logger);
        var model = new MainWindowViewModel(new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, "github", null), settings);
        // The app folder is the scenario's working directory: its tree models a toolkit started from its own folder,
        // where the reference kept settings.json and the patches. The port's folder-not-CWD fix (bug #36) is proved by
        // DowngraderRunTests and AppFolderFilesTests, so these goldens stay the reference's.
        var session = new AppSession(runtime, model, PcInfo.Read(machine.Host, logger), game)
        {
            Host = machine.Host,
            AppFolder = machine.Host.Expand(machine.Host.Block.Cwd!),
            DeltaPatchHttp = http,
        };
        var main = new MainWindow(runtime, MainTabs.Compose(session)) { DataContext = model };
        session.MainWindow = main;
        main.Show();
        DispatcherWait.Until(() => main.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "DowngradeManager"));
        main.UpdateLayout();
        return (main, session);
    }

    /// <summary>A Current frame's version labels as the driver's <c>_downgrader_frame</c> reads them.</summary>
    private static JsonObject Frame(Window window, string prefix) => new()
    {
        ["values"] = new JsonArray([.. Enumerable.Range(0, 3).Select(i =>
        {
            var value = ComposedSession.Named<TextBlock>(window, $"{prefix}{i}");
            return (JsonNode)new JsonObject { ["text"] = value.Text, ["color"] = Colour(value.Foreground) };
        })]),
    };

    /// <summary>
    /// The driver's <c>machine_snapshot</c>: every file under the root's <paramref name="folders"/>, with its golden path,
    /// size, SHA-256 and read-only bit, sorted by the case-folded full path.
    /// </summary>
    private static JsonArray Snapshot(MaterializedScenario machine, params string[] folders) => new([..
        folders.Select(f => Path.Combine(machine.Root, f))
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .OrderBy(PythonCaseFold.Of, StringComparer.Ordinal)
            .Select(path => (JsonNode)new JsonObject
            {
                ["path"] = ArchivePatcherGolden.GoldenPath(machine, path),
                ["size"] = new FileInfo(path).Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                ["readonly"] = File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly),
            })]);

    /// <summary>The driver's <c>file_snapshot</c>: the text if it is strict UTF-8, else base64; null if there's no file.</summary>
    private static JsonObject? FileText(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        try
        {
            return new JsonObject { ["text"] = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes) };
        }
        catch (DecoderFallbackException)
        {
            return new JsonObject { ["base64"] = Convert.ToBase64String(bytes) };
        }
    }

    /// <summary>An Error Window report: outside Behaviour Parity, so counted per operation rather than compared.</summary>
    private static bool IsErrorReport(LogEntry entry) => entry.Message.StartsWith("StdErr : ", StringComparison.Ordinal);

    private static string Colour(IBrush? brush)
    {
        var colour = ((ISolidColorBrush)brush!).Color;
        return TkColours.TryGetValue(colour, out var name) ? name : colour.ToString();
    }

    /// <summary>
    /// The scenario's canned responses, each held until <see cref="Release"/>: the driver holds every download until the
    /// Patch All callback has returned, as a real download outlasts the loop.
    /// </summary>
    internal sealed class HeldHandler : DelegatingHandler
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _requests = [];
        private readonly IReadOnlyDictionary<string, string> _portOnly;

        /// <param name="inner">The scenario's canned responses; null for a scenario without an <c>http/</c> folder.</param>
        /// <param name="portOnly">
        /// Bodies served with a 200 by logical resource, for requests only the port makes (see
        /// <see cref="PortOnlyResponses"/>); they take precedence over <paramref name="inner"/>.
        /// </param>
        public HeldHandler(ParityHttpHandler? inner, IReadOnlyDictionary<string, string>? portOnly = null)
        {
            InnerHandler = inner ?? new ParityHttpHandler(Path.Combine(Path.GetTempPath(), "cmt-no-http"));
            _portOnly = portOnly ?? new Dictionary<string, string>();
        }

        /// <summary>The logical resource of each request, in order.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        public void Release() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resource = ParityHttpHandler.LogicalResource(request.RequestUri!) ?? request.RequestUri!.ToString();
            lock (_requests)
            {
                _requests.Add(resource);
            }

            await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (_portOnly.TryGetValue(resource, out var body))
            {
                var content = new ByteArrayContent(await File.ReadAllBytesAsync(body, cancellationToken).ConfigureAwait(false));
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content, RequestMessage = request };
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
