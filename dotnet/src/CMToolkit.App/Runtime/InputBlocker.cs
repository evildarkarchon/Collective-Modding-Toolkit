using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The input block that stands in for the Reference Implementation's frozen Tk UI thread (ADR-0003). While any block
/// is held, every attached window discards keyboard and pointer input, but still paints, moves and minimises. A user's
/// Close or Escape is not discarded: it is deferred, then honoured once the last block ends, the way Windows queued it
/// for the frozen Tk loop.
/// </summary>
/// <remarks>
/// <para>Attached windows are the main window and its modals (which attach themselves, stating their Escape and close
/// behaviour), the Error Window, and every other window owned, directly or through other windows, by an attached one,
/// such as the Scanner's docked panes. Those owned windows are attached automatically when they open, so a new owned
/// window is blocked without doing anything: every Toplevel froze with the Tk thread. A window that must stay live
/// opts out with <see cref="IExemptFromInputBlock"/>.</para>
/// <para>Attached windows also route every Escape and user close through here, so each window states its Escape and
/// close behaviour once, in <see cref="Attach(Window, Action, Action)"/>, and the blocked and live cases can't drift
/// apart. UI thread only.</para>
/// </remarks>
public sealed class InputBlocker
{
    /// <summary>
    /// Which blocker each attached window is under. Static because the hook that finds an opening window's owner is a
    /// class handler, shared by every runtime; weak, so a closed window isn't kept alive by it.
    /// </summary>
    private static readonly ConditionalWeakTable<Window, InputBlocker> AttachedWindows = new();

    /// <summary>
    /// The input events a block discards, besides <see cref="InputElement.KeyDownEvent"/>. KeyDown has its own handler,
    /// which tells Escape (deferred) from every other key (discarded) in one place: Avalonia doesn't promise to run two
    /// handlers on the same element in the order they were added, so a separate Escape handler could find the key
    /// already discarded.
    /// </summary>
    private static readonly RoutedEvent[] BlockedEvents =
    [
        InputElement.KeyUpEvent,
        InputElement.TextInputEvent,
        InputElement.PointerPressedEvent,
        InputElement.PointerReleasedEvent,
        InputElement.PointerMovedEvent,
        InputElement.PointerWheelChangedEvent,
    ];

    /// <summary>Deferred Escapes and closes, oldest first, as Windows would have queued them.</summary>
    private readonly List<(Window Window, Action Action)> _deferred = [];

    private int _depth;

