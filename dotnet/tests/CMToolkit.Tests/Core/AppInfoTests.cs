using System.Reflection;
using CMToolkit.Core;

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
    [Trait("Parity", "SHELL-6")]
    public void The_window_title_matches_the_reference_at_the_parity_baseline()
    {
        // cm_checker.py: wm_title(f"{APP_TITLE} v{APP_VERSION}"). Stays 0.6.2-dev until the cutover picks a version.
        Assert.Equal("Collective Modding Toolkit v0.6.2-dev", AppInfo.WindowTitle);
    }
}
