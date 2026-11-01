using Avalonia.Headless.XUnit;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Overview Report module (<see cref="OverviewState"/>) at its interface, over fake gather functions: what its load
/// and refresh leave current, what they report and log, and when it announces a refresh.
/// </summary>
public sealed class OverviewStateTests
{
    private static readonly OverviewReport OldGen = new(InstallType.OldGen, [], null, []);
    private static readonly OverviewReport NextGen = new(InstallType.NextGen, [], null, []);

    [AvaloniaFact]
    public void A_load_sets_the_report_and_raises_nothing()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        OverviewReport? passed = NextGen;
        var state = new OverviewState(runtime, previous =>
        {
            passed = previous;
            return OldGen;
        });
        var refreshed = 0;
        state.Refreshed += (_, _) => refreshed++;

        Await(state.LoadAsync());

        Assert.Null(passed);
        Assert.Same(OldGen, state.Report);
        Assert.Equal(InstallType.OldGen, state.InstallType);
        Assert.Equal(0, refreshed);
    }

    [AvaloniaFact]
    public void A_refresh_gathers_from_the_previous_report_replaces_it_and_raises_refreshed_once()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var gathered = new List<OverviewReport?>();
        var state = new OverviewState(runtime, previous =>
        {
            gathered.Add(previous);
            return gathered.Count == 1 ? OldGen : NextGen;
        });
        Await(state.LoadAsync());
        var refreshed = new List<OverviewReport?>();
        state.Refreshed += (_, _) => refreshed.Add(state.Report);

        Await(state.RefreshAsync());

        Assert.Equal([null, OldGen], gathered);
        Assert.Same(NextGen, state.Report);
        Assert.Equal([NextGen], refreshed);
        Assert.Equal([new LogEntry(LogLevel.Debug, "Overview : Refresh")], logger.Entries);
    }

    [AvaloniaTheory]
    [InlineData("Overview load")]
    [InlineData("Overview refresh")]
    public void A_gathering_that_dies_part_way_leaves_what_it_had_gathered_current_reports_once_and_throws(string operation)
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var partial = NextGen with { Warnings = [new OverviewWarning("Title", "Message")] };
        var calls = 0;
        var state = new OverviewState(runtime, _ => ++calls == 1 && operation == "Overview refresh"
            ? OldGen
            : throw new OverviewIncompleteException(partial, new IOException("The modules couldn't be read.")));
        if (operation == "Overview refresh")
        {
            Await(state.LoadAsync());
        }

        var refreshed = 0;
        state.Refreshed += (_, _) => refreshed++;

        var task = operation == "Overview load" ? state.LoadAsync() : state.RefreshAsync();
        DispatcherWait.Until(() => task.IsCompleted);

        var failure = Assert.IsType<OperationFailedException>(task.Exception?.InnerException);
        Assert.IsType<OverviewIncompleteException>(failure.InnerException);
        Assert.Same(partial, state.Report);
        var report = Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader(operation)}", report.Message, StringComparison.Ordinal);
        Assert.Equal(0, refreshed);
    }

    [AvaloniaFact]
    public void A_refresh_whose_gathering_dies_with_nothing_gathered_leaves_the_previous_report_current()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var calls = 0;
        var state = new OverviewState(runtime, _ => ++calls == 1 ? OldGen : throw new InvalidDataException("Unreadable."));
        Await(state.LoadAsync());
        var refreshed = 0;
        state.Refreshed += (_, _) => refreshed++;

        var task = state.RefreshAsync();
        DispatcherWait.Until(() => task.IsCompleted);

        Assert.IsType<OperationFailedException>(task.Exception?.InnerException);
        Assert.Same(OldGen, state.Report);
        Assert.Equal(0, refreshed);
    }

    [AvaloniaFact]
    public void Input_is_blocked_while_the_gathering_runs()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var state = new OverviewState(runtime, _ =>
        {
            started.Set();
            release.Wait(TaskTimeouts.Default);
            return OldGen;
        });

        var load = state.LoadAsync();
        DispatcherWait.Until(() => started.IsSet);
        Assert.True(runtime.Input.IsBlocking);
        release.Set();
        DispatcherWait.Until(() => load.IsCompleted);
        Assert.False(runtime.Input.IsBlocking);

        started.Reset();
        release.Reset();
        var refresh = state.RefreshAsync();
        DispatcherWait.Until(() => started.IsSet);
        Assert.True(runtime.Input.IsBlocking);
        release.Set();
        DispatcherWait.Until(() => refresh.IsCompleted);
        Assert.False(runtime.Input.IsBlocking);
    }

    [Fact]
    public void Only_the_module_can_set_the_report()
    {
        var setter = typeof(OverviewState).GetProperty(nameof(OverviewState.Report))!.SetMethod;

        Assert.True(setter is null || setter.IsPrivate);
    }

    /// <summary>Runs the dispatcher until <paramref name="task"/> has finished, then rethrows its failure, if any.</summary>
    private static void Await(Task task)
    {
        DispatcherWait.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
