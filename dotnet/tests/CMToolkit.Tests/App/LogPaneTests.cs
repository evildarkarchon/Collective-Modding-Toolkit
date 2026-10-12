using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// The log pane (§9.5): what it writes to the app log (LOG-1), and its key blocking, which lets Ctrl+A and Ctrl+C
/// through whatever the lock keys (T-13; the reference's exact-mask test never did on Windows, bug #88, fixed per
/// ADR-0006). A blocking test first shows that a bare selectable text would have acted on the key, so the pane's
/// blocking is what stops it.
/// </summary>
public sealed class LogPaneTests
{
    /// <summary>Every lock-key state Tk folds into its modifier mask, with Caps Lock and Num Lock each on and off.</summary>
    public static TheoryData<bool, bool> LockStates => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [AvaloniaFact]
    [Trait("Parity", "LOG-1")]
    public void Bad_lines_are_logged_at_error_the_rest_at_info_unless_skipped()
    {
        var logger = new CapturingLogger();
        var pane = new LogPane(logger);

        pane.LogMessage(LogType.Bad, "bad");
        pane.LogMessage(LogType.Good, "good");
        pane.LogMessage(LogType.Info, "info");
        pane.LogMessage(LogType.Bad, "shown only", skipLogging: true);
        pane.Log(new LogLine(LogType.Info, "also shown only", WriteToLogFile: false));
        pane.Log(new LogLine(LogType.Bad, "from core"));

        Assert.Equal(
            [
                new LogEntry(LogLevel.Error, "bad"),
                new LogEntry(LogLevel.Information, "good"),
                new LogEntry(LogLevel.Information, "info"),
                new LogEntry(LogLevel.Error, "from core"),
            ],
            logger.Entries);
        Assert.Equal(12, pane.Runs.Count);
    }

    /// <summary>
    /// Bug #88, fixed (ADR-0006): Ctrl+A selects the whole pane, with or without Shift, whatever the lock keys. The
    /// reference's exact-mask filter blocked it under every lock-key state.
    /// </summary>
    [AvaloniaTheory]
    [Trait("Parity", "T-13"), Deviation(88, "Ctrl+A and Ctrl+C work in the log pane whatever the lock keys and Shift, and Escape still closes its window.")]
    [MemberData(nameof(LockStates))]
    public void Ctrl_a_selects_the_whole_pane_under_any_lock_key_state(bool capsLock, bool numLock)
    {
        foreach (var shift in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
        {
            var (window, pane) = ShowPane(new LockKeys(capsLock, numLock, ScrollLock: false));
            Assert.Equal(string.Empty, pane.SelectedText);

            Press(window, Key.A, RawInputModifiers.Control | shift);

            Assert.Equal("\U0001F4AD line one\n✅ line two\n", pane.SelectedText);
        }
    }

    /// <summary>
    /// Bug #88, fixed (ADR-0006): Ctrl+C copies the pane's selection to the clipboard, with or without Shift, whatever
    /// the lock keys. The reference's exact-mask filter blocked it under every lock-key state.
    /// </summary>
    [AvaloniaTheory]
    [Trait("Parity", "T-13"), Deviation(88, "Ctrl+A and Ctrl+C work in the log pane whatever the lock keys and Shift, and Escape still closes its window.")]
    [MemberData(nameof(LockStates))]
    public void Ctrl_c_copies_the_selection_under_any_lock_key_state(bool capsLock, bool numLock)
    {
        foreach (var shift in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
        {
            var (window, _) = ShowPane(new LockKeys(capsLock, numLock, ScrollLock: false));
            var text = window.GetLogicalDescendants().OfType<SelectableTextBlock>().Single();
            // After the 💭 emoji (two UTF-16 units) and its space: "line".
            text.SelectionStart = 3;
            text.SelectionEnd = 7;
            var clipboard = window.Clipboard!;
            clipboard.SetTextAsync("before").GetAwaiter().GetResult();

            Press(window, Key.C, RawInputModifiers.Control | shift);

            Assert.Equal("line", clipboard.TryGetTextAsync().GetAwaiter().GetResult());
        }
    }

    [AvaloniaFact]
    [Trait("Parity", "T-13")]
    public void Other_keys_are_still_blocked()
    {
        // Ctrl+Insert is the platform's other Copy gesture: a bare selectable text copies on it, the pane lets only
        // Ctrl+A, Ctrl+C and Escape through.
        var bare = ShowBare();
        bare.SelectionStart = 0;
        bare.SelectionEnd = 4;
        var clipboard = TopLevel.GetTopLevel(bare)!.Clipboard!;
        clipboard.SetTextAsync("before").GetAwaiter().GetResult();
        Press(bare, Key.Insert, RawInputModifiers.Control);
        Assert.Equal("line", clipboard.TryGetTextAsync().GetAwaiter().GetResult());

        var (window, _) = ShowPane(default);
        var text = window.GetLogicalDescendants().OfType<SelectableTextBlock>().Single();
        text.SelectionStart = 3;
        text.SelectionEnd = 7;
        clipboard = window.Clipboard!;
        clipboard.SetTextAsync("before").GetAwaiter().GetResult();
        Press(window, Key.Insert, RawInputModifiers.Control);

        Assert.Equal("before", clipboard.TryGetTextAsync().GetAwaiter().GetResult());
    }

    [AvaloniaFact]
    [Trait("Parity", "T-13")]
    public void A_click_gives_the_pane_keyboard_focus_and_it_has_no_context_menu()
    {
        var (_, pane) = ShowPane(default);
        var text = pane.GetLogicalDescendants().OfType<SelectableTextBlock>().Single();

        Assert.True(text.IsFocused);
        Assert.True(pane.HasKeyboardFocus);
        Assert.Null(text.ContextFlyout);
        Assert.Null(text.ContextMenu);
    }

    /// <summary>A shown window holding just a log pane with two lines, clicked so its text has focus.</summary>
    private static (Window Window, LogPane Pane) ShowPane(LockKeys locks)
    {
        var pane = new LogPane(new CapturingLogger()) { ReadLockKeys = () => locks };
        pane.LogMessage(LogType.Info, "line one");
        pane.LogMessage(LogType.Good, "line two");
        var window = new Window { Width = 400, Height = 200, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        MouseClick.On(window.GetLogicalDescendants().OfType<SelectableTextBlock>().Single());
        return (window, pane);
    }

    /// <summary>A shown window holding a bare, focused selectable text: what a key does without the pane's blocking.</summary>
    private static SelectableTextBlock ShowBare()
    {
        var text = new SelectableTextBlock { Text = "line one\nline two", Focusable = true };
        var window = new Window { Width = 400, Height = 200, Content = text };
        window.Show();
        text.Focus();
        Dispatcher.UIThread.RunJobs();
        return text;
    }

    private static void Press(Control target, Key key, RawInputModifiers modifiers)
    {
        var window = TopLevel.GetTopLevel(target) as Window ?? throw new InvalidOperationException("Not in a window.");
        var physical = key switch
        {
            Key.A => PhysicalKey.A,
            Key.C => PhysicalKey.C,
            Key.Insert => PhysicalKey.Insert,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
        window.KeyPress(key, modifiers, physical, key.ToString().ToLowerInvariant());
        window.KeyRelease(key, modifiers, physical, key.ToString().ToLowerInvariant());
        Dispatcher.UIThread.RunJobs();
    }
}
