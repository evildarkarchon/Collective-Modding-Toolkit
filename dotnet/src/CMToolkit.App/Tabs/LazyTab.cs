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
/// The whole load, building included, runs in one <see cref="UiActions.Blocking"/> span, so no click, key or tab switch
/// lands between the load and its content appearing. An exception mid-load leaves <c>_loading</c> set for good, which is
/// what stops every later selection from retrying.
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
    /// Handles this tab becoming the selected one (<c>CMCTabFrame.load</c>): switches to it if it is loaded, or loads it
    /// the first time. A UI Action: start it through <see cref="UiActions.Start"/>, which reports its failure.
    /// </summary>
    /// <remarks>
    /// An exception leaves the flags where it found them, as in the reference: one thrown mid-load never clears
    /// <c>_loading</c>, which is what stops every later selection from retrying, so the tab stays on its loading text.
    /// </remarks>
    /// <returns>A task that completes when the load, if any, has finished.</returns>
    /// <exception cref="OperationFailedException">
    /// The load failed; the error has been reported, by the Operation that died or by the load's blocking span.
    /// </exception>
    public async Task SelectAsync()
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
        var label = new TextBlock
        {
            Text = Page.LoadingText ?? "",
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Classes.Add("large");
        _label = label;
        Content = label;

        await _runtime.Actions.Blocking(async () =>
        {
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
                label.Text = Page.LoadingError ?? "Failed to load tab.";
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("CmtBad"));
            }

            _loading = false;
        });
    }

    /// <summary>Handles another tab being selected while this one was current.</summary>
    public void Deselect() => Page.SwitchFrom();
}
