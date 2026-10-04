using System.Runtime.InteropServices;

namespace CmtVfsProbe;

/// <summary>
/// Win32 calls the probe needs. Toolhelp32 is the dependency map's choice for the parent-PID lookup;
/// the rest are diagnostics that tell us *why* an outcome happened (elevation, usvfs presence, the
/// API CPython 3.12+ uses for stat on 24H2).
/// </summary>
internal static unsafe partial class Native
{
    private const uint Th32csSnapProcess = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private static readonly nint InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        // ushort rather than char keeps the struct blittable for LibraryImport.
        public fixed ushort szExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint snapshot, ProcessEntry32W* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint snapshot, ProcessEntry32W* entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, void* info, uint length, out uint returned);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string name);

    /// <summary>One row of the Toolhelp32 process snapshot.</summary>
    internal readonly record struct ProcessRow(uint Pid, uint ParentPid, string ExeFile);

    /// <summary>
    /// Takes a single Toolhelp32 snapshot of every process. <c>szExeFile</c> keeps the <c>.exe</c>
    /// suffix and needs no process handle, so it works across elevation boundaries.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The snapshot could not be taken.</exception>
    internal static Dictionary<uint, ProcessRow> SnapshotProcesses()
    {
        var rows = new Dictionary<uint, ProcessRow>();
        nint snap = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snap == InvalidHandle)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            ProcessEntry32W entry = default;
            entry.dwSize = (uint)sizeof(ProcessEntry32W);
            if (!Process32FirstW(snap, &entry))
                return rows;
            do
            {
                string exe = new((char*)entry.szExeFile);
                rows[entry.th32ProcessID] = new ProcessRow(entry.th32ProcessID, entry.th32ParentProcessID, exe);
            } while (Process32NextW(snap, &entry));
        }
        finally
        {
            CloseHandle(snap);
        }
        return rows;
    }

    /// <summary>
    /// Full image path via <c>QueryFullProcessImageNameW</c> with limited-query access, which (unlike
    /// <c>Process.MainModule</c>) is normally granted even for an elevated target.
    /// </summary>
    /// <returns>The path, or <c>"error &lt;code&gt;"</c> when the process can't be opened or queried.</returns>
    internal static string QueryImagePath(uint pid)
    {
        nint h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == 0)
            return $"error {Marshal.GetLastPInvokeError()}";
        try
        {
            char* buf = stackalloc char[32768];
            uint size = 32768;
            return QueryFullProcessImageName(h, 0, buf, ref size)
                ? new string(buf, 0, (int)size)
                : $"error {Marshal.GetLastPInvokeError()}";
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>Whether the process token is elevated.</summary>
    /// <returns><c>"true"</c>/<c>"false"</c>, or <c>"error &lt;code&gt;"</c> when the token can't be read.</returns>
    internal static string QueryElevated(uint pid)
    {
        nint h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == 0)
            return $"error {Marshal.GetLastPInvokeError()}";
        try
        {
            if (!OpenProcessToken(h, TokenQuery, out nint token))
                return $"error {Marshal.GetLastPInvokeError()}";
            try
            {
                uint elevation = 0;
                return GetTokenInformation(token, TokenElevationClass, &elevation, sizeof(uint), out _)
                    ? (elevation != 0 ? "true" : "false")
                    : $"error {Marshal.GetLastPInvokeError()}";
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>Whether a module with this name is loaded in the probe's own process (usvfs injection check).</summary>
    internal static bool IsModuleLoaded(string name) => GetModuleHandle(name) != 0;

    // FileStatBasicByNameInfo = 0. FILE_STAT_BASIC_INFORMATION puts FileAttributes after seven LARGE_INTEGERs.
    private const int FileStatBasicByNameInfo = 0;
    private const int FileAttributesOffset = 56;
    private static readonly delegate* unmanaged<char*, int, void*, uint, int> GetFileInformationByNamePtr = ResolveGetFileInformationByName();

    private static delegate* unmanaged<char*, int, void*, uint, int> ResolveGetFileInformationByName()
    {
        // CPython resolves it from the api-set; try that first, then the hosting DLLs. Absent before 24H2.
        foreach (string lib in new[] { "api-ms-win-core-file-l2-1-4.dll", "kernelbase.dll", "kernel32.dll" })
        {
            if (NativeLibrary.TryLoad(lib, out nint handle)
                && NativeLibrary.TryGetExport(handle, "GetFileInformationByName", out nint fn))
                return (delegate* unmanaged<char*, int, void*, uint, int>)fn;
        }
        return null;
    }

    /// <summary>
    /// Calls <c>GetFileInformationByName(FileStatBasicByNameInfo)</c>, the 24H2 API CPython 3.12+
    /// uses for <c>os.stat</c>. This is the "Python-style stat" contrast column, not something the port uses.
    /// </summary>
    /// <returns>
    /// <c>"attrs 0x…"</c> on success, <c>"error &lt;code&gt;"</c> on failure, or <c>"unavailable"</c>
    /// when the export doesn't exist on this Windows build.
    /// </returns>
    internal static string StatByName(string path)
    {
        if (GetFileInformationByNamePtr == null)
            return "unavailable";
        byte* buf = stackalloc byte[256];
        fixed (char* p = path)
        {
            Marshal.SetLastSystemError(0);
            int ok = GetFileInformationByNamePtr(p, FileStatBasicByNameInfo, buf, 256);
            // Function pointers don't capture last-error for us; read it straight away.
            int err = Marshal.GetLastSystemError();
            return ok != 0
                ? $"attrs 0x{*(uint*)(buf + FileAttributesOffset):X}"
                : $"error {err}";
        }
    }
}
