using Avalonia.Controls;
using Avalonia.Platform;

namespace CMToolkit.App.Views;

/// <summary>
/// The window icon, <c>images/icon-32.png</c>. The reference set it with <c>wm_iconphoto(True, …)</c>, whose
/// <c>default=True</c> gives every later Toplevel the same icon, so code-built windows load it here.
/// </summary>
internal static class AppIcon
{
    private static readonly Uri Source = new("avares://cm-toolkit/Assets/Images/icon-32.png");

    /// <summary>Loads a fresh <see cref="WindowIcon"/> from the embedded image.</summary>
    public static WindowIcon Load()
    {
        using var stream = AssetLoader.Open(Source);
        return new WindowIcon(stream);
    }
}
