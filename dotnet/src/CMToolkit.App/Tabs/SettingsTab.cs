using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CMToolkit.App.Runtime;
using CMToolkit.Core.Settings;

namespace CMToolkit.App.Tabs;

/// <summary>
/// <c>tabs._settings.SettingsTab</c> (SETT-1, SETT-2): the Update Channel and Log Level radios. Each click saves
/// <c>settings.json</c> at once (SET-7), and nothing else: the log level and the update check only read the settings
/// at startup, so a change takes effect at the next launch.
/// </summary>
public sealed class SettingsTab : TabPage
{
    /// <summary><c>TOOLTIP_UPDATE_SOURCE</c>, on every Update Channel radio.</summary>
    public const string UpdateSourceTooltip =
        "GitHub will always have the latest release.\nNexus Mods releases may be delayed due to\ntheir review process or to await more testing.";

    /// <summary><c>TOOLTIP_LOG_LEVEL</c>, on every Log Level radio.</summary>
    public const string LogLevelTooltip =
        "Sets the minimum importance level for messages sent to the log file.\nDEBUG: Extra verbose.\nINFO: Default.\nERROR: Only log errors.";

    private readonly AppRuntime _runtime;
    private readonly SettingsStore _settings;
    private readonly UpdateSource _updateSource;
    private readonly SettingsLogLevel _logLevel;

    /// <summary>
    /// Takes the radios' values from the settings in effect now, as the reference's <c>StringVar</c>s were set in the
    /// tab's constructor at startup rather than when the tab is first shown.
    /// </summary>
    /// <param name="runtime">The app runtime, whose background operations write the file.</param>
    /// <param name="settings">The settings the radios show and save.</param>
    public SettingsTab(AppRuntime runtime, SettingsStore settings)
        : base("Settings", "SettingsTab")
    {
        _runtime = runtime;
        _settings = settings;
        _updateSource = settings.Current.UpdateSource;
        _logLevel = settings.Current.LogLevel;
    }

    /// <inheritdoc/>
    protected internal override Control BuildContent()
    {
        // _build_gui sets every row and column weight to 0, so the frames keep their size, top-left; column 0 is empty.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        grid.Children.Add(Frame("Update Channel", 1, UpdateSourceTooltip,
        [
            Radio("All: GitHub & Nexus Mods", _updateSource == UpdateSource.Both, s => s with { UpdateSource = UpdateSource.Both }),
            Radio("Early: GitHub", _updateSource == UpdateSource.GitHub, s => s with { UpdateSource = UpdateSource.GitHub }),
            Radio("Stable: Nexus Mods", _updateSource == UpdateSource.Nexus, s => s with { UpdateSource = UpdateSource.Nexus }),
            Radio("Never: Don't Check", _updateSource == UpdateSource.None, s => s with { UpdateSource = UpdateSource.None }),
        ]));
        // WARNING is a valid log_level but has no radio, so at WARNING none is selected (SETT-1).
        grid.Children.Add(Frame("Log Level", 2, LogLevelTooltip,
        [
            Radio("Debug", _logLevel == SettingsLogLevel.Debug, s => s with { LogLevel = SettingsLogLevel.Debug }),
            Radio("Info", _logLevel == SettingsLogLevel.Info, s => s with { LogLevel = SettingsLogLevel.Info }),
            Radio("Error", _logLevel == SettingsLogLevel.Error, s => s with { LogLevel = SettingsLogLevel.Error }),
        ]));

        return grid;
    }

    /// <summary>One labelframe (padding=5, padx/pady=5, sticky=NSEW) of radios packed top-down, anchored west.</summary>
    private static GroupBox Frame(string title, int column, string tooltip, RadioButton[] radios)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var radio in radios)
        {
            radio.GroupName = title;
            radio.HorizontalAlignment = HorizontalAlignment.Left;
            ToolTip.SetTip(radio, tooltip);
            stack.Children.Add(radio);
        }

        return new GroupBox
        {
            Header = Labelframe.Title(title),
            Content = stack,
            Margin = new Thickness(5),
            Padding = Labelframe.Padding(5),
            [Grid.ColumnProperty] = column,
        };
    }

    /// <summary>
    /// A radio whose click saves <paramref name="change"/> applied to the settings in effect (<c>on_radio_change</c>).
    /// The file is written off the UI thread as a blocking operation (<see cref="SettingsStore.SaveAsync"/>), since
    /// <c>on_radio_change</c> wrote it on the Tk thread.
    /// </summary>
    /// <remarks>
    /// Click, not IsChecked: a Tk radiobutton runs its command on every click, so clicking the selected radio saves the
    /// file again; Checked would only fire on a change. The save is a UI Action started through
    /// <see cref="ErrorReporter.Handle"/>: a failed write is reported by the runner, as the exception ended
    /// <c>on_radio_change</c> in Tk, and there is nothing else to undo.
    /// </remarks>
    private RadioButton Radio(string text, bool selected, Func<AppSettings, AppSettings> change)
    {
        var radio = new RadioButton { Content = text, IsChecked = selected };
        radio.Click += (_, e) =>
        {
            // Set before the action starts: routing has moved on by the time the save completes.
            e.Handled = true;
            _ = _runtime.Errors.Handle(() => _settings.SaveAsync(_runtime.Operations, change(_settings.Current)));
        };
        return radio;
    }
}
