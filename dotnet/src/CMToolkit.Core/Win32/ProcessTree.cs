using System.Runtime.InteropServices;
using static CMToolkit.Core.Win32.NativeMethods;

namespace CMToolkit.Core.Win32;

/// <summary>One process in a Toolhelp32 snapshot.</summary>
/// <param name="ProcessId">The PID.</param>
/// <param name="ParentProcessId">The PID it was started by, which may since have exited or been reused.</param>
/// <param name="Name">The image name with its extension (<c>szExeFile</c>), such as <c>ModOrganizer.exe</c>.</param>
public sealed record ProcessEntry(int ProcessId, int ParentProcessId, string Name);

/// <summary>
/// The running process's ancestors, as psutil's <c>Process(os.getppid())</c> and repeated <c>.parent()</c> walk them
/// (MM-1, T-5). Names come from Toolhelp32's <c>szExeFile</c>, which keeps <c>.exe</c> (unlike
/// <see cref="System.Diagnostics.Process.ProcessName"/>), and exe paths from <c>QueryFullProcessImageNameW</c> with
/// limited-query access, which issue #35 measured working for a normal and an elevated Mod Organizer parent.
/// </summary>
public static class ProcessTree
{
    /// <summary>The ancestors of this process, nearest first, at most <paramref name="maxCount"/>.</summary>
    /// <exception cref="InvalidOperationException">This process's parent has exited (see <see cref="Walk"/>).</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The process snapshot couldn't be taken.</exception>
    public static IReadOnlyList<AncestorProcess> Ancestors(int maxCount)
        => Walk(Snapshot(), Environment.ProcessId, maxCount, CreationTime, ExePath);

    /// <summary>
    /// psutil's walk over a process snapshot. The first step, <c>Process(os.getppid())</c>, raises
    /// <c>NoSuchProcess</c> when the parent has exited, and the reference doesn't catch it, so neither does this: the
    /// caller fails as the reference's startup does (issue #79). Each later step is psutil's <c>parent()</c>, which
    /// stops at PID 0, at a parent that has exited, and at a parent created after its child (the parent PID was reused).
    /// </summary>
    /// <param name="processes">The snapshot, by PID.</param>
    /// <param name="selfId">The PID whose ancestors to walk.</param>
    /// <param name="maxCount">The most ancestors to return.</param>
    /// <param name="creationTime">A PID's creation time as a FILETIME, or null when it can't be read (then the reuse check passes).</param>
    /// <param name="exePath">A PID's exe path, or null when it can't be read.</param>
    /// <exception cref="InvalidOperationException">The parent of <paramref name="selfId"/> isn't in the snapshot.</exception>
    public static IReadOnlyList<AncestorProcess> Walk(
        IReadOnlyDictionary<int, ProcessEntry> processes,
        int selfId,
        int maxCount,
        Func<int, long?> creationTime,
        Func<int, string?> exePath)
    {
        var parentId = processes.TryGetValue(selfId, out var self)
            ? self.ParentProcessId
            : throw new InvalidOperationException($"Process {selfId} isn't in the process snapshot.");
        if (!processes.TryGetValue(parentId, out var current))
        {
            throw new InvalidOperationException($"The parent process (pid={parentId}) has exited.");
        }

        var ancestors = new List<AncestorProcess>();
        while (ancestors.Count < maxCount)
        {
            ancestors.Add(new AncestorProcess(current.Name, exePath(current.ProcessId)));

            // psutil's parent(): None for the lowest PID, for a parent that's gone, and for a reused parent PID.
            if (current.ProcessId == 0 || !processes.TryGetValue(current.ParentProcessId, out var parent))
            {
                break;
            }

            if (creationTime(parent.ProcessId) is { } parentCreated
                && creationTime(current.ProcessId) is { } childCreated
                && parentCreated > childCreated)
            {
                break;
            }

            current = parent;
        }

        return ancestors;
    }

    /// <summary>Every running process, by PID, from one Toolhelp32 snapshot.</summary>
    private static Dictionary<int, ProcessEntry> Snapshot()
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new System.ComponentModel.Win32Exception();
        }

        try
        {
            var processes = new Dictionary<int, ProcessEntry>();
            var entry = new ProcessEntry32 { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessEntry32>() };
            for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
            {
                ReadOnlySpan<char> exeFile = MemoryMarshal.Cast<ushort, char>((ReadOnlySpan<ushort>)entry.ExeFile);
                var length = exeFile.IndexOf('\0');
                var name = new string(length < 0 ? exeFile : exeFile[..length]);
                processes[(int)entry.ProcessId] = new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, name);
            }

            return processes;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>A process's creation time, or null if it can't be opened (protected processes, or it has exited).</summary>
    private static long? CreationTime(int processId)
        => WithProcess(processId, handle => GetProcessTimes(handle, out var created, out _, out _, out _) ? created : (long?)null);

    /// <summary>A process's full exe path in Win32 form, or null if it can't be read.</summary>
    private static unsafe string? ExePath(int processId)
        => WithProcess(processId, handle =>
        {
            // 32K covers long paths; QueryFullProcessImageNameW fails rather than truncating.
            var buffer = new char[32768];
            var size = (uint)buffer.Length;
            fixed (char* chars = buffer)
            {
                return QueryFullProcessImageName(handle, 0, chars, ref size) ? new string(buffer, 0, (int)size) : null;
            }
        });

    /// <summary>Runs <paramref name="query"/> on a limited-query handle to the process, or returns null if it can't be opened.</summary>
    private static T? WithProcess<T>(int processId, Func<nint, T?> query)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, (uint)processId);
        if (handle == 0)
        {
            return default;
        }

        try
        {
            return query(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
