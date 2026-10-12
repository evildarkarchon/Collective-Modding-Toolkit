using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.Core;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Views;

/// <summary>
/// <c>logger.Logger</c>, the Downgrader's and Archive Patcher's log pane (§9.5): a read-only, word-wrapped text 8 lines
/// high at the small size, with a vertical scrollbar. Each message is one line, its emoji coloured by type and its text
/// in the default colour, and the pane scrolls to the end after each.
/// </summary>
/// <remarks>
/// Read-only as the reference's Text was: every key pressed in it goes through <c>block_text_input</c>
/// (<see cref="TkKeys.BlockTextInputLetsThrough"/>), which means to let Ctrl+A and Ctrl+C through but, by T-13's exact
/// modifier match, never does on Windows (bug #88), so no key reaches the text. The mouse still selects, and a click
/// gives the text keyboard focus, as Tk's Text class binding does; while it has focus the window's Escape does nothing
/// (<see cref="HasKeyboardFocus"/>), because the binding's <c>break</c> also stopped the toplevel's. There is no context
/// menu, as a Tk Text has none, so nothing in the pane can be copied.
/// </remarks>
public sealed class LogPane : UserControl
{
    /// <summary>The pane's height in lines (<c>Text(height=8)</c>).</summary>
    public const int Lines = 8;

    /// <summary>The width of the column the vertical <c>ttk.Scrollbar</c> takes beside the text, measured from sv_ttk.</summary>
    private const double ScrollBarWidth = 24;

    private static readonly IReadOnlyDictionary<LogType, string> Emoji = new Dictionary<LogType, string>
    {
        [LogType.Bad] = "\u274C ",
        [LogType.Good] = "\u2705 ",
        [LogType.Info] = "\U0001F4AD ",
    };

    private static readonly IReadOnlyDictionary<LogType, string> EmojiColourKey = new Dictionary<LogType, string>
    {
        [LogType.Bad] = "CmtBad",
        [LogType.Good] = "CmtGood",
        [LogType.Info] = "CmtInfo",
    };

    /// <summary>
    /// The emoji's font: Cascadia Mono (which has none of the three), falling back to Segoe UI Symbol's monochrome
    /// glyphs. Tk draws the fallback glyph through GDI, which has no colour fonts, so it is a one-colour outline in the
    /// tag's colour; Skia would pick Segoe UI Emoji's colour glyph, which ignores the foreground.
    /// </summary>
    private static readonly FontFamily EmojiFont = new("avares://cm-toolkit/Assets/Fonts#Cascadia Mono, Segoe UI Symbol");

    private readonly ILogger _logger;
    private readonly SelectableTextBlock _text;
    private readonly ScrollViewer _scroller;

    /// <param name="logger">The app log, which every message not marked <c>skip_logging</c> is also written to.</param>
    public LogPane(ILogger logger)
    {
        _logger = logger;
        _text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Inlines = [],
        };
        _text.Classes.Add("small");
        // A classic Tk Text, not a ttk widget: tk_setPalette gives it sv_ttk's foreground, not the ttk labels' grey.
        _text.Bind(SelectableTextBlock.ForegroundProperty, _text.GetResourceObservable("SvFg"));
        // A Tk Text takes keyboard focus when clicked (text.tcl's TextButton1 ends with `focus $w`), which matters
        // because a focused pane swallows the window's Escape. handledEventsToo: the text's own selection handling
        // marks the press handled.
        _text.Focusable = true;
        // Hit-testable below the last line too, as the whole of a Tk Text takes the click.
        _text.Background = Brushes.Transparent;
        _text.AddHandler(PointerPressedEvent, (_, _) => _text.Focus(), RoutingStrategies.Bubble, handledEventsToo: true);
        // A Tk Text has no context menu, so there is no Copy item to fall back on either.
        _text.ContextFlyout = null;
        _text.ContextMenu = null;

