using Habitat.Services;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS: {name}");
}

Check(!BackdropThemeDetector.IsDark(Enumerable.Repeat(1d, 10), true), "White backdrop switches to dark text");
Check(BackdropThemeDetector.IsDark(Enumerable.Repeat(0d, 10), false), "Black backdrop switches to light text");
Check(!BackdropThemeDetector.IsDark([0.9, 0.95, 1, 1, 0], true), "A dark outlier does not hide a light backdrop");
Check(BackdropThemeDetector.IsDark([0, 0, 0.05, 0.1, 1], false), "A light outlier does not hide a dark backdrop");
Check(BackdropThemeDetector.IsDark([0.5], true) && !BackdropThemeDetector.IsDark([0.5], false), "Intermediate background keeps the theme stable");
Check(!BackdropThemeDetector.IsDark([0.58], true) && BackdropThemeDetector.IsDark([0.419], false), "Both hysteresis boundaries allow a transition");
Check(BackdropThemeDetector.IsDark([], true) && !BackdropThemeDetector.IsDark([], false), "Unavailable samples preserve the theme");
Check(BackdropThemeDetector.IsDark([double.NaN, double.PositiveInfinity, -1, 2], true), "Invalid samples preserve the theme");
Check(BackdropThemeDetector.IsDark([0.1, 0.9], true), "Even sample counts use both central values");

foreach (var scale in new[] { 1d, 1.25, 1.5, 2, 3 })
{
    var width = (int)(860 * scale);
    var height = (int)(680 * scale);
    var points = BackdropThemeDetector.SamplePoints(width, height, scale).ToArray();
    Check(points.Length == 10 && points.All(point =>
        point.X > 0 && point.X < width && point.Y >= 140 * scale && point.Y < height &&
        (point.X < 30 * scale || point.X > width - 30 * scale)),
        $"Samples remain inside blank client gutters at scale {scale}");
}
Check(!BackdropThemeDetector.SamplePoints(20, 100, 1).Any(), "Tiny client areas provide no samples");
