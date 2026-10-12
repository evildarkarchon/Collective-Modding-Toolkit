using Avalonia.Input;
using CMToolkit.App.Views;
using CMToolkit.Tests.Parity;

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

    /// <summary>
    /// Bug #88, fixed (ADR-0006): under every measured lock-key and Shift state, Ctrl+A and Ctrl+C get through the
    /// filter. The reference's exact-mask test let neither through in any of them.
    /// </summary>
    [Theory]
    [Trait("Parity", "T-13"), Deviation(88, "Ctrl+A and Ctrl+C work in the log pane whatever the lock keys and Shift, and Escape still closes its window.")]
    [MemberData(nameof(Measured))]
    public void Ctrl_a_and_ctrl_c_get_through_under_every_lock_key_state(bool numLock, bool capsLock, bool shift, int state, bool upper)
    {
        _ = (state, upper);
        var modifiers = KeyModifiers.Control | (shift ? KeyModifiers.Shift : KeyModifiers.None);
        var locks = new LockKeys(capsLock, numLock, ScrollLock: false);

        Assert.True(TkKeys.BlockTextInputLetsThrough(TkKeys.Of(Key.A, modifiers, locks)));
        Assert.True(TkKeys.BlockTextInputLetsThrough(TkKeys.Of(Key.C, modifiers, locks)));
    }

    /// <summary>
    /// Bug #88, fixed (ADR-0006): the filter tests Control as a bit (lock keys and Shift ignored) and the keysym in
    /// either case, and lets Escape through, where the reference matched the mask exactly against <c>"AC"</c>.
    /// </summary>
    [Theory]
    [Trait("Parity", "T-13"), Deviation(88, "Ctrl+A and Ctrl+C work in the log pane whatever the lock keys and Shift, and Escape still closes its window.")]
    [InlineData(12, "A", true)]
    [InlineData(12, "C", true)]
    [InlineData(12, "a", true)]
    [InlineData(12, "c", true)]
    [InlineData(4, "a", true)]
    [InlineData(5, "A", true)]
    [InlineData(0x4 | 0x20, "c", true)]
    [InlineData(0, "a", false)]
    [InlineData(1, "A", false)]
    [InlineData(8, "c", false)]
    [InlineData(12, "B", false)]
    [InlineData(4, "AC", false)]
    [InlineData(4, "", false)]
    [InlineData(0, "Escape", true)]
    [InlineData(12, "Escape", true)]
    [InlineData(0, "Delete", false)]
    [InlineData(0, "BackSpace", false)]
    public void The_filter_lets_ctrl_a_ctrl_c_and_escape_through(int state, string keysym, bool passes)
    {
        Assert.Equal(passes, TkKeys.BlockTextInputLetsThrough(new TkKeyEvent(state, keysym)));
    }

    [Fact]
    [Trait("Parity", "T-13")]
    public void Escape_is_described_by_its_tk_keysym()
    {
        Assert.Equal(new TkKeyEvent(0, "Escape"), TkKeys.Of(Key.Escape, KeyModifiers.None, default));
    }

    [Fact]
    [Trait("Parity", "T-13")]
    public void Alt_and_scroll_lock_join_the_mask()
    {
        var key = TkKeys.Of(Key.A, KeyModifiers.Control | KeyModifiers.Alt, new LockKeys(CapsLock: false, NumLock: true, ScrollLock: true));

        Assert.Equal(TkKeys.Control | TkKeys.Alt | TkKeys.NumLock | TkKeys.ScrollLock, key.State);
    }
}
