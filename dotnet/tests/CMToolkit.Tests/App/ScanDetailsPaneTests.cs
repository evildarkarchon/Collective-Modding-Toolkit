using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Scanner details pane's failure paths. What it shows, opens and copies (SCN-R4..R7) is proven by the
/// <c>scanner-details</c> scenarios; these pin that a failed copy or folder open reaches the Error Window once.
/// </summary>
public sealed class ScanDetailsPaneTests
{
    private const string Url = "https://www.nexusmods.com/fallout4/mods/1";

    /// <summary>A file problem whose solution is a link, so the solution label copies on a right click.</summary>
    private static Problem LinkedProblem(PythonPath path)
        => Problem.ForFile(ProblemTypes.FileNotFound, path, new PythonPath(path.Name), mod: null, "Summary", "Solution", [Url]);

    [AvaloniaFact]
    public void A_copy_details_that_fails_reaches_the_error_window_once()
    {
        // A pane in no window has no clipboard, so the copy throws.
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var pane = new ScanDetailsPane(runtime, SystemHostEnvironment.Instance);
        pane.Show(LinkedProblem(new PythonPath("missing.esp")), usingStage: false);

        pane.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: The copy button is not in a window with a clipboard.",
            runtime.Errors.Window!.Text,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_url_copy_that_fails_reaches_the_error_window_once()
    {
        // A pane in no window has no clipboard, so the right click's copy throws.
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var pane = new ScanDetailsPane(runtime, SystemHostEnvironment.Instance);
        pane.Show(LinkedProblem(new PythonPath("missing.esp")), usingStage: false);
        var label = pane.SolutionLabel;

        label.RaiseEvent(new PointerPressedEventArgs(
            label,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
            label,
            default,
            timestamp: 0,
            new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
            KeyModifiers.None));

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.StartsWith(
            $"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: The details pane is not in a window with a clipboard.",
            runtime.Errors.Window!.Text,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_folder_open_that_fails_reaches_the_error_window_once()
    {
        // The click is synchronous, so its throw reaches Dispatcher.UnhandledException during dispatch (SHELL-12).
        using var temp = new TempDirectory();
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var pane = new ScanDetailsPane(runtime, SystemHostEnvironment.Instance)
        {
            OpenFolder = _ => throw new IOException("no shell"),
        };
        pane.Show(LinkedProblem(new PythonPath(temp.Path)), usingStage: false);
        var window = new Window { Content = pane, Width = 800, Height = 300 };
        window.Show();
        window.UpdateLayout();

        using (runtime.Errors.InstallGlobalHandlers())
        {
            MouseClick.On(pane.PathLabel);
            Dispatcher.UIThread.RunJobs();
        }

        // Counted by its message: with the global handlers in, a stray unobserved task from another test may land too.
        Assert.Single(logger.Entries, e => e.Message.Contains("System.IO.IOException: no shell", StringComparison.Ordinal));
        Assert.Contains($"{ErrorReporter.UiThreadHeader}\nSystem.IO.IOException: no shell", runtime.Errors.Window!.Text, StringComparison.Ordinal);
    }
}
