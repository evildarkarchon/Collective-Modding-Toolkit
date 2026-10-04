using Avalonia;

namespace CMToolkit.App;

internal static class Program
{
    /// <summary>
    /// Entry point. Avalonia and its third-party code aren't safe to use before <see cref="AppBuilder"/> is
    /// configured, so nothing else runs here.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>The Avalonia configuration, shared with the designer previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
