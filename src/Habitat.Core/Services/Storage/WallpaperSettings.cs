namespace Habitat.Services;

internal sealed record WallpaperImage(string Id, string Name, double Luminance);
internal sealed record WallpaperIllustration(string Id, string NameKey, string FileName, double Luminance);

internal sealed record WallpaperSettings
{
    public const string Original = "original";
    public const int MaxImages = 40;
    public string SelectedId { get; init; } = Original;
    public string Fit { get; init; } = "fill";
    public double Attenuation { get; init; } = 35;
    public WallpaperImage[] Images { get; init; } = [];
    public static readonly WallpaperIllustration[] Illustrations =
    [
        new("pencil", "WallpaperPencil", "pencil.png", 0.92),
        new("dunes", "WallpaperDunes", "dunes.png", 0.88),
        new("tide", "WallpaperTide", "tide.png", 0.84),
        new("botanical", "WallpaperBotanical", "botanical.png", 0.91),
        new("orbit", "WallpaperOrbit", "orbit.png", 0.10)
    ];
    public static readonly string[] BuiltInIds = [Original, "mist", "dusk", "forest", .. Illustrations.Select(image => image.Id)];

    public static bool IsImageId(string? id) => Guid.TryParseExact(id, "N", out _);
    public static WallpaperIllustration? Illustration(string id) => Illustrations.FirstOrDefault(image => image.Id == id);
    public static bool UsesImage(string id) => IsImageId(id) || Illustration(id) is not null;

    public WallpaperSettings Normalize()
    {
        var images = (Images ?? []).Where(image => image is not null && IsImageId(image.Id))
            .DistinctBy(image => image.Id).Take(MaxImages)
            .Select(image => image with
            {
                Name = string.IsNullOrWhiteSpace(image.Name) ? image.Id : image.Name[..Math.Min(image.Name.Length, 160)],
                Luminance = double.IsFinite(image.Luminance) ? Math.Clamp(image.Luminance, 0, 1) : 0.5
            }).ToArray();
        return this with
        {
            Images = images,
            SelectedId = BuiltInIds.Contains(SelectedId) || images.Any(image => image.Id == SelectedId) ? SelectedId : Original,
            Fit = Fit == "fit" ? "fit" : "fill",
            Attenuation = double.IsFinite(Attenuation) ? Math.Clamp(Attenuation, 0, 85) : 35
        };
    }

    public double? Luminance => SelectedId switch
    {
        Original => null,
        "mist" => 0.78,
        "dusk" => 0.23,
        "forest" => 0.29,
        _ => Illustration(SelectedId)?.Luminance ?? Images.FirstOrDefault(image => image.Id == SelectedId)?.Luminance
    };

    public bool IsDark(bool current) => Luminance is double luminance
        ? BackdropThemeDetector.IsDark([luminance], current) : current;
}
