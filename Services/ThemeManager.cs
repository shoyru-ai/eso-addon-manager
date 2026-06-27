using System.Windows;
using System.Windows.Media;

namespace EsoAddons.Services;

/// <summary>Switches the app between dark and light by mutating the shared palette brushes
/// (the named SolidColorBrush resources in App.xaml). Controls bind to the brush instances via
/// StaticResource, so changing each brush's Color updates the whole UI live.</summary>
public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";

    public static void Apply(string theme)
    {
        var light = string.Equals(theme, Light, System.StringComparison.OrdinalIgnoreCase);
        var r = Application.Current?.Resources;
        if (r is null) return;

        // Replace the brush instances (the changing keys are referenced via DynamicResource, so this
        // updates the whole UI live — mutating a StaticResource brush's Color would NOT propagate).
        void Set(string key, string hex) => r[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        if (light)
        {
            Set("Bg", "#FFF4F4F7");
            Set("Panel", "#FFFFFFFF");
            Set("PanelAlt", "#FFECECF1");
            Set("Border", "#FFD5D5DE");
            Set("Text", "#FF1B1B1F");
            Set("Muted", "#FF6B6B77");
            Set("UpdateRow", "#265B8DEF");
        }
        else
        {
            Set("Bg", "#FF1B1B1F");
            Set("Panel", "#FF26262C");
            Set("PanelAlt", "#FF222227");
            Set("Border", "#FF3A3A44");
            Set("Text", "#FFE7E7EC");
            Set("Muted", "#FF9A9AA6");
            Set("UpdateRow", "#1F5B8DEF");
        }
        // Accent / Good / Danger work on both themes and stay constant.
    }
}
