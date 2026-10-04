using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CmtPanesPrototype.Views;

/// <summary>The Scanner side pane's content: the ScanSetting checkboxes and the Scan Game button.</summary>
public partial class SidePaneView : UserControl
{
    // scan_settings.ScanSetting, in enum order, with its tooltip text abbreviated.
    private static readonly (string Label, string Tip)[] Settings =
    [
        ("Overview Issues", "Include problems found on the Overview tab."),
        ("Errors", "Files that could not be read."),
        ("Wrong File Formats", "Files in a format the game can't use in that folder."),
        ("Loose Previs", "Loose precombine/previs files."),
        ("Junk Files", "Files not used by the game or mod managers."),
        ("Problem Overrides", "Loose files that override known-problem files."),
        ("Race Subgraphs", "Counts race animation subgraph records (RACE \\ SADD)."),
    ];

    public SidePaneView()
    {
        InitializeComponent();
        foreach (var (label, tip) in Settings)
        {
            var check = new CheckBox { Content = label, IsChecked = true };
            ToolTip.SetTip(check, tip);
            // SidePane.on_checkbox_toggle: the button is disabled while every setting is off.
            check.IsCheckedChanged += (_, _) => ScanButton.IsEnabled = Checks.Children.OfType<CheckBox>().Any(c => c.IsChecked == true);
            Checks.Children.Add(check);
        }
    }

    /// <summary>Raised by the Scan Game button; the host runs the (fake) scan.</summary>
    public event EventHandler? ScanRequested;

    /// <summary>Mirrors <c>button_scan.configure(state=..., text=...)</c> around a scan.</summary>
    public void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        ScanButton.Content = scanning ? "Scanning..." : "Scan Game";
    }

    private void OnScan(object? sender, RoutedEventArgs e) => ScanRequested?.Invoke(this, EventArgs.Empty);
}
