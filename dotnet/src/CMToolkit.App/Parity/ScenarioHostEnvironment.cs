using System.Text.Json;
using CMToolkit.Core;
using CMToolkit.Core.Win32;

namespace CMToolkit.App.Parity;

/// <summary>
/// A Parity Scenario's fake machine for the published exe, so a screenshot pair compares the reference and the port on
/// the same host (parity/README.md, "Screenshots"). The screenshot driver sets <see cref="ScenarioVariable"/> and
/// <see cref="RootVariable"/>; without them the app runs on <see cref="SystemHostEnvironment"/>, as it always does for
/// users.
/// </summary>
/// <remarks>
/// It reads the same <c>host</c> block as the tests' <c>FakeHostEnvironment</c> and the driver's fakes, with the same
/// rules (a leading <c>&lt;ROOT&gt;</c> expands to the materialized tree; registry keys and value names match
/// case-insensitively; only listed environment variables exist), and a test checks the two agree on every committed
/// scenario. Two differences: with no <c>appDir</c> the exe's own folder is used, as the driver uses the reference's own
/// assets; and <c>dialogs</c> are not scripted, since the screenshot runs show real windows.
/// <para>
/// The JSON is read with <see cref="JsonDocument"/>, which needs no reflection, so this stays trim- and AOT-safe.
/// </para>
/// </remarks>
public sealed class ScenarioHostEnvironment : IHostEnvironment
{
    /// <summary>The environment variable naming a scenario's <c>scenario.json</c>.</summary>
    public const string ScenarioVariable = "CMT_PARITY_SCENARIO";

    /// <summary>The environment variable naming the materialized tree that <c>&lt;ROOT&gt;</c> stands for.</summary>
    public const string RootVariable = "CMT_PARITY_ROOT";

