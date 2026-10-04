using System.Text.Json;
using System.Text.Json.Serialization;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// A Parity Scenario's <c>scenario.json</c>: what to run, which Parity Inventory IDs it proves, the fake machine's host
/// description, and the tree extras git can't hold. The schema is documented in <c>parity/README.md</c>; the Python
/// driver reads the same file. Unknown keys are rejected, so a typo fails loudly instead of being ignored.
/// </summary>
/// <param name="Operation">
/// The Reference Implementation path the driver runs and the Core or App call the C# test makes, for example
/// <c>download-source</c>.
/// </param>
/// <param name="Parity">The Parity Inventory IDs this scenario proves once a C# test passes it.</param>
/// <param name="Host">The fake machine: everything Core reads from the OS rather than from the tree.</param>
/// <param name="Description">What the scenario sets up, for humans.</param>
/// <param name="EmptyDirs">Tree-relative folders to create empty (git can't commit an empty folder).</param>
/// <param name="Attributes">Tree-relative paths and the file attributes to set on them.</param>
public sealed record ScenarioManifest(
    string Operation,
    IReadOnlyList<string> Parity,
    HostBlock Host,
    string? Description = null,
    IReadOnlyList<string>? EmptyDirs = null,
    IReadOnlyList<AttributeEntry>? Attributes = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parses a <c>scenario.json</c>.</summary>
    /// <exception cref="JsonException">The JSON doesn't fit the schema.</exception>
    public static ScenarioManifest Parse(string json)
        => JsonSerializer.Deserialize<ScenarioManifest>(json, Options)
           ?? throw new JsonException("scenario.json is null.");
}

/// <summary>Attributes to set on one tree path, from the closed vocabulary <c>readonly</c>, <c>hidden</c>, <c>system</c>.</summary>
public sealed record AttributeEntry(string Path, IReadOnlyList<string> Set);

/// <summary>
/// The <c>host</c> block. It drives both the Python driver's monkeypatches and the C# <see cref="FakeHostEnvironment"/>.
/// Every string may start with <c>&lt;ROOT&gt;</c>, which expands to the scenario's temp root; the rest of the string
/// is kept as written. Members the C# side doesn't read yet are still parsed, so the schema stays validated on both
/// sides until the slice that needs them adds them to <see cref="CMToolkit.Core.IHostEnvironment"/>.
/// </summary>
/// <param name="AppDir">The folder holding the exe (C# <c>AppContext.BaseDirectory</c>, Python <c>sys._MEIPASS</c>).</param>
/// <param name="Cwd">The working directory the app starts in.</param>
/// <param name="Argv0">The exe name, <c>sys.argv[0]</c> (OVW-P1).</param>
/// <param name="Env">Environment variables that override the real ones.</param>
/// <param name="KnownFolders">
/// <c>Documents</c> and <c>LocalAppData</c> (CSIDL_PERSONAL and CSIDL_LOCAL_APPDATA, <c>utils.get_environment_path</c>).
/// </param>
/// <param name="Registry">
/// Keys such as <c>HKLM\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4</c> (hive <c>HKLM</c> or <c>HKCU</c>; matched
/// case-insensitively), each mapping value names to typed values. A key or value that isn't listed doesn't exist.
/// </param>
/// <param name="Processes">The parent-process chain, starting at the parent (MM-1).</param>
/// <param name="Os">The OS strings and build (PC-1, PC-2).</param>
/// <param name="Pc">Hardware facts that don't come from the registry (PC-3).</param>
/// <param name="Dialogs">Scripted answers, in call order, for the message and file dialogs that ask a question.</param>
public sealed record HostBlock(
    string? AppDir = null,
    string? Cwd = null,
    string? Argv0 = null,
    IReadOnlyDictionary<string, string>? Env = null,
    IReadOnlyDictionary<string, string>? KnownFolders = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, RegistryValue>>? Registry = null,
    IReadOnlyList<HostProcess>? Processes = null,
    HostOs? Os = null,
    HostPc? Pc = null,
    IReadOnlyList<DialogAnswer>? Dialogs = null);

/// <summary>A registry value: <c>REG_SZ</c>/<c>REG_EXPAND_SZ</c> (string) or <c>REG_DWORD</c>/<c>REG_QWORD</c> (number).</summary>
public sealed record RegistryValue(string Type, JsonElement Value);

/// <summary>One ancestor process: its image name (with <c>.exe</c>), its exe path, and its file version, if it has one.</summary>
public sealed record HostProcess(string Name, string? Exe = null, IReadOnlyList<int>? FileVersion = null);

/// <summary>
/// <c>platform.system()</c>, <c>platform.release()</c>, <c>sys.getwindowsversion().build</c>, and whether
/// <c>ntdll</c> exports <c>wine_get_version</c>.
/// </summary>
public sealed record HostOs(string System, string Release, int Build, bool Wine = false);

/// <summary>Total physical memory in bytes.</summary>
public sealed record HostPc(long RamBytes);

/// <summary>
/// The answer to one dialog that asks a question: <c>askyesno</c> (bool), <c>askopenfilename</c> (a path, or <c>""</c>
/// for Cancel), and so on. Dialogs that only inform (<c>showerror</c>, <c>showwarning</c>, <c>showinfo</c>) need no answer.
/// </summary>
public sealed record DialogAnswer(string Function, JsonElement Answer);
