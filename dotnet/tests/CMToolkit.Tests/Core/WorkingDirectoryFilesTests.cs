using CMToolkit.Core;

namespace CMToolkit.Tests.Core;

/// <summary>
/// <c>settings.json</c>, <c>cm-toolkit.log</c> and downloaded Delta Patches are relative to the working directory, not
/// the exe folder (ADR-0002, issue #36). Relative paths are resolved by the OS against the working directory at the
/// moment the file is opened, which is when <c>pathlib.Path("settings.json")</c> resolves too.
/// </summary>
public sealed class WorkingDirectoryFilesTests
{
    [Fact]
    public void Settings_is_settings_json_in_the_working_directory()
    {
        Assert.Equal("settings.json", WorkingDirectoryFiles.Settings);
        Assert.False(Path.IsPathRooted(WorkingDirectoryFiles.Settings));
    }

    [Fact]
    public void Log_is_cm_toolkit_log_in_the_working_directory()
    {
        Assert.Equal("cm-toolkit.log", WorkingDirectoryFiles.Log);
        Assert.False(Path.IsPathRooted(WorkingDirectoryFiles.Log));
    }

    [Theory]
    [InlineData(
        "https://github.com/evildarkarchon/Collective-Modding-Toolkit/releases/download/delta-patches/OG-to-NG/Fallout4.exe.xdelta",
        "Fallout4.exe.xdelta")]
    [InlineData("https://example.invalid/a/b/steam_api64.dll.xdelta", "steam_api64.dll.xdelta")]
    public void A_delta_patch_is_saved_under_its_url_file_name_in_the_working_directory(string url, string expected)
    {
        // downgrader.py: file_path = Path(Path(url).name)
        var path = WorkingDirectoryFiles.DeltaPatch(url);

        Assert.Equal(expected, path);
        Assert.False(Path.IsPathRooted(path));
    }

    [Fact]
    public void Relative_paths_resolve_against_the_current_working_directory()
    {
        Assert.Equal(
            Path.Combine(Environment.CurrentDirectory, "settings.json"),
            Path.GetFullPath(WorkingDirectoryFiles.Settings));
    }
}
