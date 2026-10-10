using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lumi;

/// Light or dark colours, following the desktop's theme as Avalonia sees it.
public static class Theme
{
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static IBrush Background => Brush(IsDark ? "#F0202020" : "#F5F9F9F9");
    public static IBrush Card => Brush(IsDark ? "#14FFFFFF" : "#0D000000");
    public static IBrush Border => Brush(IsDark ? "#33FFFFFF" : "#22000000");
    public static IBrush Text => Brush(IsDark ? "#FFF2F2F2" : "#FF1A1A1A");
    public static IBrush Secondary => Brush(IsDark ? "#FF9E9E9E" : "#FF6B6B6B");
    public static IBrush Selection => Brush("#FF2F6FDB");
    public static IBrush Accent => new SolidColorBrush(Mascot.IsHalloween ? Mascot.Pumpkin : Mascot.Teal);
    public static IBrush Error => Brush("#FFE5484D");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static TextBlock Label(string text, double size = 13, IBrush? color = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Text,
        FontWeight = weight ?? FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// The hotkey's key label, as printed on that interface language's usual keyboard (the same physical key).
    public static string HotkeyKey => Lang.Codes[Lang.Index] switch { "ru" => "Ё", "de" => "^", "fr" => "²", _ => "`" };
}
