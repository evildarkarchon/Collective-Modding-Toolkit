using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Error Window, "An Error Occurred" (SHELL-12, as decided on issue #18): every unhandled exception goes to it and
/// to the log the moment it happens, and the app survives.
/// </summary>
public sealed class ErrorWindowTests
{
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void A_report_opens_the_error_window_and_logs_it_as_stderr()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var error = Thrown("boom");

        runtime.Errors.Report("Exception on the UI thread", error);

        var window = Assert.IsType<ErrorWindow>(runtime.Errors.Window);
        Assert.True(window.IsVisible);
        Assert.Equal("An Error Occurred", window.Title);
        Assert.Equal($"Exception on the UI thread\n{error}", window.Text);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal($"StdErr : Exception on the UI thread\n{error}", entry.Message);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void Later_errors_are_appended_while_the_window_is_open()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var first = Thrown("first");
        var second = Thrown("second");

        runtime.Errors.Report("A", first);
        var window = runtime.Errors.Window;
        runtime.Errors.Report("B", second);

        Assert.Same(window, runtime.Errors.Window);
        Assert.Equal($"A\n{first}\n\nB\n{second}", window!.Text);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void Closing_the_window_discards_its_text_and_the_next_error_opens_a_new_one()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        runtime.Errors.Report("A", Thrown("first"));
        var first = runtime.Errors.Window!;

        first.Close();
        Assert.Null(runtime.Errors.Window);

        var second = Thrown("second");
        runtime.Errors.Report("B", second);

        Assert.NotSame(first, runtime.Errors.Window);
        Assert.Equal($"B\n{second}", runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void The_window_is_a_resizable_non_modal_read_only_monospace_text_area_of_about_120_by_25_characters()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        runtime.Errors.Report("A", Thrown("boom"));
        var window = runtime.Errors.Window!;

        Assert.True(window.CanResize);
        Assert.Null(window.Owner);
        var text = window.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(text.IsReadOnly);
        Assert.Equal("Cascadia Mono", text.FontFamily.Name);
        // Cascadia Mono at 13 px advances 8 px per character under Tk's GDI rounding, on a 17 px line (SvPalette).
        Assert.InRange(text.Bounds.Width, 120 * 8, 120 * 8 + 20);
        Assert.InRange(text.Bounds.Height, 25 * 17, 25 * 17 + 20);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void A_report_from_another_thread_is_logged_at_once_and_reaches_the_window_on_the_ui_thread()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var error = Thrown("worker");

        Task.Run(() => runtime.Errors.Report("Exception in a worker", error)).Wait();

        Assert.Single(logger.Entries);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal($"Exception in a worker\n{error}", runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public async Task A_failing_background_operation_reaches_the_window()
    {
        var runtime = new AppRuntime(new CapturingLogger());

        await Assert.ThrowsAsync<OperationFailedException>(() => runtime.Operations.RunAsync(
            "Load F4SETab", OperationPhase.Blocking, () => throw new InvalidOperationException("boom")));

        Assert.StartsWith(
            "Exception in background operation 'Load F4SETab'\nSystem.InvalidOperationException: boom",
            runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void An_exception_escaping_ui_thread_code_is_reported_and_the_app_survives()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        using (runtime.Errors.InstallGlobalHandlers())
        {
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("from a click handler"));
            Dispatcher.UIThread.RunJobs();
        }

        Assert.StartsWith(
            "Exception on the UI thread\nSystem.InvalidOperationException: from a click handler",
            runtime.Errors.Window!.Text);
    }

    [AvaloniaFact]
    public async Task An_operation_failure_that_escapes_ui_thread_code_is_not_reported_twice()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        using (runtime.Errors.InstallGlobalHandlers())
        {
            // The usual shape: an async void event handler awaits an operation that dies, and lets it propagate.
            var handlerFinished = new TaskCompletionSource();
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await runtime.Operations.RunAsync(
                        "Refresh", OperationPhase.Blocking, () => throw new InvalidOperationException());
                }
                finally
                {
                    handlerFinished.SetResult();
                }
            });
            await handlerFinished.Task;
            // The async void method rethrows on the dispatcher after its finally block; let that job run.
            Dispatcher.UIThread.RunJobs();
        }

        // The global hooks are process-wide, so another test's stray error may show up here too; only this
        // operation's reports matter.
        Assert.Single(logger.Messages, m => m.Contains("'Refresh'", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains(ErrorReporter.UiThreadHeader, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-12")]
    public void An_unobserved_task_exception_is_reported()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        using (runtime.Errors.InstallGlobalHandlers())
        {
            AbandonFaultedTask("never observed");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Contains(logger.Messages, m =>
            m.StartsWith("StdErr : Unobserved exception in a background task\n", StringComparison.Ordinal)
            && m.Contains("never observed", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Disposing_the_global_handlers_unhooks_them()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        runtime.Errors.InstallGlobalHandlers().Dispose();

        var survived = false;
        Dispatcher.UIThread.UnhandledException += MarkHandled;
        try
        {
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("after dispose"));
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= MarkHandled;
        }

        Assert.True(survived);
        Assert.Empty(logger.Entries);

        void MarkHandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            survived = true;
            e.Handled = true;
        }
    }

    /// <summary>Faults a task nobody awaits. Not inlined, so nothing on this stack keeps the task alive for the GC.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonFaultedTask(string message)
        => _ = Task.FromException(new InvalidOperationException(message));
}
