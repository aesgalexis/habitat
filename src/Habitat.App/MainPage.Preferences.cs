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
            () => _services.PickWindowsAppAsync(_windowHost?.Handle ?? 0), _services.Wallpapers,
            () => _services.PickWallpapersAsync(_windowHost?.Handle ?? 0))
        {
            Width = Math.Max(280, Math.Min(740, XamlRoot.Size.Width - 96)),
            Height = Math.Max(180, Math.Min(480, XamlRoot.Size.Height - 180))
        };
        if (category is not null) view.SelectCategory(category);
        var dialog = UiLanguage.Watch(new ContentDialog { XamlRoot = XamlRoot, Title = UiText.Get("Preferences"), Content = view,
            CloseButtonText = UiText.Get("Close"), DefaultButton = ContentDialogButton.Close }, ("Title", () => (object?)(UiText.Get("Preferences"))), ("CloseButtonText", () => (object?)(UiText.Get("Close"))));
        dialog.Resources["ContentDialogMaxWidth"] = 840d;
        StyleDialog(dialog);
        var previewRevision = 0;
        void PreviewAppearance()
        {
            var revision = ++previewRevision;
            var wallpaper = view.Wallpaper.Settings;
            _previewWallpaper = wallpaper;
            var dark = view.Theme.SelectedIndex == 1 || view.Theme.SelectedIndex == 2 && DetectHybridTheme();
            RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            _windowHost?.SetBackgroundAppearance(view.BackgroundOpacity.Value, dark);
            PaintSurface();
            _ = RunOperationAsync(async () =>
            {
                await RenderWallpaperAsync(wallpaper);
                if (revision == previewRevision && _appearanceDialogOpen)
                    await view.Wallpaper.PreviewAsync(dark);
            });
            StyleDialog(dialog);
        }
        view.Theme.SelectionChanged += (_, _) => PreviewAppearance();
        view.BackgroundOpacity.ValueChanged += (_, _) => PreviewAppearance();
        view.Wallpaper.Changed += PreviewAppearance;
        view.Wallpaper.RestoreOriginal += PreviewAppearance;
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
                SetStatus(UiText.Get("TheShortcutPreferenceCouldNotBeSavedTry"), StatusTone.Warning);
                return;
            }
            _preferences = updated;
            await RunOperationAsync(ApplyGlobalHotkeyAsync);
        };
        _appearanceDialogOpen = true;
        PreviewAppearance();
        try
        {
            await ShowDialogAsync(dialog);
            // A file picker/import may still be finishing when the window closes.
            await view.Wallpaper.PendingImport;
        }
        finally { _appearanceDialogOpen = false; previewRevision++; }

        var locationChanged = _preferences.WeatherCity != view.WeatherCity.Text.Trim() ||
            _preferences.UseDeviceLocation != view.UseDeviceLocation.IsOn;
        var updatedPreferences = _preferences with
        {
            Theme = view.Theme.SelectedIndex switch { 1 => "night", 2 => "hybrid", _ => "day" },
            Opacity = view.BackgroundOpacity.Value, TextOnly = view.TextOnly.IsOn,
            InterfaceLanguage = view.InterfaceLanguage.SelectedIndex == 1 ? "es" : "en",
            Wallpaper = view.Wallpaper.Settings,
            Markets = view.Markets.Settings.Normalize(),
            SidebarWidgets = view.Sidebar.EnabledWidgets,
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
            view.Wallpaper.Complete(false, _preferences.Wallpaper);
            ApplyPreferences();
            SetStatus(UiText.Get("PreferencesCouldNotBeSavedTryAgain"), StatusTone.Warning);
            FocusComposer();
            return;
        }
        view.Wallpaper.Complete(true, _preferences.Wallpaper);
        _preferences = updatedPreferences;
        _marketsSession.Configure(_preferences.Markets);
        ApplyInterfaceLanguage();
        ApplyDestinationPreferences();
        ApplyPreferences();
        if (locationChanged)
        {
            _services.ResetLocationAccess();
            _weatherSnapshot = null;
            WeatherLocation.Text = UiLanguage.Value(WeatherLocation, "Text", () => _preferences.UseDeviceLocation ? UiText.Get("CurrentLocation") : _preferences.WeatherCity);
            WeatherTemperature.Text = "—";
            WeatherOutlook.Text = UiLanguage.Value(WeatherOutlook, "Text", () => UiText.Get("LoadingWeather"));
            _nextWeatherRefresh = DateTimeOffset.MinValue;
            PaintWeather();
        }
        FocusComposer();
    }
}
