namespace Habitat.Services;

internal static class BackdropThemeDetector
{
    public static IEnumerable<(int X, int Y)> SamplePoints(int width, int height, double scale)
    {
        // Stay inside the transparent 30-DIP side gutters, below the widgets.
        var inset = (int)Math.Ceiling(12 * scale);
        var top = (int)Math.Ceiling(140 * scale);
        var bottom = height - inset - 1;
        if (width <= 2 * inset || bottom <= top) yield break;

        for (var row = 0; row < 5; row++)
        {
            var y = top + (bottom - top) * row / 4;
            yield return (inset, y);
            yield return (width - inset - 1, y);
        }
    }

    public static bool IsDark(IEnumerable<double> samples, bool current)
    {
        var values = samples.Where(value => double.IsFinite(value) && value >= 0 && value <= 1)
            .OrderBy(value => value).ToArray();
        if (values.Length == 0) return current;

        var middle = values.Length / 2;
        var median = values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
        // Hysteresis prevents repeated theme changes on intermediate backgrounds.
        return current ? median < 0.58 : median < 0.42;
    }
}
