using Avalonia.Controls;
using Avalonia.LogicalTree;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Support;

/// <summary>
/// The app's tabs as startup composes them (<see cref="MainTabs.Compose"/> over an <see cref="AppSession"/>, the
/// session's main window set), on a scenario's machine, shown with the Overview loaded. Unlike the real startup it
/// logs to a <see cref="CapturingLogger"/>, so a test can compare every line, at every level.
/// </summary>
public sealed class ComposedSession : IDisposable
{
    /// <param name="machine">The materialized scenario; the caller disposes it.</param>
    public ComposedSession(MaterializedScenario machine)
    {
        // Off the UI thread: blocking on it there would deadlock the locator's continuations. No dialog is scripted.
        var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            .GetAwaiter().GetResult() ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var runtime = new AppRuntime(Logger);
        var model = new MainWindowViewModel(new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, "github", null));
        Session = new AppSession(runtime, model, PcInfo.Read(machine.Host, Logger), game) { Host = machine.Host };
        Window = new MainWindow(runtime, MainTabs.Compose(Session)) { DataContext = model };
        Session.MainWindow = Window;
        Window.Show();
        DispatcherWait.Until(() => Window.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "ArchivePatcher"));
        Window.UpdateLayout();
    }

    /// <summary>Everything the app logged, from startup on.</summary>
    public CapturingLogger Logger { get; } = new();

    /// <summary>The session the tabs were composed over.</summary>
    public AppSession Session { get; }

    /// <summary>The main window, shown, its Overview loaded.</summary>
    public MainWindow Window { get; }

    /// <summary>The one control named <paramref name="name"/> in <paramref name="window"/>.</summary>
    public static T Named<T>(Window window, string name)
        where T : Control
        => window.GetLogicalDescendants().OfType<T>().Where(c => c.Name == name).Distinct().Single();

    /// <summary>Selects the tab at <paramref name="index"/> and waits for its button labelled <paramref name="button"/>.</summary>
    public Button TabButton(int index, string button)
    {
        var tabs = Window.GetLogicalDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = index;
        DispatcherWait.Until(() => Window.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == button));
        Window.UpdateLayout();
        return Window.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == button);
    }

    /// <summary>The Archive Patcher a click opened, once it is listed.</summary>
    public ArchivePatcherWindow WaitForArchivePatcher()
    {
        DispatcherWait.Until(() => Window.OwnedWindows.OfType<ArchivePatcherWindow>().Any());
        var patcher = Window.OwnedWindows.OfType<ArchivePatcherWindow>().Single();
        patcher.UpdateLayout();
        return patcher;
    }

    /// <inheritdoc/>
    public void Dispose() => Window.Close();
}
