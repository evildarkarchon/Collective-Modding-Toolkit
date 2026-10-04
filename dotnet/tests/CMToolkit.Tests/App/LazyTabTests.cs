using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>Lazy tab loading (SHELL-8): <c>helpers.CMCTabFrame.load</c> and <c>CMChecker.on_tab_changed</c>.</summary>
public sealed class LazyTabTests
{
    private sealed record Shell(AppRuntime Runtime, CapturingLogger Logger, MainWindow Window, IReadOnlyList<ProbeTab> Pages)
    {
        public TabControl Tabs => Window.FindControl<TabControl>("Tabs")!;

        public LazyTab Tab(int index) => (LazyTab)((TabItem)Tabs.Items[index]!).Content!;

        public void Select(int index)
        {
            Tabs.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Shell ShowShell(Action<IReadOnlyList<ProbeTab>>? setUp = null)
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        IReadOnlyList<ProbeTab> pages =
        [
            new("Overview", "OverviewTab"),
            new("F4SE", "F4SETab", "Scanning DLLs..."),
            new("Scanner", "ScannerTab"),
        ];
        setUp?.Invoke(pages);
        var window = new MainWindow(runtime, pages)
        {
            DataContext = new MainWindowViewModel(
                new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, "github", null)),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Shell(runtime, logger, window, pages);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void The_first_tab_loads_when_the_window_opens_and_the_others_only_when_first_selected()
    {
        var shell = ShowShell();

        Assert.Equal(["load", "build", "switch_to"], shell.Pages[0].Calls);
        Assert.Empty(shell.Pages[1].Calls);
        Assert.Same(shell.Pages[0].Built, shell.Tab(0).Content);

        shell.Select(1);

        Assert.Equal(["load", "build", "switch_to"], shell.Pages[1].Calls);
        Assert.Empty(shell.Pages[2].Calls);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void Later_selections_switch_to_the_loaded_tab_without_reloading_it_and_leaving_a_tab_switches_from_it()
    {
        var shell = ShowShell();

        shell.Select(1);
        shell.Select(0);
        shell.Select(1);

        Assert.Equal(["load", "build", "switch_to", "switch_from", "switch_to", "switch_from"], shell.Pages[0].Calls);
        Assert.Equal(["load", "build", "switch_to", "switch_from", "switch_to"], shell.Pages[1].Calls);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void While_loading_the_tab_shows_its_loading_text_centred_at_20pt_and_input_is_blocked()
    {
        var gate = new TaskCompletionSource();
        var shell = ShowShell(pages => pages[1].Gate = gate.Task);

        shell.Select(1);

        var label = Assert.IsType<TextBlock>(shell.Tab(1).Content);
        Assert.Equal("Scanning DLLs...", label.Text);
        Assert.Equal(27, label.FontSize);
        Assert.Equal(TextAlignment.Center, label.TextAlignment);
        Assert.Equal(HorizontalAlignment.Center, label.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, label.VerticalAlignment);
        Assert.True(shell.Runtime.Input.IsBlocking);

        gate.SetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(shell.Pages[1].Built, shell.Tab(1).Content);
        Assert.False(shell.Runtime.Input.IsBlocking);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void A_tab_without_loading_text_shows_an_empty_loading_label()
    {
        var gate = new TaskCompletionSource();
        var shell = ShowShell(pages => pages[2].Gate = gate.Task);

        shell.Select(2);

        Assert.Equal("", Assert.IsType<TextBlock>(shell.Tab(2).Content).Text);
        gate.SetResult();
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void A_load_that_reports_failure_shows_its_error_in_bad_and_is_never_retried()
    {
        var shell = ShowShell(pages => pages[1].Load = page =>
        {
            page.FailWith("Data/F4SE/Plugins folder not found");
            return Task.FromResult(false);
        });

        shell.Select(1);
        shell.Select(0);
        shell.Select(1);

        var label = Assert.IsType<TextBlock>(shell.Tab(1).Content);
        Assert.Equal("Data/F4SE/Plugins folder not found", label.Text);
        Assert.Equal(Color.Parse("#AF5A66"), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
        // One load, never retried; switch_from still runs on leaving, loaded or not.
        Assert.Equal(["load", "switch_from"], shell.Pages[1].Calls);
        Assert.Contains(
            new LogEntry(LogLevel.Error, "Load Tab : F4SETab : Failed : Data/F4SE/Plugins folder not found"),
            shell.Logger.Entries);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void A_failed_load_without_error_text_shows_the_default_and_logs_none()
    {
        var shell = ShowShell(pages => pages[2].Load = _ => Task.FromResult(false));

        shell.Select(2);

        Assert.Equal("Failed to load tab.", Assert.IsType<TextBlock>(shell.Tab(2).Content).Text);
        Assert.Contains(new LogEntry(LogLevel.Error, "Load Tab : ScannerTab : Failed : None"), shell.Logger.Entries);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void A_load_that_throws_leaves_the_loading_text_up_reports_the_error_and_is_never_retried()
    {
        Shell? shell = null;
        shell = ShowShell(pages => pages[1].Load = async _ =>
        {
            await shell!.Runtime.Operations.RunAsync(
                "Load F4SETab", OperationPhase.Blocking, () => throw new InvalidOperationException("no DLLs"));
            return true;
        });

        shell.Select(1);
        shell.Select(0);
        shell.Select(1);

        var label = Assert.IsType<TextBlock>(shell.Tab(1).Content);
        Assert.Equal("Scanning DLLs...", label.Text);
        // One load, never retried; switch_from still runs on leaving, loaded or not.
        Assert.Equal(["load", "switch_from"], shell.Pages[1].Calls);
        Assert.Contains("System.InvalidOperationException: no DLLs", shell.Runtime.Errors.Window!.Text, StringComparison.Ordinal);
        Assert.False(shell.Runtime.Input.IsBlocking);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void An_exception_in_the_ui_part_of_a_load_is_reported_too()
    {
        var shell = ShowShell(pages => pages[2].Load = _ => throw new InvalidOperationException("ui side"));

        shell.Select(2);

        Assert.Equal("", Assert.IsType<TextBlock>(shell.Tab(2).Content).Text);
        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: ui side", shell.Runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-8")]
    public void Switching_and_loading_are_logged_at_debug()
    {
        var shell = ShowShell();

        shell.Select(1);
        shell.Select(0);

        Assert.Equal(
            [
                new(LogLevel.Debug, "Switch Tab : OverviewTab"),
                new(LogLevel.Debug, "Load Tab : OverviewTab"),
                new(LogLevel.Debug, "Switch Tab : F4SETab"),
                new(LogLevel.Debug, "Load Tab : F4SETab"),
                new LogEntry(LogLevel.Debug, "Switch Tab : OverviewTab"),
            ],
            shell.Logger.Entries);
    }
}
