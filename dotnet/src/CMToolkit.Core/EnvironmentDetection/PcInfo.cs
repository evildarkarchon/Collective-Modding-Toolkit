using System.Security;
using System.Text.RegularExpressions;
using CMToolkit.Core.PythonSemantics;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// The PC Specs the Overview shows (<c>helpers.PCInfo</c>): OS, RAM, CPU and GPU, read once at startup (PC-1..PC-5).
/// The Overview renders them (PC-6); this is only the values.
/// </summary>
/// <param name="UsingWine">Whether <c>ntdll</c> exports <c>wine_get_version</c> (PC-1).</param>
/// <param name="Os">
/// <c>Linux (WINE)</c> under Wine, else <c>"{system} {release} {version}"</c>, such as <c>Windows 11 24H2</c>. An
/// unknown build leaves the trailing space: <c>Windows 11 </c> (PC-2, T-6).
/// </param>
/// <param name="Ram">Total physical memory in GiB, rounded half to even (PC-3, T-11).</param>
/// <param name="Cpu">The cleaned-up processor name, or <c>Unknown CPU</c> if it couldn't be read (PC-4).</param>
/// <param name="Gpu">The first video adapter's name, or <c>Unknown GPU</c> (PC-5).</param>
/// <param name="Vram">Its memory in GiB, rounded half to even, or 0 (PC-5, T-11).</param>
public sealed partial record PcInfo(bool UsingWine, string Os, long Ram, string Cpu, string Gpu, long Vram)
{
    private const double Gibibyte = 1024d * 1024d * 1024d;
    private const string CpuKey = @"Hardware\Description\System\CentralProcessor\0";
    private const string VideoKey = @"HARDWARE\DEVICEMAP\VIDEO";

    /// <summary>
    /// <c>helpers.os_versions</c>: the build → release name table. A build missing from it gives an empty version.
    /// </summary>
    private static readonly Dictionary<int, string> OsVersions = new()
    {
        [18362] = "1903",
        [18363] = "1909",
        [19041] = "2004",
        [19042] = "20H2",
        [19043] = "21H1",
        [19044] = "21H2",
        [19045] = "22H2",
        [22000] = "21H2",
        [22621] = "22H2",
        [22631] = "23H2",
        [26100] = "24H2",
        [26200] = "25H2",
    };

    /// <summary>
    /// Reads every fact the way <c>PCInfo.__init__</c> does, in its order. CPU and GPU registry failures are logged
    /// (<c>logger.exception("get_cpu():")</c> / <c>"get_gpu():"</c>) and give the defaults; nothing else is caught.
    /// </summary>
    /// <param name="host">The machine.</param>
    /// <param name="logger">Receives the CPU/GPU lookup failures, at Error with the exception.</param>
    public static PcInfo Read(IHostEnvironment host, ILogger logger)
    {
        var usingWine = host.IsWine;
        // Under Wine the OS isn't read at all, as in the reference.
        var info = usingWine ? null : host.OsInfo;
        var os = info is null ? "Linux (WINE)" : FormatOs(info);
        var ram = RoundGibibytes(host.TotalPhysicalMemory);
        var cpu = ReadCpu(host, logger);
        var (gpu, vram) = ReadGpu(host, logger);
        return new PcInfo(usingWine, os, ram, cpu, gpu, vram)
        {
            IsWindows24H2OrLater = info is { System: "Windows", Build: >= Windows24H2Build },
        };
    }

    /// <summary>The build of Windows 11 24H2, the reference's own <c>win11_24h2</c> threshold (<c>utils.py</c>).</summary>
    private const int Windows24H2Build = 26100;

    /// <summary>
    /// Whether this is Windows at build 26100 (Windows 11 24H2) or later, not under Wine. The Overview's MO2 2.5.2
    /// warning keys on this (OVW-5). The reference compared <see cref="Os"/> with <c>"Windows 11 24H2"</c> exactly, so
    /// 25H2 and any build missing from its table got no warning despite the tooltip's "24H2+" (B-8, bug #27, fixed).
    /// </summary>
    public bool IsWindows24H2OrLater { get; init; }

