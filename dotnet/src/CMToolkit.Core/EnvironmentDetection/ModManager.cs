using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>The mod managers the toolkit recognises among its ancestor processes (MM-1).</summary>
public enum ModManagerKind
{
    /// <summary>Mod Organizer 2, process <c>ModOrganizer.exe</c>.</summary>
    ModOrganizer,

    /// <summary>Vortex, process <c>Vortex.exe</c>.</summary>
    Vortex,
}

/// <summary>
/// The mod manager that launched the toolkit (<c>ModManagerInfo</c>'s name, exe path and version). MO2's configuration
/// is read separately by <see cref="Mo2ConfigurationReader"/>; Vortex has none (MO2-11).
/// </summary>
/// <param name="Kind">Which manager.</param>
/// <param name="ExePath">The manager's exe, as <c>Path(proc.exe())</c>.</param>
/// <param name="Version">
/// The first three parts of the exe's file version, or 0.0.0 when it has none (or an all-zero one, T-4).
/// </param>
public sealed record ModManager(ModManagerKind Kind, PythonPath ExePath, IReadOnlyList<int> Version)
{
    /// <summary>The display name the reference uses everywhere: <c>Mod Organizer</c> or <c>Vortex</c>.</summary>
    public string Name => Kind == ModManagerKind.ModOrganizer ? "Mod Organizer" : "Vortex";

    /// <summary>
    /// <c>str(packaging.version.Version("a.b.c"))</c>. The parts are integers, so PEP 440 normalisation leaves the
    /// dotted form unchanged.
    /// </summary>
    public string VersionText => string.Join('.', Version);
}

/// <summary>Finds the mod manager among the toolkit's ancestors (<c>utils.find_mod_manager</c>, MM-1).</summary>
public static class ModManagerDetection
{
    /// <summary>How many ancestors are checked, starting at the parent.</summary>
    public const int MaxAncestors = 8;

    /// <summary>
    /// The first of up to <see cref="MaxAncestors"/> ancestors whose image name is exactly <c>ModOrganizer.exe</c> or
    /// <c>Vortex.exe</c> (case-sensitive, extension included), or null when none is. No manager is a valid state.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// The matching process's exe path can't be read. The reference's <c>proc.exe()</c> raises <c>AccessDenied</c>
    /// there, uncaught, so startup fails (SHELL-13). Issue #35 measured it readable at both elevations.
    /// </exception>
    /// <exception cref="InvalidOperationException">The parent process has exited (<see cref="IHostEnvironment.GetAncestorProcesses"/>).</exception>
    public static ModManager? Find(IHostEnvironment host)
    {
        foreach (var process in host.GetAncestorProcesses(MaxAncestors))
        {
            ModManagerKind? kind = process.Name switch
            {
                "ModOrganizer.exe" => ModManagerKind.ModOrganizer,
                "Vortex.exe" => ModManagerKind.Vortex,
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            var exe = process.ExePath
                      ?? throw new UnauthorizedAccessException($"Access denied reading the exe path of {process.Name}.");
            var exePath = new PythonPath(exe);
            var parts = host.GetFileVersion(exePath.ToString());
            // get_file_version gives None for no resource, and the reference uses 0.0.0; .NET gives 0.0.0.0 instead,
            // whose first three parts are the same 0.0.0 (T-4), so only a failed read needs the explicit default.
            IReadOnlyList<int> version = parts is null ? [0, 0, 0] : [.. parts.Take(3)];
            return new ModManager(kind.Value, exePath, version);
        }

        return null;
    }
}
