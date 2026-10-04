using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// Building the app before its main window is shown (SHELL-4), and what a failure there leaves (SHELL-13, as decided
/// on issue #18): PyInstaller's <c>Unhandled exception in script</c> dialog, no main window, exit code 1.
/// </summary>
public sealed class StartupSequenceTests
{
    [AvaloniaFact]
    [Trait("Parity", "SHELL-4")]
    public async Task A_successful_build_hands_back_the_main_window_unshown()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var built = new Window();

        var result = await AppStartup.BuildAsync(runtime, () => Task.FromResult(built));

        Assert.Same(built, result.Window);
        Assert.False(result.Window.IsVisible);
        Assert.False(result.Failed);
        Assert.Equal(0, result.ExitCode);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-13")]
    public async Task A_failed_build_shows_pyinstallers_dialog_instead_of_the_main_window_and_exits_with_1()
    {
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        Window? partlyBuilt = null;

        var result = await AppStartup.BuildAsync(runtime, async () =>
        {
            partlyBuilt = new Window();
            // The environment detection is Core work, run off the UI thread; it fails there.
            await Task.Run(() => throw new FileNotFoundException("ModOrganizer.ini not found"));
            return partlyBuilt;
        });

        Assert.True(result.Failed);
        Assert.Equal(1, result.ExitCode);
        var dialog = Assert.IsType<StartupFailureDialog>(result.Window);
        Assert.False(partlyBuilt!.IsVisible);
        Assert.Equal("Unhandled exception in script", dialog.Title);
        Assert.Contains(
            dialog.GetLogicalDescendants().OfType<TextBlock>(),
            t => t.Text == "Failed to execute script 'main' due to unhandled exception: ModOrganizer.ini not found");
        var trace = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
        Assert.True(trace.IsReadOnly);
        Assert.StartsWith("System.IO.FileNotFoundException: ModOrganizer.ini not found", trace.Text);

        // Logged as any unhandled error is, but the Error Window, a Toplevel of the app that never started, stays shut.
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.StartsWith(
            $"StdErr : {ErrorReporter.StartupHeader}\nSystem.IO.FileNotFoundException: ModOrganizer.ini not found",
            entry.Message);
        Assert.Null(runtime.Errors.Window);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-13")]
    public void The_startup_failure_dialog_closes_from_its_close_button()
    {
        var dialog = new StartupFailureDialog(new InvalidOperationException("boom"));
        dialog.Show();
        dialog.UpdateLayout();

        Assert.False(dialog.CanResize);
        var close = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close"));
        var centre = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), dialog)!.Value;
        dialog.MouseDown(centre, MouseButton.Left);
        dialog.MouseUp(centre, MouseButton.Left);

        Assert.False(dialog.IsVisible);
    }
}

