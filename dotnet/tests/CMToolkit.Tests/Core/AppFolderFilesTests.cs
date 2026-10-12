using CMToolkit.Core;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Core;

/// <summary>
/// <c>settings.json</c>, <c>cm-toolkit.log</c> and downloaded Delta Patches live in the app's own folder, the folder
/// holding the exe. The reference resolved all three against the working directory, so a launch from a shortcut, a
/// terminal or an MO2 entry with another "Start in" scattered them (bug #36); the port fixes that (ADR-0006).
/// </summary>
public sealed class AppFolderFilesTests
{
    private const string AppFolder = @"C:\Games\Tools\CM-Toolkit";

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches are resolved against the exe's folder, not the working directory.")]
    public void Settings_is_settings_json_in_the_app_folder()
    {
        Assert.Equal(@"C:\Games\Tools\CM-Toolkit\settings.json", AppFolderFiles.Settings(AppFolder));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches are resolved against the exe's folder, not the working directory.")]
    public void Log_is_cm_toolkit_log_in_the_app_folder()
    {
        Assert.Equal(@"C:\Games\Tools\CM-Toolkit\cm-toolkit.log", AppFolderFiles.Log(AppFolder));
    }

    [Theory]
    [Trait("Parity", "DG-6")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches are resolved against the exe's folder, not the working directory.")]
    [InlineData(
        "https://github.com/evildarkarchon/Collective-Modding-Toolkit/releases/download/delta-patches/OG-to-NG/Fallout4.exe.xdelta",
        @"C:\Games\Tools\CM-Toolkit\Fallout4.exe.xdelta")]
    [InlineData("https://example.invalid/a/b/steam_api64.dll.xdelta", @"C:\Games\Tools\CM-Toolkit\steam_api64.dll.xdelta")]
    public void A_delta_patch_is_saved_under_its_url_file_name_in_the_app_folder(string url, string expected)
    {
        // downgrader.py: file_path = Path(Path(url).name), which was relative to the working directory.
        Assert.Equal(expected, AppFolderFiles.DeltaPatch(AppFolder, url));
    }

    [Fact]
    [Trait("Parity", "SET-1"), Trait("Parity", "SHELL-1"), Trait("Parity", "DG-6")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches are resolved against the exe's folder, not the working directory.")]
    public void The_paths_do_not_depend_on_the_working_directory()
    {
        // Rooted, so opening them never consults the process's current directory.
        Assert.True(Path.IsPathFullyQualified(AppFolderFiles.Settings(AppFolder)));
        Assert.True(Path.IsPathFullyQualified(AppFolderFiles.Log(AppFolder)));
        Assert.True(Path.IsPathFullyQualified(AppFolderFiles.DeltaPatch(AppFolder, "https://example.invalid/p.xdelta")));
    }
}
