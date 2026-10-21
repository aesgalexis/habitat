using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Habitat.Services;

// Imported files are decoded, bounded and copied into app-owned immutable PNGs.
// Preferences commit separately; only explicitly retired assets are deleted.
internal sealed class WallpaperService(AppDataPaths paths)
{
    internal string ImagePath(string id)
    {
        if (WallpaperSettings.Illustration(id) is { } illustration)
            return Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers", illustration.FileName);
        return WallpaperSettings.IsImageId(id) ? Path.Combine(paths.WallpapersDirectory, id + ".png")
            : throw new ArgumentException("Invalid wallpaper ID.", nameof(id));
    }

    internal bool Exists(string id) => WallpaperSettings.UsesImage(id)
        ? File.Exists(ImagePath(id)) : WallpaperSettings.BuiltInIds.Contains(id);

    internal async Task<WallpaperImage> ImportAsync(string path) => await Task.Run(async () =>
    {
        using var input = File.OpenRead(path);
        if (input.Length > 25 * 1024 * 1024) throw new InvalidDataException(UiText.Get("WallpaperTooLarge"));
        using var source = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(source);
        if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId &&
            decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId)
            throw new InvalidDataException(UiText.Get("WallpaperInvalid"));
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 ||
            (long)decoder.PixelWidth * decoder.PixelHeight > 60_000_000)
            throw new InvalidDataException(UiText.Get("WallpaperTooLarge"));

        var scale = Math.Min(1d, 3200d / Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
        var width = Math.Max(1u, (uint)(decoder.OrientedPixelWidth * scale));
        var height = Math.Max(1u, (uint)(decoder.OrientedPixelHeight * scale));
        // Scaling precedes EXIF rotation, so its dimensions use source coordinates.
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1u, (uint)(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1u, (uint)(decoder.PixelHeight * scale))
        };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var pixels = data.DetachPixelData();
        // Analyze a small representative grid against the same neutral matte used by the view.
        var luminances = new List<double>();
        var pixelCount = pixels.Length / 4;
        for (var index = 0; index < pixelCount; index += Math.Max(1, pixelCount / 2048))
        {
            var offset = index * 4;
            var alpha = pixels[offset + 3] / 255d;
            luminances.Add((0.2126 * pixels[offset + 2] + 0.7152 * pixels[offset + 1] +
                0.0722 * pixels[offset]) / 255d * alpha + 0.5 * (1 - alpha));
        }
        luminances.Sort();
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        // RespectExifOrientation already returned pixels in display orientation.
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        output.Seek(0);
        using var bytes = new MemoryStream();
        await output.AsStreamForRead().CopyToAsync(bytes);
        var image = new WallpaperImage(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(path),
            luminances[luminances.Count / 2]);
        Directory.CreateDirectory(paths.WallpapersDirectory);
        AtomicFile.Write(ImagePath(image.Id), bytes.ToArray());
        return image;
    });

    internal async Task<BitmapImage> LoadAsync(string id, int decodeWidth = 2560)
    {
        // A memory-backed stream releases the file before the image is displayed.
        var bytes = await File.ReadAllBytesAsync(ImagePath(id));
        using var memory = new MemoryStream(bytes);
        using var stream = memory.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var longestSide = (int)Math.Min((uint)decodeWidth, Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var bitmap = new BitmapImage { AutoPlay = false };
        // Bound the longest edge, including unusually tall portraits; never upscale.
        if (decoder.PixelWidth >= decoder.PixelHeight) bitmap.DecodePixelWidth = longestSide;
        else bitmap.DecodePixelHeight = longestSide;
        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    internal void Delete(IEnumerable<WallpaperImage> retired, IEnumerable<WallpaperImage> retained)
    {
        var keep = retained.Select(image => image.Id).ToHashSet();
        foreach (var image in retired.Where(image => WallpaperSettings.IsImageId(image.Id) && !keep.Contains(image.Id)))
        {
            try { File.Delete(ImagePath(image.Id)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
