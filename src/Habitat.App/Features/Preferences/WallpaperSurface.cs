using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Habitat.Features.Preferences;

// Decorative, non-interactive layer shared by the full window and the preview.
internal sealed class WallpaperSurface : Grid
{
    private readonly WallpaperService _service;
    private readonly Border _picture = new();
    private readonly Border _veil = new();
    private string? _loadedId;
    private string? _pendingId;
    private Task<BitmapImage>? _pendingImage;
    private int _revision;
    internal string? DisplayedId => _loadedId;
    internal bool HasWallpaper => Visibility == Visibility.Visible;

    internal WallpaperSurface(WallpaperService service)
    {
        _service = service;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
        Children.Add(_picture);
        Children.Add(_veil);
    }

    internal async Task<bool> ApplyAsync(WallpaperSettings settings, bool dark, int decodeWidth = 2560)
    {
        var revision = ++_revision;
        if (settings.SelectedId == WallpaperSettings.Original)
        {
            _picture.Background = null;
            _loadedId = null;
            _pendingId = null;
            _pendingImage = null;
            Visibility = Visibility.Collapsed;
            return true;
        }
        try
        {
            if (_loadedId != settings.SelectedId)
            {
                Brush brush;
                if (!WallpaperSettings.UsesImage(settings.SelectedId))
                    brush = BuiltInBrush(settings.SelectedId);
                else
                {
                    if (_pendingId != settings.SelectedId || _pendingImage is null)
                    {
                        _pendingId = settings.SelectedId;
                        _pendingImage = _service.LoadAsync(settings.SelectedId, decodeWidth);
                    }
                    brush = new ImageBrush { ImageSource = await _pendingImage };
                }
                if (revision != _revision) return true;
                _picture.Background = brush;
                _loadedId = settings.SelectedId;
                _pendingId = null;
                _pendingImage = null;
            }
            if (_picture.Background is ImageBrush image)
            {
                image.Stretch = settings.Fit == "fit" ? Stretch.Uniform : Stretch.UniformToFill;
                // Keep the illustrated subjects visible when a narrow window crops the landscape.
                image.AlignmentX = settings.Fit == "fill" && settings.SelectedId is "pencil" or "botanical" or "orbit"
                    ? AlignmentX.Right : AlignmentX.Center;
            }
            _veil.Background = new SolidColorBrush(dark ? Color.FromArgb(255, 16, 22, 35) : Color.FromArgb(255, 242, 246, 251));
            _veil.Opacity = settings.Attenuation / 100;
            Visibility = Visibility.Visible;
            return true;
        }
        catch (Exception)
        {
            if (revision != _revision) return true;
            _picture.Background = null;
            _loadedId = null;
            _pendingId = null;
            _pendingImage = null;
            Visibility = Visibility.Collapsed;
            return false;
        }
    }

    internal static Brush BuiltInBrush(string id)
    {
        var colors = id switch
        {
            "mist" => new[] { 0xDCE8EF, 0xB7CAC9, 0xE8DBCD },
            "forest" => new[] { 0x122F32, 0x36594D, 0x728476 },
            _ => new[] { 0x151D38, 0x4A3D68, 0x966B7B }
        };
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        for (var index = 0; index < colors.Length; index++)
            brush.GradientStops.Add(new GradientStop { Offset = index / 2d,
                Color = Color.FromArgb(255, (byte)(colors[index] >> 16), (byte)(colors[index] >> 8), (byte)colors[index]) });
        return brush;
    }
}
