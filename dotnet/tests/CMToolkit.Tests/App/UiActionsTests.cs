using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The UI Action runner, <c>runtime.Actions</c> (ADR-0009): <c>Start</c> runs an action and sends its first failure
/// to the Error Window once, and <c>Schedule</c> starts one later on the UI thread.
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
            await runtime.Operations.RunAsync("Refresh", OperationPhase.Blocking, () => throw new InvalidOperationException("boom"));
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
            () => runtime.Operations.RunAsync("Refresh", OperationPhase.Blocking, () => throw new InvalidOperationException()));
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
            () => runtime.Operations.RunAsync("Poll", OperationPhase.Interactive, () => throw new InvalidOperationException("polled")));
        DispatcherWait.Until(() => logger.Entries.Count >= 2);
        Dispatcher.UIThread.RunJobs();

        // One report each, under the action's and the operation's own headers; nothing reached the global handlers.
        Assert.Collection(
            logger.Entries,
            entry => Assert.StartsWith(
                $"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: scheduled", entry.Message),
            entry => Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Poll")}\n", entry.Message));
    }
}
