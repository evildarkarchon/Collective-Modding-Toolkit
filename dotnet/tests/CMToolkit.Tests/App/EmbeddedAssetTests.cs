using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;

namespace CMToolkit.Tests.App;

/// <summary>The font and images are embedded in the exe as Avalonia resources (ADR-0002).</summary>
public sealed class EmbeddedAssetTests
{
    private static readonly FontFamily Cascadia = new("avares://cm-toolkit/Assets/Fonts#Cascadia Mono");

    [AvaloniaTheory]
    [InlineData(FontWeight.Normal)]
    [InlineData(FontWeight.Bold)]
    [Trait("Parity", "SHELL-3")]
    public void Cascadia_mono_loads_from_the_exe_in_both_weights(FontWeight weight)
    {
        // SHELL-3 loads the font process-private with AddFontResourceExW; the embedded font collection is the
        // equivalent, private to the app and independent of what's installed on the machine.
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(Cascadia, weight: weight), out var glyphs));

        Assert.Equal("Cascadia Mono", glyphs.FamilyName);
        Assert.Equal(weight, glyphs.Weight);
        // A real bold face, not a synthesised one (the bold update links need true bold).
        Assert.Equal(FontSimulations.None, glyphs.FontSimulations);
    }

    [AvaloniaTheory]
    [InlineData("Images/check-20.png")]
    [InlineData("Images/icon-256.png")]
    [InlineData("Images/icon-32.png")]
    [InlineData("Images/info-16.png")]
    [InlineData("Images/logo-discord.png")]
    [InlineData("Images/logo-github.png")]
    [InlineData("Images/logo-nexusmods.png")]
    [InlineData("Images/refresh-32.png")]
    [InlineData("Images/update-24.png")]
    [InlineData("Images/warning-16.png")]
    [Trait("Parity", "SHELL-16")]
    public void Every_reference_image_is_embedded(string relative)
    {
        // SHELL-16 resolves images under <base>/assets/images; the port embeds the same set in the exe.
        Assert.True(AssetLoader.Exists(new Uri($"avares://cm-toolkit/Assets/{relative}")));
    }

    [AvaloniaFact]
    public void Download_source_txt_is_not_embedded_because_it_differs_per_archive()
    {
        Assert.False(AssetLoader.Exists(new Uri("avares://cm-toolkit/Assets/download-source.txt")));
    }
}
