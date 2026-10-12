namespace CMToolkit.Core;

/// <summary>The registry hives the Reference Implementation reads (§12).</summary>
public enum RegistryRoot
{
    /// <summary><c>HKEY_LOCAL_MACHINE</c>.</summary>
    LocalMachine,

    /// <summary><c>HKEY_CURRENT_USER</c>.</summary>
    CurrentUser,
}

/// <summary>A registry value's type, as far as the Reference Implementation tells them apart.</summary>
public enum RegistryDataType
{
    /// <summary><c>REG_SZ</c>.</summary>
    String,

    /// <summary><c>REG_EXPAND_SZ</c>, never expanded (Python's <c>winreg</c> doesn't).</summary>
    ExpandString,

    /// <summary><c>REG_DWORD</c>.</summary>
    DWord,

    /// <summary><c>REG_QWORD</c>.</summary>
    QWord,

    /// <summary>Any other type (<c>REG_BINARY</c>, <c>REG_MULTI_SZ</c>, ...). The reference rejects all of them.</summary>
    Other,
}

/// <summary>One registry value, as <c>winreg.QueryValueEx</c> returns it: the data and its type.</summary>
/// <param name="Type">The value's type.</param>
/// <param name="Data">
/// A <see cref="string"/> for <see cref="RegistryDataType.String"/> and <see cref="RegistryDataType.ExpandString"/>, a
/// <see cref="long"/> for <see cref="RegistryDataType.DWord"/> and <see cref="RegistryDataType.QWord"/>, and whatever
/// the platform returns otherwise.
/// </param>
public sealed record RegistryData(RegistryDataType Type, object? Data)
{
    /// <summary>The data if this is a <c>REG_SZ</c> string, the only kind the reference's string reads accept; otherwise null.</summary>
    public string? AsRegSz => Type == RegistryDataType.String ? Data as string : null;
}

/// <summary>
/// <c>platform.system()</c>, <c>platform.release()</c> and <c>sys.getwindowsversion().build</c> (PC-2). The release is
/// <c>"11"</c> on Windows 11, not <c>"10"</c> (T-6).
/// </summary>
/// <param name="System">The OS name, <c>Windows</c>.</param>
/// <param name="Release">The release name from Python's client or server release table, such as <c>11</c>.</param>
/// <param name="Build">The OS build number, such as 26100.</param>
public sealed record OperatingSystemInfo(string System, string Release, int Build);

/// <summary>One ancestor of the running process (MM-1).</summary>
/// <param name="Name">The image name with its extension, such as <c>ModOrganizer.exe</c> (Toolhelp32 <c>szExeFile</c>).</param>
/// <param name="ExePath">The full exe path, or null if it can't be read (psutil's <c>AccessDenied</c>).</param>
public sealed record AncestorProcess(string Name, string? ExePath);
