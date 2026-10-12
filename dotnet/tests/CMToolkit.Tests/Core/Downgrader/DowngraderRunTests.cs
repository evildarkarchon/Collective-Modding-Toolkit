using System.Net;
using System.Net.Sockets;
using CMToolkit.Core;
using CMToolkit.Core.Downgrader;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Downgrader;

/// <summary>
/// <see cref="DowngraderRun"/> through a scripted <see cref="IDowngraderHost"/>: what the <c>downgrader/run-*</c>
/// scenarios can't script (options toggled while a patch downloads, the poll's phases, a refresh that fails, a second
/// click mid-download), and each step's file outcomes on a small machine of CRC-forged stand-ins. The expectations come
/// from <c>downgrader.py</c> (DG-4..DG-6) and the inventory's bug rows, not from the port. Downloads go to an in-memory
/// server, never the network.
/// </summary>
public sealed class DowngraderRunTests
{
    private static readonly string Assets = Path.Combine(RepoPaths.DotnetRoot, "..", "parity", "assets");
    private static readonly string Patches = Path.Combine(Assets, "delta-patches", "out");
    private static readonly string StandIns = Path.Combine(Assets, "crc32-forge", "out", "downgrader");

    [Fact]
    [Trait("Parity", "DG-4")]
    [Trait("Parity", "B-11"), Deviation(28, "Patch All comes back on when nothing was queued, even if the last file was skipped by the CRC check.")]
    public async Task Bug_28_every_skip_says_why_and_the_button_comes_back_when_nothing_was_queued()
    {
        // Bug #28 (B-11), fixed (ADR-0006): Archive2Interop.dll's unknown CRC gets past patch_files, so in the reference
        // it "needed" a patch and the button was never re-enabled although nothing was patched or queued. The port
        // re-enables the button whenever the queue isn't running once the loop is done.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        machine.Place("Fallout4Launcher.exe", "0E696744");
        machine.Place("CreationKit.exe", "49E45284");
        machine.Place(@"Tools\Archive2\Archive2.exe", "4CDFC7B5");
        machine.PlaceText(@"Tools\Archive2\Archive2Interop.dll", "not a known build");
        var host = new ScriptedHost();

        await machine.Run().PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.Equal(
            [
                "Info: Skipped Fallout4.exe: Already Old-Gen.",
                "Info: Skipped Fallout4Launcher.exe: Unsupported Version.",
                "Info: Skipped steam_api64.dll: Not Found.",
                "Info: Skipped CreationKit.exe: Unsupported Version.",
                "Info: Skipped Archive2.exe: Already Old-Gen.",
                "Info: Skipped Archive2Interop.dll: Unsupported Version.",
            ],
            host.Lines);
        Assert.Equal([true], host.Events.Items.OfType<PatchAllEnabled>().Select(e => e.Enabled));
        Assert.DoesNotContain(host.Events.Items, e => e is QueueRunning);
        Assert.Empty(host.Polls);
        Assert.Equal(0, host.Refreshes);
    }

    [Fact]
    [Trait("Parity", "DG-4")]
    public async Task A_shared_next_gen_and_anniversary_file_is_not_next_gen_so_it_reaches_the_crc_check()
    {
        // `case desired_version.value` matches only "Next-Gen", so steam_api64's NG/AE build goes on to patch_file,
        // whose check finds it isn't an Old-Gen CRC.
        using var machine = new Machine();
        machine.Place("steam_api64.dll", "E36E7B4D");
        var host = new ScriptedHost();

        await machine.Run().PatchAllAsync(InstallType.NextGen, host, host.Events);

        Assert.Contains("Info: Skipped steam_api64.dll: Unsupported Version.", host.Lines);
        Assert.True(File.Exists(machine.GamePath("steam_api64.dll")));
    }

    [Theory]
    [Trait("Parity", "DG-5")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_valid_backup_of_the_desired_version_is_restored_and_the_queue_finishes_at_once(bool keepBackups)
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        var host = new ScriptedHost { KeepBackups = keepBackups };
        var run = machine.Run();

        await run.PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        // Keep Backups copies the Old-Gen backup into place and keeps the Next-Gen one; otherwise both are gone.
        Assert.Equal(keepBackups, File.Exists(machine.GamePath("Fallout4_upgradeBackup.exe")));
        Assert.Equal(keepBackups, File.Exists(machine.GamePath("Fallout4_downgradeBackup.exe")));
        Assert.Equal(
            [
                "Good: Patched Fallout4.exe",
                "running",
                "refresh",
                "versions: Old-Gen, Not Found, Not Found, Not Found, Not Found, Not Found",
                "button on",
                "idle",
                "Info: Skipped Fallout4Launcher.exe: Not Found.",
                "Info: Skipped steam_api64.dll: Not Found.",
                "Info: Skipped CreationKit.exe: Not Found.",
                "Info: Skipped Archive2.exe: Not Found.",
                "Info: Skipped Archive2Interop.dll: Not Found.",
                "button on",
            ],
            host.Script);
        Assert.False(run.InProgress);
    }

