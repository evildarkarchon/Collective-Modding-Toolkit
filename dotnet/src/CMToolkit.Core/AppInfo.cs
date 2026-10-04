using System.Reflection;

namespace CMToolkit.Core;

/// <summary>The app's name and version (<c>globals.py</c> <c>APP_TITLE</c>/<c>APP_VERSION</c>).</summary>
public static class AppInfo
{
    /// <summary>The app's display name.</summary>
    public const string Name = "Collective Modding Toolkit";

    /// <summary>
    /// The single <c>&lt;Version&gt;</c> from <c>Directory.Build.props</c>, read back through
    /// <see cref="AssemblyInformationalVersionAttribute"/> so it keeps its PEP 440-style form (e.g. <c>0.6.2-dev</c>).
    /// Every project shares that props file, so Core's own assembly carries the same value as the exe.
    /// </summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("CMToolkit.Core has no AssemblyInformationalVersion attribute.");

    /// <summary>The main window title, <c>Collective Modding Toolkit v&lt;version&gt;</c> (SHELL-6).</summary>
    public static string WindowTitle => $"{Name} v{Version}";
}
