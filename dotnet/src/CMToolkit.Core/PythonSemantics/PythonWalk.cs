using System.IO.Enumeration;

namespace CMToolkit.Core.PythonSemantics;

/// <summary>One folder <see cref="PythonWalk.TopDown"/> visits: <c>(dirpath, dirnames, filenames)</c>.</summary>
/// <param name="Path">The folder, joined from the top as <c>os.walk</c> joins it.</param>
/// <param name="Relative">The folder relative to the top (<c>path.relative_to(top)</c>); <c>.</c> for the top itself.</param>
/// <param name="Depth">0 for the top, 1 for its children, and so on.</param>
/// <param name="Folders">
/// The sub-folders, in enumeration order. Removing names from it before the walk moves on prunes them, as deleting
/// from <c>dirnames</c> does.
/// </param>
/// <param name="Files">Everything else in the folder, in enumeration order.</param>
public sealed record WalkStep(PythonPath Path, PythonPath Relative, int Depth, List<string> Folders, IReadOnlyList<string> Files);

/// <summary>
/// <c>pathlib.Path.walk(top_down=True)</c> as CPython 3.14 runs it, through <c>os.walk</c>: a depth-first pre-order walk
/// in directory enumeration order (on NTFS, the same order <c>os.scandir</c> gives, which the parity goldens compare
/// exactly), where symbolic links and junctions to folders count as files and are never entered, and a folder that
/// can't be listed (or fails mid-listing) is skipped silently with everything under it.
/// </summary>
public static class PythonWalk
{
    private static readonly EnumerationOptions Options = new()
    {
        // os.scandir lists hidden and system entries too.
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Walks <paramref name="top"/> lazily: each step is yielded before its sub-folders are listed, so the caller may
    /// prune <see cref="WalkStep.Folders"/> first.
    /// </summary>
    /// <param name="top">The folder to walk, as the reference names it.</param>
    /// <param name="resolve">Turns a reference path into the file-system path to list (relative paths resolve against the host's working directory).</param>
    public static IEnumerable<WalkStep> TopDown(PythonPath top, Func<PythonPath, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(resolve);
        var stack = new Stack<(PythonPath Path, PythonPath Relative, int Depth)>();
        stack.Push((top, new PythonPath(string.Empty), 0));
        while (stack.Count > 0)
        {
            var (path, relative, depth) = stack.Pop();
            var listing = List(resolve(path));
            if (listing is null)
            {
                continue;
            }

            var step = new WalkStep(path, relative, depth, listing.Value.Folders, listing.Value.Files);
            yield return step;
            // os.walk pushes reversed(dirs), so they are popped in order.
            for (var i = step.Folders.Count - 1; i >= 0; i--)
            {
                var name = step.Folders[i];
                stack.Push((path.Join(name), relative.Join(name), depth + 1));
            }
        }
    }

    /// <summary>One <c>os.scandir</c> pass: (folders, files), or null when the folder can't be listed.</summary>
    private static (List<string> Folders, List<string> Files)? List(string path)
    {
        var folders = new List<string>();
        var files = new List<string>();
        try
        {
            var entries = new FileSystemEnumerable<(string Name, bool IsFolder)>(
                path,
                (ref FileSystemEntry entry) => (entry.FileName.ToString(), IsFolder(ref entry)),
                Options);
            foreach (var (name, isFolder) in entries)
            {
                (isFolder ? folders : files).Add(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // os.walk with no onerror: a scandir failure, at the start or mid-listing, skips the folder.
            return null;
        }

        return (folders, files);
    }

    /// <summary>
    /// <c>entry.is_dir(follow_symlinks=False) and not entry.is_junction()</c>: a folder that isn't a symbolic link or a
    /// junction. Other reparse points (cloud placeholders, deduplicated folders) are still folders.
    /// </summary>
    private static bool IsFolder(ref FileSystemEntry entry)
    {
        if (!entry.IsDirectory)
        {
            return false;
        }

        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return true;
        }

        // LinkTarget is non-null only for symbolic links and junctions (mount points).
        try
        {
            return entry.ToFileSystemInfo().LinkTarget is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // DirEntry.is_dir() answers False rather than raise.
            return false;
        }
    }
}
