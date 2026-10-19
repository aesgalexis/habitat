#if HABITAT_UI_CHECKS
using Habitat.Features.Search;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckYouTubeThumbnailsUiAsync(string logPath)
    {
        var savedSearch = _services.YouTubeSearch;
        var savedImages = _services.SearchThumbnail;
        var savedLauncher = _launchBrowser;
        var search = new ThumbnailCheckService();
        var section = (SearchSection)_destinationSections[ComposerDestination.YouTube];
        var requests = new List<Uri>();
        try
        {
            _services.YouTubeSearch = search;
            _services.SearchThumbnail = uri => { requests.Add(uri); return new WriteableBitmap(2, 2); };
            SelectDestination(ComposerDestination.YouTube);
            Composer.Text = "Synthetic thumbnails";
            await LaunchDestinationAsync(ComposerDestination.YouTube, Composer.Text);
            await Task.Delay(120);
            section.View.UpdateLayout();
            var images = Descendants<Image>(section.View).ToArray();
            var buttons = Descendants<Button>(section.View).Where(button => AutomationProperties.GetName(button).StartsWith("Open video: ")).ToArray();
            var initial = requests.Count;
            if (images.Length != 20 || buttons.Length != 20 || initial <= 0 || initial >= 20 ||
                requests.Any(uri => uri.Host != "i.ytimg.com" || !uri.AbsolutePath.EndsWith("/mqdefault.jpg")))
                throw new Exception($"YouTube thumbnails must load only in the viewport: images={images.Length}, buttons={buttons.Length}, requests={initial}.");
            if (buttons.Any(button => button.ActualWidth < 96 || button.ActualWidth > 160.1 ||
                Math.Abs(button.ActualHeight - button.ActualWidth * 9 / 16) > 1))
                throw new Exception("YouTube thumbnail slots must remain compact and preserve 16:9.");

            Uri? opened = null;
            _launchBrowser = uri => { opened = uri; return Task.FromResult(true); };
            ((IInvokeProvider)new ButtonAutomationPeer(buttons[0]).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(40);
            if (opened?.AbsoluteUri != search.Page.Results[0].Url)
                throw new Exception("Clicking a thumbnail must open its own exact video link.");

            var scroll = Descendants<ScrollViewer>(section.View).First(view => view.Content is StackPanel);
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Task.Delay(100);
            if (requests.Count <= initial || images[^1].Source is null)
                throw new Exception("Scrolling must load the newly visible video thumbnails.");
            var afterScroll = requests.Count;
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(100);
            if (requests.Count > 20 || requests.Select(uri => uri.AbsoluteUri).Distinct().Count() != requests.Count)
                throw new Exception("Scrolling back must reuse thumbnails already loaded on the page.");
            SelectDestination(ComposerDestination.Google);
            await Task.Delay(80);
            if (images.Any(image => image.Source is not null))
                throw new Exception("Leaving YouTube must release the image sources from the detached section.");
            SelectDestination(ComposerDestination.YouTube);
            await Task.Delay(100);
            if (requests.Count <= afterScroll || images[0].Source is null)
                throw new Exception("Returning to YouTube must restore thumbnails for the visible results.");

            section.RequestClear();
            await Task.Delay(80);
            if (images.Any(image => image.Source is not null) || section.Results.Count != 0 || Composer.Text.Length != 0)
                throw new Exception("Clear search must release thumbnail sources and clear the associated query.");
            _services.SearchThumbnail = _ => throw new IOException("Synthetic thumbnail failure");
            Composer.Text = "Failed thumbnails";
            await LaunchDestinationAsync(ComposerDestination.YouTube, Composer.Text);
            await Task.Delay(100);
            var failedImages = Descendants<Image>(section.View).ToArray();
            if (section.Results.Count != 20 || failedImages.Length != 20 || failedImages.Any(image => image.Source is not null) ||
                !Descendants<FontIcon>(section.View).Any(icon => icon.ActualWidth > 0))
                throw new Exception("Image failures must retain the results and a fixed-size fallback without blocking search.");
            section.RequestClear();
            File.AppendAllText(logPath, $"PASS: YouTube thumbnails use native images and safe small JPEG URLs; initially {initial}/20 requested; scrolling loads visible rows once; thumbnail click, aspect ratio, mode switch, reset and image failures verified without network\n");

            var channel = new SearchResult("Habitat Studio", "https://www.youtube.com/@habitatstudio", "@habitatstudio · 12K subscribers");
            search.Page = new SearchPage([channel, search.Page.Results[0]], 20);
            requests.Clear();
            _services.SearchThumbnail = uri => { requests.Add(uri); return new WriteableBitmap(2, 2); };
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                RequestedTheme = theme;
                Composer.Text = "Habitat Studio";
                await LaunchDestinationAsync(ComposerDestination.YouTube, Composer.Text);
                await Task.Delay(100);
                section.View.UpdateLayout();
                var titles = Descendants<HyperlinkButton>(section.View).ToArray();
                if (section.Results[0] != channel || titles.Length != 2 || AutomationProperties.GetName(titles[0]) != channel.Title ||
                    AutomationProperties.GetHelpText(titles[0]) != "Open channel in your browser. " + channel.Url ||
                    !Descendants<TextBlock>(section.View).Any(block => block.Text == "Channel" && block.ActualHeight > 0) ||
                    Descendants<Image>(section.View).Count() != 1 || !section.CanGoNext)
                    throw new Exception("A matching channel must render before videos with an English label and accessible link, without a video thumbnail, in both themes.");
                opened = null;
                ((IInvokeProvider)new HyperlinkButtonAutomationPeer(titles[0]).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(40);
                if (opened?.AbsoluteUri != channel.Url || requests.Any(uri => uri.AbsoluteUri.Contains("habitatstudio")))
                    throw new Exception("A channel click must open its exact channel URL and never request a video thumbnail for it.");
            }
            section.RequestClear();
            if (section.HasContent || section.Results.Count != 0 || Composer.Text.Length != 0)
                throw new Exception("Clearing channel results must restore the shared initial search screen.");
            File.AppendAllText(logPath, "PASS: matching YouTube channel precedes videos in both themes, has an English Channel label and accessible browser link, uses no video thumbnail and clears with the shared search state\n");

            if (Environment.GetCommandLineArgs().Contains("--thumbnails-live-smoke"))
            {
                search.Page = new SearchPage([new("Synthetic live thumbnail", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "Small public JPEG check")]);
                BitmapImage? bitmap = null;
                _services.SearchThumbnail = uri => bitmap = AppServices.CreateSearchThumbnail(uri);
                Composer.Text = "Synthetic live thumbnail";
                await LaunchDestinationAsync(ComposerDestination.YouTube, Composer.Text);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (bitmap is null || bitmap.PixelWidth == 0) await Task.Delay(50, deadline.Token);
                if (bitmap.PixelWidth != 320 || bitmap.PixelHeight != 180 || bitmap.DecodePixelWidth != 320 || bitmap.AutoPlay)
                    throw new Exception("The real YouTube thumbnail must decode as a still 320×180 image.");
                File.AppendAllText(logPath, "PASS: real YouTube CDN JPEG displayed through native WinUI BitmapImage at 320x180 with autoplay disabled\n");
                section.RequestClear();
            }
        }
        finally
        {
            _services.YouTubeSearch = savedSearch;
            _services.SearchThumbnail = savedImages;
            _launchBrowser = savedLauncher;
        }

        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }
    }

    private sealed class ThumbnailCheckService : ISearchService
    {
        internal SearchPage Page = new(Enumerable.Range(1, 20).Select(index => new SearchResult(
            "Synthetic video " + index, "https://www.youtube.com/watch?v=" + index.ToString().PadLeft(11, 'v'),
            "Synthetic channel · 12:34\nSmall thumbnail check")).ToArray());
        public Task<SearchPage> SearchAsync(string query, int start, CancellationToken token) => Task.FromResult(Page);
        public void Reset() { }
    }
}
#endif
