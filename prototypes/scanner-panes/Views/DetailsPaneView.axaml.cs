using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CmtPanesPrototype.Views;

/// <summary>The Scanner details pane's content for the selected result row.</summary>
public partial class DetailsPaneView : UserControl
{
    public DetailsPaneView() => InitializeComponent();

    /// <summary>
    /// ResultDetailsPane.set_info, reduced: fills the three value labels and rebuilds the button column
    /// (Copy Details always; File List / Auto-Fix when the result has them).
    /// </summary>
    public void SetInfo(ProblemItem item)
    {
        FilePath.Text = item.Text;
        Summary.Text = item.Summary;
        Solution.Text = item.Solution;

        // set_info swaps the path label between a hand cursor + "Click to open location" tip and X_cursor.
        FilePath.Cursor = new Cursor(item.LocationExists ? StandardCursorType.Hand : StandardCursorType.No);
        ToolTip.SetTip(FilePath, item.LocationExists ? "Click to open location" : null);

        Buttons.Children.Clear();
        Buttons.Children.Add(PaneButton("Copy Details"));
        if (item.HasFileList)
        {
            Buttons.Children.Add(PaneButton("File List"));
        }

        if (item.HasAutoFix)
        {
            var fix = PaneButton("Auto-Fix");
            fix.Classes.Add("accent");
            Buttons.Children.Add(fix);
        }
    }

    // ttk.Button(padding=(0, 5)) packed fill=X, padx=5, pady=(5, 0).
    private static Button PaneButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(4, 7),
        Margin = new Thickness(5, 5, 5, 0),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
    };
}
