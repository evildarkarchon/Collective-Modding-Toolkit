using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core.Overview;

namespace CMToolkit.Tests.Support;

/// <summary>An Overview Report module for tests that only need a report to read, not a gathering to watch.</summary>
public static class FixedOverview
{
    /// <summary>
    /// Builds the module over <paramref name="runtime"/> with a gather function that always returns
    /// <paramref name="report"/>, and waits for its load. It logs nothing, so a test's log holds only what it
    /// exercises. Call it on the UI thread; a refresh of the returned module gathers <paramref name="report"/> again.
    /// </summary>
    /// <returns>The module, loaded, with <paramref name="report"/> current and input no longer blocked.</returns>
    public static OverviewState Load(AppRuntime runtime, OverviewReport report)
    {
        var state = new OverviewState(runtime, _ => report);
        var load = state.LoadAsync();
        DispatcherWait.Until(() => load.IsCompleted);
        load.GetAwaiter().GetResult();
        return state;
    }
}
