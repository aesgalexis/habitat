using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private SidebarCatalog _sidebarCatalog = new();

    private void BuildSidebarCustomization()
    {
        SidebarToggle.ContextFlyout = SidebarContextMenu();
        _widgets.ContextFlyout = SidebarContextMenu();
        ApplySidebarPreferences();
    }

    private MenuFlyout SidebarContextMenu()
    {
        var menu = new MenuFlyout();
        var customize = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("SidebarCustomize") },
            ("Text", () => UiText.Get("SidebarCustomize")));
        customize.Click += async (_, _) => await RunOperationAsync(() => ShowPreferencesAsync("Sidebar"));
        menu.Items.Add(customize);
        return menu;
    }

    private void ApplySidebarPreferences()
    {
        _sidebarCatalog = new SidebarCatalog(_preferences.SidebarWidgets);
        _widgets.Children.Clear();
        _widgets.RowDefinitions.Clear();
        TasksButton.Visibility = GmailWidget.Visibility = MarketsButton.Visibility = Visibility.Collapsed;
        foreach (var id in _sidebarCatalog.Ordered)
        {
            FrameworkElement widget = id switch
            {
                SidebarCatalog.Weather => WeatherBlock,
                SidebarCatalog.Gmail => GmailWidget,
                SidebarCatalog.Tasks => TasksButton,
                SidebarCatalog.Markets => MarketsButton,
                _ => throw new InvalidOperationException("Unknown sidebar widget.")
            };
            Grid.SetRow(widget, _widgets.RowDefinitions.Count);
            _widgets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
            widget.Visibility = Visibility.Visible;
            _widgets.Children.Add(widget);
        }
        // Facebook remains available beside the configurable consolidated widgets.
        Grid.SetRow(FacebookButton, _widgets.RowDefinitions.Count);
        _widgets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
        _widgets.Children.Add(FacebookButton);
        if (!_sidebarCatalog.Enabled.Contains(SidebarCatalog.Tasks) && _tasksPanelOpen) SetTasksOpen(false);
        UpdateWritingLayout();
    }
}
