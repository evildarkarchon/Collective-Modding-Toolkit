using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace CMToolkit.Tests.Support;

/// <summary>Real headless mouse clicks, which go through hit testing, capture and IsEnabled like a user's.</summary>
public static class MouseClick
{
    /// <summary>
    /// Presses and releases the left button over the centre of <paramref name="control"/>, which must be in a shown
    /// window, then runs the jobs the click queued.
    /// </summary>
    public static void On(Control control)
    {
        var window = TopLevel.GetTopLevel(control) as Window
            ?? throw new InvalidOperationException("The control is not in a window.");
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not laid out.");
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
