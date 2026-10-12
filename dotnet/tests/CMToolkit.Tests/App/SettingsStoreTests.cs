using System.Diagnostics;
using Avalonia.Headless.XUnit;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// <see cref="SettingsStore.SaveAsync"/>, the save every UI handler makes: the file is written off the UI thread, and
/// saves made faster than the writes land still reach the file in order.
/// </summary>
public sealed class SettingsStoreTests
{
    [AvaloniaFact]
    public void A_save_made_while_an_earlier_write_is_under_way_lands_after_it_and_wins()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var log = new HeldSaveLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, log);
        var runtime = new AppRuntime(new CapturingLogger());
        var first = store.Current with { LogLevel = SettingsLogLevel.Debug };
        var second = first with { UpdateSource = UpdateSource.None };

        // The first write is held mid-way, then the second save is made: two quick clicks on two radios.
        log.Hold(count: 1);
        var firstSave = store.SaveAsync(runtime.Operations, first);
        DispatcherWait.Until(() => log.WritesOnUiThread.Count == 1);
        var secondSave = store.SaveAsync(runtime.Operations, second);
        Assert.Equal(second, store.Current);

        // Room for the second write to overtake the held one, if anything let it.
        var overtake = Stopwatch.StartNew();
        DispatcherWait.Until(() => secondSave.IsCompleted || overtake.ElapsedMilliseconds > 300);
        log.Release();
        DispatcherWait.Until(() => firstSave.IsCompleted && secondSave.IsCompleted);

        Assert.Equal([false, false], log.WritesOnUiThread);
        var text = File.ReadAllText(settingsPath);
        Assert.Contains("\"log_level\": \"DEBUG\"", text, StringComparison.Ordinal);
        Assert.Contains("\"update_source\": \"none\"", text, StringComparison.Ordinal);
        Assert.Equal(second, store.Current);
    }
}