    /// <summary><c>PCInfo._get_os</c>. The table is consulted only for <c>Windows</c>.</summary>
    private static string FormatOs(OperatingSystemInfo info)
    {
        var version = info.System == "Windows" ? OsVersions.GetValueOrDefault(info.Build, string.Empty) : string.Empty;
        return $"{info.System} {info.Release} {version}";
    }

    /// <summary><c>round(bytes / 1024**3)</c>: true division, then Python's round-half-to-even (T-11).</summary>
    private static long RoundGibibytes(long bytes) => (long)Math.Round(bytes / Gibibyte, MidpointRounding.ToEven);

    /// <summary>
    /// <c>PCInfo._get_cpu</c>. The clean-up also runs on the default when the value has the wrong type, so a
    /// non-<c>REG_SZ</c> name gives <c>Unknown</c> (" CPU" is one of the removed words); only a failed lookup keeps
    /// <c>Unknown CPU</c>.
    /// </summary>
    private static string ReadCpu(IHostEnvironment host, ILogger logger)
    {
        var model = "Unknown CPU";
        try
        {
            if (host.ReadRegistryValue(RegistryRoot.LocalMachine, CpuKey, "ProcessorNameString").AsRegSz is { } name)
            {
                model = name;
            }
        }
        catch (Exception ex) when (IsOsError(ex))
        {
            logger.LogError(ex, "get_cpu():");
            return model;
        }

        if (model.Contains("Intel", StringComparison.Ordinal) && !model.StartsWith("Intel", StringComparison.Ordinal))
        {
            model = "Intel " + model.Replace("Intel", string.Empty, StringComparison.Ordinal);
        }

        model = CpuNoise().Replace(model, string.Empty);
        model = PythonWhitespace().Replace(model, " ");
        // rsplit("@", 1)[0]: everything before the last "@", or all of it when there is none.
        var at = model.LastIndexOf('@');
        return PythonText.Strip(at < 0 ? model : model[..at]);
    }

    /// <summary>
    /// <c>PCInfo._get_gpu</c>: the <c>\Device\Video0</c> mapping, then the adapter string and memory size of the key it
    /// names. Both values are read before either is used, so a missing adapter string loses the memory size too.
    /// </summary>
    private static (string Gpu, long Vram) ReadGpu(IHostEnvironment host, ILogger logger)
    {
        var model = "Unknown GPU";
        long memory = 0;
        try
        {
            if (host.ReadRegistryValue(RegistryRoot.LocalMachine, VideoKey, @"\Device\Video0").AsRegSz is { } device)
            {
                const string prefix = @"\Registry\Machine\";
                if (device.StartsWith(prefix, StringComparison.Ordinal))
                {
                    device = device[prefix.Length..];
                }

                var adapter = host.ReadRegistryValue(RegistryRoot.LocalMachine, device, "HardwareInformation.AdapterString");
                var size = host.ReadRegistryValue(RegistryRoot.LocalMachine, device, "HardwareInformation.qwMemorySize");
                if (adapter.AsRegSz is { } name)
                {
                    model = PythonText.Strip(name);
                }

                if (size is { Type: RegistryDataType.QWord, Data: long bytes })
                {
                    memory = RoundGibibytes(bytes);
                }
            }
        }
        catch (Exception ex) when (IsOsError(ex))
        {
            logger.LogError(ex, "get_gpu():");
        }

        return (model, memory);
    }

    /// <summary>The exceptions that stand for Python's <c>OSError</c> from <c>winreg</c>, the only ones the reference catches.</summary>
    private static bool IsOsError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;

    /// <summary><c>helpers.pattern_cpu</c>, verbatim. <c>\d</c> is Unicode digits in both Python and .NET.</summary>
    [GeneratedRegex(@"(?:\d+(?:th|rd|nd) Gen| ?Processor| ?CPU|\d*[- ]Core|\(TM\)|\(R\))", RegexOptions.CultureInvariant)]
    private static partial Regex CpuNoise();

    /// <summary>
    /// <c>helpers.pattern_whitespace</c> (<c>\s+</c>). Python's <c>\s</c> also matches U+001C..U+001F, which .NET's
    /// doesn't, so they are added (see <see cref="PythonText.IsSpace"/>).
    /// </summary>
    [GeneratedRegex(@"[\s\u001C-\u001F]+", RegexOptions.CultureInvariant)]
    private static partial Regex PythonWhitespace();
}
