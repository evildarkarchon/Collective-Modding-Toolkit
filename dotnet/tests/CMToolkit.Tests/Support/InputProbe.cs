using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using CMToolkit.App.Runtime;

namespace CMToolkit.Tests.Support;

/// <summary>
/// A window attached to an <see cref="InputBlocker"/>, with a button that counts clicks and a focused text box, so
/// tests can push real headless input at it and see what got through. Escape and user-close requests are counted
/// rather than acted on. The button is not focusable, so clicking it leaves the text box focused.
/// </summary>
public sealed class InputProbe
{
    private readonly Button _button;
    private readonly TextBox _textBox;

    private InputProbe(Window window, Button button, TextBox textBox)
    {
        Window = window;
        _button = button;
        _textBox = textBox;
    }

    public Window Window { get; }

    public int Clicks { get; private set; }

    public int Escapes { get; private set; }

    public int CloseRequests { get; private set; }

    public string Text => _textBox.Text ?? "";

    /// <summary>Shows a 300×200 probe window and attaches it to <paramref name="runtime"/>'s input block.</summary>
    public static InputProbe Show(AppRuntime runtime)
    {
        var button = new Button { Content = "Click", Height = 50, Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var textBox = new TextBox();
        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = new StackPanel { Children = { button, textBox } },
        };
        var probe = new InputProbe(window, button, textBox);
        button.Click += (_, _) => probe.Clicks++;
        runtime.Input.Attach(window, onEscape: () => probe.Escapes++, onCloseRequest: () => probe.CloseRequests++);
        window.Show();
        textBox.Focus();
        return probe;
    }

    /// <summary>Presses and releases the left button over the middle of the button.</summary>
    public void ClickButton()
    {
        var centre = _button.TranslatePoint(new Point(_button.Bounds.Width / 2, _button.Bounds.Height / 2), Window)!.Value;
        Window.MouseDown(centre, MouseButton.Left);
        Window.MouseUp(centre, MouseButton.Left);
    }

    /// <summary>Types <paramref name="text"/> into whatever has focus (the text box, unless something moved it).</summary>
    public void Type(string text) => Window.KeyTextInput(text);

    /// <summary>Presses and releases Escape.</summary>
    public void PressEscape()
    {
        Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    }
}
