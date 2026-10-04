using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// <c>modal_window.ModalWindow</c> (MOD-1) and <c>modal_window.AboutWindow</c> (MOD-2). AboutWindow is the concrete
/// modal used to exercise the base.
/// </summary>
public sealed class ModalWindowTests
{
    private static (AppRuntime Runtime, Window Owner) ShowOwner()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var owner = new Window { Width = 760, Height = 450 };
        owner.Show();
        return (runtime, owner);
    }

    private static AboutWindow ShowAbout(AppRuntime runtime, Window owner, string text = "About text", int width = 500, int height = 300)
    {
        var about = new AboutWindow(runtime, width, height, "About Downgrading", text);
        _ = about.ShowModalAsync(owner);
        return about;
    }

    /// <summary>Presses and releases a key. Tk's bindings fire on the press, which may close the window first.</summary>
    private static void Press(Window window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        if (window.IsVisible)
        {
            window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        }
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-1")]
    public void A_modal_is_an_owned_fixed_size_dialog_centred_on_the_screen()
    {
        var (runtime, owner) = ShowOwner();
        var about = ShowAbout(runtime, owner, width: 500, height: 300);

        Assert.True(about.IsVisible);
        Assert.Same(owner, about.Owner);
        Assert.True(about.IsDialog);
        Assert.Equal("About Downgrading", about.Title);
        Assert.Equal(new Size(500, 300), about.ClientSize);
        Assert.False(about.CanResize);
        Assert.False(about.CanMaximize);
        Assert.False(about.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterScreen, about.WindowStartupLocation);
        Assert.NotNull(about.Icon);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-1")]
    public void Escape_and_a_user_close_close_it()
    {
        var (runtime, owner) = ShowOwner();
        var byEscape = ShowAbout(runtime, owner);
        Press(byEscape, Key.Escape, PhysicalKey.Escape);
        Assert.False(byEscape.IsVisible);

        var byCloseButton = ShowAbout(runtime, owner);
        UserClose.Request(byCloseButton);
        Assert.False(byCloseButton.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-1")]
    public void While_processing_data_neither_escape_nor_a_user_close_closes_it()
    {
        var (runtime, owner) = ShowOwner();
        var about = ShowAbout(runtime, owner);
        about.ProcessingData = true;

        Press(about, Key.Escape, PhysicalKey.Escape);
        UserClose.Request(about);
        about.RequestClose();

        Assert.True(about.IsVisible);

        about.ProcessingData = false;
        about.RequestClose();
        Assert.False(about.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-1")]
    public void Closing_the_owner_closes_it_even_while_processing_data()
    {
        // root.destroy() takes every Toplevel with it, whatever their own close guard says.
        var (runtime, owner) = ShowOwner();
        var about = ShowAbout(runtime, owner);
        about.ProcessingData = true;

        owner.Close();

        Assert.False(owner.IsVisible);
        Assert.False(about.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-1")]
    public void A_modal_opened_from_a_modal_is_owned_by_it_and_closing_it_returns_to_the_first()
    {
        var (runtime, owner) = ShowOwner();
        var first = ShowAbout(runtime, owner);
        var second = ShowAbout(runtime, first);

        Assert.Same(first, second.Owner);

        Press(second, Key.Escape, PhysicalKey.Escape);
        Assert.False(second.IsVisible);
        Assert.True(first.IsVisible);

        Press(first, Key.Escape, PhysicalKey.Escape);
        Assert.False(first.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_blocking_operation_blocks_an_open_modal_and_defers_its_escape()
    {
        var (runtime, owner) = ShowOwner();
        var about = ShowAbout(runtime, owner);
        var gate = new TaskCompletionSource();

        var operation = runtime.Operations.RunAsync("Patch All", OperationPhase.Blocking, () => gate.Task.Wait());
        Press(about, Key.Space, PhysicalKey.Space);
        Press(about, Key.Escape, PhysicalKey.Escape);
        Assert.True(about.IsVisible);

        gate.SetResult();
        await operation;
        Dispatcher.UIThread.RunJobs();

        Assert.False(about.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-2")]
    public void About_shows_its_text_at_10pt_left_justified_from_the_top_above_a_close_button()
    {
        var (runtime, owner) = ShowOwner();
        var about = ShowAbout(runtime, owner, "Line one\nLine two");

        var label = about.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Line one\nLine two");
        Assert.Equal(13, label.FontSize);
        Assert.Equal(TextAlignment.Left, label.TextAlignment);
        Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
        var top = label.TranslatePoint(default, about)!.Value.Y;
        Assert.Equal(10, top);

        var close = about.GetVisualDescendants().OfType<Button>().Single();
        Assert.Equal("Close", close.Content);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-2")]
    public void About_wraps_its_text_at_the_window_width_not_the_padded_label_width()
    {
        // wraplength=win_width while the label is 20 px narrower (padx=10). 24 Cascadia Mono 13 px characters are
        // 192 px: one line at Tk's 200 px wraplength, two lines if it wrapped at the label's 180 px.
        var (runtime, owner) = ShowOwner();
        var text = "0000000000 0000000000 00";
        var about = ShowAbout(runtime, owner, text, width: 200, height: 150);

        var label = about.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text);
        Assert.Equal(17, label.Bounds.Height);
        // anchor=N centres the 192 px block in the 180 px label, so it overhangs both sides equally.
        Assert.Equal(10 - 6, label.TranslatePoint(default, about)!.Value.X);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-2")]
    public void About_close_button_spans_the_window_inside_the_padding()
    {
        // ttk.Button(width=win_width // 2) is in characters, far wider than the window, so grid shrinks it to the
        // column: the full width less padx=10 on each side.
        var (runtime, owner) = ShowOwner();
        var about = ShowAbout(runtime, owner, width: 500, height: 300);

        var close = about.GetVisualDescendants().OfType<Button>().Single();
        Assert.Equal(480, close.Bounds.Width);
        Assert.Equal(HorizontalAlignment.Stretch, close.HorizontalAlignment);
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-2")]
    public void About_closes_on_space_and_on_its_close_button()
    {
        var (runtime, owner) = ShowOwner();
        var bySpace = ShowAbout(runtime, owner);
        Press(bySpace, Key.Space, PhysicalKey.Space);
        Assert.False(bySpace.IsVisible);

        var byButton = ShowAbout(runtime, owner);
        var close = byButton.GetVisualDescendants().OfType<Button>().Single();
        var centre = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), byButton)!.Value;
        byButton.MouseDown(centre, MouseButton.Left);
        byButton.MouseUp(centre, MouseButton.Left);
        Assert.False(byButton.IsVisible);
    }
}
