#if HABITAT_UI_CHECKS
using Habitat.Features.Preferences;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckSidebarPreferencesUiAsync(string log)
    {
        var original = _preferences;
        var originalSession = _marketsSession;
        var originalDraft = Composer.Text;
        var handler = new MarketUiHandler();
        using var http = new HttpClient(handler);
        using var service = new MarketsService(http);
        Task? showing = null;
        try
        {
            _marketsSession = new MarketsSession(service);
            _marketsSession.Configure(original.Markets);
            Composer.Text = "Keep my draft · mañana";
            foreach (var language in new[] { "en", "es" })
            {
                _preferences = _preferences with { SidebarWidgets = SidebarCatalog.Defaults, InterfaceLanguage = language };
                ApplyInterfaceLanguage();
                ApplySidebarPreferences();
                if (MarketsButton.Parent is not null || _widgets.RowDefinitions.Count != 3)
                    throw new InvalidOperationException("New profiles must omit Markets without leaving an empty row.");
                TickMarkets();
                if (handler.Requests != 0 || _marketsSession.IsRefreshing)
                    throw new InvalidOperationException("Disabled Markets started a periodic request.");
                if ((SidebarToggle.ContextFlyout as MenuFlyout)?.Items[0] is not MenuFlyoutItem item ||
                    item.Text != UiText.Get("SidebarCustomize") || _widgets.ContextFlyout is not MenuFlyout)
                    throw new InvalidOperationException("Sidebar customization context menus are missing or untranslated.");
                showing = ShowPreferencesAsync("Sidebar");
                await Task.Delay(120);
                if (_activeDialog?.Content is not PreferencesView view || view.Category != "Sidebar")
                    throw new InvalidOperationException("Sidebar preferences did not open directly.");
                var settings = view.Sidebar;
                if (!Descendants(settings).OfType<TextBlock>().Any(text => text.Text == UiText.Get("SidebarFixedWeather")))
                    throw new InvalidOperationException("Fixed Weather explanation is missing.");
                var choices = Descendants(settings).OfType<ComboBox>().Single();
                if (choices.Items.Count != 1 || (choices.Items[0] as ComboBoxItem)?.Tag as string != SidebarCatalog.Markets)
                    throw new InvalidOperationException("Only disabled widgets should be available to add.");
                await InvokeAsync(FindButton(settings, UiText.Get("SidebarAdd")));
                if (choices.IsEnabled || FindButton(settings, UiText.Get("SidebarAdd")).IsEnabled)
                    throw new InvalidOperationException("Adding all widgets must disable Add.");
                await InvokeAsync(FindButton(settings, UiText.Format("MoveUp", SidebarCatalog.Label(SidebarCatalog.Markets))));
                await InvokeAsync(FindButton(settings, UiText.Format("RemoveMessage", SidebarCatalog.Label(SidebarCatalog.Gmail))));
                await InvokeAsync(FindButton(settings, UiText.Format("MoveDown", SidebarCatalog.Label(SidebarCatalog.Markets))));
                if (!settings.EnabledWidgets.SequenceEqual(new[] { SidebarCatalog.Tasks, SidebarCatalog.Markets }))
                    throw new InvalidOperationException("Add, remove or move failed.");
                view.SelectCategory("Appearance");
                view.SelectCategory("Sidebar");
                _activeDialog.Hide();
                await showing;
                showing = null;
                if (!Preferences.Load(_services.Paths.PreferencesPath).SidebarWidgets!.SequenceEqual(settings.EnabledWidgets) ||
                    Grid.GetRow(TasksButton) != 1 || Grid.GetRow(MarketsButton) != 2 || GmailWidget.Parent is not null ||
                    Composer.Text != "Keep my draft · mañana")
                    throw new InvalidOperationException("Closing preferences failed to save/apply order or preserve the draft.");

                foreach (var order in new string[][] { [], ["markets"], ["tasks", "gmail"], ["markets", "tasks", "gmail"] })
                {
                    _preferences = _preferences with { SidebarWidgets = order };
                    ApplySidebarPreferences();
                    foreach (var expanded in new[] { false, true })
                    {
                        _sidebarManualExpanded = expanded;
                        UpdateWritingLayout();
                        UpdateLayout();
                        var expected = new SidebarCatalog(order).Ordered.ToArray();
                        if (_widgets.Children.Count != expected.Length || _widgets.RowDefinitions.Count != expected.Length ||
                            _widgets.Children[0] != WeatherBlock || Grid.GetRow(WeatherBlock) != 0 ||
                            Math.Abs(_widgets.ActualHeight - (expected.Length * 78 + (expected.Length - 1) * 12)) > 1)
                            throw new InvalidOperationException("Sidebar left gaps or moved Weather.");
                        for (var index = 0; index < expected.Length; index++)
                        {
                            FrameworkElement widget = expected[index] switch
                            {
                                "weather" => WeatherBlock, "gmail" => GmailWidget, "tasks" => TasksButton, _ => MarketsButton
                            };
                            if (_widgets.Children[index] != widget || Grid.GetRow(widget) != index || widget.ActualWidth != _widgets.ActualWidth)
                                throw new InvalidOperationException("Sidebar order or width diverged between expanded and collapsed states.");
                        }
                        SetTasksOpen(true);
                        UpdateLayout();
                        if (_taskArea.Margin.Top < _widgets.Margin.Top + _widgets.ActualHeight + 16)
                            throw new InvalidOperationException("Tasks panel overlaps the configured widgets.");
                        SetTasksOpen(false);
                    }
                }
                SetTasksOpen(true);
                _preferences = _preferences with { SidebarWidgets = [] };
                ApplySidebarPreferences();
                if (_tasksPanelOpen || _taskArea.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Removing Tasks left its panel open.");
            }
            _preferences = _preferences with { SidebarWidgets = [SidebarCatalog.Markets] };
            ApplySidebarPreferences();
            TickMarkets();
            for (var attempt = 0; _marketsSession.IsRefreshing && attempt < 50; attempt++) await Task.Delay(20);
            if (handler.Requests == 0) throw new InvalidOperationException("Re-enabling Markets did not resume refreshes.");
            File.AppendAllText(log, "PASS: sidebar management and persistence in both languages, fixed Weather, compact/expanded custom order without gaps, task panel positioning, preserved draft, and Markets refresh gating\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            _marketsSession = originalSession;
            _preferences = original;
            ApplyInterfaceLanguage();
            ApplySidebarPreferences();
            _sidebarManualExpanded = null;
            SetTasksOpen(false);
            Composer.Text = originalDraft;
            SavePreferences();
        }
    }
}
#endif
