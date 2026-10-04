using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CmtPanesPrototype.Views;

/// <summary>The Scanner tab body. Raises <see cref="ItemSelected"/> when a result row is picked.</summary>
public partial class ScannerView : UserControl
{
    public ScannerView()
    {
        InitializeComponent();
        Populate();
    }

    /// <summary>Raised with the selected result row (Tk: <c>&lt;&lt;TreeviewSelect&gt;&gt;</c> -> on_row_select).</summary>
    public event EventHandler<ProblemItem>? ItemSelected;

    /// <summary>Selects the first row whose group title contains <paramref name="groupText"/> (the <c>--pick</c> path).</summary>
    public bool Pick(string groupText)
    {
        var item = ScanData.Results.FirstOrDefault(g => g.Title.Contains(groupText, StringComparison.OrdinalIgnoreCase))?.Items[0];
        if (item is null)
        {
            return false;
        }

        Tree.SelectedItem = item;
        return true;
    }

    /// <summary>
    /// Fake ScannerTab.start_threaded_scan: clears the tree, ticks the progress bar and "Scanning..." label for
    /// ~1.4 s, then repopulates. <paramref name="done"/> runs on the UI thread once results are back.
    /// </summary>
    public void RunFakeScan(Action done)
    {
        Tree.ItemsSource = null;
        ResultsInfo.Text = "";
        ScanningText.IsVisible = true;
        string[] folders = ["Data", "F4SE", "Meshes", "Textures", "Sound", "Scripts", "Materials"];
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (step < folders.Length)
            {
                Progress.Value = (step + 1) * 100.0 / folders.Length;
                ScanningText.Text = $"Scanning... {step + 1}/{folders.Length}: {folders[step]}";
                step++;
                return;
            }

            timer.Stop();
            ScanningText.IsVisible = false;
            Populate();
            done();
        };
        Progress.Value = 1;
        ScanningText.Text = "Refreshing Overview...";
        timer.Start();
    }

    private void Populate()
    {
        Tree.ItemsSource = ScanData.Results;
        ResultsInfo.Text = $"{ScanData.Count} Results ~ Select an item for details";
        Progress.Value = 100;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Tree.SelectedItem is ProblemItem item)
        {
            ItemSelected?.Invoke(this, item);
        }
    }

    private void OnCollapse(object? sender, RoutedEventArgs e) => SetExpanded(false);

    private void OnExpand(object? sender, RoutedEventArgs e) => SetExpanded(true);

    /// <summary>ScannerTab.set_expanded: open/close every top-level group.</summary>
    private void SetExpanded(bool expanded)
    {
        foreach (var item in Tree.GetRealizedContainers().OfType<TreeViewItem>())
        {
            item.IsExpanded = expanded;
        }
    }
}
