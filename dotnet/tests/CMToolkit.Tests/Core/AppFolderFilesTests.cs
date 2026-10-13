using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

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
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void Settings_is_settings_json_in_the_app_folder()
    {
        Assert.Equal(@"C:\Games\Tools\CM-Toolkit\settings.json", AppFolderFiles.Settings(AppFolder));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void Log_is_cm_toolkit_log_in_the_app_folder()
    {
        Assert.Equal(@"C:\Games\Tools\CM-Toolkit\cm-toolkit.log", AppFolderFiles.Log(AppFolder));
    }

    [Theory]
    [Trait("Parity", "DG-6")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
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
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void The_paths_do_not_depend_on_the_working_directory()
    {
        // Rooted, so opening them never consults the process's current directory.
        Assert.True(Path.IsPathFullyQualified(AppFolderFiles.Settings(AppFolder)));
        Assert.True(Path.IsPathFullyQualified(AppFolderFiles.Log(AppFolder)));
        Assert.True(Path.IsPathFullyQualified(AppFolderFiles.DeltaPatch(AppFolder, "https://example.invalid/p.xdelta")));
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void A_folder_the_user_can_create_files_in_is_writable_and_the_probe_leaves_nothing_behind()
    {
        using var dir = new TempDirectory();

        Assert.True(AppFolderFiles.IsWritable(dir.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Path));
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void A_missing_folder_is_not_writable()
    {
        using var dir = new TempDirectory();

        Assert.False(AppFolderFiles.IsWritable(Path.Combine(dir.Path, "Missing")));
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    [SupportedOSPlatform("windows")]
    public void A_folder_whose_acl_denies_creating_files_is_not_writable()
    {
        // A real protected folder, such as Program Files to a standard user: the probe must see the denial by trying.
        using var dir = new TempDirectory();
        var info = new DirectoryInfo(dir.Path);
        var deny = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            Assert.False(AppFolderFiles.IsWritable(dir.Path));
        }
        finally
        {
            // The owner keeps WRITE_DAC, so the rule can always come off again, and TempDirectory can delete the folder.
            acl = info.GetAccessControl();
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
    }

    [Fact]
    [Trait("Parity", "SET-1"), Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void A_writable_exe_folder_is_the_app_folder_and_logs_nothing()
    {
        using var dir = new TempDirectory();
        var host = MachineWithExeIn(dir.Path, localAppData: @"<ROOT>\LocalAppData");
        var log = new CapturingLogger();

        var choice = AppFolderFiles.Choose(host, isWritable: _ => true);
        choice.Log(log);

        Assert.Equal(Path.Combine(dir.Path, "App"), choice.Folder);
        Assert.Null(choice.UnwritableExeFolder);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "LocalAppData", "CM-Toolkit")));
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Parity", "SET-1"), Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void An_unwritable_exe_folder_falls_back_to_a_cm_toolkit_folder_in_local_app_data_and_says_so()
    {
        using var dir = new TempDirectory();
        var exe = Path.Combine(dir.Path, "App");
        var host = MachineWithExeIn(dir.Path, localAppData: @"<ROOT>\LocalAppData");
        var probed = new List<string>();
        var log = new CapturingLogger();

        var choice = AppFolderFiles.Choose(host, isWritable: folder =>
        {
            probed.Add(folder);
            return false;
        });
        choice.Log(log);

        var fallback = Path.Combine(dir.Path, "LocalAppData", "CM-Toolkit");
        Assert.Equal([exe], probed);
        Assert.Equal(fallback, choice.Folder);
        Assert.Equal(exe, choice.UnwritableExeFolder);
        Assert.True(Directory.Exists(fallback));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(
            $"App folder {exe} is not writable; settings.json, cm-toolkit.log and Delta Patches are kept in {fallback} instead.",
            entry.Message);
    }

    [Fact]
    [Trait("Parity", "SET-1"), Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void Without_a_local_app_data_folder_an_unwritable_exe_folder_stays_the_app_folder()
    {
        // SHGetFolderPathW failed: there is nowhere better, so the log fails to open there and startup says why.
        using var dir = new TempDirectory();
        var host = MachineWithExeIn(dir.Path, localAppData: "");

        var choice = AppFolderFiles.Choose(host, isWritable: _ => false);

        Assert.Equal(Path.Combine(dir.Path, "App"), choice.Folder);
        Assert.Null(choice.UnwritableExeFolder);
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void The_working_directorys_settings_json_is_copied_into_an_app_folder_that_has_none()
    {
        // Where the reference kept it: a user upgrading from it, started from a shortcut, keeps their settings.
        using var dir = new TempDirectory();
        byte[] bytes = [.. "{\n\t\"log_level\": \"DEBUG\"\n}"u8];
        var source = dir.WriteBytes(@"Cwd\settings.json", bytes);
        var target = Path.Combine(dir.Path, "App", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var log = new CapturingLogger();

        Assert.True(AppFolderFiles.MigrateSettings(target, Path.Combine(dir.Path, "Cwd"), log));

        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal($"Settings : Copied {source} to the app folder: {target}", entry.Message);
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void An_app_folder_settings_json_is_never_overwritten_by_the_working_directorys()
    {
        // Once copied, the app folder's file is the one in use, so later launches from that folder leave it alone.
        using var dir = new TempDirectory();
        dir.WriteBytes(@"Cwd\settings.json", [.. "{\"log_level\": \"DEBUG\"}"u8]);
        byte[] kept = [.. "{\"log_level\": \"INFO\"}"u8];
        var target = dir.WriteBytes(@"App\settings.json", kept);
        var log = new CapturingLogger();

        Assert.False(AppFolderFiles.MigrateSettings(target, Path.Combine(dir.Path, "Cwd"), log));

        Assert.Equal(kept, File.ReadAllBytes(target));
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void Nothing_is_copied_when_the_working_directory_is_the_app_folder_or_has_no_settings_json()
    {
        using var dir = new TempDirectory();
        var app = Path.Combine(dir.Path, "App");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.Combine(dir.Path, "Cwd"));
        var log = new CapturingLogger();

        // The same folder spelled differently is still the same folder: there is nothing to copy it from.
        Assert.False(AppFolderFiles.MigrateSettings(Path.Combine(app, "settings.json"), app.ToUpperInvariant() + @"\", log));
        Assert.False(AppFolderFiles.MigrateSettings(Path.Combine(app, "settings.json"), Path.Combine(dir.Path, "Cwd"), log));

        Assert.Empty(Directory.EnumerateFileSystemEntries(app));
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void A_failed_copy_is_logged_and_startup_goes_on()
    {
        // The settings then load from the app folder as if there had been nothing to copy: defaults, saved there.
        using var dir = new TempDirectory();
        var source = dir.WriteBytes(@"Cwd\settings.json", [.. "{}"u8]);
        var target = Path.Combine(dir.Path, "Missing", "settings.json");
        var log = new CapturingLogger();

        Assert.False(AppFolderFiles.MigrateSettings(target, Path.Combine(dir.Path, "Cwd"), log));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"Settings : Failed to copy {source} to the app folder: {target}", entry.Message);
    }

    /// <summary>A machine whose exe is in <c>App</c> under <paramref name="root"/>, with the given <c>LocalAppData</c>.</summary>
    private static FakeHostEnvironment MachineWithExeIn(string root, string localAppData)
    {
        Directory.CreateDirectory(Path.Combine(root, "App"));
        return new FakeHostEnvironment(
            new HostBlock(
                AppDir: @"<ROOT>\App",
                KnownFolders: new Dictionary<string, string> { ["LocalAppData"] = localAppData }),
            root);
    }
}
