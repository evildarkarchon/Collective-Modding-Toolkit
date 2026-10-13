using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// File-system checks on reference paths, made the way Python makes them: a relative path is relative to the host's
/// working directory (<see cref="IHostEnvironment.CurrentDirectory"/>), which in tests isn't the test process's own.
/// </summary>
/// <remarks>
/// <see cref="File.Exists(string)"/> and <see cref="Directory.Exists(string)"/> stand in for the reference's
/// <c>is_file</c>/<c>is_dir</c>: the 24H2 probes are a named parity exemption (issue #7), confirmed under MO2's VFS by
/// issue #35. Like Python's <c>Path.is_file()</c>/<c>is_dir()</c>, they answer false rather than throw on an error.
/// </remarks>
internal static class HostPaths
{
    /// <summary><paramref name="path"/> made absolute against the host's working directory.</summary>
    public static string Resolve(IHostEnvironment host, PythonPath path)
        => Path.GetFullPath(path.ToString(), host.CurrentDirectory);

    /// <summary><c>path.is_file()</c>.</summary>
    public static bool IsFile(IHostEnvironment host, PythonPath path) => File.Exists(Resolve(host, path));

    /// <summary><c>path.is_dir()</c>.</summary>
    public static bool IsDirectory(IHostEnvironment host, PythonPath path) => Directory.Exists(Resolve(host, path));

    /// <summary><c>path.exists()</c>: a file or a folder.</summary>
    public static bool Exists(IHostEnvironment host, PythonPath path) => Path.Exists(Resolve(host, path));
}
