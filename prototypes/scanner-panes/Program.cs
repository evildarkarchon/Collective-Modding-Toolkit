using Avalonia;

namespace CmtPanesPrototype;

internal static class Program
{
    /// <summary>
    /// PROTOTYPE entry point. <c>--variant A|B|C</c>, <c>--pick &lt;group text&gt;</c> (select a result on start),
    /// <c>--x/--y</c> (initial physical position) and <c>--selftest [log path]</c>, so every state is reload-stable and
    /// scriptable from capture.ps1 / drag.ps1.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        int? x = null, y = null;
        for (var i = 0; i < args.Length; i++)
        {
            var next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--variant" when next is not null:
                    PrototypeState.Variant = Enum.TryParse<Variant>(next, ignoreCase: true, out var v) ? v : Variant.A;
                    break;
                case "--pick" when next is not null:
                    PrototypeState.PickOnStart = next;
                    break;
                case "--x" when next is not null:
                    x = int.Parse(next);
                    break;
                case "--y" when next is not null:
                    y = int.Parse(next);
                    break;
                case "--selftest":
                    PrototypeState.SelfTest = true;
                    if (next is not null && !next.StartsWith("--", StringComparison.Ordinal))
                    {
                        PrototypeState.SelfTestLog = Path.GetFullPath(next);
                    }

                    break;
            }
        }

        if (x is not null && y is not null)
        {
            PrototypeState.StartPosition = new PixelPoint(x.Value, y.Value);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration; also used by the previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
