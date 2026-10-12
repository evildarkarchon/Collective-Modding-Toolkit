using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CMToolkit.Core.Win32;

/// <summary>
/// The real machine's registry, Wine check and OS version, behind <see cref="SystemHostEnvironment"/>. Each reproduces
/// what the Python call it stands for returns (<c>winreg</c>, <c>windll.ntdll</c>, <c>platform</c>).
/// </summary>
internal static class WindowsHost
{
    /// <summary>
    /// <c>platform._WIN32_CLIENT_RELEASES</c> (CPython 3.14): the first entry whose (major, minor, build) is at most the
    /// OS's names the release, so Windows 11 is <c>11</c> although its version is 10.0.
    /// </summary>
    private static readonly (int Major, int Minor, int Build, string Release)[] ClientReleases =
    [
        (10, 1, 0, "post11"),
        (10, 0, 22000, "11"),
        (6, 4, 0, "10"),
        (6, 3, 0, "8.1"),
        (6, 2, 0, "8"),
        (6, 1, 0, "7"),
        (6, 0, 0, "Vista"),
        (5, 2, 3790, "XP64"),
        (5, 2, 0, "XPMedia"),
        (5, 1, 0, "XP"),
        (5, 0, 0, "2000"),
    ];

    /// <summary><c>platform._WIN32_SERVER_RELEASES</c> (CPython 3.14), used when the product type isn't a workstation.</summary>
    private static readonly (int Major, int Minor, int Build, string Release)[] ServerReleases =
    [
        (10, 1, 0, "post2025Server"),
        (10, 0, 26100, "2025Server"),
        (10, 0, 20348, "2022Server"),
        (10, 0, 17763, "2019Server"),
        (6, 4, 0, "2016Server"),
        (6, 3, 0, "2012ServerR2"),
        (6, 2, 0, "2012Server"),
        (6, 1, 0, "2008ServerR2"),
        (6, 0, 0, "2008Server"),
        (5, 2, 0, "2003Server"),
        (5, 0, 0, "2000Server"),
    ];

    /// <inheritdoc cref="IHostEnvironment.ReadRegistryValue"/>
    public static RegistryData ReadRegistryValue(RegistryRoot root, string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The registry is Windows-only.");
        }

        // The predefined hive keys are shared static instances, so they are deliberately not disposed.
        var hive = root == RegistryRoot.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = hive.OpenSubKey(keyPath)
                        ?? throw new FileNotFoundException($@"Registry key not found: {root}\{keyPath}");
        // DoNotExpandEnvironmentNames: winreg returns REG_EXPAND_SZ data unexpanded.
        var data = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                   ?? throw new FileNotFoundException($@"Registry value not found: {root}\{keyPath}\{valueName}");
        return key.GetValueKind(valueName) switch
        {
            RegistryValueKind.String => new RegistryData(RegistryDataType.String, data),
            RegistryValueKind.ExpandString => new RegistryData(RegistryDataType.ExpandString, data),
            RegistryValueKind.DWord => new RegistryData(RegistryDataType.DWord, (long)(uint)(int)data),
            RegistryValueKind.QWord => new RegistryData(RegistryDataType.QWord, (long)data),
            _ => new RegistryData(RegistryDataType.Other, data),
        };
    }

    /// <summary><c>hasattr(windll.ntdll, "wine_get_version")</c>.</summary>
    public static bool IsWine()
        => NativeLibrary.TryLoad("ntdll.dll", out var ntdll) && NativeLibrary.TryGetExport(ntdll, "wine_get_version", out _);

    /// <summary>
    /// <c>platform.system()</c>, <c>platform.release()</c> and the build. Python asks WMI for the version and product
    /// type; <c>RtlGetVersion</c> reports the same numbers without COM, and isn't subject to manifest version lies.
    /// </summary>
    public static OperatingSystemInfo OsInfo()
    {
        var info = new NativeMethods.OsVersionInfoEx { OsVersionInfoSize = (uint)Marshal.SizeOf<NativeMethods.OsVersionInfoEx>() };
        if (NativeMethods.RtlGetVersion(ref info) != 0)
        {
            throw new InvalidOperationException("RtlGetVersion failed.");
        }

        var (major, minor, build) = ((int)info.MajorVersion, (int)info.MinorVersion, (int)info.BuildNumber);
        var releases = info.ProductType == NativeMethods.VerNtWorkstation ? ClientReleases : ServerReleases;
        // Python compares version tuples, falling back to "" when none matches.
        var release = releases.FirstOrDefault(r => (r.Major, r.Minor, r.Build).CompareTo((major, minor, build)) <= 0).Release ?? string.Empty;
        return new OperatingSystemInfo("Windows", release, build);
    }
}
