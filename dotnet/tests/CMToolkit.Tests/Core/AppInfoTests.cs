using System.Reflection;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Core;

public sealed class AppInfoTests
{
    [Fact]
    public void The_version_is_the_informational_version_without_a_source_revision_suffix()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Equal(informational, AppInfo.Version);
        // IncludeSourceRevisionInInformationalVersion=false: no "+<commit sha>" leaks into the title or the log.
        Assert.DoesNotContain('+', AppInfo.Version);
    }

    [Fact]
    [Trait("Parity", "SHELL-6"), Deviation(71, "A deliberate change at the Cutover, not a reference bug: the version is 0.7.0, the first C# release, where the reference ran as 0.6.2-dev.")]
    public void The_window_title_is_the_app_name_and_the_first_csharp_release()
    {
        // cm_checker.py: wm_title(f"{APP_TITLE} v{APP_VERSION}"). The reference read 0.6.2-dev; the Cutover (#71) made
        // the first C# release 0.7.0, which compares greater than 0.6.2 under PEP 440.
        Assert.Equal("Collective Modding Toolkit v0.7.0", AppInfo.WindowTitle);
    }
}
