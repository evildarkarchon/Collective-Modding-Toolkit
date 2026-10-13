using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CMToolkit.App.Views;

/// <summary>
/// An image-only <c>ttk.Label</c> (<c>compound="image"</c>, <c>image=cmc.get_image(path)</c>): one of the embedded
/// <c>Assets/Images</c> PNGs at its natural size, one image pixel to one DIP as Tk shows a <c>PhotoImage</c>.
/// </summary>
public sealed class AssetImage : Image
{
    /// <param name="assetPath">The reference's assets-relative path, such as <c>images/info-16.png</c>.</param>
    public AssetImage(string assetPath)
    {
        AssetPath = assetPath;
        // The reference's assets/images/x.png is embedded as Assets/Images/x.png (the folders differ only in case).
        var name = assetPath.StartsWith("images/", StringComparison.Ordinal)
            ? assetPath["images/".Length..]
            : throw new ArgumentException($"Not an images/ asset: {assetPath}", nameof(assetPath));
        using var stream = AssetLoader.Open(new Uri($"avares://cm-toolkit/Assets/Images/{name}"));
        var bitmap = new Bitmap(stream);
        Source = bitmap;
        Width = bitmap.PixelSize.Width;
        Height = bitmap.PixelSize.Height;
        Stretch = Stretch.None;
    }

    /// <summary>The reference's assets-relative path of the image shown, such as <c>images/icon-256.png</c>.</summary>
    public string AssetPath { get; }
}
