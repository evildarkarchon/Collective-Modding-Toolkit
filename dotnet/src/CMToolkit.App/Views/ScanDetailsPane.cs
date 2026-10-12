using Avalonia.Controls;
using CMToolkit.Core.Problems;

namespace CMToolkit.App.Views;

/// <summary>
/// The Scanner details pane's content (<c>tabs._scanner.ResultDetailsPane</c>, SCN-4): the selected result's details
/// inside the pane's 2 px groove. The window is created on the first selection and lives as the panes lifecycle says
/// (SCN-5).
/// </summary>
/// <remarks>
/// The rows (SCN-R4), the clickable path (SCN-R5), the solution text (SCN-R6) and the buttons (SCN-R7) are the details
/// pane slice's (#57): it fills <see cref="Rows"/> from <see cref="Show"/>. Until then the pane shows the groove alone.
/// </remarks>
public sealed class ScanDetailsPane : UserControl
{
    /// <summary>Builds the empty pane.</summary>
    public ScanDetailsPane()
    {
        // grid_columnconfigure(1, weight=1): the labels, the values, then the buttons column.
        Rows = new Grid { Name = "Rows", ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Content = new GrooveBorder { Child = Rows };
    }

    /// <summary>The pane's grid (<c>ResultDetailsPane</c> itself, which grids its labels and button frame).</summary>
    public Grid Rows { get; }

    /// <summary>The result shown, or null before the first <see cref="Show"/>.</summary>
    public Problem? Problem { get; private set; }

    /// <summary>Whether the Mod row is shown: the results came from an MO2 stage-mode scan.</summary>
    public bool UsingStage { get; private set; }

    /// <summary><c>ResultDetailsPane.set_info</c>: shows <paramref name="problem"/>.</summary>
    /// <param name="problem">The selected result.</param>
    /// <param name="usingStage">Whether the Mod row is shown (stage mode).</param>
    public void Show(Problem problem, bool usingStage)
    {
        Problem = problem;
        UsingStage = usingStage;
    }
}
