using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary><c>utils.get_environment_path</c>: a shell folder that must exist.</summary>
public static class KnownFolders
{
    /// <summary>
    /// The folder's path, as the reference's <c>Path(buf.value)</c>. A failed lookup is an empty string, which is
    /// <c>.</c>, the CWD, and so passes the check.
    /// </summary>
    /// <exception cref="FileNotFoundException"><c>Folder does not exist:\n&lt;path&gt;</c> (INI-1), fatal at startup.</exception>
    public static PythonPath Get(IHostEnvironment host, KnownFolder folder)
    {
        var path = new PythonPath(host.GetKnownFolderPath(folder));
        if (!HostPaths.IsDirectory(host, path))
        {
            throw new FileNotFoundException($"Folder does not exist:\n{path}");
        }

        return path;
    }
}
