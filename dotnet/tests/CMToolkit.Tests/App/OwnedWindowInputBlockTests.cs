using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// ADR-0003's input block reaches every window the app owns, not only the main window and its modals: in the reference
/// every Toplevel froze with the Tk thread. Windows owned by an attached window are attached when they open, so a new
/// one needs nothing; the Error Window is attached by its reporter; the message box alone stays live.
/// </summary>
public sealed class OwnedWindowInputBlockTests
{
    [AvaloniaFact]
    public async Task A_window_owned_by_an_attached_window_discards_clicks_during_a_blocking_operation_and_takes_them_after()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var owned = OwnedButtonWindow.Show(probe.Window);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        MouseClick.On(owned.Button);
        Assert.Equal(0, owned.Clicks);

        gate.SetResult();
        await operation;
        MouseClick.On(owned.Button);
        Assert.Equal(1, owned.Clicks);
    }

    [AvaloniaFact]
    public async Task A_window_owned_through_another_owned_window_is_blocked_too()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var middle = OwnedButtonWindow.Show(probe.Window);
        var owned = OwnedButtonWindow.Show(middle.Window);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        MouseClick.On(owned.Button);
        Assert.Equal(0, owned.Clicks);

        gate.SetResult();
        await operation;
    }

    [AvaloniaFact]
    public async Task An_interactive_operation_leaves_owned_windows_live()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var owned = OwnedButtonWindow.Show(probe.Window);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Interactive, () => gate.Task.Wait());
        MouseClick.On(owned.Button);
        Assert.Equal(1, owned.Clicks);

        gate.SetResult();
        await operation;
    }

    [AvaloniaFact]
    public async Task A_user_close_of_an_owned_window_during_a_blocking_operation_is_deferred_then_honoured()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var owned = OwnedButtonWindow.Show(probe.Window);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        Assert.False(UserClose.Request(owned.Window));
        Assert.True(owned.Window.IsVisible);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.False(owned.Window.IsVisible);
    }

    [AvaloniaFact]
    public void A_user_close_of_an_owned_window_is_not_intercepted_when_nothing_blocks()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var owned = OwnedButtonWindow.Show(probe.Window);

        Assert.True(UserClose.Request(owned.Window));
        Assert.False(owned.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_user_close_of_the_error_window_during_a_blocking_operation_is_deferred_then_honoured()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        runtime.Errors.Report(ErrorReporter.UiThreadHeader, new InvalidOperationException("boom"));
        var window = runtime.Errors.Window!;
        Assert.False(UserClose.Request(window));
        Assert.True(window.IsVisible);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
        Assert.Null(runtime.Errors.Window);
    }

    [AvaloniaFact]
    public async Task A_message_box_over_an_attached_window_stays_live_during_a_blocking_operation()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        var shown = MessageBox.ShowWarningAsync(probe.Window, "Warning", "Fallout4.ccc not found.");
        var box = probe.Window.OwnedWindows.OfType<MessageBoxWindow>().Single();
        // A size-to-content box gets its final layout after it opens; settle it so the button has real bounds.
        box.UpdateLayout();
        MouseClick.On(box.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "OK")));

        await shown.WithTimeout();
        gate.SetResult();
        await operation;
    }

    /// <summary>A plain window with one button that counts clicks, shown owned by another window.</summary>
    private sealed class OwnedButtonWindow
    {
        private OwnedButtonWindow(Window window, Button button)
        {
            Window = window;
            Button = button;
        }

        public Window Window { get; }

        public Button Button { get; }

        public int Clicks { get; private set; }

        /// <summary>Shows a 200×100 window owned by <paramref name="owner"/>, attached by nothing but its owner.</summary>
        public static OwnedButtonWindow Show(Window owner)
        {
            var button = new Button { Content = "Click", Height = 50, HorizontalAlignment = HorizontalAlignment.Stretch };
            var window = new Window { Width = 200, Height = 100, Content = button };
            var result = new OwnedButtonWindow(window, button);
            button.Click += (_, _) => result.Clicks++;
            window.Show(owner);
            return result;
        }
    }
}
