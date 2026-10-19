#if HABITAT_UI_CHECKS
using Habitat.Features.Search;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task ExportChromeStoreAssetsAsync(Window window)
    {
        if (!_services.Paths.IsIsolated) throw new InvalidOperationException("Store screenshots require isolated data.");
        RequestedTheme = ElementTheme.Dark;
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 23, 36));
        Width = 1280;
        Height = 800;
        var scale = XamlRoot.RasterizationScale;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1300 * scale), (int)(850 * scale)));
        SelectDestination(ComposerDestination.Google);
        Composer.Text = "";
        SetStatus("Browser extension preview", StatusTone.Active);
        await CaptureAsync("google-start-1280x800.png");
        _services.Search = new StoreSearchExample();
        Composer.Text = "Microsoft WinUI documentation";
        await LaunchDestinationAsync(ComposerDestination.Google, Composer.Text);
        await CaptureAsync("google-results-1280x800.png");

        async Task CaptureAsync(string name)
        {
            UpdateLayout();
            await Task.Delay(250);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(this, 1280, 800);
            var pixels = await bitmap.GetPixelsAsync();
            var file = await StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory, "HABITAT.exe"));
            var folder = await file.GetParentAsync();
            var image = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            using var stream = await image.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
            await encoder.FlushAsync();
        }
    }

    private sealed class StoreSearchExample : ISearchService
    {
        public void Reset() { }
        public Task<SearchPage> SearchAsync(string query, int start, CancellationToken token) => Task.FromResult(new SearchPage([
            new("WinUI documentation — Microsoft Learn", "https://learn.microsoft.com/windows/apps/winui/",
                "Build native Windows apps with modern controls, layouts and accessibility. Sample content for this store screenshot."),
            new("Windows App SDK — Microsoft Learn", "https://learn.microsoft.com/windows/apps/windows-app-sdk/",
                "Explore the Windows App SDK and get started with a desktop project. Sample content for this store screenshot."),
            new("Microsoft UI XAML — GitHub", "https://github.com/microsoft/microsoft-ui-xaml",
                "Source code and community resources for Windows UI. Sample content for this store screenshot.")
        ]));
    }
}
#endif
