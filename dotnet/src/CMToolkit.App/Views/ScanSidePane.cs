using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CMToolkit.App.Tabs;
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

    /// <summary>
    /// Builds the pane with each box ticked as <paramref name="initial"/> says, and Scan Game enabled unless every box
    /// is unticked.
    /// </summary>
    /// <param name="initial">Each box's starting state: the saved settings (<see cref="ScanSettings.Stored"/>).</param>
    /// <exception cref="KeyNotFoundException">A setting is missing from <paramref name="initial"/>.</exception>
    public ScanSidePane(IReadOnlyDictionary<ScanSetting, bool> initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        var checks = new StackPanel { Name = "Checks", HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var setting in ScanSettings.All)
        {
            // B-1, bug #20 fixed (ADR-0006): the reference made each box a BooleanVar(value=True) and never read the
            // stored scanner_* values, so every box was ticked again on each launch and each return to the tab.
            var check = new CheckBox { Name = "Check" + setting, Content = setting.Label(), IsChecked = initial[setting], HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(check, setting.Tooltip());
            // on_checkbox_toggle: Scan Game is disabled while every box is unticked.
            check.IsCheckedChanged += (_, _) => ScanButton!.IsEnabled = _checks.Values.Any(c => c.IsChecked == true);
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
        // on_checkbox_toggle's rule from the start, since the saved boxes may all be unticked.
        ScanButton.IsEnabled = _checks.Values.Any(c => c.IsChecked == true);
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

    /// <summary>The Scan Game button (<c>button_scan</c>).</summary>
    public Button ScanButton { get; }

    /// <summary>The checkboxes, by setting.</summary>
    public IReadOnlyDictionary<ScanSetting, CheckBox> Checks => _checks;

    /// <summary>Each box's state now, for the scan's <see cref="ScanSettings.Snapshot"/>.</summary>
    public IReadOnlyDictionary<ScanSetting, bool> Values => _checks.ToDictionary(c => c.Key, c => c.Value.IsChecked == true);

    /// <summary>
    /// <c>button_scan.configure(...)</c> around a scan: <c>Scanning...</c> and disabled while it runs, <c>Scan Game</c>
    /// and enabled once its results are in.
    /// </summary>
    public void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        ScanButton.Content = scanning ? "Scanning..." : "Scan Game";
    }
}
