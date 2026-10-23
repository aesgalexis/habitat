#if HABITAT_UI_CHECKS
using Habitat.Features.Markets;
using Habitat.Features.Preferences;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckMarketsUiAsync(string log)
    {
        var originalPreferences = _preferences;
        var originalService = _services.Markets;
        var originalSession = _marketsSession;
        var originalDestination = _destination;
        var originalDraft = Composer.Text;
        var originalChatDraft = _composerDrafts.Text(ComposerDestination.OpenAI);
        using var http = new HttpClient(new MarketUiHandler());
        using var service = new MarketsService(http);
        Task? showing = null;
        try
        {
            _preferences = _preferences with { SidebarWidgets = SidebarCatalog.Available.ToArray() };
            ApplySidebarPreferences();
            _services.Markets = service;
            _marketsSession = new MarketsSession(service);
            _marketsSession.Changed += RenderMarkets;
            _marketsSession.Configure(new() { Symbols = ["AAPL", "MSFT"], PinnedSymbol = "AAPL" });
            await _marketsSession.RefreshAsync(_operations.Token);
            foreach (var language in new[] { "en", "es" })
            {
                _preferences = _preferences with { InterfaceLanguage = language, Markets = _marketsSession.Settings };
                ApplyInterfaceLanguage();
                foreach (var expanded in new[] { false, true })
                {
                    _sidebarManualExpanded = expanded;
                    UpdateWritingLayout();
                    UpdateLayout();
                    if (Grid.GetRow(MarketsButton) != 3 || MarketsButton.ActualWidth != _widgets.ActualWidth ||
                        MarketsDetails.Visibility != (expanded ? Visibility.Visible : Visibility.Collapsed) ||
                        MarketsCompactChange.Visibility != (expanded ? Visibility.Collapsed : Visibility.Visible) ||
                        !AutomationProperties.GetName(MarketsButton).StartsWith(UiText.Get("Markets")))
                        throw new InvalidOperationException("Markets widget lost its fourth row, width, compact quote or localized accessibility.");
                    var marketTop = MarketsButton.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;
                    var taskBottom = TasksButton.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, TasksButton.ActualHeight)).Y;
                    if (marketTop < taskBottom + 12) throw new InvalidOperationException("Markets overlaps the Tasks widget.");
                    SetTasksOpen(true);
                    UpdateLayout();
                    var panelTop = _taskArea.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;
                    if (panelTop < marketTop + MarketsButton.ActualHeight + 16) throw new InvalidOperationException("Tasks opens over the Markets widget.");
                    SetTasksOpen(false);
                }
                showing = ShowMarketsAsync();
                await Task.Delay(100);
                File.AppendAllText(log, $"Markets panel opened in {language}\n");
                if (_activeDialog?.Content is not MarketsView view || !_marketsPanelOpen || !view.Explain.IsEnabled ||
                    view.Explain.Content as string != UiText.Get("MarketsExplain") || view.SelectedSymbol != "AAPL")
                    throw new InvalidOperationException("Markets dialog, selection, localized labels or explanation action failed.");
                if (_activeDialog.Title as string != UiText.Get("Markets")) throw new InvalidOperationException("Markets title did not follow the interface language.");
                SelectDestination(ComposerDestination.OpenAI);
                Composer.Text = "Keep this exact user draft · mañana";
                InvokeMarketButton(view.Explain);
                await showing;
                showing = null;
                File.AppendAllText(log, $"Markets explanation returned to the editor in {language}\n");
                if (!Composer.Text.ReplaceLineEndings("\n").StartsWith("Keep this exact user draft · mañana\n\n") ||
                    !Composer.Text.Contains(UiText.Get("MarketsQuestion").Split('{')[0]) || !Composer.Text.Contains("https://finance.yahoo.com/quote/AAPL/") ||
                    _bridge?.HasMessageInFlight == true || HasDestinationSection)
                    throw new InvalidOperationException("Market question replaced a draft, sent automatically or forced a results section.");
            }
            showing = ShowMarketsAsync();
            await Task.Delay(100);
            InvokeMarketButton(((MarketsView)_activeDialog!.Content).Manage);
            for (var attempt = 0; attempt < 40 && _activeDialog?.Content is not PreferencesView; attempt++) await Task.Delay(50);
            if (_activeDialog?.Content is not PreferencesView preferences || preferences.Category != "Markets")
                throw new InvalidOperationException("Manage favorites did not open the Markets preference category.");
            preferences.Markets.SymbolInput.Text = " nvda ";
            InvokeMarketButton(preferences.Markets.AddButton);
            preferences.Markets.Pinned.SelectedItem = preferences.Markets.Pinned.Items.Cast<ComboBoxItem>().First(item => item.Tag as string == "NVDA");
            _activeDialog.Hide();
            await showing;
            showing = null;
            var saved = Preferences.Load(_services.Paths.PreferencesPath).Markets;
            if (!saved.Symbols.SequenceEqual(["AAPL", "MSFT", "NVDA"]) || saved.PinnedSymbol != "NVDA" || _marketsSession.CurrentSymbol != "NVDA")
                throw new InvalidOperationException("Favorites and pin did not survive saving and session reconfiguration.");
            File.AppendAllText(log, "PASS: Markets fourth sidebar row, both widths and languages, native panel, retained chat draft without sending, editable favorites and persisted pin; synthetic data only\n");
        }
        finally
        {
            if (showing is not null)
            {
                for (var attempt = 0; attempt < 50 && !showing.IsCompleted; attempt++)
                {
                    _activeDialog?.Hide();
                    await Task.WhenAny(showing, Task.Delay(100));
                }
                await showing.WaitAsync(TimeSpan.FromSeconds(1));
            }
            _marketsSession = originalSession;
            _services.Markets = originalService;
            _marketNews.Clear();
            _preferences = originalPreferences;
            ApplySidebarPreferences();
            ApplyInterfaceLanguage();
            SelectDestination(ComposerDestination.OpenAI);
            Composer.Text = originalChatDraft;
            SelectDestination(originalDestination);
            Composer.Text = originalDraft;
            _sidebarManualExpanded = null;
            UpdateWritingLayout();
            SavePreferences();
        }
    }

    private static void InvokeMarketButton(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private sealed class MarketUiHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            var symbol = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            var body = request.RequestUri.Host == "feeds.finance.yahoo.com" ? "<rss><channel /></rss>" :
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    chart = new { result = new[] { new {
                        meta = new { symbol, currency = "USD", longName = "Synthetic asset", regularMarketPrice = 105, regularMarketTime = 1792508400, regularMarketChangePercent = 5 },
                        timestamp = new[] { 1792157400, 1792416600, 1792503000 },
                        indicators = new { quote = new[] { new { close = new[] { 90, 100, 105 } } } }
                    } }, error = (object?)null }
                });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
#endif
