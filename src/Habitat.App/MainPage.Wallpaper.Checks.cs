#if HABITAT_UI_CHECKS
using Habitat.Features.Preferences;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckWallpaperUiAsync(string log)
    {
        var original = _preferences;
        Task? showing = null;
        var wallpapers = _services.Wallpapers;
        var source = Path.Combine(_services.Paths.LocalDirectory, "test-photo.png");
        Directory.CreateDirectory(_services.Paths.LocalDirectory);
        using (var file = File.Create(source))
        using (var stream = file.AsRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 2, 1, 96, 96,
                [0, 0, 0, 255, 0, 0, 0, 255]);
            await encoder.FlushAsync();
        }
        try
        {
            foreach (var illustration in WallpaperSettings.Illustrations)
            {
                if (!wallpapers.Exists(illustration.Id)) throw new InvalidOperationException("A bundled wallpaper is missing.");
                var bitmap = await wallpapers.LoadAsync(illustration.Id, 480);
                if (bitmap.PixelWidth <= bitmap.PixelHeight || bitmap.DecodePixelWidth != 480)
                    throw new InvalidOperationException("A bundled wallpaper did not decode as a bounded landscape image.");
                var artwork = new WallpaperSurface(wallpapers);
                var artworkSettings = new WallpaperSettings { SelectedId = illustration.Id, Fit = "fit" };
                if (!await artwork.ApplyAsync(artworkSettings, artworkSettings.IsDark(false), 480) ||
                    artwork.DisplayedId != illustration.Id || !artwork.HasWallpaper)
                    throw new InvalidOperationException("A bundled illustration did not render.");
                wallpapers.Delete([new WallpaperImage(illustration.Id, "built-in", 0)], []);
                if (!wallpapers.Exists(illustration.Id))
                    throw new InvalidOperationException("Library cleanup removed a bundled illustration.");
            }
            var image = await wallpapers.ImportAsync(source);
            File.Delete(source);
            var decoded = await wallpapers.LoadAsync(image.Id, 320);
            if (decoded.PixelWidth <= 0 || image.Luminance > 0.01)
                throw new InvalidOperationException("Wallpaper import did not retain a decodable independent copy.");
            var surface = new WallpaperSurface(wallpapers);
            var settings = new WallpaperSettings { SelectedId = image.Id, Images = [image] };
            if (!await surface.ApplyAsync(settings, true) || !surface.HasWallpaper || surface.IsHitTestVisible)
                throw new InvalidOperationException("Wallpaper decoration did not load or intercepts input.");
            var racingSurface = new WallpaperSurface(wallpapers);
            var pending = racingSurface.ApplyAsync(settings, true);
            await racingSurface.ApplyAsync(new(), false);
            await pending;
            if (racingSurface.HasWallpaper) throw new InvalidOperationException("A stale image replaced the original background.");
            await surface.ApplyAsync(new(), false);
            var invalidSource = Path.Combine(_services.Paths.LocalDirectory, "invalid.png");
            File.WriteAllText(invalidSource, "invalid image");
            var importView = new WallpaperView(settings, wallpapers, () => Task.FromResult<IReadOnlyList<string>>([invalidSource]));
            await importView.AddAsync();
            if (importView.Settings.SelectedId != image.Id || importView.IsBusy)
                throw new InvalidOperationException("An invalid import lost the existing wallpaper.");
            var canceledView = new WallpaperView(settings, wallpapers, () => Task.FromResult<IReadOnlyList<string>>([]));
            await canceledView.AddAsync();
            if (canceledView.Settings.SelectedId != image.Id)
                throw new InvalidOperationException("Canceling the picker changed the wallpaper.");
            var rotatedPath = Path.Combine(_services.Paths.LocalDirectory, "rotated.jpg");
            using (var file = File.Create(rotatedPath))
            using (var stream = file.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 3, 2, 96, 96, new byte[24]);
                await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet
                {
                    { "System.Photo.Orientation", new BitmapTypedValue((ushort)6, Windows.Foundation.PropertyType.UInt16) }
                });
                await encoder.FlushAsync();
            }
            var rotated = await wallpapers.ImportAsync(rotatedPath);
            var portraitBitmap = await wallpapers.LoadAsync(rotated.Id, 480);
            if (portraitBitmap.DecodePixelHeight != 3 || portraitBitmap.DecodePixelWidth != 0)
                throw new InvalidOperationException("Portrait decoding enlarged the image or used the wrong size bound.");
            using (var file = File.OpenRead(wallpapers.ImagePath(rotated.Id)))
            using (var stream = file.AsRandomAccessStream())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.PixelWidth != 2 || decoder.PixelHeight != 3)
                    throw new InvalidOperationException("EXIF orientation distorted a portrait wallpaper.");
            }
            wallpapers.Delete([rotated], []);
            var multiple = new WallpaperView(settings, wallpapers,
                () => Task.FromResult<IReadOnlyList<string>>([wallpapers.ImagePath(image.Id), rotatedPath]));
            await multiple.AddAsync();
            if (multiple.Settings.Images.Length != 3 || multiple.Settings.SelectedId == image.Id)
                throw new InvalidOperationException("Multiple images were not imported into the library.");
            var temporaryImages = multiple.Settings.Images.Where(entry => entry.Id != image.Id).ToArray();
            multiple.Complete(false, settings);
            if (!File.Exists(wallpapers.ImagePath(image.Id)) ||
                temporaryImages.Any(entry => File.Exists(wallpapers.ImagePath(entry.Id))))
                throw new InvalidOperationException("Rolling back an import removed an existing asset or leaked new assets.");
            _preferences = _preferences with { Wallpaper = settings };
            foreach (var language in new[] { "en", "es" })
            {
                _preferences = _preferences with { InterfaceLanguage = language };
                ApplyInterfaceLanguage();
                showing = ShowPreferencesAsync();
                await Task.Delay(150);
                var view = (PreferencesView)_activeDialog!.Content;
                if (AutomationProperties.GetName(view.Wallpaper.Choices) != UiText.Get("Wallpaper"))
                    throw new InvalidOperationException("Wallpaper accessibility did not follow the selected language.");
                foreach (var illustration in WallpaperSettings.Illustrations)
                {
                    var choice = view.Wallpaper.Choices.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == illustration.Id);
                    if (choice.Content as string != UiText.Get(illustration.NameKey))
                        throw new InvalidOperationException("An illustration label did not follow the interface language.");
                    view.Wallpaper.Choices.SelectedItem = choice;
                    if (!view.Wallpaper.Fit.IsEnabled)
                        throw new InvalidOperationException("Bundled illustrations lost their fit controls.");
                }
                view.Wallpaper.Choices.SelectedIndex = 2;
                view.Wallpaper.Attenuation.Value = 52;
                view.Categories.SelectedIndex = 1;
                view.Categories.SelectedIndex = 0;
                _activeDialog.Hide();
                await showing;
                showing = null;
                var saved = Preferences.Load(_services.Paths.PreferencesPath);
                if (saved.Wallpaper.SelectedId != "dusk" || saved.Wallpaper.Attenuation != 52 ||
                    saved.Wallpaper.Images.Single().Name != image.Name)
                    throw new InvalidOperationException("Wallpaper edits were not retained and persisted.");
            }
            showing = ShowPreferencesAsync();
            await Task.Delay(100);
            var restore = (PreferencesView)_activeDialog!.Content;
            restore.Wallpaper.ResetOriginal();
            _activeDialog.Hide();
            await showing;
            showing = null;
            var restored = Preferences.Load(_services.Paths.PreferencesPath);
            if (restored.Theme != "hybrid" || restored.Opacity != 15 || restored.Wallpaper.SelectedId != "original" ||
                restored.Wallpaper.Images.Length != 1 || _wallpaperSurface.HasWallpaper)
                throw new InvalidOperationException("Restore did not return to Hybrid 15% while retaining the library.");
            var preferencePath = _services.Paths.PreferencesPath;
            var savedBytes = File.ReadAllBytes(preferencePath);
            File.Delete(preferencePath);
            Directory.CreateDirectory(preferencePath);
            try
            {
                showing = ShowPreferencesAsync();
                await Task.Delay(100);
                ((PreferencesView)_activeDialog!.Content).Wallpaper.Choices.SelectedIndex = 1;
                _activeDialog.Hide();
                await showing;
                showing = null;
                if (_preferences.Wallpaper.SelectedId != "original" || _wallpaperSurface.HasWallpaper ||
                    !File.Exists(wallpapers.ImagePath(image.Id)))
                    throw new InvalidOperationException("Failed preference save did not roll back appearance and retain existing assets.");
            }
            finally
            {
                Directory.Delete(preferencePath);
                File.WriteAllBytes(preferencePath, savedBytes);
            }
            wallpapers.Delete([image], []);
            if (await surface.ApplyAsync(settings, true) || surface.HasWallpaper)
                throw new InvalidOperationException("Missing wallpaper did not fall back cleanly.");
            File.AppendAllText(log, "PASS: bundled illustration loading/retention/fit in both languages, wallpaper import/copy, invalid files, picker cancellation, non-interactive decoration, missing-file recovery, persistence and restore to Hybrid 15%\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            _preferences = original;
            ApplyInterfaceLanguage();
            ApplyPreferences();
            SavePreferences();
        }
    }
}
#endif
