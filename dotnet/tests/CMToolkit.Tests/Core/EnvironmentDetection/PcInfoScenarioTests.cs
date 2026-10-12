using System.Text.Json;
using System.Text.Json.Nodes;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Core.EnvironmentDetection;

/// <summary>
/// PC-1..PC-5: the <c>environment/pc-*</c> scenarios, recorded from <c>helpers.PCInfo()</c>, replayed against
/// <see cref="PcInfo.Read"/>. The scenarios cite their IDs (and T-6, T-11) in <c>scenario.json</c>.
/// </summary>
public sealed class PcInfoScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("environment", "pc-info");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var logger = new RecordingLogger();

        var pc = PcInfo.Read(machine.Host, logger);

        // Full, not Semantic: the display holds the log lines Core itself writes (the get_cpu()/get_gpu() exceptions).
        GoldenAssert.Matches(scenario.LoadGolden(), Project(pc, logger), GoldenCompareMode.Full);
    }

    /// <summary>
    /// PC-4's name clean-up on names the scenarios don't cover. The expected names were produced by the reference's
    /// own <c>PCInfo._get_cpu()</c> under CPython 3.14, including Python's wider whitespace set (U+001C..U+001F).
    /// </summary>
    [Theory]
    [Trait("Parity", "PC-4")]
    [InlineData("11th Gen Intel(R) Core(TM) i7-11700K @ 3.60GHz", "Intel i7-11700K")]
    [InlineData("Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz", "Intel i5-8400")]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor           ", "AMD Ryzen 7 7800X3D")]
    [InlineData("Genuine Intel(R) CPU 0000 @ 2.40GHz", "Intel Genuine 0000")]
    [InlineData("Intel(R) Xeon(R) CPU E5-2690 v4 @ 2.60GHz", "Intel Xeon E5-2690 v4")]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900K", "Intel i9-13900K")]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", "AMD Ryzen 5 3600")]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", "AMD Ryzen 9 7950X")]
    [InlineData("AMD Phenom(tm) II X4 965 Processor", "AMD Phenom(tm) II X4 965")]
    [InlineData("Intel(R) Pentium(R) CPU G4560 @ 3.50GHz", "Intel Pentium G4560")]
    [InlineData("VIA Nano Processor U2250 @ 1.6+ GHz", "VIA Nano U2250")]
    [InlineData("Unknown CPU", "Unknown")]
    [InlineData("\u001CAMD\u001F\u0085Ryzen\u3000Processor", "AMD Ryzen")]
    [InlineData("x @ y @ z", "x @ y")]
    [InlineData("@", "")]
    [InlineData("", "")]
    [InlineData("IntelIntel 2nd Gen 3rd Gen 4th Gen 21st Gen Core", "IntelIntel 21st Gen")]
    [InlineData("Apple M1 Quad-Core CPU", "Apple M1 Quad")]
    public void Cpu_names_are_cleaned_like_the_reference(string processorName, string expected)
    {
        var host = new FakeHostEnvironment(
            new HostBlock(
                Registry: new Dictionary<string, IReadOnlyDictionary<string, RegistryValue>>
                {
                    [@"HKLM\Hardware\Description\System\CentralProcessor\0"] = new Dictionary<string, RegistryValue>
                    {
                        ["ProcessorNameString"] = new("REG_SZ", JsonSerializer.SerializeToElement(processorName)),
                    },
                },
                Os: new HostOs("Windows", "11", 26100),
                Pc: new HostPc(1L << 30)),
            Path.GetTempPath());

        Assert.Equal(expected, PcInfo.Read(host, new RecordingLogger()).Cpu);
    }

    private static JsonObject Project(PcInfo pc, RecordingLogger logger) => new()
    {
        ["pc"] = new JsonObject
        {
            ["usingWine"] = pc.UsingWine,
            ["os"] = pc.Os,
            ["ram"] = pc.Ram,
            ["cpu"] = pc.Cpu,
            ["gpu"] = pc.Gpu,
            ["vram"] = pc.Vram,
            ["display"] = new JsonObject { ["log"] = ProjectLog(logger) },
        },
    };

    /// <summary>
    /// The log calls as the driver's <c>log_lines</c> writes them: Python level names, and a traceback reduced to
    /// <c>"exception": true</c>.
    /// </summary>
    private static JsonArray ProjectLog(RecordingLogger logger)
    {
        var lines = new JsonArray();
        foreach (var record in logger.Records)
        {
            var line = new JsonObject
            {
                ["level"] = record.Level switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Information => "INFO",
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    LogLevel.Critical => "CRITICAL",
                    _ => throw new ArgumentOutOfRangeException(nameof(logger), record.Level, null),
                },
                ["message"] = record.Message,
            };
            if (record.Exception is not null)
            {
                line["exception"] = true;
            }

            lines.Add(line);
        }

        return lines;
    }
}
