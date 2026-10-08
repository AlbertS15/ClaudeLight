using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Lumi;

/// Light or dark colours, following the Windows app theme.
public static class Theme
{
    public static bool IsDark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
    }

    public static Brush Background => Brush(IsDark ? "#F0202020" : "#F5F9F9F9");
    public static Brush Card => Brush(IsDark ? "#14FFFFFF" : "#0D000000");
    public static Brush Border => Brush(IsDark ? "#33FFFFFF" : "#22000000");
    public static Brush Text => Brush(IsDark ? "#FFF2F2F2" : "#FF1A1A1A");
    public static Brush Secondary => Brush(IsDark ? "#FF9E9E9E" : "#FF6B6B6B");
    public static Brush Selection => Brush("#FF2F6FDB");
    public static Brush Accent => new SolidColorBrush(Mascot.Teal);
    public static Brush Error => Brush("#FFE5484D");

    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static TextBlock Label(string text, double size = 13, Brush? color = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Text,
        FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// The hotkey's key label, as printed on that interface language's usual keyboard (the same physical key).
    public static string HotkeyKey => Lang.Codes[Lang.Index] switch { "ru" => "Ё", "de" => "^", "fr" => "²", _ => "`" };
}
