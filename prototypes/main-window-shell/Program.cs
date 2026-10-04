using Avalonia;

namespace CmtShellPrototype;

internal static class Program
{
    /// <summary>
    /// PROTOTYPE entry point. Accepts <c>--variant A|B|C</c>, <c>--scenario baseline|problems</c> and <c>--open &lt;dialog&gt;</c> so a
    /// given look is reload-stable and can be screenshotted from a script.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--variant":
                    PrototypeState.Variant = Variants.Parse(args[i + 1]);
                    break;
                case "--open":
                    PrototypeState.OpenOnStart = args[i + 1];
                    break;
                case "--textmode":
                    PrototypeState.TextRenderingMode = Enum.Parse<Avalonia.Media.TextRenderingMode>(args[i + 1], ignoreCase: true);
                    break;
                case "--scenario":
                    PrototypeState.Scenario = args[i + 1] == "problems" ? Scenario.Problems : Scenario.Baseline;
                    break;
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration; also used by the previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
