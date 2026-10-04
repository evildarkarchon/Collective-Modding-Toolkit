using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace CmtVfsProbe;

/// <summary>
/// Throwaway probe for #35. Run it once from MO2 and once outside MO2 (double-click). Each run writes
/// <c>report-&lt;label&gt;-&lt;time&gt;.json</c> next to the exe. The first run also writes <c>targets.json</c>,
/// so every later run probes exactly the same paths. A path is VFS-only when the MO2 run sees it and
/// the plain run doesn't.
/// </summary>
internal static class Program
{
    private static readonly string[] IniNames = ["Fallout4.ini", "Fallout4Prefs.ini", "Fallout4Custom.ini"];
    private static readonly string[] ModuleExts = [".esp", ".esm", ".esl", ".ba2"];

    // Installer metadata folders MO2 never maps into Data. Everything else is filtered against the real
    // Data folder when targets are built outside the VFS.
    private static readonly HashSet<string> NonDataDirs = new(StringComparer.OrdinalIgnoreCase) { "fomod" };

#if PROBE_ADMIN
    private const string Variant = "adminexe";
#else
    private const string Variant = "asinvoker";
#endif

    /// <summary>Entry point. Never throws: a fatal error is recorded in the report and printed.</summary>
    private static int Main(string[] args)
    {
        bool noWait = args.Contains("--no-wait");
        string? iniOverride = ArgValue(args, "--mo2-ini");
        string? extraLabel = ArgValue(args, "--label");
        string outDir = AppContext.BaseDirectory;
        var report = new JsonObject();
        string label = "unknown";
        int exit = 0;

        try
        {
            label = Run(report, outDir, iniOverride, extraLabel);
        }
        catch (Exception ex)
        {
            report["fatal"] = ex.ToString();
            Console.WriteLine($"FATAL: {ex}");
            exit = 1;
        }

        string file = Path.Combine(outDir, $"report-{label}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try
        {
            using var fs = File.Create(file);
            using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
            report.WriteTo(w);
            Console.WriteLine();
            Console.WriteLine($"Report written: {file}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write report {file}: {ex.Message}");
            exit = 1;
        }

        if (!noWait)
        {
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return exit;
    }

    /// <summary>Runs every probe section into <paramref name="report"/>.</summary>
    /// <returns>The run label used in the report file name.</returns>
    private static string Run(JsonObject report, string outDir, string? iniOverride, string? extraLabel)
    {
        bool elevated = Environment.IsPrivilegedProcess;
        bool usvfs = Native.IsModuleLoaded("usvfs_x64.dll");
        report["environment"] = new JsonObject
        {
            ["variant"] = Variant,
            ["osVersion"] = Environment.OSVersion.Version.ToString(),
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["nativeAot"] = !RuntimeFeature.IsDynamicCodeSupported,
            ["processPath"] = Environment.ProcessPath,
            ["pid"] = Environment.ProcessId,
            ["elevated"] = elevated,
            ["usvfsLoaded"] = usvfs,
            ["documents"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["localAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["startedLocal"] = DateTime.Now.ToString("O"),
        };
        Console.WriteLine($"cmt-vfs-probe ({Variant}) on Windows {Environment.OSVersion.Version}, elevated={elevated}, usvfs loaded={usvfs}");

        string? mo2Exe = ProbeParentChain(report);
        // Label from the MM-1 hit itself, so a failed image-path query still files the run as an MO2 run.
        bool underMo2 = ((string?)report["mm1Result"])?.StartsWith("ModOrganizer.exe") == true;
        string label = (underMo2 ? "mo2" : "plain") + (elevated ? "-elevated" : "") + (Variant == "adminexe" ? "-adminexe" : "");
        if (!string.IsNullOrWhiteSpace(extraLabel))
            label += "-" + extraLabel;
        report["label"] = label;

        Mo2Config? cfg = LoadMo2Config(report, mo2Exe, iniOverride);
        JsonArray targets = LoadOrBuildTargets(report, outDir, cfg);
        ProbeTargets(report, targets);
        if (cfg != null)
        {
            ProbeIniAndPlugins(report, cfg);
            ProbeDataSummary(report, cfg.GamePath);
        }
        return label;
    }

    // ---------------------------------------------------------------- parent chain (MM-1, T-5)

    /// <summary>
    /// Walks up to 8 ancestors from the parent, as MM-1 does. Records several ways of reading each
    /// ancestor's identity so the port can pick the one that survives elevation.
    /// </summary>
    /// <returns>The MO2 exe path when an ancestor is <c>ModOrganizer.exe</c>, else null.</returns>
    private static string? ProbeParentChain(JsonObject report)
    {
        var chain = new JsonArray();
        report["parentChain"] = chain;
        Dictionary<uint, Native.ProcessRow> rows = Native.SnapshotProcesses();
        uint self = (uint)Environment.ProcessId;
        uint pid = rows.TryGetValue(self, out var me) ? me.ParentPid : 0;
        string? mo2Exe = null;
        string? mm1Hit = null;

        for (int i = 0; i < 8 && pid != 0; i++)
        {
            var node = new JsonObject { ["depth"] = i + 1, ["pid"] = (long)pid };
            chain.Add((JsonNode)node);
            if (!rows.TryGetValue(pid, out var row))
            {
                node["toolhelpExeFile"] = "(not in snapshot: exited or PID gone)";
                break;
            }
            node["toolhelpExeFile"] = row.ExeFile;
            node["parentPid"] = (long)row.ParentPid;
            node["Process.ProcessName"] = Try(() => Process.GetProcessById((int)pid).ProcessName);
            node["Process.MainModule.FileName"] = Try(() => Process.GetProcessById((int)pid).MainModule?.FileName ?? "(null)");
            node["Process.StartTime"] = Try(() => Process.GetProcessById((int)pid).StartTime.ToString("O"));
            string image = Native.QueryImagePath(pid);
            node["QueryFullProcessImageName"] = image;
            node["elevated"] = Native.QueryElevated(pid);
            if (!image.StartsWith("error"))
            {
                node["fileVersion"] = Try(() =>
                {
                    var v = FileVersionInfo.GetVersionInfo(image);
                    return $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}";
                });
            }

            if (mm1Hit == null && (row.ExeFile == "ModOrganizer.exe" || row.ExeFile == "Vortex.exe"))
            {
                mm1Hit = $"{row.ExeFile} at depth {i + 1}";
                if (row.ExeFile == "ModOrganizer.exe" && !image.StartsWith("error"))
                    mo2Exe = image;
            }
            pid = row.ParentPid;
        }

        report["mm1Result"] = mm1Hit ?? "no mod manager in the first 8 ancestors";
        Console.WriteLine($"MM-1 parent walk: {report["mm1Result"]}");
        return mo2Exe;
    }

    // ---------------------------------------------------------------- MO2 config (MO2-1..6, done right)

    private sealed record Mo2Config(string IniPath, string GamePath, string Profile, string ModsDir, string ProfilesDir);

    /// <summary>
    /// Finds and reads ModOrganizer.ini the way MO2-1..3 do (portable.txt, then the CurrentInstance
    /// registry value). Paths are resolved correctly rather than with the reference's B-6 bug, since
    /// the probe only needs them to pick targets.
    /// </summary>
    private static Mo2Config? LoadMo2Config(JsonObject report, string? mo2Exe, string? iniOverride)
    {
        var node = new JsonObject();
        report["mo2Config"] = node;
        string? ini = iniOverride;
        if (ini == null && mo2Exe != null)
        {
            string dir = Path.GetDirectoryName(mo2Exe)!;
            if (File.Exists(Path.Combine(dir, "portable.txt")))
                ini = Path.Combine(dir, "ModOrganizer.ini");
        }
        if (ini == null)
        {
            string? instance = Registry.CurrentUser.OpenSubKey(@"Software\Mod Organizer Team\Mod Organizer")?.GetValue("CurrentInstance") as string;
            node["currentInstance"] = instance;
            if (!string.IsNullOrEmpty(instance))
                ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModOrganizer", instance, "ModOrganizer.ini");
        }
        node["iniPath"] = ini;
        if (ini == null || !File.Exists(ini))
        {
            node["error"] = "ModOrganizer.ini not found; pass --mo2-ini <path>. Only fixed targets will be probed.";
            Console.WriteLine(node["error"]);
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string section = "";
        foreach (string raw in File.ReadAllLines(ini))
        {
            if (raw.StartsWith('['))
            {
                section = raw.Trim('[', ']');
                continue;
            }
            int eq = raw.IndexOf('=');
            if (eq < 0 || (section != "General" && section != "Settings"))
                continue;
            string v = raw[(eq + 1)..];
            if (v.StartsWith("@ByteArray(") && v.EndsWith(')'))
                v = v["@ByteArray(".Length..^1];
            values[raw[..eq]] = v.Replace(@"\\", @"\");
        }

        string baseDir = values.GetValueOrDefault("base_directory") ?? Path.GetDirectoryName(ini)!;
        string Dir(string key, string def)
        {
            string v = values.GetValueOrDefault(key) ?? $"%BASE_DIR%/{def}";
            return v.Contains("%BASE_DIR%")
                ? Path.GetFullPath(Path.Combine(baseDir, v.Replace("%BASE_DIR%", "").TrimStart('/', '\\')))
                : Path.GetFullPath(v);
        }

        var cfg = new Mo2Config(
            ini,
            values.GetValueOrDefault("gamePath") ?? "",
            values.GetValueOrDefault("selected_profile") ?? "",
            Dir("mod_directory", "mods"),
            Dir("profiles_directory", "profiles"));
        node["gamePath"] = cfg.GamePath;
        node["selectedProfile"] = cfg.Profile;
        node["modsDir"] = cfg.ModsDir;
        node["profilesDir"] = cfg.ProfilesDir;
        node["iniVersion"] = values.GetValueOrDefault("version");
        return cfg;
    }

    // ---------------------------------------------------------------- targets

    /// <summary>
    /// Loads <c>targets.json</c> from the exe folder, or builds and saves it. Candidates come from the
    /// enabled mods in the selected profile's modlist.txt. Whether each is really VFS-only is decided
    /// afterwards by comparing the MO2 run against the plain run.
    /// </summary>
    private static JsonArray LoadOrBuildTargets(JsonObject report, string outDir, Mo2Config? cfg)
    {
        string file = Path.Combine(outDir, "targets.json");
        if (File.Exists(file))
        {
            report["targetsSource"] = "loaded " + file;
            return (JsonArray)JsonNode.Parse(File.ReadAllText(file))!;
        }

        var targets = new JsonArray();
        void Add(string kind, string path, string? source = null) =>
            targets.Add((JsonNode)new JsonObject { ["kind"] = kind, ["path"] = path, ["source"] = source });

        if (cfg == null || cfg.GamePath.Length == 0)
        {
            report["targetsSource"] = "no MO2 config: nothing to probe";
            return targets;
        }

        string data = Path.Combine(cfg.GamePath, "Data");
        Add("real-file", Path.Combine(cfg.GamePath, "Fallout4.exe"));
        Add("real-file", Path.Combine(data, "Fallout4.esm"));
        Add("real-dir", data);
        Add("missing-file", Path.Combine(data, "__cmt_vfs_probe_missing__.esp"));
        Add("missing-dir", Path.Combine(data, "__cmt_vfs_probe_missing_dir__"));
        Add("missing-file-in-missing-dir", Path.Combine(data, "__cmt_vfs_probe_missing_dir__", "x.esp"));

        string modlist = Path.Combine(cfg.ProfilesDir, cfg.Profile, "modlist.txt");
        var mods = File.Exists(modlist)
            ? File.ReadAllLines(modlist)
                .Where(l => l.StartsWith('+') && !l.EndsWith("_separator", StringComparison.Ordinal))
                .Select(l => l[1..])
                .ToList()
            : [];
        // Outside the VFS, File/Directory.Exists see the real Data folder, so candidates already present
        // there are dropped. Under MO2 that check would be meaningless, which is why targets.json should
        // come from a plain run.
        bool canFilter = !Native.IsModuleLoaded("usvfs_x64.dll");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Fresh(string dataPath) =>
            seen.Add(dataPath) && (!canFilter || (!File.Exists(dataPath) && !Directory.Exists(dataPath)));

        int files = 0, dirs = 0, nested = 0, nestedDirs = 0;
        foreach (string mod in mods)
        {
            string modDir = Path.Combine(cfg.ModsDir, mod);
            if (!Directory.Exists(modDir))
                continue;
            if (files < 6)
            {
                string? f = Directory.EnumerateFiles(modDir)
                    .Where(p => ModuleExts.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                    .Select(p => Path.Combine(data, Path.GetFileName(p)))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(Fresh);
                if (f != null)
                {
                    files++;
                    Add("candidate-vfs-file", f, mod);
                }
            }
            if (dirs < 6)
            {
                string? d = Directory.EnumerateDirectories(modDir)
                    .Where(p => !NonDataDirs.Contains(Path.GetFileName(p)))
                    .Select(p => Path.Combine(data, Path.GetFileName(p)))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(Fresh);
                if (d != null)
                {
                    dirs++;
                    Add("candidate-vfs-dir", d, mod);
                }
            }
            string plugins = Path.Combine(modDir, "F4SE", "Plugins");
            if (nested < 4 && Directory.Exists(plugins))
            {
                string? dll = Directory.EnumerateFiles(plugins, "*.dll")
                    .Select(p => Path.Combine(data, "F4SE", "Plugins", Path.GetFileName(p)))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(Fresh);
                if (dll != null)
                {
                    nested++;
                    Add("candidate-vfs-nested-file", dll, mod);
                }
            }
            if (nestedDirs < 4)
            {
                // Depth 2, e.g. Data\meshes\<ModFolder>: a VFS-only folder inside a real one.
                string? nd = Directory.EnumerateDirectories(modDir)
                    .Where(p => !NonDataDirs.Contains(Path.GetFileName(p)))
                    .SelectMany(p => Directory.EnumerateDirectories(p)
                        .Select(c => Path.Combine(data, Path.GetFileName(p), Path.GetFileName(c))))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(Fresh);
                if (nd != null)
                {
                    nestedDirs++;
                    Add("candidate-vfs-nested-dir", nd, mod);
                }
            }
            if (files >= 6 && dirs >= 6 && nested >= 4 && nestedDirs >= 4)
                break;
        }

        using (var fs = File.Create(file))
        using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
            targets.WriteTo(w);
        report["targetsSource"] = $"built from {modlist} ({mods.Count} enabled mods), filteredAgainstRealData={canFilter}, saved to {file}";
        return targets;
    }

    // ---------------------------------------------------------------- per-target probes

    private enum Cls { Ok, NotFound, Permission, NotADirectory, Other }

    private const int HResultErrorDirectory = unchecked((int)0x8007010B);

    // .NET reports "enumerate a file" as ERROR_INVALID_PARAMETER, where Python's FindFirstFileW-based
    // iterdir gets ERROR_DIRECTORY (NotADirectoryError). Both mean "not a directory" for the emulation.
    private const int HResultInvalidParameter = unchecked((int)0x80070057);

    /// <summary>Maps an exception to the Python <c>OSError</c> subclass the reference would have caught.</summary>
    private static Cls Classify(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => Cls.NotFound,
        UnauthorizedAccessException => Cls.Permission,
        IOException io when io.HResult is HResultErrorDirectory or HResultInvalidParameter => Cls.NotADirectory,
        _ => Cls.Other,
    };

    private static (string Text, Cls Cls) Outcome(Func<string> op)
    {
        try
        {
            return ($"ok: {op()}", Cls.Ok);
        }
        catch (Exception ex)
        {
            return ($"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}", Classify(ex));
        }
    }

    /// <summary>
    /// Probes each target with plain .NET APIs, the 24H2 stat API, and an emulation of the
    /// reference's open/iterdir probes (<c>utils.py:90-146</c>). Records whether .NET agrees with
    /// the reference probe, which is the parity question.
    /// </summary>
    private static void ProbeTargets(JsonObject report, JsonArray targets)
    {
        var results = new JsonArray();
        report["targets"] = results;
        var listings = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        Console.WriteLine();
        Console.WriteLine("kind                          F.Exists D.Exists  ref(is_file,is_dir,exists)  agree  inParent  path");

        foreach (JsonNode? t in targets)
        {
            string kind = (string)t!["kind"]!;
            string path = (string)t["path"]!;
            var r = new JsonObject { ["kind"] = kind, ["path"] = path, ["source"] = (string?)t["source"] };
            results.Add((JsonNode)r);

            bool fileExists = File.Exists(path);
            bool dirExists = Directory.Exists(path);
            r["File.Exists"] = fileExists;
            r["Directory.Exists"] = dirExists;
            r["FileInfo.Exists"] = new FileInfo(path).Exists;
            r["DirectoryInfo.Exists"] = new DirectoryInfo(path).Exists;
            r["File.GetAttributes"] = Try(() => File.GetAttributes(path).ToString());
            r["GetFileInformationByName"] = Native.StatByName(path);

            var open = Outcome(() =>
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Span<byte> head = stackalloc byte[4];
                int n = fs.Read(head);
                return $"{n} bytes read, length {fs.Length}";
            });
            var enumerate = Outcome(() =>
            {
                using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                return e.MoveNext() ? "non-empty" : "empty";
            });
            r["open"] = open.Text;
            r["enumerate"] = enumerate.Text;

            string parent = Path.GetDirectoryName(path)!;
            if (!listings.TryGetValue(parent, out var names))
            {
                try
                {
                    names = new HashSet<string>(Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    // An unlistable parent (missing dir targets) is reported as null rather than aborting the run.
                    names = null;
                }
                listings[parent] = names;
            }
            bool? inParent = names?.Contains(Path.GetFileName(path));
            r["listedInParent"] = inParent;

            string isFile = RefIsFile(open.Cls, enumerate.Cls);
            string isDir = RefIsDir(enumerate.Cls);
            string exists = RefExists(open.Cls, enumerate.Cls);
            r["ref.is_file"] = isFile;
            r["ref.is_dir"] = isDir;
            r["ref.exists"] = exists;
            bool agree = isFile == (fileExists ? "true" : "false")
                && isDir == (dirExists ? "true" : "false")
                && exists == (fileExists || dirExists ? "true" : "false");
            r["dotnetAgreesWithRefProbe"] = agree;

            Console.WriteLine($"{kind,-29} {fileExists,-8} {dirExists,-8}  {isFile + "," + isDir + "," + exists,-26}  {agree,-5}  {inParent?.ToString() ?? "n/a",-8}  {path}");
        }
    }

    // Emulations of utils.is_file / is_dir / exists on the 24H2 branch. "raises" = an OSError escapes.
    private static string RefIsFile(Cls open, Cls enumerate) => open switch
    {
        Cls.Ok => "true",
        Cls.NotFound => "false",
        Cls.Permission => enumerate == Cls.NotADirectory ? "true" : "false",
        _ => "raises",
    };

    private static string RefIsDir(Cls enumerate) => enumerate switch
    {
        Cls.NotADirectory or Cls.NotFound => "false",
        Cls.Ok => "true",
        _ => "raises",
    };

    private static string RefExists(Cls open, Cls enumerate) => open switch
    {
        Cls.Ok => "true",
        Cls.NotFound => "false",
        Cls.Permission => enumerate is Cls.Ok or Cls.Permission or Cls.NotADirectory ? "true" : "false",
        _ => "raises",
    };

    // ---------------------------------------------------------------- INIs and plugins.txt (INI-2, OVW-M4)

    /// <summary>
    /// Reads the game INIs and plugins.txt at the paths the reference uses, and reports which MO2
    /// profiles hold a byte-identical copy. Under MO2 a match with a profile copy (and a different hash
    /// in the plain run) means the VFS served the profile's file.
    /// </summary>
    private static void ProbeIniAndPlugins(JsonObject report, Mo2Config cfg)
    {
        var node = new JsonObject();
        report["profileFiles"] = node;

        var profiles = new JsonArray();
        node["profiles"] = profiles;
        string[] profileDirs = Directory.Exists(cfg.ProfilesDir) ? Directory.GetDirectories(cfg.ProfilesDir) : [];
        foreach (string p in profileDirs)
        {
            string settings = Path.Combine(p, "settings.ini");
            bool local = File.Exists(settings) && File.ReadAllLines(settings).Any(l => l.Trim().Equals("LocalSettings=true", StringComparison.OrdinalIgnoreCase));
            profiles.Add((JsonNode)new JsonObject { ["name"] = Path.GetFileName(p), ["localSettings"] = local });
        }

        string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Fallout4");
        foreach (string name in IniNames)
            node[name] = FileFacts(Path.Combine(docs, name), name, profileDirs);

        string pluginsTxt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fallout4", "plugins.txt");
        JsonObject plugins = FileFacts(pluginsTxt, "plugins.txt", profileDirs);
        plugins["starLines"] = Try(() =>
        {
            // Strict UTF-8, as OVW-M4 reads it.
            string text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(pluginsTxt));
            return text.Split('\n').Count(l => l.StartsWith('*')).ToString();
        });
        node["plugins.txt"] = plugins;

        Console.WriteLine();
        foreach (var (key, value) in node)
        {
            if (key != "profiles")
                Console.WriteLine($"{key,-20} exists={value!["File.Exists"]} sha={value["sha256"]} matchesProfiles=[{string.Join(",", value["matchesProfiles"]!.AsArray().Select(x => (string?)x))}]");
        }
    }

    private static JsonObject FileFacts(string path, string profileFileName, string[] profileDirs)
    {
        var o = new JsonObject { ["path"] = path, ["File.Exists"] = File.Exists(path) };
        string? sha = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            sha = Sha(bytes);
            o["length"] = bytes.Length;
            o["sha256"] = sha;
        }
        catch (Exception ex)
        {
            o["readError"] = $"{ex.GetType().Name}: {ex.Message}";
        }

        var matches = new JsonArray();
        var copies = new JsonObject();
        foreach (string p in profileDirs)
        {
            string copy = Path.Combine(p, profileFileName);
            if (!File.Exists(copy))
                continue;
            string copySha = Sha(File.ReadAllBytes(copy));
            copies[Path.GetFileName(p)] = copySha;
            if (copySha == sha)
                matches.Add((JsonNode)Path.GetFileName(p));
        }
        o["matchesProfiles"] = matches;
        o["profileCopies"] = copies;
        return o;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];

    // ---------------------------------------------------------------- Data folder summary

    /// <summary>
    /// Counts what enumeration of Data sees: the top level, module/archive patterns, F4SE plugins,
    /// and a full recursive walk with timing (the Scanner walks Data recursively).
    /// </summary>
    private static void ProbeDataSummary(JsonObject report, string gamePath)
    {
        string data = Path.Combine(gamePath, "Data");
        var node = new JsonObject();
        report["dataSummary"] = node;
        node["topLevelEntries"] = Try(() => Directory.EnumerateFileSystemEntries(data).Count().ToString());
        foreach (string ext in ModuleExts)
            node["*" + ext] = Try(() => Directory.EnumerateFiles(data, "*" + ext).Count().ToString());
        node["F4SE/Plugins/*.dll"] = Try(() => Directory.EnumerateFiles(Path.Combine(data, "F4SE", "Plugins"), "*.dll").Count().ToString());
        var sw = Stopwatch.StartNew();
        // AttributesToSkip = 0: the default EnumerationOptions would hide Hidden/System files, unlike os.walk.
        node["recursiveFiles"] = Try(() => Directory.EnumerateFiles(data, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        }).Count().ToString());
        node["recursiveMs"] = sw.ElapsedMilliseconds;
        Console.WriteLine();
        Console.WriteLine($"Data: top={node["topLevelEntries"]} esp={node["*.esp"]} esm={node["*.esm"]} esl={node["*.esl"]} ba2={node["*.ba2"]} f4seDlls={node["F4SE/Plugins/*.dll"]} recursive={node["recursiveFiles"]} ({node["recursiveMs"]} ms)");
    }

    // ---------------------------------------------------------------- helpers

    private static string Try(Func<string> op)
    {
        try
        {
            return op();
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}";
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
