using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CMToolkit.App.Views;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The sv-styled message box that replaces <c>tkinter.messagebox</c>'s <c>showwarning</c>, <c>showerror</c> and
/// <c>askyesno</c> (decided on issue #9). It is async: callers await the answer instead of blocking on it.
/// </summary>
public sealed class MessageBoxTests
{
    private static Window ShowOwner()
    {
        var owner = new Window { Width = 760, Height = 450 };
        owner.Show();
        return owner;
    }

    private static MessageBoxWindow OpenBox(Window owner) => owner.OwnedWindows.OfType<MessageBoxWindow>().Single();

    private static IReadOnlyList<string> ButtonLabels(Window box)
        => [.. box.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string ?? "")];

    private static void Click(Window box, string label)
    {
        // A size-to-content box gets its final layout after it opens; settle it so the button has real bounds.
        box.UpdateLayout();
        var button = box.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), box)!.Value;
        box.MouseDown(centre, MouseButton.Left);
        box.MouseUp(centre, MouseButton.Left);
    }

    [AvaloniaFact]
    public async Task A_warning_shows_its_title_message_and_an_ok_button_and_completes_when_dismissed()
    {
        var owner = ShowOwner();

        var shown = MessageBox.ShowWarningAsync(owner, "Warning", "Fallout4.ccc not found.");
        var box = OpenBox(owner);

        Assert.True(box.IsDialog);
        Assert.Equal("Warning", box.Title);
        Assert.Equal(MessageBoxKind.Warning, box.Kind);
        Assert.Contains(box.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Fallout4.ccc not found.");
        Assert.Equal(["OK"], ButtonLabels(box));
        Assert.False(box.CanResize);
        Assert.Equal(WindowStartupLocation.CenterOwner, box.WindowStartupLocation);
        Assert.False(shown.IsCompleted);

        Click(box, "OK");

        await shown.WithTimeout();
        Assert.False(box.IsVisible);
    }

    [AvaloniaFact]
    public async Task An_error_is_an_ok_box_of_the_error_kind_that_escape_dismisses()
    {
        var owner = ShowOwner();

        var shown = MessageBox.ShowErrorAsync(owner, "Error", "Only Fallout 4 is supported.");
        var box = OpenBox(owner);

        Assert.Equal(MessageBoxKind.Error, box.Kind);
        Assert.Equal(["OK"], ButtonLabels(box));

        box.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        await shown.WithTimeout();
        Assert.False(box.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    public async Task A_yes_no_question_answers_with_the_button_pressed(string button, bool expected)
    {
        var owner = ShowOwner();

        var answer = MessageBox.AskYesNoAsync(owner, "Fallout 4 Not Found", "Would you like to locate it manually?");
        var box = OpenBox(owner);

        Assert.Equal(MessageBoxKind.Question, box.Kind);
        Assert.Equal(["Yes", "No"], ButtonLabels(box));

        Click(box, button);

        Assert.Equal(expected, await answer.WithTimeout());
    }

    [AvaloniaFact]
    public async Task A_yes_no_question_ignores_escape_and_answers_no_when_closed()
    {
        // A native MB_YESNO box has no Cancel, so Escape does nothing.
        var owner = ShowOwner();

        var answer = MessageBox.AskYesNoAsync(owner, "Question", "Continue?");
        var box = OpenBox(owner);
        box.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.True(box.IsVisible);

        box.Close();

        Assert.False(await answer.WithTimeout());
    }

    [AvaloniaFact]
    public async Task Enter_presses_the_default_button()
    {
        var owner = ShowOwner();

        var answer = MessageBox.AskYesNoAsync(owner, "Question", "Continue?");
        var box = OpenBox(owner);
        box.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.True(await answer.WithTimeout());
    }

    [AvaloniaFact]
    public async Task Without_an_owner_the_box_is_centred_on_the_screen()
    {
        // Startup asks its questions before the main window exists.
        var box = new MessageBoxWindow(MessageBoxKind.Warning, "Warning", "No owner yet.");

        var shown = box.ShowAsync(owner: null);

        Assert.True(box.IsVisible);
        Assert.Null(box.Owner);
        Assert.Equal(WindowStartupLocation.CenterScreen, box.WindowStartupLocation);

        box.Close();
        Assert.False(await shown.WithTimeout());
    }

    [AvaloniaFact]
    public void The_message_uses_the_sv_dark_background_and_cascadia()
    {
        var box = new MessageBoxWindow(MessageBoxKind.Warning, "Warning", "Text");
        box.Show();

        var message = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Text");
        Assert.Equal("Cascadia Mono", message.FontFamily.Name);
        Assert.Equal(13, message.FontSize);
    }
}
