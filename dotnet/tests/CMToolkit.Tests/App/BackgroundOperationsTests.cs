using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>The background-operation runner (ADR-0003): where work runs, and what a failing operation leaves behind.</summary>
public sealed class BackgroundOperationsTests
{
    [AvaloniaFact]
    [Trait("Parity", "THR-4")]
    public async Task Work_runs_off_the_ui_thread_and_its_result_comes_back_on_it()
    {
        var runtime = new AppRuntime(new CapturingLogger());

        var ranOnUiThread = await runtime.Operations.RunAsync(
            "probe", OperationPhase.Blocking, () => Dispatcher.UIThread.CheckAccess());

        Assert.False(ranOnUiThread);
        Assert.True(Dispatcher.UIThread.CheckAccess());
    }

    [AvaloniaFact]
    public async Task A_blocking_operation_discards_clicks_and_typing_until_it_ends()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        probe.ClickButton();
        probe.Type("a");

        Assert.Equal(0, probe.Clicks);
        Assert.Equal("", probe.Text);

        gate.SetResult();
        await operation;
        probe.ClickButton();
        probe.Type("a");

        Assert.Equal(1, probe.Clicks);
        Assert.Equal("a", probe.Text);
    }

    [AvaloniaFact]
    public async Task An_interactive_operation_leaves_the_ui_live()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Interactive, () => gate.Task.Wait());
        probe.ClickButton();
        probe.Type("a");

        Assert.Equal(1, probe.Clicks);
        Assert.Equal("a", probe.Text);

        gate.SetResult();
        await operation;
    }

    [AvaloniaFact]
    public async Task Escape_during_a_blocking_operation_is_deferred_then_honoured()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        probe.PressEscape();
        Assert.Equal(0, probe.Escapes);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, probe.Escapes);
    }

    [AvaloniaFact]
    public async Task A_user_close_during_a_blocking_operation_is_deferred_then_honoured()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("wait", OperationPhase.Blocking, () => gate.Task.Wait());
        UserClose.Request(probe.Window);
        Assert.Equal(0, probe.CloseRequests);
        Assert.True(probe.Window.IsVisible);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, probe.CloseRequests);
    }

    [AvaloniaFact]
    public async Task Escape_and_user_close_are_not_deferred_when_nothing_blocks()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);

        probe.PressEscape();
        UserClose.Request(probe.Window);

        Assert.Equal(1, probe.Escapes);
        Assert.Equal(1, probe.CloseRequests);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Phase_markers_switch_the_input_block_on_and_off()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        var interactive = new TaskCompletionSource();
        var blocking = new TaskCompletionSource();
        var reachedInteractive = new TaskCompletionSource();
        var reachedBlocking = new TaskCompletionSource();

        var operation = runtime.Operations.RunPhasedAsync("downgrade", OperationPhase.Blocking, async phases =>
        {
            phases.Report(OperationPhase.Interactive);
            reachedInteractive.SetResult();
            await interactive.Task;
            phases.Report(OperationPhase.Blocking);
            reachedBlocking.SetResult();
            await blocking.Task;
            return 42;
        });

        Assert.True(runtime.Input.IsBlocking);
        await reachedInteractive.Task;
        Dispatcher.UIThread.RunJobs();
        probe.ClickButton();
        Assert.Equal(1, probe.Clicks);

        interactive.SetResult();
        await reachedBlocking.Task;
        Dispatcher.UIThread.RunJobs();
        probe.ClickButton();
        Assert.Equal(1, probe.Clicks);

        blocking.SetResult();
        Assert.Equal(42, await operation);
        Assert.False(runtime.Input.IsBlocking);
    }

    [AvaloniaFact]
    public async Task Reporting_a_blocking_phase_returns_only_once_the_block_is_held()
    {
        // The worker must not start blocking work while input still gets through. IsBlocking is read on the worker
        // itself: reading it through the dispatcher would run a queued phase change first and hide a late block.
        var runtime = new AppRuntime(new CapturingLogger());

        var (blockedAfterBlocking, blockedAfterInteractive) = await runtime.Operations.RunPhasedAsync(
            "downgrade", OperationPhase.Interactive, phases =>
            {
                phases.Report(OperationPhase.Blocking);
                var afterBlocking = runtime.Input.IsBlocking;
                phases.Report(OperationPhase.Interactive);
                var afterInteractive = runtime.Input.IsBlocking;
                return Task.FromResult((afterBlocking, afterInteractive));
            }).WithTimeout();

        Assert.True(blockedAfterBlocking);
        Assert.False(blockedAfterInteractive);
    }

    [AvaloniaFact]
    [Trait("Parity", "THR-4")]
    public async Task A_failing_operation_is_reported_dies_alone_and_releases_the_block()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var probe = InputProbe.Show(runtime);

        var failure = await Assert.ThrowsAsync<OperationFailedException>(() => runtime.Operations.RunAsync(
            "Load OverviewTab", OperationPhase.Blocking, () => throw new InvalidOperationException("boom")));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.StartsWith(
            "StdErr : Exception in background operation 'Load OverviewTab'\nSystem.InvalidOperationException: boom",
            entry.Message);

        // The app carries on: the block is gone and the next operation runs.
        Assert.False(runtime.Input.IsBlocking);
        probe.ClickButton();
        Assert.Equal(1, probe.Clicks);
        Assert.Equal(7, await runtime.Operations.RunAsync("next", OperationPhase.Blocking, () => 7));
    }
}
