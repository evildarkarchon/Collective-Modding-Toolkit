using System.Globalization;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.ArchivePatcher;

/// <summary>A BA2 header's version byte (<c>enums.ArchiveVersion</c>): what the Archive Patcher reads and writes.</summary>
public enum ArchiveVersion
{
    /// <summary>v1, the Old-Gen version every game version reads; the <c>v1 (OG)</c> target.</summary>
    OldGen = 1,

    /// <summary>v7, a Next-Gen version the patcher only ever patches from.</summary>
    NextGen7 = 7,

    /// <summary>v8, the Next-Gen version; the <c>v8 (NG)</c> target.</summary>
    NextGen = 8,
}

/// <summary>What one Patch All run did: <c>patched</c> and <c>failed</c>, as its summary line counts them.</summary>
/// <param name="Patched">Archives whose version byte was written.</param>
/// <param name="Failed">Archives skipped or that couldn't be patched.</param>
public sealed record ArchivePatchResult(int Patched, int Failed);

/// <summary>
/// <c>patcher.ArchivePatcher</c>'s logic (§9.4): which archives the window lists for a target version and name filter
/// (AP-2), and Patch All, which rewrites the single version byte of each (AP-3). The App composes it with the Overview
/// refresh and the repopulate that follow a run.
/// </summary>
public static class ArchivePatching
{
    /// <summary>The offset of the version byte, after the <c>BTDX</c> magic.</summary>
    private const int VersionOffset = 4;

    /// <summary>ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION as HRESULTs, which Python raises as PermissionError.</summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// The Name Filter's value from the entry's text (<c>on_key_release</c>): the text case-folded, or null when it is
    /// empty, which filters nothing.
    /// </summary>
    public static string? NameFilter(string entryText) => entryText.Length > 0 ? PythonCaseFold.Of(entryText) : null;

    /// <summary>
    /// <c>files_to_patch</c>: the Overview's enabled Next-Gen archives when the target is v1, otherwise its Old-Gen ones
    /// (base game included), narrowed to those whose case-folded file name contains <paramref name="nameFilter"/>. The
    /// reference's set has no order (T-15); this keeps the Overview's.
    /// </summary>
    /// <param name="archives">The current Overview report's archives; <see cref="OverviewArchives.None"/> before one.</param>
    /// <param name="target">The Desired Version.</param>
    /// <param name="nameFilter">The case-folded filter (<see cref="NameFilter"/>), or null for none.</param>
    public static IReadOnlyList<PythonPath> FilesToPatch(OverviewArchives archives, ArchiveVersion target, string? nameFilter)
    {
        var files = target == ArchiveVersion.OldGen ? archives.NextGen : archives.OldGen;
        if (string.IsNullOrEmpty(nameFilter))
        {
            return files;
        }

        return [.. files.Where(file => PythonCaseFold.Of(file.Name).Contains(nameFilter, StringComparison.Ordinal))];
    }

    /// <summary>The order the window lists the files in: <c>sorted(files_to_patch)</c>, by path (pathlib's order).</summary>
    public static IReadOnlyList<PythonPath> InDisplayOrder(IEnumerable<PythonPath> files) => [.. files.Order(PythonPath.Ordering)];

    /// <summary>
    /// <c>ArchivePatcher.patch_files</c>: for each archive, checks the <c>BTDX</c> magic and the version byte, and writes
    /// the target's byte at offset 4 when it is one the target patches from (v7 or v8 for v1, v1 for v8). Each outcome is
    /// a <see cref="LogLine"/>, then the summary. The read-only attribute is deliberately never cleared (MO2/Windows 11
    /// issue 2174), so a read-only archive fails.
    /// </summary>
    /// <param name="files">The files to patch (<see cref="FilesToPatch"/>), in the order to patch them.</param>
    /// <param name="target">The Desired Version: <see cref="ArchiveVersion.OldGen"/> or <see cref="ArchiveVersion.NextGen"/>.</param>
    /// <param name="nameFilter">The filter in effect, only for the log line that opens the run.</param>
    /// <param name="host">The machine; relative paths resolve against its working directory.</param>
    /// <param name="logger">The app log, for the run's opening line. The pane lines are logged by whoever shows them.</param>
    /// <param name="progress">Receives the pane lines, in order. Report is called on the calling thread.</param>
    /// <returns>The run's counts. With no files, nothing is opened and both are 0.</returns>
    public static ArchivePatchResult Patch(
        IReadOnlyList<PythonPath> files,
        ArchiveVersion target,
        string? nameFilter,
        IHostEnvironment host,
        ILogger logger,
        IProgress<OperationEvent> progress)
    {
        logger.Write(LogLevel.Information, $"Files: {files.Count} | Version: {(int)target} | Filter: {nameFilter ?? "None"}");
        if (files.Count == 0)
        {
            progress.Report(new LogLine(LogType.Info, "Nothing to do!", WriteToLogFile: false));
            return new ArchivePatchResult(0, 0);
        }

        byte[] sources = target == ArchiveVersion.OldGen
            ? [(byte)ArchiveVersion.NextGen7, (byte)ArchiveVersion.NextGen]
            : [(byte)ArchiveVersion.OldGen];
        var patched = 0;
        var failed = 0;
        foreach (var file in files)
        {
            var failure = PatchOne(HostPaths.Resolve(host, file), (byte)target, sources);
            if (failure is null)
            {
                progress.Report(new LogLine(LogType.Good, $"Patched to v{(int)target}: {file.Name}"));
                patched++;
            }
            else
            {
                progress.Report(new LogLine(LogType.Bad, $"{failure}: {file.Name}"));
                failed++;
            }
        }

        progress.Report(new LogLine(LogType.Info, $"Patching complete. {patched} Successful, {failed} Failed."));
        return new ArchivePatchResult(patched, failed);
    }

    /// <summary>
    /// One archive, opened <c>r+b</c>: null once the byte is written, otherwise the failure's text before the file name.
    /// </summary>
    private static string? PatchOne(string path, byte target, byte[] sources)
    {
        try
        {
            // Python's open() shares the file for reading and writing (_wopen), and so may a running game or tool.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            Span<byte> magic = stackalloc byte[4];
            var read = stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
            if (read != magic.Length || !magic.SequenceEqual("BTDX"u8))
            {
                return "Unrecognized format";
            }

            stream.Position = VersionOffset;
            var current = stream.ReadByte();
            if (current == target)
            {
                return "Skipping already-patched archive";
            }

            if (current < 0 || !sources.Contains((byte)current))
            {
                // bytes.hex(): two lowercase digits, or nothing when the file ends after the magic.
                var hex = current < 0 ? string.Empty : ((byte)current).ToString("x2", CultureInfo.InvariantCulture);
                return $"Unrecognized version [{hex}]";
            }

            stream.Position = VersionOffset;
            stream.WriteByte(target);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return "Failed patching (File Not Found)";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || (ex is IOException io && io.HResult is SharingViolation or LockViolation))
        {
            // Access denied (a read-only file, a folder) and a file another process holds are PermissionError in Python.
            return "Failed patching (Permissions/In-Use)";
        }
        catch (IOException)
        {
            // Any other OSError, including one from flushing the byte as the file closes.
            return "Failed patching (Unknown OS Error)";
        }

        return null;
    }
}
