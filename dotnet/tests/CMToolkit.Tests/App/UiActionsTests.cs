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

/// <summary>
/// The UI Action runner, <c>runtime.Actions</c> (ADR-0009): <c>Start</c> runs an action and sends its first failure
/// to the Error Window once, and <c>Schedule</c> starts one later on the UI thread. <c>Run</c> and <c>RunPhased</c>
/// run an Operation off the UI thread in its phases (ADR-0003), and <c>Blocking</c> holds the input block across a
/// span of them.
/// </summary>
public sealed class UiActionsTests
{
    [AvaloniaFact]
    public async Task A_ui_action_that_throws_is_reported_once_under_the_ui_thread_header()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);

        await runtime.Actions.Start(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("from a UI Action");
        });

        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith(
            $"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: from a UI Action", entry.Message);
        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: from a UI Action", runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    public async Task A_ui_action_whose_operation_fails_is_not_reported_a_second_time()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var skipped = true;

        await runtime.Actions.Start(async () =>
        {
            await runtime.Actions.Run("Refresh", OperationPhase.Blocking, () => throw new InvalidOperationException("boom"));
            skipped = false;
        });

        // Only the runner's report, under the operation's own header; the rest of the action was skipped.
        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Refresh")}\n", entry.Message);
        Assert.True(skipped);
    }

    [AvaloniaFact]
    public void A_ui_action_that_throws_before_its_first_await_is_reported_before_start_returns()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);

        var started = runtime.Actions.Start(() => throw new InvalidOperationException("before any await"));

        Assert.True(started.IsCompletedSuccessfully);
        Assert.Single(logger.Entries);
        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: before any await", runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    public async Task The_task_start_returns_never_faults()
    {
        var runtime = new AppRuntime(new CapturingLogger());

        var operationFailed = runtime.Actions.Start(
            () => runtime.Actions.Run("Refresh", OperationPhase.Blocking, () => throw new InvalidOperationException()));
        var actionThrew = runtime.Actions.Start(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException();
        });
        // Awaiting rethrows a fault, so reaching the asserts already shows neither task faulted.
        await operationFailed;
        await actionThrew;

        Assert.Equal(TaskStatus.RanToCompletion, operationFailed.Status);
        Assert.Equal(TaskStatus.RanToCompletion, actionThrew.Status);
    }

    /// <summary>
    /// The action releases its input block and throws in the same step. The block's release posts the deferred Close's
    /// replay, so the report must not wait for a posted job of its own (as an <c>async void</c> rethrow would) or the
    /// window would close with no Error Window up.
    /// </summary>
    [AvaloniaFact]
    public async Task The_error_window_is_up_before_a_deferred_close_replays()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var window = new Window { Width = 300, Height = 200 };
        bool? errorWindowUpAtReplay = null;
        runtime.Input.Attach(window, onEscape: () => { }, onCloseRequest: () => errorWindowUpAtReplay = runtime.Errors.Window is not null);
        window.Show();

        await runtime.Actions.Start(async () =>
        {
            using (runtime.Input.Block())
            {
                await Task.Yield();
                UserClose.Request(window);
            }

            throw new InvalidOperationException("after the block");
        });
        Assert.Null(errorWindowUpAtReplay);
        Dispatcher.UIThread.RunJobs();

        Assert.True(errorWindowUpAtReplay);
        window.Close();
    }

    [AvaloniaFact]
    public void A_scheduled_ui_action_runs_later_on_the_ui_thread()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        bool? ranOnUiThread = null;

        runtime.Actions.Schedule(() =>
        {
            ranOnUiThread = Dispatcher.UIThread.CheckAccess();
            return Task.CompletedTask;
        });
        Assert.Null(ranOnUiThread);
        Dispatcher.UIThread.RunJobs();

        Assert.True(ranOnUiThread);
    }

    [AvaloniaFact]
    public async Task A_ui_action_scheduled_from_a_worker_runs_on_the_ui_thread()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        bool? ranOnUiThread = null;

        await Task.Run(() => runtime.Actions.Schedule(() =>
        {
            ranOnUiThread = Dispatcher.UIThread.CheckAccess();
            return Task.CompletedTask;
        }));
        DispatcherWait.Until(() => ranOnUiThread is not null);

        Assert.True(ranOnUiThread);
    }

    [AvaloniaFact]
    public void A_scheduled_ui_action_that_fails_is_reported_once_as_a_ui_action()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);

        runtime.Actions.Schedule(() => throw new InvalidOperationException("scheduled"));
        runtime.Actions.Schedule(
            () => runtime.Actions.Run("Poll", OperationPhase.Interactive, () => throw new InvalidOperationException("polled")));
        DispatcherWait.Until(() => logger.Entries.Count >= 2);
        Dispatcher.UIThread.RunJobs();

        // One report each, under the action's and the operation's own headers; nothing reached the global handlers.
        Assert.Collection(
            logger.Entries,
            entry => Assert.StartsWith(
                $"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: scheduled", entry.Message),
            entry => Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Poll")}\n", entry.Message));
    }

    [AvaloniaFact]
    [Trait("Parity", "THR-4")]
    public async Task Work_runs_off_the_ui_thread_and_its_result_comes_back_on_it()
    {
        var runtime = new AppRuntime(new CapturingLogger());

        var ranOnUiThread = await runtime.Actions.Run(
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

        var operation = runtime.Actions.Run("wait", OperationPhase.Blocking, () => gate.Task.Wait());
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

        var operation = runtime.Actions.Run("wait", OperationPhase.Interactive, () => gate.Task.Wait());
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

        var operation = runtime.Actions.Run("wait", OperationPhase.Blocking, () => gate.Task.Wait());
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

        var operation = runtime.Actions.Run("wait", OperationPhase.Blocking, () => gate.Task.Wait());
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

        var operation = runtime.Actions.RunPhased("downgrade", OperationPhase.Blocking, async phases =>
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

        var (blockedAfterBlocking, blockedAfterInteractive) = await runtime.Actions.RunPhased(
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

        var failure = await Assert.ThrowsAsync<OperationFailedException>(() => runtime.Actions.Run(
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
        Assert.Equal(7, await runtime.Actions.Run("next", OperationPhase.Blocking, () => 7));
    }

    /// <summary>
    /// The span's block outlasts each Operation's own: input stays blocked between them and through an Interactive one,
    /// and comes back when the span ends.
    /// </summary>
    [AvaloniaFact]
    public async Task A_blocking_span_holds_the_input_block_across_its_operations_and_releases_it_after()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var probe = InputProbe.Show(runtime);
        bool? blockedBetween = null;
        bool? blockedDuringInteractive = null;

        await runtime.Actions.Blocking(async () =>
        {
            await runtime.Actions.Run("first", OperationPhase.Blocking, () => { });
            blockedBetween = runtime.Input.IsBlocking;
            // Read on the worker: reading it through the dispatcher would hide a block released and taken again.
            blockedDuringInteractive = await runtime.Actions.Run("second", OperationPhase.Interactive, () => runtime.Input.IsBlocking);
        });

        Assert.True(blockedBetween);
        Assert.True(blockedDuringInteractive);
        Assert.False(runtime.Input.IsBlocking);
        probe.ClickButton();
        Assert.Equal(1, probe.Clicks);
    }

    /// <summary>
    /// The span itself throws, outside any Operation, while a user close waits on its block. The span reports the error
    /// before it releases the block, so the Error Window is up when the close replays, whoever awaits the span and
    /// however its continuations are scheduled.
    /// </summary>
    [AvaloniaFact]
    public async Task A_blocking_span_that_throws_has_the_error_window_up_before_a_deferred_close_replays()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var window = new Window { Width = 300, Height = 200 };
        bool? errorWindowUpAtReplay = null;
        runtime.Input.Attach(window, onEscape: () => { }, onCloseRequest: () => errorWindowUpAtReplay = runtime.Errors.Window is not null);
        window.Show();

        var span = runtime.Actions.Blocking(async () =>
        {
            await Task.Yield();
            UserClose.Request(window);
            throw new InvalidOperationException("in the span");
        });
        var failure = await Assert.ThrowsAsync<OperationFailedException>(() => span);
        Dispatcher.UIThread.RunJobs();

        Assert.True(errorWindowUpAtReplay);
        Assert.Equal("in the span", failure.InnerException?.Message);
        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith(
            $"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: in the span", entry.Message);
        Assert.False(runtime.Input.IsBlocking);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_blocking_span_whose_operation_fails_is_not_reported_a_second_time_and_releases_the_block()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var probe = InputProbe.Show(runtime);
        var skipped = true;

        await Assert.ThrowsAsync<OperationFailedException>(() => runtime.Actions.Blocking(async () =>
        {
            await runtime.Actions.Run("Patch", OperationPhase.Blocking, () => throw new InvalidOperationException("boom"));
            skipped = false;
        }));

        // Only the runner's report, under the Operation's own header; the rest of the span was skipped.
        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Patch")}\n", entry.Message);
        Assert.True(skipped);
        Assert.False(runtime.Input.IsBlocking);
        probe.ClickButton();
        Assert.Equal(1, probe.Clicks);
    }

    [AvaloniaFact]
    public async Task Try_run_returns_succeeded_with_the_work_s_value_and_reports_nothing()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);

        var outcome = await runtime.Actions.TryRun("probe", OperationPhase.Blocking, () => 7);

        var succeeded = Assert.IsType<OperationOutcome<int>.Succeeded>(outcome);
        Assert.Equal(7, succeeded.Value);
        Assert.Empty(logger.Entries);
    }

    [AvaloniaFact]
    public async Task Try_run_returns_failed_with_the_original_exception_reported_once_under_the_operation_s_header()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var thrown = new InvalidOperationException("boom");

        var outcome = await runtime.Actions.TryRun<int>("Patch", OperationPhase.Blocking, () => throw thrown);

        var failed = Assert.IsType<OperationOutcome<int>.Failed>(outcome);
        Assert.Same(thrown, failed.Error);
        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith(
            $"StdErr : {ErrorReporter.OperationHeader("Patch")}\nSystem.InvalidOperationException: boom", entry.Message);
        Assert.False(runtime.Input.IsBlocking);
    }

    [AvaloniaFact]
    public async Task Throw_if_failed_on_a_failed_outcome_ends_the_ui_action_without_a_second_report()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var carriedOn = false;
        var skipped = true;

        await runtime.Actions.Start(async () =>
        {
            var outcome = await runtime.Actions.TryRun<int>("Patch", OperationPhase.Blocking, () => throw new InvalidOperationException("boom"));
            carriedOn = true;
            outcome.ThrowIfFailed();
            skipped = false;
        });

        Assert.True(carriedOn);
        Assert.True(skipped);
        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Patch")}\n", entry.Message);
    }

    /// <summary>
    /// Patch All's shape: a failed outcome ended inside a <c>Blocking</c> span, after the span carried on past it. The
    /// span must treat it as already reported, or the error would be shown a second time under the UI-thread header.
    /// </summary>
    [AvaloniaFact]
    public async Task Throw_if_failed_inside_a_blocking_span_is_not_reported_a_second_time_and_releases_the_block()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var probe = InputProbe.Show(runtime);
        var skipped = true;

        await runtime.Actions.Start(() => runtime.Actions.Blocking(async () =>
        {
            var outcome = await runtime.Actions.TryRun<int>("Patch", OperationPhase.Blocking, () => throw new InvalidOperationException("boom"));
            await runtime.Actions.Run("Refresh", OperationPhase.Blocking, () => { });
            outcome.ThrowIfFailed();
            skipped = false;
        }));

        Assert.True(skipped);
        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Patch")}\n", entry.Message);
        Assert.False(runtime.Input.IsBlocking);
        probe.ClickButton();
        Assert.Equal(1, probe.Clicks);
    }

    [AvaloniaFact]
    public async Task Throw_if_failed_on_a_succeeded_outcome_does_nothing()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var finished = false;

        await runtime.Actions.Start(async () =>
        {
            var outcome = await runtime.Actions.TryRun("probe", OperationPhase.Blocking, () => 7);
            outcome.ThrowIfFailed();
            finished = true;
        });

        Assert.True(finished);
        Assert.Empty(logger.Entries);
    }

    /// <summary>
    /// An error the Operation returned rather than threw, like a Scan that stopped early, goes to the Error Window under
    /// the same header the runner gives a failure of that Operation.
    /// </summary>
    [AvaloniaFact]
    public async Task Report_failure_reports_a_returned_error_under_the_operation_s_header()
    {
        var thrownLogger = new CapturingLogger();
        var thrownRuntime = new AppRuntime(thrownLogger);
        await thrownRuntime.Actions.TryRun<int>("Scan", OperationPhase.Interactive, () => throw new IOException("unreadable"));
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);

        runtime.Actions.ReportFailure("Scan", new IOException("unreadable"));

        var entry = Assert.Single(logger.Entries);
        var header = ErrorReporter.OperationHeader("Scan");
        Assert.StartsWith($"StdErr : {header}\nSystem.IO.IOException: unreadable", entry.Message);
        Assert.StartsWith($"StdErr : {header}\n", Assert.Single(thrownLogger.Entries).Message);
        Assert.StartsWith($"{header}\nSystem.IO.IOException: unreadable", runtime.Errors.Window!.Text);
    }
}
