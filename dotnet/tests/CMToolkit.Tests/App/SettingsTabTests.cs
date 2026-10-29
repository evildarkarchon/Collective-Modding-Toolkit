using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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

    /// <summary>The tab over unsaved default settings, shown.</summary>
    private static MainWindow ShowDefaults() => ShowDefaults(new AppRuntime(new CapturingLogger()));

    /// <summary>The tab over unsaved default settings, shown with <paramref name="runtime"/>.</summary>
    private static MainWindow ShowDefaults(AppRuntime runtime)
        => TabHost.Show(runtime, new SettingsTab(runtime, SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub))));

    /// <summary>
    /// Clicks <paramref name="radio"/> and waits for the blocking operation that writes its save to end. A click made
    /// while it runs would be discarded by the input block, as ADR-0003 discards input to the frozen UI.
    /// </summary>
    private static void ClickAndWaitForTheSave(AppRuntime runtime, RadioButton radio)
    {
        MouseClick.On(radio);
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
    }

    [AvaloniaFact]
    [Trait("Parity", "SETT-2")]
    public void A_click_saves_at_once_but_the_log_level_only_changes_at_the_next_launch()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        using var log = LogFile.Open(Path.Combine(temp.Path, "cm-toolkit.log"));
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, log);
        log.MinimumLevel = store.Current.LogLevel.ToLogLevel();
        var runtime = new AppRuntime(log);
        var window = TabHost.Show(runtime, new SettingsTab(runtime, store));

        ClickAndWaitForTheSave(runtime, Radio(window, "Debug"));
        ClickAndWaitForTheSave(runtime, Radio(window, "Never: Don't Check"));

        Assert.Contains("\"log_level\": \"DEBUG\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.Contains("\"update_source\": \"none\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Assert.Equal(SettingsLogLevel.Debug, store.Current.LogLevel);
        // logger.setLevel ran once, at startup; nothing re-applies it.
        Assert.Equal(LogLevel.Information, log.MinimumLevel);
    }

    [AvaloniaFact]
    public void A_click_saves_off_the_ui_thread_in_a_blocking_operation()
    {
        // on_radio_change wrote the file on the Tk thread, so the click froze input until the write was done (ADR-0003).
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var log = new HeldSaveLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, log);
        var runtime = new AppRuntime(new CapturingLogger());
        var window = TabHost.Show(runtime, new SettingsTab(runtime, store));
        var before = File.ReadAllBytes(settingsPath);

        log.Hold();
        MouseClick.On(Radio(window, "Debug"));
        DispatcherWait.Until(() => log.WritesOnUiThread.Count == 1);

        Assert.False(log.WritesOnUiThread[0]);
        Assert.True(runtime.Input.IsBlocking);
        Assert.Equal(before, File.ReadAllBytes(settingsPath));
        // The settings in effect change at the click, so a click made before the write lands builds on this one.
        Assert.Equal(SettingsLogLevel.Debug, store.Current.LogLevel);

        log.Release();
        DispatcherWait.Until(() => !runtime.Input.IsBlocking);
        Assert.Contains("\"log_level\": \"DEBUG\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_click_is_marked_handled_before_its_save_completes()
    {
        // Routing has moved on by the time the save's await resumes, so Handled must be set before the action starts.
        var runtime = new AppRuntime(new CapturingLogger());
        var window = ShowDefaults(runtime);
        bool? handledWhileRouting = null;
        window.AddHandler(Button.ClickEvent, (_, e) => handledWhileRouting = e.Handled, RoutingStrategies.Bubble, handledEventsToo: true);

        ClickAndWaitForTheSave(runtime, Radio(window, "Debug"));

        Assert.True(handledWhileRouting);
    }

    [AvaloniaFact]
    public void A_save_that_fails_reaches_the_error_window_once()
    {
        // The runner reports the failed write under its own name; the click adds no second report.
        using var temp = new TempDirectory();
        var storeLog = ThrowingLogger.OnSave();
        var store = SettingsStore.Load(Path.Combine(temp.Path, "settings.json"), DownloadSource.GitHub, storeLog);
        var logger = new CapturingLogger();
        var runtime = new AppRuntime(logger);
        var window = TabHost.Show(runtime, new SettingsTab(runtime, store));

        storeLog.Arm();
        ClickAndWaitForTheSave(runtime, Radio(window, "Debug"));
        DispatcherWait.Until(() => runtime.Errors.Window is not null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader(SettingsStore.SaveOperationName)}\n", entry.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    [Trait("Parity", "SETT-1")]
    public void The_radios_show_the_values_from_when_the_tab_was_constructed()
    {
        // The reference's StringVars were filled in SettingsTab.__init__, at startup, not when the tab is first shown.
        var store = SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub));
        var runtime = new AppRuntime(new CapturingLogger());
        var tab = new SettingsTab(runtime, store);
        store.Save(store.Current with { UpdateSource = UpdateSource.None, LogLevel = SettingsLogLevel.Error });

        var window = TabHost.Show(runtime, tab);

        Assert.Equal(["Early: GitHub", "Info"], Radios(window).Where(r => r.IsChecked == true).Select(r => (string)r.Content!));
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-7")]
    public void A_click_keeps_settings_other_writers_saved_since_startup()
    {
        // on_radio_change writes into the shared settings dict, so a Scanner save made earlier is kept.
        var store = SettingsStore.Unsaved(AppSettings.Defaults(DownloadSource.GitHub));
        var runtime = new AppRuntime(new CapturingLogger());
        var window = TabHost.Show(runtime, new SettingsTab(runtime, store));
        store.Save(store.Current with { ScannerJunkFiles = false });

        ClickAndWaitForTheSave(runtime, Radio(window, "Error"));

        Assert.False(store.Current.ScannerJunkFiles);
        Assert.Equal(SettingsLogLevel.Error, store.Current.LogLevel);
    }

    [AvaloniaFact]
    [Trait("Parity", "SETT-1")]
    public void Radios_are_sized_and_placed_like_sv_ttk_radiobuttons()
    {
        var window = ShowDefaults();
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
        var window = ShowDefaults();
        var selected = Radio(window, "Info");
        var glyph = selected.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "CheckGlyph");
        var indicator = selected.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "Indicator");

        Assert.True(glyph.IsVisible);
        Assert.Equal(new Size(12, 12), glyph.Bounds.Size);
        Assert.Equal(Avalonia.Media.Color.Parse("#3CD648"), ((Avalonia.Media.ISolidColorBrush)indicator.Fill!).Color);
        Assert.False(Radio(window, "Debug").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "CheckGlyph").IsVisible);
    }
}
