using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CmtShellPrototype.Views;

namespace CmtShellPrototype;

public partial class App : Application
{
    private IStyle? _variantStyles;
    private IResourceProvider? _variantThemes;
    private PrototypeBar? _bar;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ApplyVariantLayers();
            var main = new MainWindow();
            desktop.MainWindow = main;
            main.Opened += (_, _) => ShowBar(main);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Swaps the variant-specific layer (B: selector Styles, C: ControlTheme dictionary) and rebuilds the main
    /// window, because implicit ControlThemes are resolved when a control is first templated, not on resource change.
    /// </summary>
    public void Rebuild()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not { } old)
        {
            return;
        }

        ApplyVariantLayers();
        var fresh = new MainWindow { Position = old.Position, WindowStartupLocation = WindowStartupLocation.Manual };
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
        if (PrototypeState.OpenOnStart is { } what)
        {
            // Posted so the main window finishes its first layout/render before a modal (or a blocking
            // MessageBoxW) takes over.
            PrototypeState.OpenOnStart = null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _bar.Open(what), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void ApplyVariantLayers()
    {
        if (_variantStyles is not null)
        {
            Styles.Remove(_variantStyles);
            _variantStyles = null;
        }

        if (_variantThemes is not null)
        {
            Resources.MergedDictionaries.Remove(_variantThemes);
            _variantThemes = null;
        }

        switch (PrototypeState.Variant)
        {
            case Variant.B:
                _variantStyles = new Theme.VariantBStyles();
                Styles.Add(_variantStyles);
                break;
            case Variant.C:
                _variantThemes = new Theme.VariantCControlThemes();
                Resources.MergedDictionaries.Add(_variantThemes);
                break;
        }
    }
}
