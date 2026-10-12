using System.Runtime.InteropServices;
using Avalonia.Input;

namespace CMToolkit.App.Views;

/// <summary>The toggle state of the three lock keys, which Tk on Windows folds into a key event's modifier mask.</summary>
/// <param name="CapsLock">Caps Lock is on.</param>
/// <param name="NumLock">Num Lock is on.</param>
/// <param name="ScrollLock">Scroll Lock is on.</param>
public readonly partial record struct LockKeys(bool CapsLock, bool NumLock, bool ScrollLock)
{
    private const int VkCapital = 0x14;
    private const int VkNumLock = 0x90;
    private const int VkScroll = 0x91;

    /// <summary>
    /// The lock keys as this thread's keyboard state has them (<c>GetKeyState</c>'s toggle bit), which is what Tk reads
    /// while handling the key message. All off where there is no Win32 keyboard state.
    /// </summary>
    public static LockKeys Read()
        => OperatingSystem.IsWindows()
            ? new LockKeys(Toggled(VkCapital), Toggled(VkNumLock), Toggled(VkScroll))
            : default;

    private static bool Toggled(int virtualKey) => (GetKeyState(virtualKey) & 1) != 0;

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);
}

/// <summary>The two fields of a Tk key event that <c>utils.block_text_input</c> reads.</summary>
/// <param name="State"><c>event.state</c>: Tk's modifier mask.</param>
/// <param name="Keysym"><c>event.keysym</c>: for a letter, the letter in the case the keyboard state gives it.</param>
public readonly record struct TkKeyEvent(int State, string Keysym);

/// <summary>
/// How Tk 8.6 on Windows describes a key press, and the log pane's key filter that reads it (§9.5, T-13).
/// </summary>
/// <remarks>
/// Measured with the reference's Tk (8.6.15 under Python 3.14.5) by posting Ctrl+A and Ctrl+C to a Text widget under
/// every Num Lock / Caps Lock / Shift combination: the mask is Shift 1, Caps Lock 2, Control 4, Num Lock 8, Alt 16 and
/// Scroll Lock 32 (tkWinX.c), and a letter's keysym is upper case exactly when one of Shift and Caps Lock is on. Held
/// mouse buttons (256 and up) are part of Tk's mask too; a key event here can't see them, so they are left out.
/// </remarks>
public static class TkKeys
{
    /// <summary><c>ShiftMask</c>.</summary>
    public const int Shift = 0x1;

    /// <summary><c>LockMask</c>: Caps Lock on.</summary>
    public const int CapsLock = 0x2;

    /// <summary><c>ControlMask</c>.</summary>
    public const int Control = 0x4;

    /// <summary><c>Mod1Mask</c>, which Tk on Windows sets while Num Lock is on.</summary>
    public const int NumLock = 0x8;

    /// <summary><c>Mod2Mask</c>, Tk on Windows' Alt.</summary>
    public const int Alt = 0x10;

    /// <summary><c>Mod3Mask</c>, which Tk on Windows sets while Scroll Lock is on.</summary>
    public const int ScrollLock = 0x20;

    /// <summary><c>globals.KEY_CTRL</c>: the one mask <c>block_text_input</c> lets a key through with.</summary>
    public const int KeyCtrl = 12;

    /// <summary>The key event Tk would deliver for <paramref name="key"/> pressed with these modifiers and lock keys.</summary>
    public static TkKeyEvent Of(Key key, KeyModifiers modifiers, LockKeys locks)
    {
        var state = 0;
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (shift)
        {
            state |= Shift;
        }

        if (locks.CapsLock)
        {
            state |= CapsLock;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            state |= Control;
        }

        if (locks.NumLock)
        {
            state |= NumLock;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            state |= Alt;
        }

        if (locks.ScrollLock)
        {
            state |= ScrollLock;
        }

        return new TkKeyEvent(state, Keysym(key, upper: shift != locks.CapsLock));
    }

    /// <summary>
    /// <c>block_text_input</c>'s test for letting a key through: the mask exactly <see cref="KeyCtrl"/> (Control with
    /// Num Lock on and nothing else) and the keysym a substring of <c>"AC"</c>. Under that mask a letter's keysym is
    /// lower case, so on Windows no key ever passes: Ctrl+A and Ctrl+C are blocked like every other key (bug #88,
    /// reproduced, not fixed).
    /// </summary>
    public static bool BlockTextInputLetsThrough(TkKeyEvent key)
        => key.State == KeyCtrl && "AC".Contains(key.Keysym, StringComparison.Ordinal);

    /// <summary>
    /// A letter's keysym in the given case; any other key gets a name of its own, which is never a substring of
    /// <c>"AC"</c> (nor empty), as no Tk keysym for those keys is.
    /// </summary>
    private static string Keysym(Key key, bool upper)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            var letter = (char)('a' + (key - Key.A));
            return (upper ? char.ToUpperInvariant(letter) : letter).ToString();
        }

        return "Key" + key;
    }
}