    [Fact]
    [Trait("Parity", "DG-5")]
    public async Task An_identical_current_backup_replaces_the_live_file_and_stale_backups_are_deleted()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_downgradeBackup.exe", "C5965A2E", from: "Fallout4.exe");
        machine.Place("Fallout4Launcher.exe", "F6A06FF5");
        machine.Place("Fallout4Launcher_downgradeBackup.exe", "C5965A2E", from: "Fallout4.exe");
        machine.Place("Fallout4Launcher_upgradeBackup.exe", "F6A06FF5", from: "Fallout4Launcher.exe");
        var server = new PatchServer();
        var host = new ScriptedHost { KeepBackups = true };

        await machine.Run(server).PatchAllAsync(InstallType.OldGen, host, host.Events);

        // Fallout4.exe: its identical Next-Gen backup stays, the live file goes, and the patch is queued from the backup.
        Assert.False(File.Exists(machine.GamePath("Fallout4.exe")));
        Assert.Equal("C5965A2E", FileCrc32.Of(machine.GamePath("Fallout4_downgradeBackup.exe")));
        // The launcher: the stale Next-Gen backup was replaced by the live file, and the "Old-Gen" one wasn't Old-Gen.
        Assert.False(File.Exists(machine.GamePath("Fallout4Launcher.exe")));
        Assert.Equal("F6A06FF5", FileCrc32.Of(machine.GamePath("Fallout4Launcher_downgradeBackup.exe")));
        Assert.False(File.Exists(machine.GamePath("Fallout4Launcher_upgradeBackup.exe")));

