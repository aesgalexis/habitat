using Habitat.Features.Preferences;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async void ShowPreferences_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(ShowPreferencesAsync);

    private Task ShowPreferencesAsync() => ShowPreferencesAsync(null);

    private async Task ShowPreferencesAsync(string? category)
    {
        if (HasAppearanceOverlay || _operations.IsStopping) return;
        var view = new PreferencesView(_preferences, _services.DiscoverWindowsAppsAsync,
            () => _services.PickWindowsAppAsync(_windowHost?.Handle ?? 0))
        {
            Width = Math.Max(280, Math.Min(740, XamlRoot.Size.Width - 96)),
            Height = Math.Max(180, Math.Min(480, XamlRoot.Size.Height - 180))
        };
        if (category is not null) view.SelectCategory(category);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Preferences", Content = view,
            CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 840d;
        StyleDialog(dialog);
        void PreviewAppearance()
        {
            var dark = view.Theme.SelectedIndex == 1 || view.Theme.SelectedIndex == 2 && DetectHybridTheme();
            RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            _windowHost?.SetBackgroundAppearance(view.BackgroundOpacity.Value, dark);
            StyleDialog(dialog);
        }
        view.Theme.SelectionChanged += (_, _) => PreviewAppearance();
        view.BackgroundOpacity.ValueChanged += (_, _) => PreviewAppearance();
        view.LocationSettings.Click += (_, _) => _locationSettingsOpened = true;
        var restoringShortcutToggle = false;
        view.GlobalHotkey.Toggled += async (_, _) =>
        {
            if (restoringShortcutToggle) return;
            var updated = _preferences with { GlobalHotkeyEnabled = view.GlobalHotkey.IsOn };
            if (!updated.Save(_services.Paths.PreferencesPath, exception => _services.ReportError("Save shortcut", exception)))
            {
                restoringShortcutToggle = true;
                try { view.GlobalHotkey.IsOn = _preferences.GlobalHotkeyEnabled; }
                finally { restoringShortcutToggle = false; }
                SetStatus("The shortcut preference could not be saved. Try again.", StatusTone.Warning);
                return;
            }
            _preferences = updated;
            await RunOperationAsync(ApplyGlobalHotkeyAsync);
        };
        _appearanceDialogOpen = true;
        try { await ShowDialogAsync(dialog); }
        finally { _appearanceDialogOpen = false; }

        var locationChanged = _preferences.WeatherCity != view.WeatherCity.Text.Trim() ||
            _preferences.UseDeviceLocation != view.UseDeviceLocation.IsOn;
        var updatedPreferences = _preferences with
        {
            Theme = view.Theme.SelectedIndex switch { 1 => "night", 2 => "hybrid", _ => "day" },
            Opacity = view.BackgroundOpacity.Value, TextOnly = view.TextOnly.IsOn,
            WeatherCity = view.WeatherCity.Text.Trim(), UseDeviceLocation = view.UseDeviceLocation.IsOn,
            EnabledDestinations = view.Destinations.EnabledDestinations,
            WindowsApps = view.Destinations.Applications, DefaultDestination = view.Destinations.DefaultDestination
        };
        updatedPreferences = updatedPreferences with
        {
            Destination = new Services.DestinationCatalog(updatedPreferences.EnabledDestinations, updatedPreferences.WindowsApps).Select(_destination)
        };
        if (!updatedPreferences.Save(_services.Paths.PreferencesPath, exception => _services.ReportError("Save preferences", exception)))
        {
            ApplyPreferences();
            SetStatus("Preferences could not be saved. Try again.", StatusTone.Warning);
            FocusComposer();
            return;
        }
        _preferences = updatedPreferences;
        ApplyDestinationPreferences();
        ApplyPreferences();
        if (locationChanged)
        {
            _services.ResetLocationAccess();
            _weatherSnapshot = null;
            WeatherLocation.Text = _preferences.UseDeviceLocation ? "Current location" : _preferences.WeatherCity;
            WeatherTemperature.Text = "—";
            WeatherOutlook.Text = "Loading weather…";
            _nextWeatherRefresh = DateTimeOffset.MinValue;
            PaintWeather();
        }
        FocusComposer();
    }
}
