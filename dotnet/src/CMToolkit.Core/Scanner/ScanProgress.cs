namespace CMToolkit.Core.Scanner;

/// <summary>
/// One raw progress update from the data walk: the folders it will enter, or the one it has entered. Internal to the
/// scan, whose <see cref="ScanProgressTracker"/> turns these into the <see cref="ScanStatus"/>es it reports.
/// </summary>
internal abstract record ScanUpdate
{
    private ScanUpdate()
    {
    }

    /// <summary>A <c>tuple</c>: the names of Data's top-level folders, which later folder names are placed in.</summary>
    /// <param name="Names">Every top-level folder, in enumeration order, before any is pruned.</param>
    public sealed record TopLevelFolders(IReadOnlyList<string> Names) : ScanUpdate;

    /// <summary>A <c>str</c>: the top-level Data folder the walk has entered.</summary>
    /// <param name="Name">The folder's name.</param>
    public sealed record Folder(string Name) : ScanUpdate;
}

/// <summary>What a scan reports for the Scanner tab to show as it goes: the scanning label's text and the progress bar's value.</summary>
/// <param name="Text">The scanning label (<c>sv_scanning_text</c>).</param>
/// <param name="Progress">The progress bar, out of 100 (<c>dv_progress</c>).</param>
public readonly record struct ScanStatus(string Text, double Progress);

/// <summary>
/// One scan's progress: turns the data walk's raw updates into statuses and passes a status on only when the label or
/// the progress changes. Each scan creates its own, so nothing (neither the folders nor the last status) carries over
/// from one scan to the next.
/// </summary>
/// <param name="statuses">Where the scan's statuses go, on the scan's thread; null for none.</param>
internal sealed class ScanProgressTracker(IProgress<ScanStatus>? statuses) : IProgress<ScanUpdate>
{
    // scan_folders = ("",) until the first tuple arrives.
    private IReadOnlyList<string> _folders = [string.Empty];

    // The last status passed on, which an equal one doesn't repeat; null before the first.
    private ScanStatus? _shown;

    /// <summary>Passes <paramref name="status"/> on, unless it is the status passed on last.</summary>
    public void Show(ScanStatus status)
    {
        if (_shown == status)
        {
            return;
        }

        _shown = status;
        statuses?.Report(status);
    }

    /// <summary>Applies <paramref name="value"/>, showing the status it gives, if any (see <see cref="Apply"/>).</summary>
    public void Report(ScanUpdate value)
    {
        if (Apply(value) is { } status)
        {
            Show(status);
        }
    }

    /// <summary>
    /// Applies <paramref name="update"/>: a folder tuple replaces the known folders and changes nothing shown; a name
    /// among them gives <c>Scanning... {index}/{max(1, n)}: {name}</c> (0-based index) and progress
    /// <c>index / n × 100</c>; any other name is ignored.
    /// </summary>
    /// <returns>What to show now, or null to leave the label and the bar as they are.</returns>
    public ScanStatus? Apply(ScanUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        switch (update)
        {
            case ScanUpdate.TopLevelFolders folders:
                _folders = [.. folders.Names];
                return null;
            case ScanUpdate.Folder folder:
                // tuple.index: the first equal name, compared exactly.
                var index = -1;
                for (var i = 0; i < _folders.Count; i++)
                {
                    if (string.Equals(_folders[i], folder.Name, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    return null;
                }

                // (current_index / len(scan_folders)) * 100: the division first, in doubles, as Python's true division.
                return new ScanStatus(
                    $"Scanning... {index}/{Math.Max(1, _folders.Count)}: {folder.Name}",
                    (double)index / _folders.Count * 100);
            default:
                throw new ArgumentOutOfRangeException(nameof(update));
        }
    }
}
