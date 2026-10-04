using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CmtShellPrototype.Views;

public partial class TreeWindowSample : Window
{
    public TreeWindowSample()
    {
        InitializeComponent();
        // ModalWindow binds <Escape>, TreeWindow also binds <space>.
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Space)
            {
                Close();
            }
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
