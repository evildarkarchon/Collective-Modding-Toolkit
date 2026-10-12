using CMToolkit.Core;
using CMToolkit.Core.ArchivePatcher;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Core.ArchivePatcher;

/// <summary>
/// <see cref="ArchivePatching"/> beyond its scenarios: an archive another process holds (a sharing violation, which
/// Python raises as PermissionError), the list's path order (<c>sorted(files_to_patch)</c>), and the empty run.
/// Expected texts are the reference's (<c>_archives.py</c>); the path order was printed by CPython 3.14.5.
/// </summary>
public sealed class ArchivePatchingTests
{
    [Fact]
    [Trait("Parity", "AP-3")]
    public void An_archive_another_process_holds_fails_as_permissions_or_in_use()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "Held - Main.ba2");
        File.WriteAllBytes(path, [.. "BTDX"u8, 8, 0, 0, 0, .. "GNRL"u8]);
        var events = new List<OperationEvent>();
        // Absolute paths: the host's working directory isn't consulted.
        var host = SystemHostEnvironment.Instance;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = ArchivePatching.Patch([new PythonPath(path)], ArchiveVersion.OldGen, null, host, new CapturingLogger(), new ListProgress(events));
            Assert.Equal(new ArchivePatchResult(0, 1), result);
        }

        Assert.Equal(
            [
                new LogLine(LogType.Bad, "Failed patching (Permissions/In-Use): Held - Main.ba2"),
                new LogLine(LogType.Info, "Patching complete. 0 Successful, 1 Failed."),
            ],
            events);
        Assert.Equal(8, File.ReadAllBytes(path)[4]);
    }

    [Fact]
    [Trait("Parity", "AP-3")]
    public void An_empty_run_has_nothing_to_do_and_does_not_log_it()
    {
        var events = new List<OperationEvent>();
        var logger = new CapturingLogger();

        var result = ArchivePatching.Patch([], ArchiveVersion.NextGen, "ss", SystemHostEnvironment.Instance, logger, new ListProgress(events));

        Assert.Equal(new ArchivePatchResult(0, 0), result);
        Assert.Equal([new LogLine(LogType.Info, "Nothing to do!", WriteToLogFile: false)], events);
        Assert.Equal([new LogEntry(LogLevel.Information, "Files: 0 | Version: 8 | Filter: ss")], logger.Entries);
    }

    [Fact]
    [Trait("Parity", "AP-1")]
    public void The_list_is_in_pathlib_order()
    {
        string[] unsorted =
        [
            @"C:\Data\b.ba2", @"C:\Data\Sub\..\a.ba2", @"C:\Data\A.ba2", @"C:\Data\a b.ba2", @"C:\Data\a.ba2x", @"C:\Data\Z",
            @"C:\Data\z\a", @"C:\Data\[x].ba2", @"C:\Data\_x.ba2", "C:\\Data\\\uFF21.ba2", "C:\\Data\\\U0001F600.ba2",
        ];

        var sorted = ArchivePatching.InDisplayOrder(unsorted.Select(p => new PythonPath(p))).Select(p => p.ToString());

        Assert.Equal(
            [
                @"C:\Data\[x].ba2", @"C:\Data\_x.ba2", @"C:\Data\a b.ba2", @"C:\Data\A.ba2", @"C:\Data\a.ba2x", @"C:\Data\b.ba2",
                @"C:\Data\Sub\..\a.ba2", @"C:\Data\Z", @"C:\Data\z\a", "C:\\Data\\\uFF21.ba2", "C:\\Data\\\U0001F600.ba2",
            ],
            sorted);
    }

    /// <summary>Collects reports as they come, on the reporting thread.</summary>
    private sealed class ListProgress(List<OperationEvent> events) : IProgress<OperationEvent>
    {
        public void Report(OperationEvent value) => events.Add(value);
    }
}
