#if HABITAT_UI_CHECKS
using Habitat.Features.Search;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckSearchUiAsync(string logPath, ComposerDestination target = ComposerDestination.Google)
    {
        var isYouTube = target == ComposerDestination.YouTube;
        var provider = isYouTube ? "YouTube" : "Google";
        var other = isYouTube ? ComposerDestination.Google : ComposerDestination.YouTube;
        var saved = isYouTube ? _services.YouTubeSearch : _services.Search;
        var launcher = _launchBrowser;
        var theme = RequestedTheme;
        var fake = new SearchCheckService { YouTube = isYouTube };
        var section = (SearchSection)_destinationSections[target];
        var peer = (SearchSection)_destinationSections[other];
        Uri? opened = null;
        var launches = 0;
        try
        {
            if (isYouTube) _services.YouTubeSearch = fake; else _services.Search = fake;
            _launchBrowser = uri => { launches++; opened = uri; return Task.FromResult(true); };
            SelectDestination(target);
            Composer.Text = "café & snow 雪";
            var query = Composer.Text;
            if (section.HasContent || fake.Calls != 0) throw new Exception("Selecting Google must not start a search.");
            await LaunchDestinationAsync(target, query);
            if (fake.LastQuery != query || section.Results.Count != 1 || !section.HasContent || Composer.Text != query ||
                launches != 0 || Conversation.Children.Count != 0 || _bridge!.HasMessageInFlight ||
                !ReferenceEquals(_destinationContent.Content, section.View) || ConversationScroll.Visibility != Visibility.Collapsed)
                throw new Exception("Google must show native results, retain its query and avoid launching the browser or sending to chat.");
            foreach (var mode in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                RequestedTheme = mode;
                await Task.Delay(80);
                section.View.UpdateLayout();
                if (!ContainsText(section.View, "Título ☕ <b>plain</b>") || !ContainsText(section.View, "Descripción & 雪") ||
                    section.View.ActualHeight <= 0 || ContainsWebView(section.View))
                    throw new Exception("Search titles and snippets must render as selectable native text in both themes.");
            }
            await section.OpenResultAsync(section.Results[0]);
            if (launches != 1 || opened?.AbsoluteUri != fake.FirstUrl) throw new Exception("A result must open its exact link only when clicked.");
            if (!section.CanGoNext || section.CanGoPrevious) throw new Exception("The first search page must enable only available next navigation.");
            await section.NextAsync();
            if (fake.LastStart != 10 || !section.CanGoPrevious || section.CanGoNext || section.Results[0].Title != "Second page")
                throw new Exception("Next page must follow Google's returned offset.");
            await section.PreviousAsync();
            if (fake.LastStart != 0 || section.CanGoPrevious || !section.CanGoNext) throw new Exception("Previous page must recover the preceding search page.");
            SelectDestination(other);
            if (ReferenceEquals(_destinationContent.Content, section.View)) throw new Exception("Google results must belong only to the search destination.");
            Composer.Text = "Independent draft";
            SelectDestination(target);
            if (Composer.Text != query || section.Results.Count != 1) throw new Exception("Returning to search must restore its own draft and results.");

            fake.Error = new BrowserSearchException("Open your selected browser and reload the HABITAT extension, then retry.");
            await section.SubmitAsync(query);
            if (section.Results.Count != 0 || !section.StatusText.StartsWith("Open your selected browser") || Composer.Text != query)
                throw new Exception("Search errors must explain recovery and discard stale results without losing the query.");
            fake.Error = null;
            await section.RetryAsync();
            if (section.Results.Count != 1 || fake.LastQuery != query) throw new Exception("Search retry must reuse its own query.");
            _launchBrowser = _ => Task.FromResult(false);
            await section.OpenResultAsync(section.Results[0]);
            if (!section.StatusText.StartsWith("Could not open") || section.Results.Count != 1) throw new Exception("Browser opening failure must retain the results.");
            _launchBrowser = uri => { launches++; opened = uri; return Task.FromResult(true); };
            await section.OpenSearchAsync();
            if (opened != BrowserSearchService.PageUri(target, query, 0)) throw new Exception("Open in browser must use the current search and page.");

            fake.Pending = new TaskCompletionSource<SearchPage>();
            var pending = LaunchDestinationAsync(target, query);
            var calls = fake.Calls;
            SendCurrentMessage();
            if (SendButton.IsEnabled || fake.Calls != calls) throw new Exception("A pending search must reject duplicate submissions.");
            SelectDestination(ComposerDestination.GoogleMaps);
            if (!SendButton.IsEnabled || _destinationSections.ContainsKey(ComposerDestination.GoogleMaps)) throw new Exception("Google search must not take over Maps or block other destinations.");
            SelectDestination(target);
            section.Cancel();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            if (section.IsBusy || section.Results.Count != 0 || section.StatusText != "Search canceled.") throw new Exception("Search cancel must release an unresponsive browser immediately.");
            fake.Pending.SetResult(new SearchPage([new("Stale result", "https://example.com/late", "")]));
            fake.Pending = null;
            await Task.Delay(20);
            if (section.Results.Count != 0) throw new Exception("Canceled search results must never appear later.");

            await section.SubmitAsync(query);
            if (isYouTube)
            {
                Composer.Text = "";
                await Task.Delay(80);
                if (fake.Resets != 0 || !section.HasContent)
                    throw new Exception("Replacing a YouTube query must not immediately close its browser tab.");
                Composer.Text = "A different YouTube query";
                var beforeReplacement = fake.Calls;
                SendCurrentMessage();
                await Task.Delay(900);
                if (fake.Calls != beforeReplacement + 1 || fake.LastQuery != Composer.Text || fake.Resets != 0 || !section.HasContent)
                    throw new Exception("A consecutive YouTube search must cancel the empty-field reset and preserve its browser tab.");
                Composer.Text = query;
                await LaunchDestinationAsync(target, query);
                File.AppendAllText(logPath, "PASS: replacing the YouTube query through an empty editor and sending again preserves its browser tab; a stale empty-field timer cannot close the new search\n");
            }
            SelectDestination(other);
            Composer.Text = "";
            if (!section.HasContent || fake.Resets != 0) throw new Exception("Clearing another destination must not reset Google.");
            SelectDestination(target);
            if (!section.HasContent || fake.Resets != 0) throw new Exception("Restoring a Google draft must not reset its results.");
            Composer.Text = "";
            await Task.Delay(isYouTube ? 900 : 40);
            if (section.HasContent || section.Results.Count != 0 || section.IsBusy || fake.Resets != 1 ||
                _destinationContent.Visibility != Visibility.Collapsed || Composer.VerticalAlignment != VerticalAlignment.Top ||
                !ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), Composer))
                throw new Exception($"An empty Google query must reset results and request tab closure, return to the centered editor and retain focus. Content={section.HasContent}, results={section.Results.Count}, busy={section.IsBusy}, resets={fake.Resets}, visibility={_destinationContent.Visibility}, alignment={Composer.VerticalAlignment}, focus={Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name}, text={Composer.Text.Length}.");
            Composer.Text = query;
            await LaunchDestinationAsync(target, query);
            if (!ContainsText(section.View, "Open in browser") || !ContainsText(section.View, "Clear search"))
            {
                await Task.Delay(40);
                section.View.UpdateLayout();
                if (!ContainsText(section.View, "Open in browser") || !ContainsText(section.View, "Clear search"))
                    throw new Exception("Search needs visible English Open in browser and Clear search buttons.");
            }
            var peerResults = peer.Results;
            var peerContent = peer.HasContent;
            section.RequestClear();
            if (Composer.Text != "" || section.HasContent || section.CanGoNext || section.CanGoPrevious || fake.Resets != 2)
                throw new Exception("Clear search must empty the composer, results and pagination using the same reset action.");
            if (!ReferenceEquals(peer.Results, peerResults) || peer.HasContent != peerContent)
                throw new Exception("Resetting a search must preserve the other provider's results and state.");

            fake.Pending = new TaskCompletionSource<SearchPage>();
            Composer.Text = "Old query";
            var oldSearch = LaunchDestinationAsync(target, Composer.Text);
            var oldResponse = fake.Pending;
            section.RequestClear();
            if (section.IsBusy || section.HasContent || !fake.LastToken.IsCancellationRequested || fake.Resets != 3)
                throw new Exception("Reset must immediately cancel and hide an unfinished search.");
            fake.Pending = new TaskCompletionSource<SearchPage>();
            Composer.Text = "New query";
            var newSearch = LaunchDestinationAsync(target, Composer.Text);
            oldResponse.SetResult(new SearchPage([new("Stale", "https://example.com/old", "")]));
            await oldSearch;
            if (!section.IsBusy || !section.HasContent || section.Results.Count != 0 || Composer.Text != "New query")
                throw new Exception("Finishing a reset search must not clear or unlock the newer search.");
            fake.Pending.SetResult(new SearchPage([new("Fresh result", "https://example.com/new", "Fresh description")]));
            await newSearch;
            fake.Pending = null;
            if (section.Results.Single().Title != "Fresh result") throw new Exception("A fresh search must succeed immediately after reset.");
            File.AppendAllText(logPath, $"PASS: empty {provider} query and Clear search reset query/results/pages, preserve other destinations, request tab closure, restore centered focus and reject stale results while a new search runs\n");
            fake.Empty = true;
            await section.SubmitAsync("No matches");
            if (section.StatusText != "No results found. Try another search." || section.CanGoNext) throw new Exception("Search needs an explicit empty state.");
            fake.Pending = new TaskCompletionSource<SearchPage>();
            using var delayed = new SearchSection(fake.SearchAsync, _ => Task.FromResult(false), _ => { }, target);
            pending = delayed.SubmitAsync(query);
            delayed.Dispose();
            await pending;
            fake.Pending.SetResult(new SearchPage([new("Late", "https://example.com/late", "")]));
            if (delayed.Results.Count != 0 || !fake.LastToken.IsCancellationRequested) throw new Exception("Closing search must cancel work and ignore late results.");
            File.AppendAllText(logPath, $"PASS: {provider} native titles/links/snippets in both themes; exact queries; no WebView/chat/browser launch on search; pagination, isolated modes, links, errors, retry, empty state, cancel and shutdown protection (browser simulated)\n");
            if (isYouTube) await CheckYouTubeThumbnailsUiAsync(logPath);
        }
        finally
        {
            if (isYouTube) _services.YouTubeSearch = saved; else _services.Search = saved;
            _launchBrowser = launcher;
            RequestedTheme = theme;
        }

        static bool ContainsText(DependencyObject parent, string text)
        {
            if (parent is TextBlock block && block.Text == text && block.ActualHeight > 0) return true;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (ContainsText(VisualTreeHelper.GetChild(parent, i), text)) return true;
            return false;
        }
        static bool ContainsWebView(DependencyObject parent)
        {
            if (parent is WebView2) return true;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (ContainsWebView(VisualTreeHelper.GetChild(parent, i))) return true;
            return false;
        }
    }

    private sealed class SearchCheckService : ISearchService
    {
        internal int Calls, LastStart;
        internal string? LastQuery;
        internal CancellationToken LastToken;
        internal Exception? Error;
        internal TaskCompletionSource<SearchPage>? Pending;
        internal bool Empty;
        internal bool YouTube;
        internal string FirstUrl => YouTube ? "https://www.youtube.com/watch?v=abcdefghijk" : "https://example.com/one";
        internal int Resets;
        public void Reset() => Resets++;
        public Task<SearchPage> SearchAsync(string query, int start, CancellationToken token)
        {
            Calls++;
            LastQuery = query;
            LastStart = start;
            LastToken = token;
            return Error is { } error ? Task.FromException<SearchPage>(error) : Pending?.Task ??
                Task.FromResult(Empty ? new SearchPage([]) : start == 0
                    ? new SearchPage([new("Título ☕ <b>plain</b>", FirstUrl, "Descripción & 雪")], 10)
                    : new SearchPage([new("Second page", "https://example.com/two", "Second fragment")]));
        }
    }
}
#endif
