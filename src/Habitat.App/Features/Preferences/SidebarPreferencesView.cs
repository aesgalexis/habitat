using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed class SidebarPreferencesView : StackPanel
{
    private readonly List<string> _enabled;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly ComboBox _available = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _add = new();
    internal string[] EnabledWidgets => _enabled.ToArray();
    internal ComboBox ContactCardSize { get; } = new()
    {
        Header = "Contact card size", HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "Small cards (default)", "Large cards" }
    };
    internal string FacebookCardSize => ContactCardSize.SelectedIndex == 1 ? "large" : "small";

    public SidebarPreferencesView(IEnumerable<string>? enabled, string facebookCardSize = "small")
    {
        Spacing = 12;
        _enabled = new SidebarCatalog(enabled).Enabled.ToList();
        Children.Add(UiLanguage.Watch(new TextBlock { TextWrapping = TextWrapping.Wrap,
            Text = UiText.Get("SidebarFixedWeather") }, ("Text", () => UiText.Get("SidebarFixedWeather"))));
        Children.Add(_rows);
        _available.Header = UiText.Get("SidebarAvailable");
        UiLanguage.Watch(_available, ("Header", () => UiText.Get("SidebarAvailable")));
        UiLanguage.SetName(_available, () => UiText.Get("SidebarAvailable"));
        Children.Add(_available);
        _add.Content = UiText.Get("SidebarAdd");
        UiLanguage.Watch(_add, ("Content", () => UiText.Get("SidebarAdd")));
        _add.Click += (_, _) =>
        {
            if ((_available.SelectedItem as ComboBoxItem)?.Tag is not string id) return;
            _enabled.Add(id);
            Refresh(id);
        };
        Children.Add(_add);
        ContactCardSize.SelectedIndex = facebookCardSize == "large" ? 1 : 0;
        AutomationProperties.SetName(ContactCardSize, "Contact card size");
        Children.Add(ContactCardSize);
        Refresh();
    }

    private void Refresh(string? focusId = null, int direction = 0)
    {
        _rows.Children.Clear();
        Button? focus = null;
        foreach (var id in _enabled.ToArray())
        {
            var row = new Grid { ColumnSpacing = 4, Tag = id };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            for (var column = 0; column < 3; column++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UiLanguage.Watch(new TextBlock { Text = SidebarCatalog.Label(id),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                ("Text", () => SidebarCatalog.Label(id))));
            var up = Action("↑", () => UiText.Format("MoveUp", SidebarCatalog.Label(id)), () => Move(id, -1));
            var down = Action("↓", () => UiText.Format("MoveDown", SidebarCatalog.Label(id)), () => Move(id, 1));
            up.IsEnabled = _enabled.IndexOf(id) > 0;
            down.IsEnabled = _enabled.IndexOf(id) < _enabled.Count - 1;
            var remove = Action(UiText.Get("Remove"), () => UiText.Format("RemoveMessage", SidebarCatalog.Label(id)), () =>
            {
                var index = _enabled.IndexOf(id);
                _enabled.Remove(id);
                Refresh(_enabled.ElementAtOrDefault(Math.Min(index, _enabled.Count - 1)));
                if (_enabled.Count == 0) _add.Focus(FocusState.Programmatic);
            });
            UiLanguage.Watch(remove, ("Content", () => UiText.Get("Remove")));
            Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); Grid.SetColumn(remove, 3);
            row.Children.Add(up); row.Children.Add(down); row.Children.Add(remove);
            _rows.Children.Add(row);
            if (id == focusId) focus = direction == -1 && up.IsEnabled ? up : direction == 1 && down.IsEnabled ? down : remove;
        }
        _available.Items.Clear();
        foreach (var id in SidebarCatalog.Available.Where(id => !_enabled.Contains(id)))
            _available.Items.Add(UiLanguage.Watch(new ComboBoxItem { Content = SidebarCatalog.Label(id), Tag = id },
                ("Content", () => SidebarCatalog.Label(id))));
        _available.SelectedIndex = _available.Items.Count > 0 ? 0 : -1;
        _available.IsEnabled = _add.IsEnabled = _available.Items.Count > 0;
        focus?.Focus(FocusState.Programmatic);
    }

    private void Move(string id, int direction)
    {
        var index = _enabled.IndexOf(id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _enabled.Count) return;
        (_enabled[index], _enabled[target]) = (_enabled[target], _enabled[index]);
        Refresh(id, direction);
    }

    private static Button Action(string text, Func<string> accessible, System.Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), MinWidth = 32 };
        UiLanguage.SetName(button, accessible);
        UiLanguage.SetToolTip(button, accessible);
        button.Click += (_, _) => action();
        return button;
    }
}
