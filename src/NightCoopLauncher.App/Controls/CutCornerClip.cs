using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NightCoopLauncher.App.Controls;

/// <summary>
/// Builds the outline of a rectangle whose bottom right corner is cut off. The cut has a
/// fixed size, so it looks the same on a small button and on a wide one.
/// </summary>
public sealed class CutCornerClip : IMultiValueConverter
{
    private const double CutSize = 16;

    public static CutCornerClip Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double width, double height] || width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var cut = Math.Min(CutSize, Math.Min(width, height) / 2);
        var figure = new PathFigure(
            new Point(0, 0),
            [
                new LineSegment(new Point(width, 0), true),
                new LineSegment(new Point(width, height - cut), true),
                new LineSegment(new Point(width - cut, height), true),
                new LineSegment(new Point(0, height), true),
            ],
            closed: true);

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
