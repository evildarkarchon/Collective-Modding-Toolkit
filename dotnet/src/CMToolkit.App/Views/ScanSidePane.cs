using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.Core.Scanner;

namespace CMToolkit.App.Views;

/// <summary>
/// The Scanner side pane's content (<c>tabs._scanner.SidePane</c>, SCN-3): a <c>Scan Settings</c> labelframe of the seven
/// <see cref="ScanSetting"/> checkboxes, each starting as the saved <c>scanner_*</c> settings say (SET-8), over the
/// accent <c>Scan Game</c> button, inside the pane's 2 px groove.
/// </summary>
public sealed class ScanSidePane : UserControl
{
    private readonly Dictionary<ScanSetting, CheckBox> _checks = [];

    // The only answer to whether a Scan is running. The pane keeps no copy of it: every application of the rule reads
    // IsRunning afresh, so a box change, a Scan's start or end, or a re-dock can't act on a stale answer.
    private readonly ScanState _scan;

    /// <summary>
    /// Builds the pane with each box ticked as <paramref name="initial"/> says, and Scan Game's rule applied from the
    /// start: <c>Scanning...</c> and disabled while a Scan runs, otherwise enabled unless every box is unticked.
    /// </summary>
    /// <param name="initial">Each box's starting state: the saved settings (<see cref="ScanSettings.Stored"/>).</param>
    /// <param name="scan">
    /// The Scan module, which says whether a Scan is running. The pane listens to its <see cref="ScanState.Started"/>
    /// and <see cref="ScanState.Ended"/> only while it is attached to the visual tree.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="initial"/> or <paramref name="scan"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">A setting is missing from <paramref name="initial"/>.</exception>
    public ScanSidePane(IReadOnlyDictionary<ScanSetting, bool> initial, ScanState scan)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(scan);
        _scan = scan;
        var checks = new StackPanel { Name = "Checks", HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var setting in ScanSettings.All)
        {
            // B-1, bug #20 fixed (ADR-0006): the reference made each box a BooleanVar(value=True) and never read the
            // stored scanner_* values, so every box was ticked again on each launch and each return to the tab.
            var check = new CheckBox { Name = "Check" + setting, Content = setting.Label(), IsChecked = initial[setting], HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(check, setting.Tooltip());
            // Scan Game's rule is applied again, so unticking every box disables it, and ticking one mid-scan can't
            // enable it. The change is then announced so the tab can save it (bug #20).
            check.IsCheckedChanged += (_, _) =>
            {
                ApplyScanButtonRule();
                BoxChanged?.Invoke(this, setting);
            };
            _checks[setting] = check;
            checks.Children.Add(check);
        }

        ScanButton = new Button
        {
            Name = "Scan",
            Content = "Scan Game",
            HorizontalAlignment = HorizontalAlignment.Center,
            // pack(side=BOTTOM, padx=10, pady=10), padding=5.
            Margin = new Thickness(10),
            Padding = new Thickness(9, 8),
        };
        ScanButton.Classes.Add("accent");
        // The rule from the start, since the saved boxes may all be unticked, or a Scan may be running.
        ApplyScanButtonRule();
        ScanButton.Click += (_, e) =>
        {
            e.Handled = true;
            ScanRequested?.Invoke(this, EventArgs.Empty);
        };
        DockPanel.SetDock(ScanButton, Dock.Bottom);

        // Labelframe(text="Scan Settings", labelanchor=N, padding=5).pack(expand=True, fill=BOTH, padx=5, pady=5).
        var frame = new GroupBox
        {
            Name = "ScanSettings",
            Header = Labelframe.Title("Scan Settings"),
            Content = checks,
            Margin = new Thickness(5),
            // padding=5 below the label; no right inset: Tk lets "Wrong File Formats" run to the frame's edge
            // (prototype measurement).
            Padding = new Thickness(1, 5, 0, 1),
        };
        frame.Classes.Add("centred");

        Content = new GrooveBorder { Child = new DockPanel { Children = { ScanButton, frame } } };
    }

    /// <summary>Raised when Scan Game is clicked.</summary>
    public event EventHandler? ScanRequested;

    /// <summary>
    /// Raised after a box is ticked or unticked, by the user or in code, with the setting it stands for; not for the
    /// starting states the pane is built with.
    /// </summary>
    public event EventHandler<ScanSetting>? BoxChanged;

    /// <summary>The Scan Game button (<c>button_scan</c>).</summary>
    public Button ScanButton { get; }

    /// <summary>The checkboxes, by setting.</summary>
    public IReadOnlyDictionary<ScanSetting, CheckBox> Checks => _checks;

    /// <summary>Each box's state now, for the scan's <see cref="ScanSettings.Snapshot"/>.</summary>
    public IReadOnlyDictionary<ScanSetting, bool> Values => _checks.ToDictionary(c => c.Key, c => c.Value.IsChecked == true);

    /// <summary>
    /// Starts listening to the Scan module's start and end, and applies Scan Game's rule, since a Scan may have started
    /// or ended while the pane was out of the tree.
    /// </summary>
    /// <param name="e">The attachment's arguments.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Subscribed only while attached, so the module, which outlives every pane, never holds a pane that was built
        // but never shown, or one that has been closed. Detach always precedes the next attach, so this can't subscribe
        // twice.
        _scan.Started += OnScanStarted;
        _scan.Ended += OnScanEnded;
        ApplyScanButtonRule();
    }

    /// <summary>Stops listening to the Scan module, so a closed or undocked pane holds no handler on it.</summary>
    /// <param name="e">The detachment's arguments.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _scan.Started -= OnScanStarted;
        _scan.Ended -= OnScanEnded;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Applies Scan Game's rule when a Scan starts, with <see cref="ScanState.IsRunning"/> already true.</summary>
    private void OnScanStarted(object? sender, EventArgs e) => ApplyScanButtonRule();

    /// <summary>Applies Scan Game's rule when a Scan ends, with <see cref="ScanState.IsRunning"/> already false.</summary>
    private void OnScanEnded(object? sender, ScanEnded e) => ApplyScanButtonRule();

    /// <summary>
    /// Scan Game's one rule, from whether the Scan module says a Scan is running and the boxes as they are now: enabled
    /// only while no Scan runs and some box is ticked, and showing <c>Scanning...</c> exactly while a Scan runs.
    /// </summary>
    private void ApplyScanButtonRule()
    {
        var scanning = _scan.IsRunning;
        ScanButton.IsEnabled = !scanning && _checks.Values.Any(c => c.IsChecked == true);
        ScanButton.Content = scanning ? "Scanning..." : "Scan Game";
    }
}
