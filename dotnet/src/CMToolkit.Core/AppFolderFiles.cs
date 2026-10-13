using Microsoft.Extensions.Logging;

namespace CMToolkit.Core;

/// <summary>
/// The files the app keeps in its own folder, the folder holding the exe (or <c>%LOCALAPPDATA%\CM-Toolkit</c> when that
/// can't be written to, <see cref="Choose"/>): <c>settings.json</c>, <c>cm-toolkit.log</c> and downloaded Delta Patches.
/// </summary>
/// <remarks>
/// The Reference Implementation resolved all three against the <b>current working directory</b>
/// (<c>Path("settings.json")</c>, <c>basicConfig(filename="cm-toolkit.log")</c>, <c>Path(Path(url).name)</c>), while its
/// assets came from the install. Launched from a shortcut, a terminal or an MO2 entry whose "Start in" pointed
/// elsewhere, it silently started on default settings and wrote a fresh settings file, log and patches into that other
/// folder. That is bug #36, which the port fixes as a Deviation (ADR-0006): every path here is rooted in the folder the
/// caller passes, so the working directory doesn't decide where they go. It is read only once, by
/// <see cref="MigrateSettings"/>, to bring a reference user's <c>settings.json</c> across.
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

    /// <summary>
    /// The folder, below <see cref="KnownFolder.LocalAppData"/>, that stands in for an exe folder the app can't write to.
    /// </summary>
    public const string FallbackFolderName = "CM-Toolkit";

    /// <summary>
    /// Picks the app folder for a run: the exe's folder (<see cref="IHostEnvironment.AppDirectory"/>) when
    /// <paramref name="isWritable"/> says so; otherwise <c>%LOCALAPPDATA%\CM-Toolkit</c>, which is created if missing.
    /// </summary>
    /// <param name="host">The machine: its exe folder and its <c>LocalAppData</c> known folder.</param>
    /// <param name="isWritable">The writability test; the App passes <see cref="IsWritable"/>.</param>
    /// <returns>
    /// The choice. When the <c>LocalAppData</c> lookup fails there is nowhere better, so an unwritable exe folder is
    /// kept; the log then fails to open there and startup shows why (SHELL-13), as before the fallback existed.
    /// </returns>
    /// <exception cref="IOException">The fallback folder can't be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The fallback folder can't be created.</exception>
    /// <remarks>
    /// Must run before the log opens, since the log lives in the chosen folder; <see cref="AppFolderChoice.Log"/> writes
    /// the outcome once it is open.
    /// </remarks>
    public static AppFolderChoice Choose(IHostEnvironment host, Func<string, bool> isWritable)
    {
        var exeFolder = host.AppDirectory;
        if (isWritable(exeFolder))
        {
            return new AppFolderChoice(exeFolder, UnwritableExeFolder: null);
        }

        var localAppData = host.GetKnownFolderPath(KnownFolder.LocalAppData);
        if (localAppData.Length == 0)
        {
            return new AppFolderChoice(exeFolder, UnwritableExeFolder: null);
        }

        var fallback = Path.Combine(localAppData, FallbackFolderName);
        Directory.CreateDirectory(fallback);
        return new AppFolderChoice(fallback, exeFolder);
    }

    /// <summary>
    /// Copies the working directory's <c>settings.json</c>, where the reference kept it, to
    /// <paramref name="settingsPath"/> when that file doesn't exist yet, so a user moving from the reference keeps their
    /// settings. Only the settings move: the log and Delta Patches are disposable. Never throws.
    /// </summary>
    /// <param name="settingsPath">The app folder's <c>settings.json</c>, which startup is about to load.</param>
    /// <param name="workingDirectory">The process's working directory.</param>
    /// <param name="log">The app log: an INFO line names both files after a copy, a WARNING line (with the exception)
    /// after a failed one.</param>
    /// <returns>Whether the file was copied. Nothing happens when <paramref name="settingsPath"/> exists, the working
    /// directory has no <c>settings.json</c>, or both name the same file.</returns>
    /// <remarks>
    /// Runs on every start, but copies once: afterwards the app folder has its own file, which always wins. A failed
    /// copy leaves startup to load from the app folder as if there had been nothing to copy.
    /// </remarks>
    public static bool MigrateSettings(string settingsPath, string workingDirectory, ILogger log)
    {
        var source = Settings(workingDirectory);
        if (File.Exists(settingsPath) || !File.Exists(source) || SameFile(source, settingsPath))
        {
            return false;
        }

        try
        {
            // overwrite: false, so a file that appeared since the check above is never replaced.
            File.Copy(source, settingsPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write(LogLevel.Warning, $"Settings : Failed to copy {source} to the app folder: {settingsPath}", ex);
            return false;
        }

        log.Write(LogLevel.Information, $"Settings : Copied {source} to the app folder: {settingsPath}");
        return true;
    }

    /// <summary>Whether two paths name the same file, ignoring case and relative segments as Windows does.</summary>
    private static bool SameFile(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this process can create files in <paramref name="folder"/>, decided by trying: a uniquely named probe
    /// file is created there and deleted again.
    /// </summary>
    /// <param name="folder">The folder to probe.</param>
    /// <returns>False when the probe can't be created, for whatever reason: a missing folder, an ACL that denies it,
    /// a read-only medium.</returns>
    /// <remarks>
    /// A real create rather than reading the ACL: the effective answer also depends on group membership, deny entries,
    /// integrity levels, read-only volumes and filter drivers such as MO2's VFS, which only the file system itself
    /// adds up correctly.
    /// </remarks>
    public static bool IsWritable(string folder)
    {
        var probe = Path.Combine(folder, $".cm-toolkit-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            // DeleteOnClose removes it as the handle closes, so even a probe whose delete would fail leaves nothing.
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
