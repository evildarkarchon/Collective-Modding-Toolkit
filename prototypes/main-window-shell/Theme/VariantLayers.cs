using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace CmtShellPrototype.Theme;

/// <summary>
/// Variant B layer. Constructed directly (x:Class) instead of <c>new StyleInclude(uri)</c>: runtime URI includes
/// throw "No precompiled XAML found" under NativeAOT, because nothing roots the compiled loader for that file.
/// </summary>
public partial class VariantBStyles : Styles
{
    public VariantBStyles() => AvaloniaXamlLoader.Load(this);
}

/// <summary>Variant C layer; see <see cref="VariantBStyles"/> for why it is an x:Class.</summary>
public partial class VariantCControlThemes : ResourceDictionary
{
    public VariantCControlThemes() => AvaloniaXamlLoader.Load(this);
}
