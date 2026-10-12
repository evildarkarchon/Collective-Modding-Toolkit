using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CMToolkit.Core.Win32;

/// <summary>
/// The Win32 calls Core makes directly, all through <see cref="LibraryImportAttribute"/> so NativeAOT generates the
/// marshalling (ADR-0002). Issue #4 found the parent-process lookup to be the one P/Invoke the BCL can't replace; the
/// rest stand in for facts the BCL reports differently from Python (the OS product type, a process's exe across
/// elevation, its creation time).
/// </summary>
internal static partial class NativeMethods
{
    /// <summary><c>TH32CS_SNAPPROCESS</c>.</summary>
    public const uint Th32csSnapProcess = 0x00000002;

    /// <summary><c>PROCESS_QUERY_LIMITED_INFORMATION</c>: granted for elevated processes too, unlike <c>PROCESS_QUERY_INFORMATION</c>.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary><c>VER_NT_WORKSTATION</c>, the <c>wProductType</c> of a client Windows.</summary>
    public const byte VerNtWorkstation = 1;

    /// <summary><c>INVALID_HANDLE_VALUE</c>.</summary>
    public static readonly nint InvalidHandleValue = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageName(nint process, uint flags, char* exeName, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(nint process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    /// <summary>The true OS version and product type, whatever the exe's compatibility manifest says.</summary>
    [LibraryImport("ntdll.dll")]
    public static partial int RtlGetVersion(ref OsVersionInfoEx versionInfo);

    /// <summary><c>PROCESSENTRY32W</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public MaxPathChars ExeFile;
    }

    /// <summary><c>OSVERSIONINFOEXW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OsVersionInfoEx
    {
        public uint OsVersionInfoSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;
        public CsdChars CsdVersion;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

    /// <summary>
    /// A <c>WCHAR[MAX_PATH]</c> field. The elements are <see cref="ushort"/>, not <see cref="char"/>, because runtime
    /// marshalling doesn't count <see cref="char"/> as blittable, and the struct has to be copied as is.
    /// </summary>
    [InlineArray(260)]
    public struct MaxPathChars
    {
        private ushort _first;
    }

    /// <summary><c>szCSDVersion</c>'s <c>WCHAR[128]</c>, as <see cref="ushort"/> for the same reason as <see cref="MaxPathChars"/>.</summary>
    [InlineArray(128)]
    public struct CsdChars
    {
        private ushort _first;
    }
}
