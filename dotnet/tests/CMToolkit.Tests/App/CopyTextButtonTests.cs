using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// <c>utils.copy_text_button</c>'s failure path. What a copy does (THR-3) is proven by the About and Scanner details
/// scenarios; this pins that a copy that fails reaches the Error Window once.
/// </summary>
public sealed class CopyTextButtonTests
{
    [AvaloniaFact]
    public void A_copy_that_fails_reaches_the_error_window_once()
    {
        // A button in no window has no clipboard, so its copy throws before its first await.
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var button = new Button { Content = "Copy" };
        CopyTextButton.Attach(button, "text", runtime);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: The copy button is not in a window with a clipboard.",
            runtime.Errors.Window!.Text,
            StringComparison.Ordinal);
    }
}
