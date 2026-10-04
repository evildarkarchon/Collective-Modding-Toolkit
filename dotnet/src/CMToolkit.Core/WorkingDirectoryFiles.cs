namespace CMToolkit.Core;

/// <summary>
/// Files the app reads and writes in the <b>current working directory</b>, not the exe folder, as the Reference
/// Implementation does (ADR-0002). Under MO2 the working directory is often not the toolkit's folder, so these files
/// scatter; that is logged as issue #36 and reproduced here under Behaviour Parity.
/// </summary>
/// <remarks>
/// The paths are deliberately relative. The OS resolves them against the working directory when the file is opened,
/// which is also when Python's <c>pathlib.Path("settings.json")</c> is resolved, so a later directory change is
/// honoured in the same way. Don't pass them through <see cref="Path.GetFullPath(string)"/> early.
/// </remarks>
public static class WorkingDirectoryFiles
{
    /// <summary><c>settings.json</c> (<c>app_settings.py</c> <c>SETTINGS_PATH</c>).</summary>
    public const string Settings = "settings.json";

    /// <summary><c>cm-toolkit.log</c> (<c>main.py</c> <c>logging.basicConfig(filename=...)</c>).</summary>
    public const string Log = "cm-toolkit.log";

    /// <summary>
    /// Where a downloaded Delta Patch is saved: its URL's last path segment, in the working directory
    /// (<c>downgrader.py</c>: <c>Path(Path(url).name)</c>).
    /// </summary>
    /// <param name="url">The patch's download URL.</param>
    public static string DeltaPatch(string url) => Path.GetFileName(url);
}
