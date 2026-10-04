using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(CMToolkit.Tests.Support.TestAppBuilder))]

namespace CMToolkit.Tests.Support;

/// <summary>
/// The Avalonia app that [AvaloniaFact] tests run in: the real <see cref="CMToolkit.App.App"/> (theme, palette and
/// styles included) on the headless platform. Skia does the drawing rather than the headless stub, so text is shaped
/// with the real embedded font and rendered frames can be captured.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<CMToolkit.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
