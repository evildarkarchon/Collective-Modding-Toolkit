using CMToolkit.Core.Win32;

namespace CMToolkit.Core;

/// <summary>
/// The machine the app runs on, as far as Core needs to know it: the facts Core reads from the OS rather than from
/// files it is pointed at. Parity tests swap in a fake driven by a Parity Scenario's <c>host</c> block, which also
/// drives the Python driver's monkeypatches, so both sides see the same fake machine (ADR-0004).
/// </summary>
/// <remarks>
/// Deliberately small: each slice adds the members it needs, such as the registry, known folders, the parent-process
/// chain, file-version resources and the OS build (the Core seams resolution on issue #7). There is no file-system
/// member; parity fixtures are real temp trees.
/// </remarks>
public interface IHostEnvironment
{
    /// <summary>
    /// The folder holding the exe, where <c>assets\</c> lives. The Reference Implementation's equivalent is
    /// <c>sys._MEIPASS</c> (<c>utils.get_asset_path</c>).
    /// </summary>
    string AppDirectory { get; }

    /// <summary>
    /// One registry value (<c>winreg.OpenKey</c> + <c>QueryValueEx</c>), with <c>REG_EXPAND_SZ</c> left unexpanded.
    /// Key paths and value names are case-insensitive. The 64-bit view is read; paths name <c>WOW6432Node</c>
    /// explicitly where the reference does.
    /// </summary>
    /// <param name="root">The hive.</param>
    /// <param name="keyPath">The key below the hive, such as <c>HARDWARE\DEVICEMAP\VIDEO</c>.</param>
    /// <param name="valueName">The value's name; empty for the key's default value.</param>
    /// <exception cref="FileNotFoundException">The key or the value doesn't exist (Python's <c>FileNotFoundError</c>).</exception>
    /// <exception cref="IOException">Another registry failure (Python's <c>OSError</c>).</exception>
    /// <exception cref="UnauthorizedAccessException">The key can't be opened for reading.</exception>
    RegistryData ReadRegistryValue(RegistryRoot root, string keyPath, string valueName);

    /// <summary>Whether <c>ntdll</c> exports <c>wine_get_version</c>, i.e. the app runs under Wine (PC-1).</summary>
    bool IsWine { get; }

    /// <summary>The OS name, Python release name and build (PC-2). Not read under Wine.</summary>
    OperatingSystemInfo OsInfo { get; }

    /// <summary>Total physical memory in bytes (PC-3; psutil's <c>virtual_memory().total</c>).</summary>
    long TotalPhysicalMemory { get; }

    /// <summary>
    /// The running process's ancestors, nearest first: the parent, its parent, and so on, at most
    /// <paramref name="maxCount"/> of them (MM-1). The walk stops early where psutil's <c>parent()</c> would return
    /// <see langword="None"/>: a parent that has exited, or a PID reused by a process younger than its child.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The direct parent has exited. psutil's <c>Process(os.getppid())</c> raises <c>NoSuchProcess</c> then, which the
    /// reference doesn't catch.
    /// </exception>
    IReadOnlyList<AncestorProcess> GetAncestorProcesses(int maxCount);

    /// <summary>
    /// The four parts of a file's version resource (<c>VS_FIXEDFILEINFO</c> file version; <c>utils.get_file_version</c>),
    /// or null when the file has no version resource or can't be read, where Python gets <see langword="None"/> (T-4).
    /// A resource that says <c>0.0.0.0</c> is a version, as it is to Python.
    /// </summary>
    IReadOnlyList<int>? GetFileVersion(string path);

    /// <summary>An environment variable (<c>os.getenv</c>), or null if it isn't set.</summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>
    /// The process's working directory (<c>Path.cwd()</c>): where the game path search looks second (GP-1), and what
    /// relative paths, such as an MO2 <c>gamePath</c> of <c>.</c>, resolve against.
    /// </summary>
    string CurrentDirectory { get; }

    /// <summary>
    /// A shell folder's path as <c>SHGetFolderPathW(None, csidl, None, 0, buf)</c> leaves it in the buffer: the current
    /// path, or an empty string when the lookup fails (the buffer stays zeroed). Callers check that it exists, as
    /// <c>utils.get_environment_path</c> does.
    /// </summary>
    string GetKnownFolderPath(KnownFolder folder);

    /// <summary>
    /// The program as the command line names it (<c>sys.argv[0]</c>): the No Mod Manager problem shows its file name
    /// (OVW-P1).
    /// </summary>
    string Argv0 { get; }
}

/// <summary>The real machine.</summary>
public sealed class SystemHostEnvironment : IHostEnvironment
{
    private SystemHostEnvironment()
    {
    }

    /// <summary>The single instance; the machine has no state of its own to hold.</summary>
    public static SystemHostEnvironment Instance { get; } = new();

    /// <inheritdoc/>
    public string AppDirectory => AppContext.BaseDirectory;

    /// <inheritdoc/>
    public RegistryData ReadRegistryValue(RegistryRoot root, string keyPath, string valueName)
        => WindowsHost.ReadRegistryValue(root, keyPath, valueName);

    /// <inheritdoc/>
    public bool IsWine => WindowsHost.IsWine();

    /// <inheritdoc/>
    public OperatingSystemInfo OsInfo => WindowsHost.OsInfo();

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="GC.GetGCMemoryInfo()"/> reports the machine's physical memory when no container or GC heap limit
    /// applies; issue #4's spike matched it to psutil's total to the byte.
    /// </remarks>
    public long TotalPhysicalMemory => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    /// <inheritdoc/>
    public IReadOnlyList<AncestorProcess> GetAncestorProcesses(int maxCount) => ProcessTree.Ancestors(maxCount);

    /// <inheritdoc/>
    public IReadOnlyList<int>? GetFileVersion(string path) => FileVersionResource.Read(path);

    /// <inheritdoc/>
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc/>
    public string CurrentDirectory => Environment.CurrentDirectory;

    /// <inheritdoc/>
    /// <remarks>
    /// Issue #4 mapped <c>SHGetFolderPathW</c> to <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>.
    /// Neither creates the folder. Both verify that it exists by default (no <c>CSIDL_FLAG_DONT_VERIFY</c> /
    /// <c>KF_FLAG_DONT_VERIFY</c>) and fail when it doesn't, which .NET reports as an empty string and the reference
    /// reads from its zeroed buffer, so the two agree there too.
    /// </remarks>
    public string GetKnownFolderPath(KnownFolder folder) => Environment.GetFolderPath(folder switch
    {
        KnownFolder.Documents => Environment.SpecialFolder.Personal,
        KnownFolder.LocalAppData => Environment.SpecialFolder.LocalApplicationData,
        _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, null),
    });

    /// <inheritdoc/>
    /// <remarks>
    /// The first command-line token as typed, like the frozen reference's <c>sys.argv[0]</c>, rather than
    /// <see cref="Environment.ProcessPath"/>, which would always add the folder and <c>.exe</c>.
    /// </remarks>
    public string Argv0 => Environment.GetCommandLineArgs()[0];
}
