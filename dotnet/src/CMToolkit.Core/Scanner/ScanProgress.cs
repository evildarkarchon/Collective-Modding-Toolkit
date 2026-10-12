namespace CMToolkit.Core.Scanner;

/// <summary>
/// One progress update the scan worker reports (<c>queue_progress</c>, SCN-S4): the reference's queue items other than
/// the final problems list, which here is the worker's return value.
/// </summary>
public abstract record ScanUpdate
{
    private ScanUpdate()
    {
    }

    /// <summary>A <c>tuple</c>: the names of Data's top-level folders, which later folder names are placed in.</summary>
    /// <param name="Names">Every top-level folder, in enumeration order, before any is pruned.</param>
    public sealed record TopLevelFolders(IReadOnlyList<string> Names) : ScanUpdate;

    /// <summary>
    /// A <c>str</c>: the step the worker is on, a top-level Data folder's name or <c>Race Subgraph Records</c>.
    /// </summary>
    /// <param name="Name">The folder (or step) name.</param>
    public sealed record Folder(string Name) : ScanUpdate;
}

/// <summary>The scanning label's text and the progress bar's value after an update.</summary>
/// <param name="Text">The scanning label (<c>sv_scanning_text</c>).</param>
/// <param name="Progress">The progress bar, out of 100 (<c>dv_progress</c>).</param>
public readonly record struct ScanStatus(string Text, double Progress);

/// <summary>
/// <c>ScannerTab.check_scan_progress</c>'s reading of the progress queue (SCN-S4). Owned by the tab for its lifetime,
/// as <c>scan_folders</c> was: the last folder tuple is kept across scans.
/// </summary>
public sealed class ScanProgressTracker
{
    // scan_folders = ("",) until the first tuple arrives.
    private IReadOnlyList<string> _folders = [string.Empty];

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
