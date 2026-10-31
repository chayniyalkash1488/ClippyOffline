using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Control = System.Windows.Controls.Control;

namespace ClippyOffline;

/// <summary>Shared WPF palette + styles (settings window and the context menu on the character).</summary>
internal static class AppTheme
{
    private static readonly string[] Keys =
    {
        "ThemeBg", "ThemeFg", "ThemeMuted", "ThemeField", "ThemeBtn", "ThemeBtnHover", "ThemeBtnDown",
        "ThemeBorder", "ThemeAccent", "ThemeDisabled", "ThemeHoverBorder", "ThemeCheckBg", "ThemeCheckTick",
        "ThemeThumb", "ThemeScrollThumb", "ThemeScrollThumbHover", "ThemeScrollTrack", "ThemeMenuBorder",
        "ThemeMenuHover", "ThemeMenuHoverFg"
    };

    private static readonly string[] DarkColors =
    {
        "#202024", "#EBEBF0", "#9A9AA6", "#2D2D32", "#3A3A42", "#4B4B55", "#5A5A64",
        "#5A5A64", "#6AA9FF", "#6E6E78", "#5A5A64", "#6AA9FF", "#202024",
        "#EBEBF0", "#5A5A64", "#9A9AA6", "#2D2D32", "#5A5A64",
        "#4B4B55", "#EBEBF0"
    };

    private static readonly string[] LightColors =
    {
        "#FFFFFF", "#141418", "#696970", "#FFFFFF", "#ECECF0", "#DEDEE6", "#CCCCD6",
        "#B0B0BA", "#0067C0", "#96969E", "#B0B0BA", "#0067C0", "#FFFFFF",
        "#141418", "#CCCCD6", "#696970", "#FFFFFF", "#B0B0BA",
        "#DEDEE6", "#141418"
    };

    private static readonly string[] XpColors =
    {
        "#ECE9D8", "#000000", "#716F64", "#FFFFFF", "#F5F4EA", "#FFF3C8", "#E2DECB",
        "#003C74", "#316AC5", "#ACA899", "#FFC73C", "#FFFFFF", "#21A121",
        "#ECE9D8", "#C4D4FA", "#A0B8F4", "#F3F1EC", "#ACA899",
        "#316AC5", "#FFFFFF"
    };

    private static SolidColorBrush B(string hex)
    {
        var br = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        br.Freeze();
        return br;
    }

    /// <summary>Adds control styles (SettingsTheme.xaml) and the palette to the given resources.</summary>
    public static void Attach(ResourceDictionary resources, bool dark, bool xp = false)
    {
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SettingsTheme.xaml", UriKind.Absolute) });
        Fill(resources, dark, xp);
    }

    /// <summary>Replaces the palette brushes; styles use DynamicResource, so UI updates immediately.</summary>
    public static void Fill(ResourceDictionary r, bool dark, bool xp = false)
    {
        var colors = xp ? XpColors : (dark ? DarkColors : LightColors);
        for (int i = 0; i < Keys.Length; i++) r[Keys[i]] = B(colors[i]);
        r["ThemeRadius"] = new CornerRadius(xp ? 3 : 4);
        r["ThemeFieldRadius"] = new CornerRadius(xp ? 0 : 4);
    }

    public static void ApplyFont(Control c, bool xp)
    {
        if (xp)
        {
            c.FontFamily = new FontFamily("Tahoma");
            c.FontSize = 12;
        }
        else
        {
            c.ClearValue(Control.FontFamilyProperty);
            c.ClearValue(Control.FontSizeProperty);
        }
    }
}
