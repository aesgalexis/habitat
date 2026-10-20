using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed class DestinationsView : StackPanel
{
    private readonly List<string> _enabled;
    private readonly List<WindowsAppDestination> _apps;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly ComboBox _available = new() { Header = "Available destinations", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _startup = new() { Header = "On startup", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _add = new() { Content = "Add destination" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    internal string[] EnabledDestinations => _enabled.ToArray();
    internal WindowsAppDestination[] Applications => _apps.ToArray();
    internal string? DefaultDestination => (_startup.SelectedItem as ComboBoxItem)?.Tag as string;

    public DestinationsView(Habitat.Preferences saved, Func<Task<IReadOnlyList<WindowsAppDestination>>> discover,
        Func<Task<string?>> browse)
    {
        Spacing = 12;
        var catalog = new DestinationCatalog(saved.EnabledDestinations, saved.WindowsApps);
        _enabled = catalog.Enabled.ToList();
        _apps = catalog.WindowsApps.ToList();
        Children.Add(_rows);
        Children.Add(_startup);
        Children.Add(_available);
        _add.Click += (_, _) =>
        {
            if ((_available.SelectedItem as ComboBoxItem)?.Tag is not string id) return;
            _enabled.Add(id);
            Refresh(DefaultDestination);
        };
        Children.Add(_add);

        var windows = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        var showWindows = new Button { Content = "Add Windows application…" };
        Children.Add(showWindows);
        Children.Add(windows);
        var installed = new ComboBox { Header = "Installed applications", HorizontalAlignment = HorizontalAlignment.Stretch };
        var load = new Button { Content = "Reload applications" };
        var name = new TextBox { Header = "Destination name", MaxLength = 80 };
        var target = new TextBox { Header = "Application or shortcut", PlaceholderText = @"C:\Program Files\Example\Example.exe" };
        var browseButton = new Button { Content = "Browse…" };
        var passText = new CheckBox { Content = "Pass editor text as one argument", IsEnabled = false };
        windows.Children.Add(installed);
        windows.Children.Add(load);
        windows.Children.Add(name);
        windows.Children.Add(target);
        windows.Children.Add(browseButton);
        windows.Children.Add(passText);
        windows.Children.Add(new TextBlock
        {
            Text = "By default, the destination only opens the application and keeps your draft. Text arguments are available for executable files; the application must support them.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 13
        });
        var saveApp = new Button { Content = "Add application" };
        windows.Children.Add(saveApp);
        Children.Add(_status);
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        WindowsAppDestination? selected = null;
        var changing = false;
        void UpdateTextOption()
        {
            passText.IsEnabled = selected?.Packaged != true &&
                Path.GetExtension(target.Text).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            if (!passText.IsEnabled) passText.IsChecked = false;
        }
        target.TextChanged += (_, _) =>
        {
            if (!changing) { selected = null; installed.SelectedIndex = -1; }
            UpdateTextOption();
        };
        installed.SelectionChanged += (_, _) =>
        {
            if ((installed.SelectedItem as ComboBoxItem)?.Tag is not WindowsAppDestination app) return;
            changing = true;
            try { selected = app; name.Text = app.Name; target.Text = app.Target; }
            finally { changing = false; }
            UpdateTextOption();
        };
        async Task LoadAsync()
        {
            if (!load.IsEnabled) return;
            load.IsEnabled = false;
            _status.Text = "Loading installed applications…";
            try
            {
                var choices = await discover();
                installed.Items.Clear();
                foreach (var app in choices) installed.Items.Add(new ComboBoxItem { Content = app.Name, Tag = app });
                _status.Text = choices.Count == 0 ? "No applications found. Use Browse… to choose an executable or shortcut." : "";
            }
            catch { _status.Text = "Could not load installed applications. Use Reload applications to retry, or choose a file with Browse…."; }
            finally { load.IsEnabled = true; }
        }
        showWindows.Click += async (_, _) =>
        {
            windows.Visibility = windows.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (windows.Visibility == Visibility.Visible && installed.Items.Count == 0) await LoadAsync();
        };
        load.Click += async (_, _) => await LoadAsync();
        browseButton.Click += async (_, _) =>
        {
            browseButton.IsEnabled = false;
            try
            {
                if (await browse() is { } path)
                {
                    target.Text = path;
                    if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileNameWithoutExtension(path);
                    _status.Text = "";
                }
            }
            catch { _status.Text = "Could not open the application picker. Enter a full executable or shortcut path."; }
            finally { browseButton.IsEnabled = true; }
        };
        saveApp.Click += (_, _) =>
        {
            var app = new WindowsAppDestination(WindowsAppDestination.NewId(), name.Text.Trim(), target.Text.Trim(),
                selected?.Packaged == true, passText.IsChecked == true);
            if (!app.IsValid || !app.Packaged && !File.Exists(app.Target))
            { _status.Text = "Enter a name and choose an installed application, executable (.exe) or shortcut (.lnk)."; return; }
            var existing = _apps.FirstOrDefault(item => item.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase) &&
                item.Packaged == app.Packaged && item.PassText == app.PassText);
            if (existing is null) _apps.Add(app);
            var id = existing?.Id ?? app.Id;
            if (!_enabled.Contains(id)) _enabled.Add(id);
            Refresh(DefaultDestination);
            windows.Visibility = Visibility.Collapsed;
            _status.Text = "Application added. Close Preferences to apply your changes.";
        };
        Refresh(saved.DefaultDestination);
    }

    private void Refresh(string? startup)
    {
        var catalog = new DestinationCatalog(_enabled, _apps);
        _rows.Children.Clear();
        if (_enabled.Count == 0) _rows.Children.Add(new TextBlock { Text = "No destinations added.", TextWrapping = TextWrapping.Wrap });
        foreach (var id in _enabled.ToArray())
        {
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < 3; index++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = catalog.Label(id), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(label);
            var up = Action("↑", $"Move {catalog.Label(id)} up", () => Move(id, -1));
            var down = Action("↓", $"Move {catalog.Label(id)} down", () => Move(id, 1));
            up.IsEnabled = _enabled.IndexOf(id) > 0;
            down.IsEnabled = _enabled.IndexOf(id) < _enabled.Count - 1;
            var remove = Action("Remove", $"Remove {catalog.Label(id)}", () => { _enabled.Remove(id); Refresh(DefaultDestination); });
            Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); Grid.SetColumn(remove, 3);
            row.Children.Add(up); row.Children.Add(down); row.Children.Add(remove);
            _rows.Children.Add(row);
        }
        _available.Items.Clear();
        foreach (var id in catalog.Available.Where(id => !_enabled.Contains(id)))
            _available.Items.Add(new ComboBoxItem { Content = catalog.Label(id), Tag = id });
        _available.SelectedIndex = _available.Items.Count > 0 ? 0 : -1;
        _available.IsEnabled = _available.Items.Count > 0;
        _add.IsEnabled = _available.Items.Count > 0;
        _startup.Items.Clear();
        _startup.Items.Add(new ComboBoxItem { Content = "Last used destination" });
        foreach (var id in _enabled) _startup.Items.Add(new ComboBoxItem { Content = catalog.Label(id), Tag = id });
        _startup.SelectedItem = _startup.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == startup) ?? _startup.Items[0];
    }

    private void Move(string id, int offset)
    {
        var index = _enabled.IndexOf(id);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _enabled.Count) return;
        (_enabled[index], _enabled[target]) = (_enabled[target], _enabled[index]);
        Refresh(DefaultDestination);
    }

    private static Button Action(string text, string accessible, System.Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), MinWidth = 32 };
        AutomationProperties.SetName(button, accessible);
        ToolTipService.SetToolTip(button, accessible);
        button.Click += (_, _) => action();
        return button;
    }
}
