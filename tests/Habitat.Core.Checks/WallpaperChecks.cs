using Habitat;
using Habitat.Services;

internal static class WallpaperChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "wallpaper-preferences.json");
        File.WriteAllText(path, """{"Theme":"hybrid","Opacity":15}""");
        var original = Preferences.Load(path);
        check(original.Theme == "hybrid" && original.Opacity == 15 &&
            original.Wallpaper.SelectedId == WallpaperSettings.Original,
            "Existing profiles retain the original transparent Hybrid 15% background.");
        var image = new WallpaperImage(Guid.NewGuid().ToString("N"), "Mi foto · 夜", 0.12);
        var configured = original with { Wallpaper = new WallpaperSettings
        {
            SelectedId = image.Id, Images = [image], Fit = "fit", Attenuation = 48
        } };
        check(configured.Save(path), "Wallpaper preferences save atomically.");
        var loaded = Preferences.Load(path);
        check(loaded.Wallpaper.SelectedId == image.Id && loaded.Wallpaper.Images.Single().Name == image.Name &&
            loaded.Wallpaper.Fit == "fit" && loaded.Wallpaper.Attenuation == 48,
            "Wallpaper selection, library, user names, fit and attenuation survive a restart.");
        var invalid = new WallpaperSettings { SelectedId = "../preferences.json", Fit = "stretch", Attenuation = double.NaN,
            Images = [new("../preferences.json", "bad", 0), image, image] }.Normalize();
        check(invalid.SelectedId == WallpaperSettings.Original && invalid.Images.Length == 1 &&
            invalid.Attenuation == 35 && invalid.Fit == "fill", "Invalid wallpaper IDs and settings cannot escape the image library.");
        check(new WallpaperSettings { Images = null! }.Normalize().Images.Length == 0,
            "Null wallpaper libraries recover to an empty library.");
        File.WriteAllText(path, """{"Theme":"night","Opacity":42,"Wallpaper":null}""");
        loaded = Preferences.Load(path);
        check(loaded.Theme == "night" && loaded.Opacity == 42 && loaded.Wallpaper.SelectedId == "original",
            "Missing wallpaper settings preserve other appearance choices.");
        check(configured.Wallpaper.IsDark(false) && !new WallpaperSettings { SelectedId = "mist" }.IsDark(true),
            "Hybrid uses image luminance for custom and built-in backgrounds.");
        var bounded = new WallpaperSettings { Images = Enumerable.Range(0, 100)
            .Select(index => new WallpaperImage(Guid.NewGuid().ToString("N"), index.ToString(), 0.5)).ToArray() }.Normalize();
        check(bounded.Images.Length == WallpaperSettings.MaxImages, "Wallpaper libraries have a bounded size.");
        foreach (var illustration in WallpaperSettings.Illustrations)
        {
            var preset = new WallpaperSettings { SelectedId = illustration.Id, Fit = "fit", Images = [image] };
            (original with { Wallpaper = preset }).Save(path);
            var restored = Preferences.Load(path).Wallpaper;
            check(restored.SelectedId == illustration.Id && restored.Fit == "fit" && restored.Images.Single().Id == image.Id,
                "Bundled illustrations survive restart without replacing the personal image library.");
        }
        check(new WallpaperSettings { SelectedId = "orbit" }.IsDark(false) &&
            !new WallpaperSettings { SelectedId = "pencil" }.IsDark(true),
            "The nocturnal and pencil illustrations choose suitable Hybrid contrast.");
    }
}
