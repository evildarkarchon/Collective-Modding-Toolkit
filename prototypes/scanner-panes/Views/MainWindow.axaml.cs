using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CmtPanesPrototype.Docking;

namespace CmtPanesPrototype.Views;

/// <summary>
/// PROTOTYPE main window. Owns the pane lifecycle the Python ScannerTab has (side pane created on switching to the
/// tab, details pane created on first row select, both destroyed on leaving the tab; details destroyed on a new
/// scan) and routes each pane to the host that the current <see cref="Variant"/> uses.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Variant _variant = PrototypeState.Variant;
    private OwnedPaneDocker? _docker;
    private SidePaneView? _side;
    private DetailsPaneView? _details;

    public MainWindow()
    {
        InitializeComponent();
        VariantTag.Text = "PROTOTYPE " + PrototypeState.Describe(_variant);
        if (PrototypeState.StartPosition is { } p)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = p;
        }

        if (_variant == Variant.A)
        {
            _docker = new OwnedPaneDocker(this);
        }

        Scanner.ItemSelected += (_, item) => ShowDetails(item);
        Opened += (_, _) =>
        {
            Tabs.SelectedItem = ScannerTab;
            if (PrototypeState.PickOnStart is { } pick)
            {
                Scanner.Pick(pick);
            }
        };
        Closed += (_, _) => _docker?.Dispose();
    }

    public Variant Variant => _variant;

    /// <summary>The variant A docker, for the readout and the self-test; null in B and C.</summary>
    public OwnedPaneDocker? Docker => _docker;

    public bool HasDetails => _details is not null;

    public void SelectTab(int index) => Tabs.SelectedIndex = index;

    public bool Pick(string groupText) => Scanner.Pick(groupText);

    /// <summary>Starts the fake scan, exactly like pressing Scan Game in the side pane.</summary>
    public void StartScan()
    {
        if (_side is null)
        {
            return;
        }

        _side.SetScanning(true);
        // start_threaded_scan destroys the details pane before scanning.
        CloseDetails();
        Scanner.RunFakeScan(() => _side?.SetScanning(false));
    }

    /// <summary>Escape quits like the original (<c>root.bind("&lt;Escape&gt;", root.destroy)</c>).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // TabControl.SelectionChanged also bubbles up from the TreeView inside the Scanner tab; ignore those.
        if (!ReferenceEquals(e.Source, Tabs))
        {
            return;
        }

        if (Tabs.SelectedItem == ScannerTab)
        {
            ShowSide();
        }
        else
        {
            // ScannerTab.switch_from: drop the selection and destroy both panes.
            CloseDetails();
            CloseSide();
        }
    }

    private void ShowSide()
    {
        if (_side is not null)
        {
            return;
        }

        _side = new SidePaneView();
        _side.ScanRequested += (_, _) => StartScan();
        Host(PaneSlot.Side, _side);
    }

    private void ShowDetails(ProblemItem item)
    {
        if (_details is null)
        {
            _details = new DetailsPaneView();
            Host(PaneSlot.Details, _details);
        }

        _details.SetInfo(item);
    }

    private void CloseSide()
    {
        _side = null;
        Unhost(PaneSlot.Side);
    }

    private void CloseDetails()
    {
        _details = null;
        Unhost(PaneSlot.Details);
    }

    private void Host(PaneSlot slot, Control view)
    {
        switch (_variant)
        {
            case Variant.A:
                _docker!.Show(slot, view);
                break;
            case Variant.B:
                Show(slot == PaneSlot.Side ? GrownSideHost : GrownDetailsHost, view);
                break;
            case Variant.C:
                Show(slot == PaneSlot.Side ? InTabSideHost : InTabDetailsHost, view);
                break;
        }
    }

    private void Unhost(PaneSlot slot)
    {
        switch (_variant)
        {
            case Variant.A:
                _docker!.Close(slot);
                break;
            case Variant.B:
                Hide(slot == PaneSlot.Side ? GrownSideHost : GrownDetailsHost);
                break;
            case Variant.C:
                Hide(slot == PaneSlot.Side ? InTabSideHost : InTabDetailsHost);
                break;
        }
    }

    private static void Show(ContentControl host, Control view)
    {
        host.Content = view;
        host.IsVisible = true;
    }

    private static void Hide(ContentControl host)
    {
        host.Content = null;
        host.IsVisible = false;
    }
}
