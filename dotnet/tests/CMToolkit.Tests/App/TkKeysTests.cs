using Avalonia.Input;
using CMToolkit.App.Views;

namespace CMToolkit.Tests.App;

/// <summary>
/// T-13: the Tk key events the log pane's filter sees, against what the reference's Tk (8.6.15, Python 3.14.5) reported
/// for Ctrl+A and Ctrl+C posted to a Text widget under every Num Lock / Caps Lock / Shift combination (bug #88).
/// </summary>
public sealed class TkKeysTests
{
    /// <summary>The measured table: lock keys, Shift, then Tk's <c>event.state</c> and the keysym's case.</summary>
    public static TheoryData<bool, bool, bool, int, bool> Measured => new()
    {
        // numLock, capsLock, shift, state, upper-case keysym
        { false, false, false, 4, false },
        { false, false, true, 5, true },
        { false, true, false, 6, true },
        { false, true, true, 7, false },
        { true, false, false, 12, false },
        { true, false, true, 13, true },
        { true, true, false, 14, true },
        { true, true, true, 15, false },
    };

    [Theory]
    [Trait("Parity", "T-13")]
    [MemberData(nameof(Measured))]
    public void Ctrl_A_and_Ctrl_C_are_described_as_tk_described_them(bool numLock, bool capsLock, bool shift, int state, bool upper)
    {
        var modifiers = KeyModifiers.Control | (shift ? KeyModifiers.Shift : KeyModifiers.None);
        var locks = new LockKeys(capsLock, numLock, ScrollLock: false);

        Assert.Equal(new TkKeyEvent(state, upper ? "A" : "a"), TkKeys.Of(Key.A, modifiers, locks));
        Assert.Equal(new TkKeyEvent(state, upper ? "C" : "c"), TkKeys.Of(Key.C, modifiers, locks));
    }

    /// <summary>Bug #88: under every lock-key state, neither Ctrl+A nor Ctrl+C gets through block_text_input.</summary>
    [Theory]
    [Trait("Parity", "T-13")]
    [MemberData(nameof(Measured))]
    public void Bug_88_ctrl_a_and_ctrl_c_never_get_through(bool numLock, bool capsLock, bool shift, int state, bool upper)
    {
        _ = (state, upper);
        var modifiers = KeyModifiers.Control | (shift ? KeyModifiers.Shift : KeyModifiers.None);
        var locks = new LockKeys(capsLock, numLock, ScrollLock: false);

        Assert.False(TkKeys.BlockTextInputLetsThrough(TkKeys.Of(Key.A, modifiers, locks)));
        Assert.False(TkKeys.BlockTextInputLetsThrough(TkKeys.Of(Key.C, modifiers, locks)));
    }

    [Theory]
    [Trait("Parity", "T-13")]
    [InlineData(12, "A", true)]
    [InlineData(12, "C", true)]
    [InlineData(12, "a", false)]
    [InlineData(12, "c", false)]
    [InlineData(4, "A", false)]
    [InlineData(13, "A", false)]
    [InlineData(12, "B", false)]
    [InlineData(12, "Escape", false)]
    public void The_filter_is_the_references_exact_mask_and_substring_test(int state, string keysym, bool passes)
    {
        // `event.state == KEY_CTRL and event.keysym in "AC"`, with only the inputs it was written for.
        Assert.Equal(passes, TkKeys.BlockTextInputLetsThrough(new TkKeyEvent(state, keysym)));
    }

    [Fact]
    [Trait("Parity", "T-13")]
    public void Alt_and_scroll_lock_join_the_mask()
    {
        var key = TkKeys.Of(Key.A, KeyModifiers.Control | KeyModifiers.Alt, new LockKeys(CapsLock: false, NumLock: true, ScrollLock: true));

        Assert.Equal(TkKeys.Control | TkKeys.Alt | TkKeys.NumLock | TkKeys.ScrollLock, key.State);
    }
}
