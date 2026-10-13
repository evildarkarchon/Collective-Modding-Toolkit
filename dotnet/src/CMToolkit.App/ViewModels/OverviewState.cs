using CMToolkit.Core.Overview;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CMToolkit.App.ViewModels;

/// <summary>
/// The current <see cref="OverviewReport"/>, shared by every tab that reads the game's Overview state (the reference's
/// <c>cmc.game.install_type</c>, <c>file_info</c> and <c>cmc.overview_problems</c>). The Overview tab replaces it on each
/// load and refresh; until its first load there is none, and the Install Type is <c>Unknown</c>, as
/// <c>GameInfo.__init__</c> leaves it.
/// </summary>
public sealed partial class OverviewState : ObservableObject
{
    /// <summary>The latest report, or null before the Overview tab has loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallType))]
    public partial OverviewReport? Report { get; set; }

    /// <summary>The game's Install Type (<c>install_type_sv</c>), bound live by the Overview's Version label (OVW-7).</summary>
    public InstallType InstallType => Report?.InstallType ?? InstallType.Unknown;

    /// <summary>
    /// Refreshes the Overview tab (<c>cmc.refresh_tab(Tab.Overview)</c>), as the Scanner does before each scan. Set by
    /// the Overview tab when it is built; it returns whether the refresh completed.
    /// </summary>
    public Func<Task<bool>>? Refresher { get; set; }

    /// <summary>Runs <see cref="Refresher"/>.</summary>
    /// <returns>Whether the refresh completed; a failed one has already been reported.</returns>
    /// <exception cref="InvalidOperationException">No Overview tab was built over this state.</exception>
    public Task<bool> RefreshAsync()
        => Refresher?.Invoke() ?? throw new InvalidOperationException("No Overview tab refreshes this state.");
}
