using System.Reflection;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;

namespace CMToolkit.Tests.Core;

/// <summary>
/// The real machine's host facts (PC-1..PC-3, MO2-2's registry and environment reads), checked on whatever Windows
/// machine runs the tests. Parity of the values themselves is proved by the scenarios against the fake host.
/// </summary>
public sealed class SystemHostEnvironmentTests
{
    private static readonly SystemHostEnvironment Host = SystemHostEnvironment.Instance;

    [Fact]
    [Trait("Parity", "PC-2")]
    public void The_os_is_windows_with_pythons_release_name()
    {
        var info = Host.OsInfo;

        Assert.Equal("Windows", info.System);
        Assert.Equal(Environment.OSVersion.Version.Build, info.Build);
        // platform.release() names Windows 11 "11" although its version is 10.0 (T-6). Developer machines are clients,
        // but CI runs on Windows Server, where Python uses its server table instead (e.g. "2025Server").
        Assert.Equal(ExpectedRelease(info.Build), info.Release);
    }

    /// <summary>
    /// The release name Python reports for this machine's Windows 10+ build. The product type is read from the
    /// registry (<c>WinNT</c> means a workstation) rather than from <c>RtlGetVersion</c>, so the expectation doesn't
    /// share the implementation's source of truth.
    /// </summary>
    private static string ExpectedRelease(int build)
    {
        var productType = Host.ReadRegistryValue(
            RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\ProductOptions", "ProductType").AsRegSz;
        if (productType == "WinNT")
        {
            return build >= 22000 ? "11" : "10";
        }

        return build switch
        {
            >= 26100 => "2025Server",
            >= 20348 => "2022Server",
            >= 17763 => "2019Server",
            _ => "2016Server",
        };
    }

    [Fact]
    [Trait("Parity", "PC-1")]
    public void Windows_is_not_wine() => Assert.False(Host.IsWine);

    [Fact]
    [Trait("Parity", "PC-3")]
    public void Total_memory_is_at_least_a_gibibyte() => Assert.True(Host.TotalPhysicalMemory >= 1L << 30);

    [Fact]
    public void A_registry_string_is_read_with_its_type()
    {
        var value = Host.ReadRegistryValue(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");

        Assert.Equal(RegistryDataType.String, value.Type);
        Assert.Equal(Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture), value.AsRegSz);
    }

    [Theory]
    [InlineData(@"SOFTWARE\CMToolkit-does-not-exist", "x")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CMToolkit-does-not-exist")]
    public void A_missing_registry_key_or_value_is_file_not_found(string key, string value)
        => Assert.Throws<FileNotFoundException>(() => Host.ReadRegistryValue(RegistryRoot.LocalMachine, key, value));

    [Fact]
    public void A_file_version_is_read_from_the_version_resource()
    {
        var core = typeof(PcInfo).Assembly;
        var expected = Version.Parse(core.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);

        Assert.Equal(
            [expected.Major, expected.Minor, expected.Build, expected.Revision],
            Host.GetFileVersion(core.Location));
    }

    [Fact]
    public void A_missing_file_has_no_version() => Assert.Null(Host.GetFileVersion(@"C:\CMToolkit-does-not-exist.exe"));

    [Fact]
    public void Environment_variables_are_the_process_environment()
    {
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), Host.GetEnvironmentVariable("PATH"));
        Assert.Null(Host.GetEnvironmentVariable("CMTOOLKIT_DOES_NOT_EXIST"));
    }
}
