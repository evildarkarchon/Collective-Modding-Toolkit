using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;

namespace CMToolkit.App;

public partial class App : Application
{
    /// <summary>Loads the compiled App.axaml: theme, palette and app-wide styles.</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Builds the shell. The Core startup work (the Download Source lookup, resolved against the exe folder) runs
    /// here, before the main window exists, as it does at import time in the Reference Implementation. Headless test
    /// sessions have no desktop lifetime, so they skip this and build their windows themselves.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = MainWindowViewModel.Load(AppContext.BaseDirectory) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
