using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// <c>tabs._settings.SettingsTab</c>. What the radios show and save is proven by the <c>settings-tab</c> scenarios;
/// these pin when values are read, what a click does not do (SETT-2), and the layout measured from the reference.
/// </summary>
public sealed class SettingsTabTests
{
    private static List<RadioButton> Radios(MainWindow window)
        => [.. TabHost.Content(window).GetLogicalDescendants().OfType<RadioButton>()];

    private static RadioButton Radio(MainWindow window, string text) => Radios(window).Single(r => (string)r.Content! == text);

    [AvaloniaFact]
    [Trait("Parity", "SETT-2")]
    public void A_click_saves_at_once_but_the_log_level_only_changes_at_the_next_launch()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        using var log = LogFile.Open(Path.Combine(temp.Path, "cm-toolkit.log"));
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, log);
        log.MinimumLevel = store.Current.LogLevel.ToLogLevel();
        var window = TabHost.Show(new AppRuntime(log), new SettingsTab(store));

        MouseClick.On(Radio(window, "Debug"));
        MouseClick.On(Radio(window, "Never: Don't Check"));

        Assert.Contains("\"log_level\": \"DEBUG\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.Contains("\"update_source\": \"none\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.Equal(SettingsLogLevel.Debug, store.Current.LogLevel);
        // logger.setLevel ran once, at startup; nothing re-applies it.
        Assert.Equal(LogLevel.Information, log.MinimumLevel);
    }

    [AvaloniaFact]
    [Trait("Parity", "SETT-1")]
    public void The_radios_show_the_values_from_when_the_tab_was_constructed()
    {
        // The reference's StringVars were filled in SettingsTab.__init__, at startup, not when the tab is first shown.
        var store = SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub));
        var tab = new SettingsTab(store);
        store.Save(store.Current with { UpdateSource = UpdateSource.None, LogLevel = SettingsLogLevel.Error });

        var window = TabHost.Show(new AppRuntime(new CapturingLogger()), tab);

        Assert.Equal(["Early: GitHub", "Info"], Radios(window).Where(r => r.IsChecked == true).Select(r => (string)r.Content!));
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-7")]
    public void A_click_keeps_settings_other_writers_saved_since_startup()
    {
        // on_radio_change writes into the shared settings dict, so a Scanner save made earlier is kept.
        var store = SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub));
        var window = TabHost.Show(new AppRuntime(new CapturingLogger()), new SettingsTab(store));
        store.Save(store.Current with { ScannerJunkFiles = false });

        MouseClick.On(Radio(window, "Error"));

        Assert.False(store.Current.ScannerJunkFiles);
        Assert.Equal(SettingsLogLevel.Error, store.Current.LogLevel);
    }

    [AvaloniaFact]
    [Trait("Parity", "SETT-1")]
    public void Radios_are_sized_and_placed_like_sv_ttk_radiobuttons()
    {
        var window = TabHost.Show(new AppRuntime(new CapturingLogger()), new SettingsTab(SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub))));
        var frames = TabHost.Content(window).GetLogicalChildren().OfType<GroupBox>().ToList();
        var page = TabHost.Content(window);

        // Measured in Tk: frames 5 px into the page; radios 28 px tall and text + 34 px wide (226 for 24 characters),
        // packed from 5 px inside the frame and 22 px down (the 17 px title, then padding 5).
        Assert.Equal(new Point(5, 5), frames[0].TranslatePoint(default, page));
        var first = Radio(window, "All: GitHub & Nexus Mods");
        Assert.Equal(new Point(5, 22), first.TranslatePoint(default, frames[0]));
        Assert.All(Radios(window), r => Assert.Equal(28, r.Bounds.Height));
        Assert.Equal(226, first.Bounds.Width);
        Assert.Equal(138, Radio(window, "Early: GitHub").Bounds.Width);
        Assert.Equal(236, frames[0].Bounds.Width);
        Assert.Equal(13, ((TextBlock)frames[0].Header!).FontSize);
        // Both frames are as tall as the taller (sticky=NSEW in one row): 139 px.
        Assert.All(frames, f => Assert.Equal(139, f.Bounds.Height));
    }

    [AvaloniaFact]
    [Trait("Parity", "SETT-1")]
    public void A_selected_radio_is_a_green_disc_with_a_black_centre()
    {
        var window = TabHost.Show(new AppRuntime(new CapturingLogger()), new SettingsTab(SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub))));
        var selected = Radio(window, "Info");
        var glyph = selected.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "CheckGlyph");
        var indicator = selected.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "Indicator");

        Assert.True(glyph.IsVisible);
        Assert.Equal(new Size(12, 12), glyph.Bounds.Size);
        Assert.Equal(Avalonia.Media.Color.Parse("#3CD648"), ((Avalonia.Media.ISolidColorBrush)indicator.Fill!).Color);
        Assert.False(Radio(window, "Debug").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "CheckGlyph").IsVisible);
    }
}
