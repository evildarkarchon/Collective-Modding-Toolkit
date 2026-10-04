using System.Xml.Linq;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>What the app does at startup, before the shell is shown, and what ships beside the exe.</summary>
public sealed class StartupTests
{
    [Fact]
    [Trait("Parity", "SET-2")]
    public void Startup_reads_the_download_source_from_the_given_exe_folder()
    {
        using var appDir = new TempDirectory();
        appDir.WriteBytes(Path.Combine("assets", "download-source.txt"), "nexus"u8.ToArray());

        var vm = MainWindowViewModel.Load(appDir.Path);

        Assert.Equal(DownloadSource.Nexus, vm.DownloadSource.Source);
        Assert.Equal(DownloadSourceOutcome.Valid, vm.DownloadSource.Outcome);
    }

    [Fact]
    [Trait("Parity", "SET-2")]
    public void The_build_output_carries_a_valid_download_source_file_beside_the_exe()
    {
        // The App's build copies assets\download-source.txt next to the exe, and the test output inherits it. The
        // repo copy says "github", like the Reference Implementation's.
        var lookup = DownloadSourceFile.Read(AppContext.BaseDirectory);

        Assert.Equal(DownloadSourceOutcome.Valid, lookup.Outcome);
        Assert.Equal(DownloadSource.GitHub, lookup.Source);
    }

    [Fact]
    public void The_exe_is_named_cm_toolkit()
    {
        Assert.Equal("cm-toolkit", typeof(CMToolkit.App.App).Assembly.GetName().Name);
    }

    [Fact]
    public void The_manifest_runs_as_invoker_long_path_aware_from_win7_to_win11_with_no_dpi_entry()
    {
        var manifest = XDocument.Load(Path.Combine(RepoPaths.AppProject, "app.manifest"));
        var all = manifest.Descendants().ToList();

        Assert.Equal("asInvoker", all.Single(e => e.Name.LocalName == "requestedExecutionLevel").Attribute("level")?.Value);
        Assert.Equal("true", all.Single(e => e.Name.LocalName == "longPathAware").Value);
        Assert.Equal(
            [
                "{35138b9a-5d96-4fbd-8e2d-a2440225f93a}", // Windows 7
                "{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}", // Windows 8
                "{1f676c76-80e1-4239-95bb-83d0f6d0da78}", // Windows 8.1
                "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", // Windows 10 and 11
            ],
            all.Where(e => e.Name.LocalName == "supportedOS").Select(e => e.Attribute("Id")?.Value));
        // Avalonia sets per-monitor-v2 awareness at startup; a manifest entry would override it (issue #13).
        Assert.DoesNotContain(all, e => e.Name.LocalName is "dpiAware" or "dpiAwareness");
    }
}
