namespace CMToolkit.Core;

/// <summary>
/// The files the app keeps in its own folder, the folder holding the exe: <c>settings.json</c>, <c>cm-toolkit.log</c>
/// and downloaded Delta Patches.
/// </summary>
/// <remarks>
/// The Reference Implementation resolved all three against the <b>current working directory</b>
/// (<c>Path("settings.json")</c>, <c>basicConfig(filename="cm-toolkit.log")</c>, <c>Path(Path(url).name)</c>), while its
/// assets came from the install. Launched from a shortcut, a terminal or an MO2 entry whose "Start in" pointed
/// elsewhere, it silently started on default settings and wrote a fresh settings file, log and patches into that other
/// folder. That is bug #36, which the port fixes as a Deviation (ADR-0006): every path here is rooted in the folder the
/// caller passes, so the working directory never matters.
/// </remarks>
public static class AppFolderFiles
{
    /// <summary>The settings file's name (<c>app_settings.py</c> <c>SETTINGS_PATH</c>).</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>The log file's name (<c>main.py</c> <c>logging.basicConfig(filename=...)</c>).</summary>
    public const string LogFileName = "cm-toolkit.log";

    /// <summary><c>settings.json</c> in <paramref name="appFolder"/>.</summary>
    /// <param name="appFolder">The app's own folder; the App passes the exe's folder.</param>
    public static string Settings(string appFolder) => Path.Combine(appFolder, SettingsFileName);

    /// <summary><c>cm-toolkit.log</c> in <paramref name="appFolder"/>.</summary>
    /// <param name="appFolder">The app's own folder; the App passes the exe's folder.</param>
    public static string Log(string appFolder) => Path.Combine(appFolder, LogFileName);

    /// <summary>
    /// Where a downloaded Delta Patch is saved and looked for: its URL's last path segment, in
    /// <paramref name="appFolder"/> (<c>downgrader.py</c>: <c>Path(Path(url).name)</c>, which was CWD-relative).
    /// </summary>
    /// <param name="appFolder">The app's own folder; the App passes the exe's folder.</param>
    /// <param name="url">The patch's download URL.</param>
    public static string DeltaPatch(string appFolder, string url) => Path.Combine(appFolder, Path.GetFileName(url));
}
