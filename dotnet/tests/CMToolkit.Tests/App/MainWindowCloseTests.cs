using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// Closing the main window: the close guard (SHELL-9, <c>CMChecker.on_close</c>) and Escape, which bypasses it
/// (SHELL-10, B-12, <c>root.bind("&lt;Escape&gt;", lambda _: root.destroy())</c>).
/// </summary>
public sealed class MainWindowCloseTests
{
    private static (AppRuntime Runtime, MainWindow Window) ShowShell()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var window = new MainWindow(runtime, PlaceholderTab.Shell())
        {
            DataContext = new MainWindowViewModel(
                new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, "github", null)),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (runtime, window);
    }

    private static void PressEscape(MainWindow window)
        => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

    [AvaloniaFact]
    [Trait("Parity", "SHELL-9")]
    public void A_user_close_closes_the_window_because_nothing_sets_the_close_guard()
    {
        var (_, window) = ShowShell();

        Assert.False(window.ProcessingData);
        UserClose.Request(window);

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-9")]
    public void A_user_close_is_ignored_while_the_close_guard_is_set()
    {
        var (_, window) = ShowShell();
        window.ProcessingData = true;

        UserClose.Request(window);

        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-10")]
    [Trait("Parity", "B-12")]
    public void Escape_closes_the_window_even_while_the_close_guard_is_set()
    {
        var (_, window) = ShowShell();
        window.ProcessingData = true;

        PressEscape(window);

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-10")]
    public void Closing_the_main_window_takes_its_modals_with_it_even_while_they_are_processing()
    {
        var (runtime, window) = ShowShell();
        var about = new AboutWindow(runtime, 500, 300, "About", "Text");
        _ = about.ShowModalAsync(window);
        about.ProcessingData = true;

        window.Close();

        Assert.False(about.IsVisible);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-9")]
    public async Task A_user_close_during_a_blocking_operation_closes_the_window_once_it_ends()
    {
        var (runtime, window) = ShowShell();
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("Refresh", OperationPhase.Blocking, () => gate.Task.Wait());
        UserClose.Request(window);
        Assert.True(window.IsVisible);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "B-12")]
    public async Task Escape_during_a_blocking_operation_closes_the_window_once_it_ends_guard_or_not()
    {
        var (runtime, window) = ShowShell();
        window.ProcessingData = true;
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("Refresh", OperationPhase.Blocking, () => gate.Task.Wait());
        PressEscape(window);
        Assert.True(window.IsVisible);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
    }
}
