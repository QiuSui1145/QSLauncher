using System.Windows.Media;
using QSLauncher.Core.Models;

namespace QSLauncher.App.ViewModels;

public sealed class AccentPreset
{
    public AccentPreset(string name, string hex)
    {
        Name = name;
        Hex = ColorMath.NormalizeHex(hex);
        if (ColorMath.TryParseHex(Hex, out var color))
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            Brush = brush;
        }
        else
        {
            Brush = Brushes.Transparent;
        }
    }

    public string Name { get; }

    public string Hex { get; }

    public SolidColorBrush Brush { get; }
}
