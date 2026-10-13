using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Core.Problems;
using CMToolkit.Core.Scanner;

namespace CMToolkit.App.Views;

/// <summary>
/// The Scanner details pane's content (<c>tabs._scanner.ResultDetailsPane</c>, SCN-4): the selected result's details
/// inside the pane's 2 px groove. The window is created on the first selection and lives as the panes lifecycle says
/// (SCN-5): it closes when the tab is left and when Scan Game starts.
/// </summary>
/// <remarks>
/// Rows (SCN-R4): <c>Mod:</c> in stage mode only, at the normal size, then <c>Problem:</c>, <c>Summary:</c> and
/// <c>Solution:</c> at the small size, the values wrapping at 560 px. The path opens its folder (SCN-R5), the solution
/// link opens or copies (SCN-R6), and the right-hand column holds Copy Details and, for a result with a file list,
/// File List (SCN-R7). Auto-Fix (SCN-R8) isn't ported: <c>AUTO_FIXES</c> is empty, so its button never appears.
/// </remarks>
public sealed class ScanDetailsPane : UserControl
{
    /// <summary><c>WINDOW_WIDTH - 200</c>: where the values wrap.</summary>
    public const int Wraplength = 560;

    private readonly AppRuntime _runtime;
    private readonly IHostEnvironment _host;
    private readonly TextBlock _modLabel;
    private readonly TextBlock _modValue;
    private readonly StackPanel _buttons;
    private Button? _copy;
    private Button? _files;
    private string? _location;
    private string? _url;

