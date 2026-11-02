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

    [AvaloniaFact]
    public void A_subscriber_that_throws_is_reported_once_and_neither_fails_the_refresh_nor_stops_the_others()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var calls = 0;
        var state = new OverviewState(runtime, _ => ++calls == 1 ? OldGen : NextGen);
        Await(state.LoadAsync());
        var before = 0;
        var after = 0;
        state.Refreshed += (_, _) => before++;
        state.Refreshed += (_, _) => throw new InvalidOperationException("A reader broke.");
        state.Refreshed += (_, _) => after++;

        Await(state.RefreshAsync());

        Assert.Same(NextGen, state.Report);
        Assert.Equal(1, before);
        Assert.Equal(1, after);
        var report = Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.UiThreadHeader}", report.Message, StringComparison.Ordinal);
        Assert.Contains("A reader broke.", report.Message, StringComparison.Ordinal);
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
    public void A_try_load_that_succeeds_makes_its_report_current_and_raises_nothing()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var state = new OverviewState(runtime, _ => OldGen);
        var refreshed = 0;
        state.Refreshed += (_, _) => refreshed++;

        var outcome = Await(state.TryLoadAsync());

        Assert.Same(OldGen, Assert.IsType<OperationOutcome<OverviewReport>.Succeeded>(outcome).Value);
        Assert.Same(OldGen, state.Report);
        Assert.Equal(0, refreshed);
        Assert.Empty(logger.Entries);
    }

    [AvaloniaFact]
    public void A_try_refresh_that_succeeds_makes_its_report_current_and_raises_refreshed_once()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var calls = 0;
        var state = new OverviewState(runtime, _ => ++calls == 1 ? OldGen : NextGen);
        Await(state.LoadAsync());
        var refreshed = new List<OverviewReport?>();
        state.Refreshed += (_, _) => refreshed.Add(state.Report);

        var outcome = Await(state.TryRefreshAsync());

        Assert.Same(NextGen, Assert.IsType<OperationOutcome<OverviewReport>.Succeeded>(outcome).Value);
        Assert.Same(NextGen, state.Report);
        Assert.Equal([NextGen], refreshed);
    }

    [AvaloniaTheory]
    [InlineData("Overview load")]
    [InlineData("Overview refresh")]
    public void A_try_whose_gathering_dies_part_way_returns_failed_leaves_the_partial_report_current_and_reports_once(
        string operation)
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

        var outcome = Await(operation == "Overview load" ? state.TryLoadAsync() : state.TryRefreshAsync());

        Assert.IsType<OverviewIncompleteException>(Assert.IsType<OperationOutcome<OverviewReport>.Failed>(outcome).Error);
        Assert.Same(partial, state.Report);
        var report = Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader(operation)}", report.Message, StringComparison.Ordinal);
        Assert.Equal(0, refreshed);
    }

    [AvaloniaFact]
    public void A_try_refresh_whose_gathering_dies_with_nothing_gathered_leaves_the_previous_report_current()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var calls = 0;
        var thrown = new InvalidDataException("Unreadable.");
        var state = new OverviewState(runtime, _ => ++calls == 1 ? OldGen : throw thrown);
        Await(state.LoadAsync());
        var refreshed = 0;
        state.Refreshed += (_, _) => refreshed++;

        var outcome = Await(state.TryRefreshAsync());

        Assert.Same(thrown, Assert.IsType<OperationOutcome<OverviewReport>.Failed>(outcome).Error);
        Assert.Same(OldGen, state.Report);
        Assert.Equal(0, refreshed);
        Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
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

    /// <summary>Runs the dispatcher until <paramref name="task"/> has finished, then returns its result.</summary>
    private static T Await<T>(Task<T> task)
    {
        DispatcherWait.Until(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }
}
