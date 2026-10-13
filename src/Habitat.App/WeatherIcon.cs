using Habitat.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VectorPath = Microsoft.UI.Xaml.Shapes.Path;

namespace Habitat;

internal static class WeatherIcon
{
    private const string Cloud = "M5 17H18A4 4 0 0 0 18 9A6 6 0 0 0 6.5 8A4.5 4.5 0 0 0 5 17";

    public static Canvas Create(WeatherCondition condition, bool isDay, Brush stroke)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var data = condition switch
        {
            WeatherCondition.Clear when isDay => "M16 12A4 4 0 1 0 8 12A4 4 0 1 0 16 12Z M12 2V4 M12 20V22 M2 12H4 M20 12H22 M5 5L6.5 6.5 M17.5 17.5L19 19 M5 19L6.5 17.5 M17.5 6.5L19 5",
            WeatherCondition.Clear => "M20.5 13.5A8.5 8.5 0 1 1 10.5 3.5A7 7 0 0 0 20.5 13.5Z",
            WeatherCondition.Rain => Cloud + " M8 20L7 22 M12 20L11 22 M16 20L15 22",
            WeatherCondition.Snow => Cloud + " M8 20V23 M6.7 20.8L9.3 22.2 M6.7 22.2L9.3 20.8 M16 20V23 M14.7 20.8L17.3 22.2 M14.7 22.2L17.3 20.8",
            WeatherCondition.Storm => Cloud + " M13 15L10 20H14L12 23",
            WeatherCondition.Fog => Cloud + " M4 20H20 M7 23H17",
            _ => Cloud
        };
        // Only these fixed vector paths enter XamlReader; no network content.
        var path = (VectorPath)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' />");
        path.Stroke = stroke;
        path.StrokeThickness = 1.5;
        path.StrokeStartLineCap = PenLineCap.Round;
        path.StrokeEndLineCap = PenLineCap.Round;
        path.StrokeLineJoin = PenLineJoin.Round;
        canvas.Children.Add(path);
        return canvas;
    }
}