        server.Release();
        await host.RunPollsAsync();
        Assert.Equal(["NG-to-OG-Fallout4.exe.xdelta", "NG-to-OG-Fallout4Launcher.exe.xdelta"], server.Requested);
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.Equal("02445570", FileCrc32.Of(machine.GamePath("Fallout4Launcher.exe")));
    }

    [Fact]
    [Trait("Parity", "DG-5")]
    public async Task A_read_only_file_is_made_writable_before_it_is_backed_up()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        File.SetAttributes(machine.GamePath("Fallout4.exe"), FileAttributes.ReadOnly | FileAttributes.Archive);
        var host = new ScriptedHost { KeepBackups = true };

        await machine.Run().PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.Equal(FileAttributes.Archive, File.GetAttributes(machine.GamePath("Fallout4_downgradeBackup.exe")));
        Assert.Contains("Good: Patched Fallout4.exe", host.Lines);
    }

    [Fact]
    [Trait("Parity", "DG-5"), Deviation(91, "A file whose only attribute is read-only is made writable, so its backup can be deleted and the restore succeeds.")]
    public async Task Bug_91_a_file_whose_only_attribute_is_read_only_is_made_writable_too()
    {
        // Bug #91, fixed (ADR-0006): CPython's chmod wrote back an attribute word of 0, which Windows ignores, so the
        // read-only bit survived into the backup, and deleting that backup (Keep Backups off) failed after the restore
        // (Patched, then Failed patching). The port sets FILE_ATTRIBUTE_NORMAL instead, so the backup is deleted.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        File.SetAttributes(machine.GamePath("Fallout4.exe"), FileAttributes.ReadOnly);
        var host = new ScriptedHost { KeepBackups = false };

        await machine.Run().PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.False(File.Exists(machine.GamePath("Fallout4_downgradeBackup.exe")));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.Equal(["Good: Patched Fallout4.exe"], host.Lines.Where(l => !l.StartsWith("Info", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Parity", "DG-5"), Deviation(91, "A file whose only attribute is read-only keeps no read-only bit into its backup.")]
    public async Task Bug_91_the_backup_of_a_file_whose_only_attribute_was_read_only_is_not_read_only()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        File.SetAttributes(machine.GamePath("Fallout4.exe"), FileAttributes.ReadOnly);
        var host = new ScriptedHost { KeepBackups = true };

        await machine.Run().PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.False(File.GetAttributes(machine.GamePath("Fallout4_downgradeBackup.exe")).HasFlag(FileAttributes.ReadOnly));
        Assert.Contains("Good: Patched Fallout4.exe", host.Lines);
    }

    [Fact]
    [Trait("Parity", "DG-5")]
    public async Task An_os_error_after_the_restore_logs_a_failure_too()
    {
        // The identical Next-Gen backup is read-only: the restore works, then deleting that backup (Keep Backups off)
        // raises PermissionError, which `except OSError` turns into a failure line after the success line.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_downgradeBackup.exe", "C5965A2E", from: "Fallout4.exe");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        File.SetAttributes(machine.GamePath("Fallout4_downgradeBackup.exe"), FileAttributes.ReadOnly);
        var host = new ScriptedHost { KeepBackups = false };

        await machine.Run().PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.Equal(["Good: Patched Fallout4.exe", "Bad: Failed patching Fallout4.exe"], host.Lines.Take(2));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.True(File.Exists(machine.GamePath("Fallout4_downgradeBackup.exe")));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "NET-3")]
    [Trait("Parity", "THR-2"), Deviation(28, "Patch All stays off while a download runs, though the loop's last file was skipped.")]
    public async Task A_missing_patch_downloads_while_the_ui_is_live_then_decodes_under_the_block()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var server = new PatchServer();
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };

        await machine.Run(server).PatchAllAsync(InstallType.NextGen, host, host.Events);

        // The click is over with the download still in flight: the window not closable, the bar at 0, and the button
        // off. (B-11: the reference turned it back on here, as the last file wasn't there; bug #28, fixed.)
        Assert.Equal(
            [
                "running",
                "% 0",
                "Info: Skipped Fallout4Launcher.exe: Not Found.",
                "Info: Skipped steam_api64.dll: Not Found.",
                "Info: Skipped CreationKit.exe: Not Found.",
                "Info: Skipped Archive2.exe: Not Found.",
                "Info: Skipped Archive2Interop.dll: Not Found.",
                "button off",
            ],
            host.Script);
        Assert.Single(host.Polls);

        server.Release();
        await host.RunPollsAsync();

        Assert.Equal([OperationPhase.Blocking], host.Phases.Items);
        Assert.Equal(["OG-to-NG-Fallout4.exe.xdelta"], server.Requested);
        Assert.Equal(DeltaPatches.Url(DeltaPatches.OldGenToNextGen, "Fallout4.exe"), server.Urls.Single().ToString());
        Assert.Equal("C5965A2E", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.True(File.Exists(machine.GamePath("Fallout4_upgradeBackup.exe")));
        Assert.True(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal(
            ["% 100", "Good: Patched Fallout4.exe", "refresh", "versions: Next-Gen, Not Found, Not Found, Not Found, Not Found, Not Found", "button on", "idle"],
            host.Script.SkipWhile(s => s != "% 100"));
    }

    [Fact]
    [Trait("Parity", "NET-3")]
    public async Task The_bar_follows_each_kibibyte_of_the_download_by_its_content_length()
    {
        using var machine = new Machine();
        machine.Place("Fallout4Launcher.exe", "02445570", fromFile: Path.Combine(Patches, "big", "og", "Fallout4Launcher.exe"));
        var big = File.ReadAllBytes(Path.Combine(Patches, "big", "OG-to-NG-Fallout4Launcher.exe.xdelta"));
        var server = new PatchServer { ["OG-to-NG-Fallout4Launcher.exe.xdelta"] = PatchServer.Body(big) };
        var host = new ScriptedHost { KeepBackups = true };

        await machine.Run(server).PatchAllAsync(InstallType.NextGen, host, host.Events);
        server.Release();
        await host.RunPollsAsync();

        // Only what a poll last set is reported; whatever it was, the values only rise, by whole KiB, to 100.
        var values = host.Events.Items.OfType<Percent>().Select(p => p.Value).ToList();
        Assert.Equal(0, values[0]);
        Assert.Equal(100, values[^1]);
        Assert.Equal(values.Order(), values);
        Assert.All(values.Skip(1).SkipLast(1), v => Assert.Equal(0, Math.Round(v * big.Length / 100) % 1024));
        Assert.Equal("F6A06FF5", FileCrc32.Of(machine.GamePath("Fallout4Launcher.exe")));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "B-10"), Deviation(15, "A saved patch is checked by decoding it; a good one is used without a download, as before.")]
    public async Task Bug_15_a_patch_already_in_the_working_directory_that_decodes_is_used_without_a_download()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        File.Copy(Path.Combine(Patches, "OG-to-NG-Fallout4.exe.xdelta"), machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta"));
        var server = new PatchServer();
        var host = new ScriptedHost { KeepBackups = false, DeletePatches = true };

        await machine.Run(server).PatchAllAsync(InstallType.NextGen, host, host.Events);

        // All within the click: applied at once, the backup and the patch deleted, the queue finished.
        Assert.Empty(server.Requested);
        Assert.Empty(host.Polls);
        Assert.Equal("C5965A2E", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.False(File.Exists(machine.GamePath("Fallout4_upgradeBackup.exe")));
        Assert.False(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal(["running", "% 100", "Good: Patched Fallout4.exe", "refresh"], host.Script.Take(4));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "B-9"), Deviation(14, "A failed decode puts the backup back as the live file instead of deleting it.")]
    public async Task Bug_14_a_failed_decode_puts_the_backup_back_when_keep_backups_is_off()
    {
        // Bug #14 (B-9), fixed (ADR-0006): a patch that doesn't decode (here a garbage one already in the working
        // directory) left the reference with neither the file nor its backup when Keep Backups was off. The port moves
        // the backup back into place, so the game is as it was before the click.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        File.WriteAllText(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta"), "not a patch");
        var host = new ScriptedHost { KeepBackups = false, DeletePatches = false };

        await machine.Run().PatchAllAsync(InstallType.NextGen, host, host.Events);
        await host.RunPollsAsync();

        Assert.Contains("Bad: Failed patching Fallout4.exe", host.Lines);
        Assert.DoesNotContain("Good: Patched Fallout4.exe", host.Lines);
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.False(File.Exists(machine.GamePath("Fallout4_upgradeBackup.exe")));
        Assert.Equal("versions: Old-Gen, Not Found, Not Found, Not Found, Not Found, Not Found", host.Script.Single(s => s.StartsWith("versions", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Parity", "B-9"), Deviation(14, "A failed decode removes its partial output and copies the backup back, keeping it with Keep Backups on.")]
    public async Task Bug_14_a_decode_failing_mid_file_leaves_the_original_and_keeps_the_backup_when_keep_backups_is_on()
    {
        // The real launcher patch cut short: xdelta3 writes the windows it verified before the cut, so the reference left
        // a fragment as the launcher. The port deletes the fragment and copies the backup back.
        using var machine = new Machine();
        machine.Place("Fallout4Launcher.exe", "02445570", fromFile: Path.Combine(Patches, "big", "og", "Fallout4Launcher.exe"));
        var big = File.ReadAllBytes(Path.Combine(Patches, "big", "OG-to-NG-Fallout4Launcher.exe.xdelta"));
        File.WriteAllBytes(machine.CwdPath("OG-to-NG-Fallout4Launcher.exe.xdelta"), big[..(big.Length / 2)]);
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };

        await machine.Run().PatchAllAsync(InstallType.NextGen, host, host.Events);
        await host.RunPollsAsync();

        Assert.Contains("Bad: Failed patching Fallout4Launcher.exe", host.Lines);
        Assert.Equal("02445570", FileCrc32.Of(machine.GamePath("Fallout4Launcher.exe")));
        Assert.Equal("02445570", FileCrc32.Of(machine.GamePath("Fallout4Launcher_upgradeBackup.exe")));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "B-10"), Deviation(15, "A saved patch that doesn't decode is deleted, so the next Patch All downloads it afresh.")]
    public async Task Bug_15_a_saved_patch_that_does_not_decode_is_deleted_so_the_next_run_downloads_it()
    {
        // Bug #15 (B-10), fixed (ADR-0006): the reference reused a same-named patch in the working directory without a
        // check, so an error page or a truncated file from an earlier run failed every run after it. The port deletes a
        // patch that doesn't decode, whatever Delete Patches says, and the next click downloads a good one.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        File.WriteAllText(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta"), "Not Found");
        var server = new PatchServer();
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };
        var run = machine.Run(server);

        await run.PatchAllAsync(InstallType.NextGen, host, host.Events);

        Assert.Empty(server.Requested);
        Assert.Equal(["Bad: Failed patching Fallout4.exe"], host.Lines.Where(l => !l.StartsWith("Info", StringComparison.Ordinal)));
        Assert.False(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));

        server.Release();
        await run.PatchAllAsync(InstallType.NextGen, host, host.Events);
        await host.RunPollsAsync();

        Assert.Equal(["OG-to-NG-Fallout4.exe.xdelta"], server.Requested);
        Assert.Equal("C5965A2E", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.True(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    [Trait("Parity", "B-10"), Deviation(15, "A response without a Content-Length downloads in full; the bar jumps to 100 when it's done.")]
    public async Task Bug_15_a_response_without_a_content_length_downloads_and_applies()
    {
        // Bug #15 (B-10), fixed (ADR-0006): the reference divided by the missing Content-Length on the first chunk, and
        // its poll then waited forever with the window unclosable. The port downloads the whole patch, showing no
        // progress until it is done.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var patch = File.ReadAllBytes(Path.Combine(Patches, "OG-to-NG-Fallout4.exe.xdelta"));
        var server = new PatchServer { ["OG-to-NG-Fallout4.exe.xdelta"] = PatchServer.Body(patch, contentLength: false) };
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };
        var run = machine.Run(server);

        await run.PatchAllAsync(InstallType.NextGen, host, host.Events);
        server.Release();
        await host.RunPollsAsync();

        Assert.Equal(patch, File.ReadAllBytes(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal("C5965A2E", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.Equal([0.0, 100.0], host.Events.Items.OfType<Percent>().Select(p => p.Value));
        Assert.False(run.InProgress);
        Assert.Equal(["button on", "idle"], host.Script.TakeLast(2));
    }

    [Fact]
    [Trait("Parity", "B-10"), Deviation(15, "A failed download is logged, its file is put back from its backup, and the queue moves on to its end.")]
    [Trait("Parity", "NET-3")]
    public async Task Bug_15_a_network_error_fails_that_file_and_the_queue_moves_on()
    {
        // Bug #15 (B-10), fixed (ADR-0006): the reference's download thread died, and the poll waited for it forever
        // with the button off and the window unclosable. The port logs the failure, puts the file back, patches the
        // next file and finishes the queue.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        machine.Place("Fallout4Launcher.exe", "02445570");
        var server = new PatchServer { ["OG-to-NG-Fallout4.exe.xdelta"] = PatchServer.Failure() };
        var host = new ScriptedHost { KeepBackups = false, DeletePatches = false };
        var run = machine.Run(server);

        await run.PatchAllAsync(InstallType.NextGen, host, host.Events);
        server.Release();
        await host.RunPollsAsync();

        Assert.Equal(
            [
                "Bad: Failed downloading OG-to-NG-Fallout4.exe.xdelta: Canned connection failure.",
                "Bad: Failed patching Fallout4.exe",
                "Good: Patched Fallout4Launcher.exe",
            ],
            host.Lines.Where(l => !l.StartsWith("Info", StringComparison.Ordinal)));
        Assert.False(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.False(File.Exists(machine.GamePath("Fallout4_upgradeBackup.exe")));
        Assert.Equal("F6A06FF5", FileCrc32.Of(machine.GamePath("Fallout4Launcher.exe")));
        Assert.False(run.InProgress);
        Assert.Equal(["versions: Old-Gen, Next-Gen, Not Found, Not Found, Not Found, Not Found", "button on", "idle"], host.Script.TakeLast(3));
    }

    [Fact]
    [Trait("Parity", "B-10"), Deviation(15, "An HTTP error status fails the download without saving the error page as the patch.")]
    public async Task Bug_15_an_error_page_is_not_saved_as_the_patch()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var server = new PatchServer { ["OG-to-NG-Fallout4.exe.xdelta"] = PatchServer.Body("Not Found"u8.ToArray(), HttpStatusCode.NotFound) };
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };

        await machine.Run(server).PatchAllAsync(InstallType.NextGen, host, host.Events);
        server.Release();
        await host.RunPollsAsync();

        Assert.Contains("Bad: Failed downloading OG-to-NG-Fallout4.exe.xdelta: The server answered 404 (Not Found).", host.Lines);
        Assert.False(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4_upgradeBackup.exe")));
    }

    [Fact]
    [Trait("Parity", "B-10"), Deviation(15, "A download cut off mid-body leaves no partial patch behind.")]
    public async Task Bug_15_a_download_cut_off_mid_body_leaves_no_partial_patch()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var server = new PatchServer { ["OG-to-NG-Fallout4.exe.xdelta"] = PatchServer.CutOff(new byte[3000], after: 2048) };
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };
        var run = machine.Run(server);

        await run.PatchAllAsync(InstallType.NextGen, host, host.Events);
        server.Release();
        await host.RunPollsAsync();

        Assert.Contains(host.Lines, l => l.StartsWith("Bad: Failed downloading OG-to-NG-Fallout4.exe.xdelta: ", StringComparison.Ordinal));
        Assert.False(File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal("C6053902", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
        Assert.False(run.InProgress);
    }

    [Theory]
    [Trait("Parity", "DG-6")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Options_toggled_while_a_patch_downloads_apply_to_that_patch(bool keepAtApply, bool deleteAtApply)
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var server = new PatchServer();
        var host = new ScriptedHost { KeepBackups = !keepAtApply, DeletePatches = !deleteAtApply };

        await machine.Run(server).PatchAllAsync(InstallType.NextGen, host, host.Events);
        // Toggled after the click, while the download is held: apply_patch reads the boxes when it runs.
        host.KeepBackups = keepAtApply;
        host.DeletePatches = deleteAtApply;
        server.Release();
        await host.RunPollsAsync();

        Assert.Equal(keepAtApply, File.Exists(machine.GamePath("Fallout4_upgradeBackup.exe")));
        Assert.Equal(!deleteAtApply, File.Exists(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
    }

    [Fact]
    [Trait("Parity", "DG-4")]
    [Trait("Parity", "B-11"), Deviation(28, "A skipped last file no longer re-enables Patch All while a download still runs.")]
    public async Task Bug_28_a_skipped_last_file_leaves_the_button_off_while_a_download_still_runs()
    {
        // Bug #28 (B-11), fixed (ADR-0006): Archive2Interop.dll isn't there, so the reference's loop ended with
        // patch_needed False and turned the button back on mid-download. The port keeps it off until the queue empties.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var server = new PatchServer();
        var host = new ScriptedHost();
        var run = machine.Run(server);

        await run.PatchAllAsync(InstallType.NextGen, host, host.Events);

        Assert.DoesNotContain("button on", host.Script);
        Assert.Equal("button off", host.Script[^1]);
        Assert.True(run.InProgress);
        Assert.Single(host.Polls);

        server.Release();
        await host.RunPollsAsync();
        Assert.Equal(["button on", "idle"], host.Script.TakeLast(2));
    }

    [Fact]
    [Trait("Parity", "DG-4"), Deviation(28, "A restore that empties the queue mid-loop no longer leaves Patch All on while a later file downloads.")]
    public async Task Bug_28_a_queue_finished_mid_loop_does_not_leave_the_button_on_for_a_later_download()
    {
        // Fallout4.exe is restored from its backup, so the queue empties and the "finished" step turns the button on in
        // the middle of the loop; the launcher then starts a download. The button is off again when the loop is done.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        machine.Place("Fallout4Launcher.exe", "F6A06FF5");
        var server = new PatchServer();
        var host = new ScriptedHost { KeepBackups = true };
        var run = machine.Run(server);

        await run.PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.True(run.InProgress);
        Assert.Equal("button off", host.Script.Last(s => s.StartsWith("button", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    public async Task A_failed_overview_refresh_ends_the_click_where_it_failed()
    {
        // The refresh raised out of check_download_queue, so out of patch_files: nothing after it in the loop runs, and
        // the run's state stays "in progress".
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        machine.Place("Fallout4Launcher.exe", "F6A06FF5");
        var host = new ScriptedHost { RefreshSucceeds = false };
        var run = machine.Run();

        await run.PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.Equal(["Good: Patched Fallout4.exe", "running", "refresh"], host.Script);
        Assert.True(run.InProgress);
        Assert.True(File.Exists(machine.GamePath("Fallout4Launcher.exe")));
    }

    [Fact]
    [Trait("Parity", "DG-4")]
    public async Task A_finished_step_in_the_middle_of_the_loop_redetects_the_files_still_to_come()
    {
        // get_info rewrote current_versions while patch_files iterated over it, so the later files are taken as they
        // are now. Here the launcher becomes Old-Gen behind the window's back after it opened.
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C5965A2E");
        machine.Place("Fallout4_upgradeBackup.exe", "C6053902", from: "Fallout4.exe");
        machine.Place("Fallout4Launcher.exe", "F6A06FF5");
        var run = machine.Run();
        machine.Place("Fallout4Launcher.exe", "02445570");
        var host = new ScriptedHost { KeepBackups = true };

        await run.PatchAllAsync(InstallType.OldGen, host, host.Events);

        Assert.Contains("Info: Skipped Fallout4Launcher.exe: Already Old-Gen.", host.Lines);
    }

    [Fact]
    [Trait("Parity", "DG-6")]
    public async Task A_file_gone_since_it_was_detected_ends_the_click_with_its_error()
    {
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        var run = machine.Run();
        File.Delete(machine.GamePath("Fallout4.exe"));
        var host = new ScriptedHost();

        await Assert.ThrowsAsync<FileNotFoundException>(() => run.PatchAllAsync(InstallType.NextGen, host, host.Events));
        Assert.Empty(host.Script);
    }

    [Fact]
    [Trait("Parity", "T-14")]
    [Trait("Parity", "NET-3")]
    public void Downloads_time_out_per_read_after_ten_seconds_follow_redirects_and_keep_the_encoding()
    {
        using var handler = DeltaPatches.CreateHandler();

        // requests' timeout=10 bounds each connect and each read, not the download (T-14).
        Assert.Equal(TimeSpan.FromSeconds(10), DeltaPatches.ReadTimeout);
        Assert.Equal(DeltaPatches.ReadTimeout, handler.ConnectTimeout);
        Assert.NotNull(handler.ConnectCallback);
        Assert.True(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
    }

    [Fact]
    [Trait("Parity", "T-14")]
    [Trait("Parity", "NET-3")]
    public async Task A_patch_trickling_in_through_a_redirect_downloads_over_the_real_stack()
    {
        // The real handler over loopback: GitHub's asset URL redirects to its object store, and the patch arrives in
        // small pieces with pauses between them, each well inside the per-read timeout.
        var patch = File.ReadAllBytes(Path.Combine(Patches, "OG-to-NG-Fallout4.exe.xdelta"));
        await using var server = LoopbackServer.Start(async (path, stream, stopping) =>
        {
            if (path != "/object-store/patch")
            {
                await Write(stream, "HTTP/1.1 302 Found\r\nLocation: /object-store/patch\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                return;
            }

            await Write(stream, $"HTTP/1.1 200 OK\r\nContent-Length: {patch.Length}\r\nConnection: close\r\n\r\n");
            foreach (var piece in patch.Chunk(100))
            {
                await Task.Delay(50, stopping);
                await stream.WriteAsync(piece, stopping);
                await stream.FlushAsync(stopping);
            }
        });
        using var machine = new Machine();
        machine.Place("Fallout4.exe", "C6053902");
        using var http = new ToLoopback(server.Url, DeltaPatches.CreateHandler());
        var host = new ScriptedHost { KeepBackups = true, DeletePatches = false };

        await machine.Run(http).PatchAllAsync(InstallType.NextGen, host, host.Events);
        await host.RunPollsAsync();

        Assert.Equal(["/OG-to-NG-Fallout4.exe.xdelta", "/object-store/patch"], server.Paths);
        Assert.Equal(patch, File.ReadAllBytes(machine.CwdPath("OG-to-NG-Fallout4.exe.xdelta")));
        Assert.Equal("C5965A2E", FileCrc32.Of(machine.GamePath("Fallout4.exe")));
    }

    private static async Task Write(Stream stream, string text)
    {
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(text));
        await stream.FlushAsync();
    }

    /// <summary>Sends the run's requests for the patch host to a loopback server instead, keeping their path's file name.</summary>
    private sealed class ToLoopback(Uri server, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(server, "/" + request.RequestUri!.Segments[^1]);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// An HTTP server on 127.0.0.1 that answers each connection with a script given the request's path, until disposed.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;
        private readonly List<string> _paths = [];

        private LoopbackServer(TcpListener listener, Func<string, NetworkStream, CancellationToken, Task> script)
        {
            _listener = listener;
            var token = _stopping.Token;
            _serving = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(token);
                    var stream = client.GetStream();
                    var head = new System.Text.StringBuilder();
                    var buffer = new byte[1];
                    while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(buffer, token) > 0)
                    {
                        head.Append((char)buffer[0]);
                    }

                    var path = head.ToString().Split(' ')[1];
                    lock (_paths)
                    {
                        _paths.Add(path);
                    }

                    await script(path, stream, token);
                }
            });
        }

        public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_paths)
                {
                    return [.. _paths];
                }
            }
        }

        public static LoopbackServer Start(Func<string, NetworkStream, CancellationToken, Task> script)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new LoopbackServer(listener, script);
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
            {
                // Stopped while waiting for a connection or mid-script: the test is over either way.
            }

            _listener.Stop();
            _listener.Dispose();
            _stopping.Dispose();
        }
    }

    /// <summary>A temp game folder and working directory on a fake host, filled with the Downgrader's stand-ins.</summary>
    private sealed class Machine : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Machine()
        {
            Directory.CreateDirectory(Path.Combine(_temp.Path, "Game"));
            Directory.CreateDirectory(Path.Combine(_temp.Path, "Cwd"));
            Host = new FakeHostEnvironment(new HostBlock(Cwd: "<ROOT>/Cwd"), _temp.Path);
            var empty = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            Install = new GameInstall(null, null, new GameDirectories(new PythonPath(GamePath(string.Empty)), null, null), new GameInis(empty, empty, "en"));
        }

        public FakeHostEnvironment Host { get; }

        public GameInstall Install { get; }

        public string GamePath(string relative) => Path.Combine(_temp.Path, "Game", relative);

        public string CwdPath(string relative) => Path.Combine(_temp.Path, "Cwd", relative);

        /// <summary>Puts the stand-in with <paramref name="crc"/> at <paramref name="relative"/> in the game folder.</summary>
        /// <param name="relative">Where it goes, under the game folder.</param>
        /// <param name="crc">The table CRC it has.</param>
        /// <param name="from">The table file whose stand-in to take; by default the one named like <paramref name="relative"/>.</param>
        /// <param name="fromFile">A file to copy instead of a stand-in.</param>
        public void Place(string relative, string crc, string? from = null, string? fromFile = null)
        {
            // The og/ng sets first: the self-made patches were built from those stand-ins, so only they decode.
            var name = from ?? Path.GetFileName(relative);
            var source = fromFile
                         ?? new[] { Path.Combine(StandIns, "og", name), Path.Combine(StandIns, "ng", name) }.FirstOrDefault(p => FileCrc32.Of(p) == crc)
                         ?? Path.Combine(StandIns, "all", name, crc);
            var target = GamePath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }

            File.Copy(source, target, overwrite: true);
            Assert.Equal(crc, FileCrc32.Of(target));
        }

        public void PlaceText(string relative, string text)
        {
            var target = GamePath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }

        /// <summary>The run over the files as they are now, as the window detects them when it opens.</summary>
        public DowngraderRun Run(HttpMessageHandler? http = null) => new(Install, Host, http, DowngraderDetection.Detect(Install, Host));

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            _temp.Dispose();
        }
    }

    /// <summary>
    /// A scripted window: options set by the test, a refresh that records itself and succeeds or fails as told, and
    /// polls kept for the test to run.
    /// </summary>
    private sealed class ScriptedHost : IDowngraderHost
    {
        private volatile bool _keepBackups = true;
        private volatile bool _deletePatches = true;

        public bool KeepBackups
        {
            get => _keepBackups;
            set => _keepBackups = value;
        }

        public bool DeletePatches
        {
            get => _deletePatches;
            set => _deletePatches = value;
        }

        public bool RefreshSucceeds { get; init; } = true;

        public int Refreshes { get; private set; }

        public SyncProgress<OperationEvent> Events { get; } = new();

        public SyncProgress<OperationPhase> Phases { get; } = new();

        public List<Func<IProgress<OperationPhase>, Task>> Polls { get; } = [];

        /// <summary>The log lines as <c>Type: text</c>.</summary>
        public List<string> Lines => [.. Events.Items.OfType<LogLine>().Select(l => $"{l.Type}: {l.Text}")];

        /// <summary>Every event, and each refresh, as a short line in order.</summary>
        public List<string> Script => [.. Events.Items.Select(Describe)];

        public Task<bool> RefreshOverviewAsync()
        {
            Refreshes++;
            Events.Report(new RefreshMarker());
            return Task.FromResult(RefreshSucceeds);
        }

        public void SchedulePoll(Func<IProgress<OperationPhase>, Task> poll) => Polls.Add(poll);

        /// <summary>Runs the scheduled polls in order, as the App would, each to its end.</summary>
        public async Task RunPollsAsync()
        {
            while (Polls.Count > 0)
            {
                var poll = Polls[0];
                Polls.RemoveAt(0);
                await poll(Phases).WithTimeout();
            }
        }

        private static string Describe(OperationEvent e) => e switch
        {
            LogLine l => $"{l.Type}: {l.Text}",
            Percent p => $"% {p.Value}",
            PatchAllEnabled b => b.Enabled ? "button on" : "button off",
            QueueRunning q => q.Running ? "running" : "idle",
            VersionsDetected v => "versions: " + string.Join(", ", v.Versions.Select(f => f.InstallType.DisplayName())),
            RefreshMarker => "refresh",
            _ => e.ToString(),
        };
    }

    /// <summary>Marks where the run asked for the Overview refresh among its events.</summary>
    private sealed record RefreshMarker : OperationEvent;

    /// <summary>An <see cref="IProgress{T}"/> that records each report at once, on the reporting thread.</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _items = [];

        public IReadOnlyList<T> Items
        {
            get
            {
                lock (_items)
                {
                    return [.. _items];
                }
            }
        }

        public void Report(T value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
    }

    /// <summary>
    /// An in-memory patch server: each patch file of <c>parity/assets/delta-patches/out</c> by its name, or a scripted
    /// response. Every response waits until <see cref="Release"/>, as a real download takes time, so the click always
    /// ends before its first download does.
    /// </summary>
    private sealed class PatchServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Uri> _urls = [];

        public Func<HttpResponseMessage> this[string name]
        {
            set => _responses[name] = value;
        }

        public IReadOnlyList<Uri> Urls
        {
            get
            {
                lock (_urls)
                {
                    return [.. _urls];
                }
            }
        }

        public IReadOnlyList<string> Requested => [.. Urls.Select(u => u.Segments[^1])];

        public static Func<HttpResponseMessage> Body(byte[] body, HttpStatusCode status = HttpStatusCode.OK, bool contentLength = true) => () =>
        {
            var content = new StreamContent(new NonSeekable(body));
            if (contentLength)
            {
                content.Headers.ContentLength = body.Length;
            }

            return new HttpResponseMessage(status) { Content = content };
        };

        public static Func<HttpResponseMessage> Failure() => () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Canned connection failure.");

        /// <summary>A 200 with the whole body's Content-Length whose body fails with an I/O error after <paramref name="after"/> bytes.</summary>
        public static Func<HttpResponseMessage> CutOff(byte[] body, int after) => () =>
        {
            var content = new StreamContent(new CutOffStream(body, after));
            content.Headers.ContentLength = body.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };

        public void Release() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_urls)
            {
                _urls.Add(request.RequestUri!);
            }

            Assert.Equal($"cm-toolkit/{AppInfo.Version}", request.Headers.UserAgent.ToString());
            await _released.Task.WaitAsync(cancellationToken);
            var name = request.RequestUri!.Segments[^1];
            return _responses.TryGetValue(name, out var respond)
                ? respond()
                : Body(File.ReadAllBytes(Path.Combine(Patches, name)))();
        }

        private sealed class NonSeekable(byte[] data) : MemoryStream(data, writable: false)
        {
            public override bool CanSeek => false;
        }

        /// <summary>A body whose connection drops after <paramref name="after"/> bytes.</summary>
        private sealed class CutOffStream(byte[] data, int after) : MemoryStream(data, writable: false)
        {
            public override bool CanSeek => false;

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position >= after)
                {
                    throw new IOException("Canned connection reset.");
                }

                // MemoryStream's array overload reads directly; its span overload would call back into this one.
                return base.Read(buffer, offset, (int)Math.Min(count, after - Position));
            }

            public override int Read(Span<byte> buffer)
            {
                var chunk = new byte[buffer.Length];
                var read = Read(chunk, 0, chunk.Length);
                chunk.AsSpan(0, read).CopyTo(buffer);
                return read;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Read(buffer.Span));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => Task.FromResult(Read(buffer, offset, count));
        }
    }
}
