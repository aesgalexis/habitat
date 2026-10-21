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
    private readonly ComboBox _available = UiLanguage.Watch(new ComboBox() { Header = UiText.Get("AvailableDestinations"), HorizontalAlignment = HorizontalAlignment.Stretch }, ("Header", () => (object?)(UiText.Get("AvailableDestinations"))));
    private readonly ComboBox _startup = UiLanguage.Watch(new ComboBox() { Header = UiText.Get("OnStartup"), HorizontalAlignment = HorizontalAlignment.Stretch }, ("Header", () => (object?)(UiText.Get("OnStartup"))));
    private readonly Button _add = UiLanguage.Watch(new Button() { Content = UiText.Get("AddDestination") }, ("Content", () => (object?)(UiText.Get("AddDestination"))));
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
        var showWindows = UiLanguage.Watch(new Button { Content = UiText.Get("AddWindowsApplication") }, ("Content", () => (object?)(UiText.Get("AddWindowsApplication"))));
        Children.Add(showWindows);
        Children.Add(windows);
        var installed = UiLanguage.Watch(new ComboBox { Header = UiText.Get("InstalledApplications"), HorizontalAlignment = HorizontalAlignment.Stretch }, ("Header", () => (object?)(UiText.Get("InstalledApplications"))));
        var load = UiLanguage.Watch(new Button { Content = UiText.Get("ReloadApplications") }, ("Content", () => (object?)(UiText.Get("ReloadApplications"))));
        var name = UiLanguage.Watch(new TextBox { Header = UiText.Get("DestinationName"), MaxLength = 80 }, ("Header", () => (object?)(UiText.Get("DestinationName"))));
        var target = UiLanguage.Watch(new TextBox { Header = UiText.Get("ApplicationOrShortcut"), PlaceholderText = @"C:\Program Files\Example\Example.exe" }, ("Header", () => (object?)(UiText.Get("ApplicationOrShortcut"))));
        var browseButton = UiLanguage.Watch(new Button { Content = UiText.Get("Browse") }, ("Content", () => (object?)(UiText.Get("Browse"))));
        var passText = UiLanguage.Watch(new CheckBox { Content = UiText.Get("PassEditorTextAsOneArgument"), IsEnabled = false }, ("Content", () => (object?)(UiText.Get("PassEditorTextAsOneArgument"))));
        windows.Children.Add(installed);
        windows.Children.Add(load);
        windows.Children.Add(name);
        windows.Children.Add(target);
        windows.Children.Add(browseButton);
        windows.Children.Add(passText);
        windows.Children.Add(UiLanguage.Watch(new TextBlock
        {
            Text = UiText.Get("ByDefaultTheDestinationOnlyOpensTheApplication"),
            TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 13
        }, ("Text", () => (object?)(UiText.Get("ByDefaultTheDestinationOnlyOpensTheApplication")))));
        var saveApp = UiLanguage.Watch(new Button { Content = UiText.Get("AddApplication") }, ("Content", () => (object?)(UiText.Get("AddApplication"))));
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
            _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("LoadingInstalledApplications"));
            try
            {
                var choices = await discover();
                installed.Items.Clear();
                foreach (var app in choices) installed.Items.Add(new ComboBoxItem { Content = app.Name, Tag = app });
                _status.Text = UiLanguage.Value(_status, "Text", () => choices.Count == 0 ? UiText.Get("NoApplicationsFoundUseBrowseToChooseAn") : "");
            }
            catch { _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotLoadInstalledApplicationsUseReloadApplications")); }
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
            catch { _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotOpenTheApplicationPickerEnterA")); }
            finally { browseButton.IsEnabled = true; }
        };
        saveApp.Click += (_, _) =>
        {
            var app = new WindowsAppDestination(WindowsAppDestination.NewId(), name.Text.Trim(), target.Text.Trim(),
                selected?.Packaged == true, passText.IsChecked == true);
            if (!app.IsValid || !app.Packaged && !File.Exists(app.Target))
            { _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("EnterANameAndChooseAnInstalledApplication")); return; }
            var existing = _apps.FirstOrDefault(item => item.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase) &&
                item.Packaged == app.Packaged && item.PassText == app.PassText);
            if (existing is null) _apps.Add(app);
            var id = existing?.Id ?? app.Id;
            if (!_enabled.Contains(id)) _enabled.Add(id);
            Refresh(DefaultDestination);
            windows.Visibility = Visibility.Collapsed;
            _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("ApplicationAddedClosePreferencesToApplyYourChanges"));
        };
        Refresh(saved.DefaultDestination);
    }

    private void Refresh(string? startup)
    {
        var catalog = new DestinationCatalog(_enabled, _apps);
        _rows.Children.Clear();
        if (_enabled.Count == 0) _rows.Children.Add(UiLanguage.Watch(new TextBlock { Text = UiText.Get("NoDestinationsAdded"), TextWrapping = TextWrapping.Wrap }, ("Text", () => (object?)(UiText.Get("NoDestinationsAdded")))));
        foreach (var id in _enabled.ToArray())
        {
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < 3; index++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = catalog.Label(id), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(label);
            var up = Action("↑", UiText.Format("MoveUp", catalog.Label(id)), () => Move(id, -1));
            var down = Action("↓", UiText.Format("MoveDown", catalog.Label(id)), () => Move(id, 1));
            up.IsEnabled = _enabled.IndexOf(id) > 0;
            down.IsEnabled = _enabled.IndexOf(id) < _enabled.Count - 1;
            var remove = Action(UiText.Get("Remove"), UiText.Format("RemoveMessage", catalog.Label(id)), () => { _enabled.Remove(id); Refresh(DefaultDestination); });
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
        _startup.Items.Add(UiLanguage.Watch(new ComboBoxItem { Content = UiText.Get("LastUsedDestination") }, ("Content", () => (object?)(UiText.Get("LastUsedDestination")))));
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
