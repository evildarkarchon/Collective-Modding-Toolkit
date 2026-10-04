using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CMToolkit.App.Runtime;

namespace CMToolkit.App.Views;

/// <summary>
/// The modal base (<c>modal_window.ModalWindow</c>, MOD-1): a fixed-size dialog owned by the window that opened it,
/// centred on the screen and application-modal. Escape and a user close close it unless <see cref="ProcessingData"/>
/// is set. Its input is under the <see cref="InputBlocker"/>, like the main window's.
/// </summary>
/// <remarks>
/// <see cref="ShowModalAsync"/> is Avalonia's <c>ShowDialog</c>: the owner, and through it every window below it in
/// the stack, takes no input until the modal closes, and closing it hands input back to the window that opened it, as
/// Tk's <c>grab_set</c> and the <c>previous_grabber</c> restore did. The Error Window stays live; it isn't in that stack.
/// </remarks>
public abstract class ModalWindow : Window
{
    /// <param name="runtime">The app runtime; the modal attaches itself to its input block.</param>
    /// <param name="title">The window title.</param>
    /// <param name="width">The client width, in Tk pixels (DIPs).</param>
    /// <param name="height">The client height.</param>
    protected ModalWindow(AppRuntime runtime, string title, double width, double height)
    {
        Runtime = runtime;
        Title = title;
        Icon = AppIcon.Load();
        Width = width;
        Height = height;
        // wm_resizable(False, False) and -fullscreen false. A transient Toplevel has no minimise box on Windows.
        CanResize = false;
        CanMaximize = false;
        CanMinimize = false;
        ShowInTaskbar = false;
        // Centred on the screen, not on the owner: (screen - size) // 2, like the main window.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        runtime.Input.Attach(this, onEscape: RequestClose, onCloseRequest: RequestClose);
    }

    /// <summary>The app runtime this modal belongs to.</summary>
    protected AppRuntime Runtime { get; }

    /// <summary>
    /// The close guard. While set, Escape, the title-bar close and <see cref="RequestClose"/> do nothing. Closing the
    /// owner still closes the modal, as <c>root.destroy()</c> took every Toplevel with it.
    /// </summary>
    public bool ProcessingData { get; set; }

    /// <summary>
    /// Shows the modal over <paramref name="owner"/>, the main window or another modal.
    /// </summary>
    /// <returns>A task that completes when the modal closes.</returns>
    public Task ShowModalAsync(Window owner) => ShowDialog(owner);

    /// <summary>Closes the modal unless <see cref="ProcessingData"/> is set (<c>_ungrab_and_destroy</c>).</summary>
    public void RequestClose()
    {
        if (!ProcessingData)
        {
            Close();
        }
    }

    /// <summary>
    /// Makes Space close the modal from anywhere in it, as the AboutWindow and TreeWindow <c>&lt;space&gt;</c> bindings do.
    /// </summary>
    protected void CloseOnSpace()
    {
        // handledEventsToo: a focused Button marks Space handled (it clicks on key-up), and Tk's Toplevel binding
        // fired whatever widget had focus. The input block is checked here explicitly because this handler can't rely
        // on running after the block's own handler.
        AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Space && !Runtime.Input.IsBlocking)
                {
                    e.Handled = true;
                    RequestClose();
                }
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // focus_set(): keyboard input goes to the modal as soon as it appears.
        Focus();
    }
}
