using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CmtPanesPrototype.Views;

namespace CmtPanesPrototype;

public partial class App : Application
{
    private PrototypeBar? _bar;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainWindow();
            desktop.MainWindow = main;
            if (PrototypeState.SelfTest)
            {
                // Posted so the window is laid out and the side pane placed before the first readout.
                main.Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = SelfTest.RunAsync(main));
            }
            else
            {
                main.Opened += (_, _) => ShowBar(main);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Rebuilds the main window for a new variant. The variant decides the pane hosting at construction, so switching
    /// means a fresh window (and fresh panes), kept at the old window's position.
    /// </summary>
    public void Rebuild()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not MainWindow old)
        {
            return;
        }

        PrototypeState.StartPosition = old.Position;
        var fresh = new MainWindow();
        desktop.MainWindow = fresh;
        fresh.Show();
        old.Close();
        _bar?.AttachTo(fresh);
    }

    private void ShowBar(MainWindow main)
    {
        _bar ??= new PrototypeBar();
        _bar.AttachTo(main);
        _bar.Show();
    }
}
