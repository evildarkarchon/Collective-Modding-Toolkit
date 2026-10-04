using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Tabs;

/// <summary>
/// A tab's content area, loading its <see cref="TabPage"/> the first time it is selected (SHELL-8;
/// <c>CMCTabFrame.load</c>). While it loads, a centred 20 pt label shows the page's loading text. If the load reports
/// failure, the label shows the error in the bad colour; if it throws, the loading text stays up. Either way the tab is
/// never retried.
/// </summary>
/// <remarks>
/// The whole load, building included, runs under an input block, because the reference ran it inside one Tk callback.
/// The state flags mirror the reference's: an exception mid-load leaves <c>_loading</c> set for good, which is what
/// stops every later selection from retrying.
/// </remarks>
public sealed class LazyTab : ContentControl
{
    private readonly AppRuntime _runtime;
    private bool _loading;
    private bool _loaded;
    private TextBlock? _label;

    /// <param name="page">The page this tab loads and shows.</param>
    /// <param name="runtime">The app runtime: its log, input block and Error Window.</param>
    public LazyTab(TabPage page, AppRuntime runtime)
    {
        Page = page;
        _runtime = runtime;
    }

    /// <summary>The page this tab loads and shows.</summary>
    public TabPage Page { get; }

    /// <summary>
    /// Handles this tab becoming the selected one: switches to it if it is loaded, or loads it the first time.
    /// Exceptions, from the load or from a later switch, are reported to the Error Window rather than thrown, so the
    /// returned task never faults and callers may discard it.
    /// </summary>
    /// <returns>A task that completes when the load, if any, has finished or failed.</returns>
    public async Task SelectAsync()
    {
        try
        {
            await SwitchToOrLoadAsync();
        }
        catch (OperationFailedException)
        {
            // The runner reported it when the operation died. A load stays on its loading text with _loading set.
        }
        catch (Exception ex)
        {
            // Reported here, not left in the task: the caller discards the task, so a fault would only surface if the
            // garbage collector raised it as unobserved, maybe never. The input block is already released, but
            // deferred input replays from a posted job, so the Error Window is up first.
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
        }
    }

    /// <summary>
    /// <c>CMCTabFrame.load</c>. An exception leaves the flags where it found them, as in the reference: one thrown
    /// mid-load never clears <c>_loading</c>, which is what stops every later selection from retrying.
    /// </summary>
    private async Task SwitchToOrLoadAsync()
    {
        _runtime.Logger.LogDebug("Switch Tab : {Tab}", Page.LogName);
        if (_loaded)
        {
            Page.SwitchTo();
            return;
        }

        // Still loading, or a previous load threw (and so never cleared the flag), or it failed (the label is up).
        if (_loading || _label is not null)
        {
            return;
        }

        _runtime.Logger.LogDebug("Load Tab : {Tab}", Page.LogName);
        _loading = true;
        _label = new TextBlock
        {
            Text = Page.LoadingText ?? "",
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label.Classes.Add("large");
        Content = _label;

        using var block = _runtime.Input.Block();
        if (await Page.LoadAsync())
        {
            Content = null;
            _label = null;
            _loaded = true;
            Content = Page.BuildContent();
            Page.SwitchTo();
        }
        else
        {
            // The reference logs loading_error with %s, so a missing one is logged as "None".
            _runtime.Logger.LogError("Load Tab : {Tab} : Failed : {Error}", Page.LogName, Page.LoadingError ?? "None");
            _label.Text = Page.LoadingError ?? "Failed to load tab.";
            _label.Bind(TextBlock.ForegroundProperty, _label.GetResourceObservable("CmtBad"));
        }

        _loading = false;
    }

    /// <summary>Handles another tab being selected while this one was current.</summary>
    public void Deselect() => Page.SwitchFrom();
}