    /// <summary>Builds the pane with its rows empty; <see cref="Show"/> fills them.</summary>
    /// <param name="runtime">The app runtime: its clipboard clock, browser and error reporting.</param>
    /// <param name="host">The machine the paths are checked on (a relative path is relative to its working directory).</param>
    public ScanDetailsPane(AppRuntime runtime, IHostEnvironment host)
    {
        _runtime = runtime;
        _host = host;

        // grid_columnconfigure(1, weight=1): the labels, the values, then the buttons column. The rows are sized to their
        // content; the last row takes what's left, so a buttons column taller than the rows doesn't stretch them.
        Rows = new Grid
        {
            Name = "Rows",
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"),
        };

        // The Mod row exists only in stage mode (the reference built it only then); it stays hidden until a stage-mode
        // Show, and a hidden row takes no space.
        _modLabel = Label("Mod:", 0, small: false);
        _modValue = new TextBlock { Margin = new Thickness(0, 5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsVisible = false };
        _modValue.Bind(TextBlock.ForegroundProperty, _modValue.GetResourceObservable("CmtNeutral2"));
        Grid.SetColumn(_modValue, 1);
        Rows.Children.Add(_modValue);
        _modLabel.IsVisible = false;

        Label("Problem:", 1, small: true);
        Label("Summary:", 2, small: true);
        Label("Solution:", 3, small: true);

        PathLabel = Value(1);
        // A Tk label's whole box takes the click, not just its glyphs.
        PathLabel.Background = Brushes.Transparent;
        PathLabel.Cursor = LocationCursor;
        PathLabel.PointerPressed += OnPathPressed;

        SummaryLabel = Value(2);

        SolutionLabel = Value(3);
        SolutionLabel.Background = Brushes.Transparent;
        SolutionLabel.PointerPressed += OnSolutionPressed;

        // frame_buttons: grid(column=2, row=0, rowspan=10, sticky=NSEW).
        _buttons = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetColumn(_buttons, 2);
        Grid.SetRowSpan(_buttons, 5);
        Rows.Children.Add(_buttons);

        Content = new GrooveBorder { Child = Rows };
    }

    /// <summary>The path label's cursor while its path can be opened (Tk's <c>hand2</c>). Shared, so tests can tell it apart.</summary>
    public static Cursor LocationCursor { get; } = new(StandardCursorType.Hand);

    /// <summary>
    /// The path label's cursor while its path can't be opened. Tk's <c>X_cursor</c> is an X glyph Avalonia has no
    /// standard cursor for; the "unavailable" cursor stands in for it (dotnet/README.md, Known residuals).
    /// </summary>
    public static Cursor NoLocationCursor { get; } = new(StandardCursorType.No);

    /// <summary>The pane's grid (<c>ResultDetailsPane</c> itself, which grids its labels and button frame).</summary>
    public Grid Rows { get; }

    /// <summary>The Problem row's value (<c>label_file_path</c>): the relative path, clickable when it can be opened.</summary>
    public TextBlock PathLabel { get; }

    /// <summary>The Summary row's value.</summary>
    public TextBlock SummaryLabel { get; }

    /// <summary>The Solution row's value (<c>label_solution</c>): clickable when its first extra line is a link.</summary>
    public TextBlock SolutionLabel { get; }

    /// <summary>The buttons column, top to bottom: Copy Details, then File List when the result has one.</summary>
    public IReadOnlyList<Button> Buttons => [.. _buttons.Children.OfType<Button>()];

    /// <summary>The result shown, or null before the first <see cref="Show"/>.</summary>
    public Problem? Problem { get; private set; }

    /// <summary>Whether the Mod row is shown: the results came from an MO2 stage-mode scan.</summary>
    public bool UsingStage { get; private set; }

    /// <summary>
    /// Opens a folder in Explorer (<c>os.startfile</c>; SCN-R5): the shell by default, a recorder in tests. A failure
    /// reaches the Error Window, as <c>os.startfile</c> raised into the Tk callback.
    /// </summary>
    public Action<string> OpenFolder { get; init; } = StartFile;

    /// <summary>
    /// <c>ResultDetailsPane.set_info</c>: shows <paramref name="problem"/>. Whether its path can be opened is checked now,
    /// not when it is clicked. The Copy Details button is made once and kept, so a <c>Copied!</c> outlasts the selection
    /// it was clicked for; File List is made afresh for each result that has one.
    /// </summary>
    /// <param name="problem">The selected result.</param>
    /// <param name="usingStage">Whether the Mod row is shown (stage mode).</param>
    public void Show(Problem problem, bool usingStage)
    {
        Problem = problem;
        UsingStage = usingStage;

        if (usingStage)
        {
            _modLabel.IsVisible = true;
            _modValue.IsVisible = true;
            _modValue.Text = ProblemDetails.ModText(problem);
        }

        // SCN-R5: the folder is worked out once, as set_info bound the click to a fixed target.
        PathLabel.Text = problem.RelativePath;
        _location = ProblemDetails.Location(_host, problem)?.ToString();
        PathLabel.Cursor = _location is null ? NoLocationCursor : LocationCursor;
        ToolTip.SetTip(PathLabel, _location is null ? null : ProblemDetails.LocationTooltip);

        SummaryLabel.Text = problem.Summary;

        // SCN-R6.
        SolutionLabel.Text = ProblemDetails.SolutionText(problem);
        _url = ProblemDetails.Url(problem);
        ToolTip.SetTip(SolutionLabel, _url is null ? null : ProblemDetails.UrlTooltip);

        // SCN-R7: the old File List goes; Copy Details is made on the first result and kept.
        if (_files is not null)
        {
            _buttons.Children.Remove(_files);
            _files = null;
        }

        if (_copy is null)
        {
            _copy = PaneButton("Copy Details");
            _copy.Click += async (_, _) => await CopyDetailsAsync();
            _buttons.Children.Add(_copy);
        }

        if (ProblemDetails.FileListWindow(problem) is { } window && problem.FileList is { } fileList)
        {
            _files = PaneButton("File List");
            _files.Click += (_, _) => OpenFileList(window.Title, window.Text, ProblemDetails.FileListHeadersOf(problem), fileList);
            _buttons.Children.Add(_files);
        }
    }

    /// <summary><c>os.startfile(path)</c>: the shell's default action, which for a folder opens it in Explorer.</summary>
    public static void StartFile(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>A right-aligned row title in the label column, added to the grid at <paramref name="row"/>.</summary>
    /// <param name="text">The title, such as <c>Problem:</c>.</param>
    /// <param name="row">The grid row.</param>
    /// <param name="small">The small font; the Mod row's title is the normal size.</param>
    private TextBlock Label(string text, int row, bool small)
    {
        // sticky=NE, padx=5, pady=5.
        var label = new TextBlock
        {
            Text = text,
            Margin = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Right,
        };
        if (small)
        {
            label.Classes.Add("small");
        }

        Grid.SetRow(label, row);
        Rows.Children.Add(label);
        return label;
    }

    /// <summary>A wrapping value: sticky=NW, padx=0, pady=5, the neutral colour, wraplength=560.</summary>
    private TextBlock Value(int row)
    {
        var value = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left };
        value.Classes.Add("small");
        value.Bind(TextBlock.ForegroundProperty, value.GetResourceObservable("CmtNeutral2"));
        var presenter = new WraplengthPresenter
        {
            Wraplength = Wraplength,
            Child = value,
            Margin = new Thickness(0, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(presenter, 1);
        Grid.SetRow(presenter, row);
        Rows.Children.Add(presenter);
        return value;
    }

    /// <summary>
    /// A buttons-column button: <c>ttk.Button(padding=(0, 5))</c> packed <c>side=TOP, anchor=E, fill=X, padx=5,
    /// pady=(5, 0)</c>.
    /// </summary>
    private static Button PaneButton(string text)
    {
        // Tk's padding=5 is Padding(9, 8) on Scan Game (ScanSidePane): the sv_ttk element adds about 3 down. Across, 3
        // matches the reference's button width (parity/screenshots/scanner/details).
        return new Button
        {
            Content = text,
            Padding = new Thickness(3, 8),
            Margin = new Thickness(5, 5, 5, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
    }

    /// <summary>
    /// A left click on the path while it can be opened opens its folder (<c>os.startfile</c>); a failure is reported
    /// in the Error Window, as the Tk callback's exception was.
    /// </summary>
    private void OnPathPressed(object? sender, PointerPressedEventArgs e)
    {
        // <Button-1> is bound only while the path can be opened.
        if (_location is not { } location || !e.GetCurrentPoint(PathLabel).Properties.IsLeftButtonPressed)
        {
            return;
        }

        try
        {
            OpenFolder(location);
        }
        catch (Exception ex)
        {
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
        }
    }

    /// <summary>On a solution that is a URL: a left click opens it in the browser, a right click copies it.</summary>
    private void OnSolutionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_url is not { } url)
        {
            return;
        }

        var point = e.GetCurrentPoint(SolutionLabel).Properties;
        if (point.IsLeftButtonPressed)
        {
            // <Button-1>: webbrowser.open(url).
            _runtime.Browser.Open(url);
        }
        else if (point.IsRightButtonPressed)
        {
            // <Button-3>: copy_text(url), the clipboard alone, with no Copied! anywhere.
            _ = CopyUrlAsync(url);
        }
    }

    /// <summary>
    /// Puts <paramref name="url"/> on the clipboard; a failure is reported in the Error Window rather than left in the
    /// task, which the click handler discards.
    /// </summary>
    private async Task CopyUrlAsync(string url)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("The details pane is not in a window with a clipboard.");
            await clipboard.SetTextAsync(url);
        }
        catch (Exception ex)
        {
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
        }
    }

    /// <summary>
    /// <c>copy_details</c>: the shown rows to the clipboard, and <c>Copied!</c> for 3 s (THR-3). The text is read at click
    /// time, since Copy Details is the one button that outlives a selection. A failure reaches the Error Window.
    /// </summary>
    private async Task CopyDetailsAsync()
    {
        try
        {
            await CopyTextButton.CopyAsync(_copy!, ProblemDetails.CopyText(Problem!, UsingStage), _runtime.Time);
        }
        catch (Exception ex)
        {
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
        }
    }

    /// <summary>File List: the TreeWindow over the main window (the reference's parent was the root, not the pane).</summary>
    /// <param name="title">The window title.</param>
    /// <param name="text">The text above the list.</param>
    /// <param name="headers">The column headings (<see cref="ProblemDetails.FileListHeadersOf"/>).</param>
    /// <param name="fileList">The counted files.</param>
    private void OpenFileList(
        string title, string text, IReadOnlyList<string> headers, IReadOnlyList<(int Count, Core.PythonSemantics.PythonPath File)> fileList)
    {
        var pane = TopLevel.GetTopLevel(this) as Window;
        if ((pane?.Owner as Window ?? pane) is not { } owner)
        {
            return;
        }

        var window = TreeWindow.Create(
            _runtime,
            ProblemDetails.FileListWidth,
            ProblemDetails.FileListHeight,
            title,
            text,
            headers,
            [.. fileList.Select(f => (f.Count, f.File.ToString()))],
            count => count.ToString(CultureInfo.InvariantCulture));
        // Not awaited: like the reference's modal, it runs on while the click handler returns.
        _ = window.ShowModalAsync(owner);
    }
}
