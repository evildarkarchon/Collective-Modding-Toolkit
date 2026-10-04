using Avalonia.Controls;
using CMToolkit.App.Tabs;

namespace CMToolkit.Tests.Support;

/// <summary>
/// A tab page whose load a test controls: it records each lifecycle call, and its load waits on <see cref="Gate"/> (open
/// by default) and then runs <see cref="Load"/>.
/// </summary>
public sealed class ProbeTab : TabPage
{
    private readonly string? _loadingText;

    public ProbeTab(string header, string logName, string? loadingText = null)
        : base(header, logName)
    {
        _loadingText = loadingText;
    }

    /// <summary>Every lifecycle call, in order: <c>load</c>, <c>build</c>, <c>switch_to</c>, <c>switch_from</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The load waits for this before running <see cref="Load"/>. Completed unless a test replaces it.</summary>
    public Task Gate { get; set; } = Task.CompletedTask;

    /// <summary>The body of the load, run after <see cref="Gate"/>; returns whether it succeeded. Succeeds by default.</summary>
    public Func<ProbeTab, Task<bool>> Load { get; set; } = _ => Task.FromResult(true);

    /// <summary>Runs inside every <c>switch_to</c>, after it is recorded; lets a test make it throw.</summary>
    public Action? OnSwitchTo { get; set; }

    /// <summary>The content the tab builds.</summary>
    public TextBlock Built { get; } = new() { Text = "built" };

    /// <inheritdoc/>
    public override string? LoadingText => _loadingText;

    /// <summary>Lets <see cref="Load"/> set the error text the reference's <c>_load</c> sets before failing.</summary>
    public void FailWith(string? error) => LoadingError = error;

    /// <inheritdoc/>
    protected override async Task<bool> LoadAsync()
    {
        Calls.Add("load");
        await Gate;
        return await Load(this);
    }

    /// <inheritdoc/>
    protected override Control BuildContent()
    {
        Calls.Add("build");
        return Built;
    }

    /// <inheritdoc/>
    protected override void SwitchTo()
    {
        Calls.Add("switch_to");
        OnSwitchTo?.Invoke();
    }

    /// <inheritdoc/>
    protected override void SwitchFrom() => Calls.Add("switch_from");
}
