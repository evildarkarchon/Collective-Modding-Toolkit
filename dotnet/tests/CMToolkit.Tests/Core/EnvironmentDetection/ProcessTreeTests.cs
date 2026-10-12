using System.Diagnostics;
using CMToolkit.Core;
using CMToolkit.Core.Win32;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Core.EnvironmentDetection;

/// <summary>
/// MM-1's walk up the ancestors, as psutil's <c>Process(os.getppid())</c> then repeated <c>.parent()</c> do it, over
/// hand-built snapshots, plus the real Toolhelp32 walk on this machine (T-5).
/// </summary>
[Trait("Parity", "MM-1")]
public sealed class ProcessTreeTests
{
    private static readonly Func<int, string?> ExeByPid = pid => $@"C:\p{pid}.exe";

    private static Dictionary<int, ProcessEntry> Snapshot(params ProcessEntry[] entries) => entries.ToDictionary(e => e.ProcessId);

    [Fact]
    public void The_walk_starts_at_the_parent_and_goes_up_nearest_first()
    {
        var processes = Snapshot(
            new ProcessEntry(10, 20, "cm-toolkit.exe"),
            new ProcessEntry(20, 30, "ModOrganizer.exe"),
            new ProcessEntry(30, 40, "explorer.exe"),
            new ProcessEntry(40, 4, "userinit.exe"));

        var ancestors = ProcessTree.Walk(processes, 10, 8, _ => null, ExeByPid);

        Assert.Equal(
            [
                new AncestorProcess("ModOrganizer.exe", @"C:\p20.exe"),
                new AncestorProcess("explorer.exe", @"C:\p30.exe"),
                new AncestorProcess("userinit.exe", @"C:\p40.exe"),
            ],
            ancestors);
    }

    [Fact]
    public void The_walk_stops_at_the_limit()
    {
        var processes = Snapshot([.. Enumerable.Range(1, 12).Select(pid => new ProcessEntry(pid, pid + 1, $"p{pid}.exe"))]);

        var ancestors = ProcessTree.Walk(processes, 1, 8, _ => null, ExeByPid);

        Assert.Equal(Enumerable.Range(2, 8).Select(pid => $"p{pid}.exe"), ancestors.Select(a => a.Name));
    }

    [Fact]
    public void A_parent_created_after_its_child_is_a_reused_pid_and_ends_the_walk()
    {
        var processes = Snapshot(
            new ProcessEntry(10, 20, "cm-toolkit.exe"),
            new ProcessEntry(20, 30, "launcher.exe"),
            new ProcessEntry(30, 1, "unrelated.exe"));
        var created = new Dictionary<int, long> { [10] = 300, [20] = 200, [30] = 250 };

        var ancestors = ProcessTree.Walk(processes, 10, 8, pid => created[pid], ExeByPid);

        Assert.Equal(["launcher.exe"], ancestors.Select(a => a.Name));
    }

    [Fact]
    public void An_unreadable_creation_time_doesnt_end_the_walk()
    {
        var processes = Snapshot(
            new ProcessEntry(10, 20, "cm-toolkit.exe"),
            new ProcessEntry(20, 30, "launcher.exe"),
            new ProcessEntry(30, 1, "services.exe"));

        var ancestors = ProcessTree.Walk(processes, 10, 8, pid => pid == 30 ? null : 100, _ => null);

        Assert.Equal(["launcher.exe", "services.exe"], ancestors.Select(a => a.Name));
        Assert.All(ancestors, a => Assert.Null(a.ExePath));
    }

    [Fact]
    public void A_grandparent_that_has_exited_ends_the_walk()
    {
        var processes = Snapshot(new ProcessEntry(10, 20, "cm-toolkit.exe"), new ProcessEntry(20, 999, "launcher.exe"));

        Assert.Equal(["launcher.exe"], ProcessTree.Walk(processes, 10, 8, _ => null, ExeByPid).Select(a => a.Name));
    }

    [Fact]
    public void Pid_0_has_no_parent()
    {
        var processes = Snapshot(new ProcessEntry(4, 0, "System"), new ProcessEntry(0, 0, "[System Process]"));

        Assert.Equal(["[System Process]"], ProcessTree.Walk(processes, 4, 8, _ => null, ExeByPid).Select(a => a.Name));
    }

    /// <summary>
    /// Bug #79, fixed (ADR-0006): psutil's <c>Process(os.getppid())</c> raises <c>NoSuchProcess</c> for a parent that
    /// has exited, and the reference doesn't catch it, so its startup fails. The port treats the first step like every
    /// later one, as psutil's <c>parent()</c> does: a parent that is gone ends the walk, here with no ancestors at all.
    /// </summary>
    [Fact]
    [Deviation(79, "A launching process that has already exited ends the ancestor walk with no ancestors instead of failing startup.")]
    public void A_parent_that_has_exited_leaves_no_ancestors()
    {
        var processes = Snapshot(new ProcessEntry(10, 20, "cm-toolkit.exe"));

        Assert.Empty(ProcessTree.Walk(processes, 10, 8, _ => null, ExeByPid));
    }

    /// <summary>
    /// T-5 (in part): on the real machine, names keep their <c>.exe</c> (unlike <see cref="Process.ProcessName"/>) and the
    /// parent's exe path is readable. Issue #35 measured the same walk finding MO2 2.5.2 both normal and elevated.
    /// </summary>
    [Fact]
    [Trait("Parity", "T-5")]
    public void The_real_walk_reports_the_parent_with_its_exe_name_and_path()
    {
        var ancestors = SystemHostEnvironment.Instance.GetAncestorProcesses(8);

        Assert.InRange(ancestors.Count, 1, 8);
        var parent = ancestors[0];
        Assert.NotNull(parent.ExePath);
        Assert.Equal(Path.GetFileName(parent.ExePath), parent.Name, ignoreCase: true);
        Assert.EndsWith(".exe", parent.Name, StringComparison.OrdinalIgnoreCase);
    }
}