        // <Key> → block_text_input, ahead of the text's own key handling (Tk's widget binding runs before the Text
        // class binding, and its "break" skips it).
        AddHandler(KeyDownEvent, BlockTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The text scrolls on its own; the bar beside it is a separate control, as the ttk.Scrollbar is a separate
        // widget gridded in the next column (sticky=NS) whether or not there is anything to scroll.
        _scroller = new ScrollViewer
        {
            Content = _text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        // The resource is unset until the pane is in a tree that has the palette.
        _scroller.Bind(HeightProperty, _scroller.GetResourceObservable("CmtLineSmall", line => line is double pitch ? pitch * Lines : AvaloniaProperty.UnsetValue));

        var bar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            AllowAutoHide = false,
            Width = ScrollBarWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            [Grid.ColumnProperty] = 1,
        };
        _scroller.ScrollChanged += (_, _) =>
        {
            bar.Maximum = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
            bar.ViewportSize = _scroller.Viewport.Height;
            bar.Value = _scroller.Offset.Y;
        };
        bar.Scroll += (_, e) => _scroller.Offset = _scroller.Offset.WithY(e.NewValue);

        // Text(borderwidth=1, relief=sunken, padx=1, pady=1) by default: a dark top-left edge, a light bottom-right
        // one, and a pixel of padding inside.
        var sunken = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new Border { BorderThickness = new Thickness(1, 1, 0, 0), Padding = new Thickness(1), Child = _scroller },
        };
        sunken.Bind(Border.BorderBrushProperty, sunken.GetResourceObservable("SvSunkenLight"));
        ((Border)sunken.Child).Bind(Border.BorderBrushProperty, sunken.GetResourceObservable("SvSunkenDark"));

        // grid_columnconfigure(0, weight=1) on the pane's frame: the text takes the width, the bar its own.
        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Top,
            Children = { sunken, bar },
        };
    }

    /// <summary>The pane's text, one run per coloured emoji and per message text, in order.</summary>
    public IReadOnlyList<Run> Runs => [.. _text.Inlines!.OfType<Run>()];

    /// <summary>The selected text, empty when nothing is selected.</summary>
    public string SelectedText => _text.SelectedText;

    /// <summary>
    /// Whether the pane has keyboard focus: then Escape doesn't close its window, as the pane's <c>&lt;Key&gt;</c>
    /// binding broke off the toplevel's <c>&lt;Escape&gt;</c> binding (bug #88).
    /// </summary>
    public bool HasKeyboardFocus => IsKeyboardFocusWithin;

    /// <summary>
    /// Reads the lock keys a key press is described with (T-13): the thread's keyboard state by default. A test seam.
    /// </summary>
    public Func<LockKeys> ReadLockKeys { get; init; } = LockKeys.Read;

    /// <summary>Shows a Core operation's log line (<see cref="LogMessage"/> with its <c>skip_logging</c>).</summary>
    public void Log(LogLine line) => LogMessage(line.Type, line.Text, skipLogging: !line.WriteToLogFile);

    /// <summary>
    /// Appends <c>{emoji}{message}\n</c>, colouring only the emoji, and scrolls to the end (<c>log_message</c>).
    /// </summary>
    /// <param name="type">Picks the emoji, its colour, and the log level.</param>
    /// <param name="message">The text after the emoji.</param>
    /// <param name="skipLogging">
    /// <c>skip_logging</c>: show it in the pane only. Otherwise it is also written to the app log, at ERROR for
    /// <see cref="LogType.Bad"/> and INFO for the rest.
    /// </param>
    public void LogMessage(LogType type, string message, bool skipLogging = false)
    {
        if (!skipLogging)
        {
            _logger.Write(type == LogType.Bad ? LogLevel.Error : LogLevel.Information, message);
        }

        var emoji = new Run(Emoji[type]) { FontFamily = EmojiFont };
        emoji.Bind(TextElement.ForegroundProperty, this.GetResourceObservable(EmojiColourKey[type]));
        _text.Inlines!.Add(emoji);
        _text.Inlines.Add(new Run(message + "\n"));
        _scroller.ScrollToEnd();
    }

    /// <summary>Empties the pane (<c>clear</c>).</summary>
    public void Clear() => _text.Inlines!.Clear();

    /// <summary>
    /// <c>utils.block_text_input</c> on every key pressed in the pane: the key is swallowed unless the reference's
    /// exact-mask test lets it through, which on Windows it never does (T-13, bug #88).
    /// </summary>
    private void BlockTextInput(object? sender, KeyEventArgs e)
    {
        if (!TkKeys.BlockTextInputLetsThrough(TkKeys.Of(e.Key, e.KeyModifiers, ReadLockKeys())))
        {
            e.Handled = true;
        }
    }
}