    /// <summary>Hooks every window's opening once, so owned windows are attached however and wherever they are shown.</summary>
    static InputBlocker()
    {
        // WindowOpenedEvent is raised before Show sets the window's Owner; its Opened event, raised just after, sees it.
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            window.Opened += OnOpened;

            void OnOpened(object? sender, EventArgs e)
            {
                window.Opened -= OnOpened;
                AttachToOwner(window);
            }
        });
    }

    /// <summary>Whether a block is held.</summary>
    public bool IsBlocking => _depth > 0;

    /// <summary>
    /// Holds a block until the returned handle is disposed. Blocks nest: input comes back, and deferred Close and
    /// Escape are honoured, only when the last one ends.
    /// </summary>
    public IDisposable Block()
    {
        Dispatcher.UIThread.VerifyAccess();
        _depth++;
        return new Handle(this);
    }

    /// <summary>
    /// Puts <paramref name="window"/> under the block, and takes over its Escape key and its user close (the title-bar
    /// button, Alt+F4).
    /// </summary>
    /// <param name="window">The main window or a modal. Detaches itself when it closes.</param>
    /// <param name="onEscape">What Escape does, anywhere in the window. Deferred while blocked.</param>
    /// <param name="onCloseRequest">
    /// What a user close does instead of closing (it cancels the close and calls this, which closes the window itself
    /// when its close guard allows). Deferred while blocked. Programmatic closes, and closes because the owner or the
    /// app is closing, are never intercepted.
    /// </param>
    public void Attach(Window window, Action onEscape, Action onCloseRequest) => AttachCore(window, onEscape, onCloseRequest);

    /// <summary>
    /// Puts <paramref name="window"/>, which has no Escape or close behaviour of its own, under the block: a plain Tk
    /// Toplevel's. While nothing blocks, Escape reaches its controls and a user close closes it as usual. While blocked,
    /// Escape is discarded with the other keys (the window has no Escape action to defer), and a user close is deferred,
    /// then honoured by closing the window.
    /// </summary>
    /// <param name="window">A window no owner chain attaches, such as the Error Window. Detaches itself when it closes.</param>
    public void Attach(Window window) => AttachCore(window, onEscape: null, onCloseRequest: null);

    /// <summary>
    /// Attaches a window that has just opened to the blocker of its nearest attached owner, unless it is attached
    /// already (the main window and modals attach in their constructors) or exempt.
    /// </summary>
    private static void AttachToOwner(Window window)
    {
        if (window is IExemptFromInputBlock || AttachedWindows.TryGetValue(window, out _))
        {
            return;
        }

        for (var owner = window.Owner as Window; owner is not null; owner = owner.Owner as Window)
        {
            if (AttachedWindows.TryGetValue(owner, out var blocker))
            {
                blocker.Attach(window);
                return;
            }
        }
    }

    /// <summary>
    /// <see cref="Attach(Window, Action, Action)"/>, where a null <paramref name="onEscape"/> leaves Escape to the
    /// window's controls (discarded while blocked) and a null <paramref name="onCloseRequest"/> leaves a user close
    /// alone while nothing blocks, and defers it as <see cref="Window.Close()"/> while blocked.
    /// </summary>
    /// <exception cref="InvalidOperationException">The window is already attached.</exception>
    private void AttachCore(Window window, Action? onEscape, Action? onCloseRequest)
    {
        // A second set of handlers would run each deferred action twice.
        if (!AttachedWindows.TryAdd(window, this))
        {
            throw new InvalidOperationException("The window is already under an input block.");
        }

        // Tunnel handlers on the window run before any control sees the event, so marking it handled here stops
        // buttons, text boxes and tab headers alike.
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        foreach (var routedEvent in BlockedEvents)
        {
            window.AddHandler(routedEvent, Discard, RoutingStrategies.Tunnel);
        }

        window.Closing += OnClosing;
        window.Closed += (_, _) => _deferred.RemoveAll(d => d.Window == window);

        void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && onEscape is not null)
            {
                e.Handled = true;
                Run(window, onEscape);
            }
            else
            {
                Discard(sender, e);
            }
        }

        void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (e.IsProgrammatic || e.CloseReason != WindowCloseReason.WindowClosing)
            {
                return;
            }

            if (onCloseRequest is not null)
            {
                e.Cancel = true;
                Run(window, onCloseRequest);
            }
            else if (IsBlocking)
            {
                // Honoured as a programmatic close, which this handler lets through.
                e.Cancel = true;
                Run(window, window.Close);
            }
        }
    }

    private void Discard(object? sender, RoutedEventArgs e)
    {
        if (IsBlocking)
        {
            e.Handled = true;
        }
    }

    /// <summary>Runs <paramref name="action"/> now, or defers it while blocked.</summary>
    private void Run(Window window, Action action)
    {
        if (IsBlocking)
        {
            _deferred.Add((window, action));
        }
        else
        {
            action();
        }
    }

    private void Release()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (--_depth > 0 || _deferred.Count == 0)
        {
            return;
        }

        // Posted, not run inline: the code that awaited the blocking work (building a tab, say) runs to its next await
        // first, as it ran to completion inside the single Tk callback before Windows delivered the queued messages.
        // If that code takes a new block straight away, the deferred input waits for that one too.
        Dispatcher.UIThread.Post(ReplayDeferred);
    }

    private void ReplayDeferred()
    {
        while (!IsBlocking && _deferred.Count > 0)
        {
            var (_, action) = _deferred[0];
            _deferred.RemoveAt(0);
            action();
        }
    }

    /// <summary>One held block. Disposing it twice releases it once.</summary>
    private sealed class Handle(InputBlocker owner) : IDisposable
    {
        private InputBlocker? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }
}
