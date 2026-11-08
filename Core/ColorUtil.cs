using System.Windows.Media;

namespace MyZoom.Core;

public static class ColorUtil
{
    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { }
        return fallback;
    }

    public static Color Parse(string? hex) => Parse(hex, Colors.Red);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static SolidColorBrush Brush(Color c, double opacity = 1)
    {
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    public static SolidColorBrush Brush(string hex, double opacity = 1) => Brush(Parse(hex), opacity);
}
