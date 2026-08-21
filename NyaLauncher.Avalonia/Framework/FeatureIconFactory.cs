using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Framework;

public static class FeatureIconFactory
{
    public static Control Create(
        string glyph,
        string? iconPath,
        double fontSize = 18)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            try
            {
                return new Image
                {
                    Source = new Bitmap(iconPath),
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
            }
            catch
            {
                // A moved, locked, or invalid image falls back to its preset glyph.
            }
        }

        return new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(glyph) ? "◇" : glyph,
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            Foreground = ThemePolygonHelper.AccentGlyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