    private const string RootToken = "<ROOT>";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip };

    private readonly JsonElement _host;

    private ScenarioHostEnvironment(JsonElement host, string root)
    {
        _host = host;
        Root = root;
    }

    /// <summary>The materialized tree's root.</summary>
    public string Root { get; }

    /// <summary>
    /// The host named by <see cref="ScenarioVariable"/> and <see cref="RootVariable"/>, or null when they aren't both set.
    /// </summary>
    /// <exception cref="IOException">The scenario file can't be read.</exception>
    /// <exception cref="JsonException">The scenario file isn't a JSON object with a <c>host</c> object.</exception>
    public static ScenarioHostEnvironment? FromEnvironmentVariables()
    {
        var scenario = Environment.GetEnvironmentVariable(ScenarioVariable);
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrEmpty(scenario) || string.IsNullOrEmpty(root))
        {
            return null;
        }

        return Parse(File.ReadAllText(scenario), root);
    }

    /// <summary>The host block of a <c>scenario.json</c>'s text, rooted at <paramref name="root"/>.</summary>
    /// <exception cref="JsonException">The text isn't a JSON object with a <c>host</c> object.</exception>
    public static ScenarioHostEnvironment Parse(string scenarioJson, string root)
    {
        using var document = JsonDocument.Parse(scenarioJson, Options);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("host", out var host)
            || host.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("scenario.json has no host object.");
        }

        // Cloned so the element outlives the document.
        return new ScenarioHostEnvironment(host.Clone(), root);
    }

    /// <summary>Expands a leading <c>&lt;ROOT&gt;</c>; the rest of the string is kept as written.</summary>
    public string Expand(string value)
        => value.StartsWith(RootToken, StringComparison.Ordinal) ? Root + value[RootToken.Length..] : value;

    /// <inheritdoc/>
    public string AppDirectory => String("appDir") is { } appDir ? Expand(appDir) : AppContext.BaseDirectory;

    /// <inheritdoc/>
    public RegistryData ReadRegistryValue(RegistryRoot root, string keyPath, string valueName)
    {
        var wanted = keyPath.ToUpperInvariant();
        var keyExists = false;
        if (_host.TryGetProperty("registry", out var registry))
        {
            foreach (var key in registry.EnumerateObject())
            {
                var separator = key.Name.IndexOf('\\', StringComparison.Ordinal);
                var hive = separator < 0 ? key.Name : key.Name[..separator];
                var listedRoot = hive.ToUpperInvariant() switch
                {
                    "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryRoot.LocalMachine,
                    "HKCU" or "HKEY_CURRENT_USER" => RegistryRoot.CurrentUser,
                    _ => throw new InvalidOperationException($"Unknown registry hive in '{key.Name}'; use HKLM or HKCU."),
                };
                if (listedRoot != root)
                {
                    continue;
                }

                var listed = (separator < 0 ? string.Empty : key.Name[(separator + 1)..]).ToUpperInvariant();
                if (listed == wanted)
                {
                    keyExists = true;
                    foreach (var value in key.Value.EnumerateObject())
                    {
                        if (string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase))
                        {
                            return ToRegistryData(value.Value);
                        }
                    }
                }
                else if (listed.StartsWith(wanted + "\\", StringComparison.Ordinal))
                {
                    keyExists = true;
                }
            }
        }

        throw new FileNotFoundException(keyExists
            ? $@"The scenario's registry key {root}\{keyPath} has no value '{valueName}'."
            : $@"The scenario's registry has no key {root}\{keyPath}.");
    }

    /// <summary>A host-block registry value (<c>{"type": ..., "value": ...}</c>), with <c>&lt;ROOT&gt;</c> expanded in strings.</summary>
    private RegistryData ToRegistryData(JsonElement value)
    {
        var data = value.GetProperty("value");
        return value.GetProperty("type").GetString() switch
        {
            "REG_SZ" => new RegistryData(RegistryDataType.String, Expand(data.GetString()!)),
            "REG_EXPAND_SZ" => new RegistryData(RegistryDataType.ExpandString, Expand(data.GetString()!)),
            "REG_DWORD" => new RegistryData(RegistryDataType.DWord, data.GetInt64()),
            "REG_QWORD" => new RegistryData(RegistryDataType.QWord, data.GetInt64()),
            var type => throw new InvalidOperationException($"Unknown registry value type '{type}'."),
        };
    }

    /// <inheritdoc/>
    public bool IsWine => _host.TryGetProperty("os", out var os) && os.TryGetProperty("wine", out var wine) && wine.GetBoolean();

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The host block has no <c>os</c>.</exception>
    public OperatingSystemInfo OsInfo
        => _host.TryGetProperty("os", out var os)
            ? new OperatingSystemInfo(os.GetProperty("system").GetString()!, os.GetProperty("release").GetString()!, os.GetProperty("build").GetInt32())
            : throw new InvalidOperationException("The scenario's host block has no os.");

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The host block has no <c>pc</c>.</exception>
    public long TotalPhysicalMemory
        => _host.TryGetProperty("pc", out var pc)
            ? pc.GetProperty("ramBytes").GetInt64()
            : throw new InvalidOperationException("The scenario's host block has no pc.");

    /// <inheritdoc/>
    /// <remarks>The host block's chain, from the parent up. An omitted <c>exe</c> is one that can't be read.</remarks>
    public IReadOnlyList<AncestorProcess> GetAncestorProcesses(int maxCount)
        => [.. Processes().Take(maxCount).Select(p => new AncestorProcess(p.Name, p.Exe))];

    /// <inheritdoc/>
    /// <remarks>The <c>fileVersion</c> of a host-block process whose exe is <paramref name="path"/>, else the real file's.</remarks>
    public IReadOnlyList<int>? GetFileVersion(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var process in Processes())
        {
            if (process is { Exe: { } exe, FileVersion: { } version }
                && string.Equals(Path.GetFullPath(exe), full, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }

        return FileVersionResource.Read(path);
    }

    /// <inheritdoc/>
    /// <remarks>Only the host block's <c>env</c> exists, matched case-insensitively.</remarks>
    public string? GetEnvironmentVariable(string name)
    {
        if (_host.TryGetProperty("env", out var env))
        {
            foreach (var variable in env.EnumerateObject())
            {
                if (string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Expand(variable.Value.GetString()!);
                }
            }
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>The host block's <c>cwd</c>, else the root. The screenshot driver also starts the exe there.</remarks>
    public string CurrentDirectory => String("cwd") is { } cwd ? Expand(cwd) : Root;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The host block doesn't list <paramref name="folder"/>.</exception>
    public string GetKnownFolderPath(KnownFolder folder)
        => _host.TryGetProperty("knownFolders", out var folders) && folders.TryGetProperty(folder.ToString(), out var path)
            ? Expand(path.GetString()!)
            : throw new InvalidOperationException($"The scenario's host block has no knownFolders.{folder}.");

    /// <summary>A top-level string member of the host block, or null.</summary>
    private string? String(string name) => _host.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>The host block's <c>processes</c>, with exe paths expanded.</summary>
    private IEnumerable<(string Name, string? Exe, IReadOnlyList<int>? FileVersion)> Processes()
    {
        if (!_host.TryGetProperty("processes", out var processes))
        {
            yield break;
        }

        foreach (var process in processes.EnumerateArray())
        {
            var exe = process.TryGetProperty("exe", out var e) && e.ValueKind == JsonValueKind.String ? Expand(e.GetString()!) : null;
            IReadOnlyList<int>? version = process.TryGetProperty("fileVersion", out var v) && v.ValueKind == JsonValueKind.Array
                ? [.. v.EnumerateArray().Select(n => n.GetInt32())]
                : null;
            yield return (process.GetProperty("name").GetString()!, exe, version);
        }
    }
}
