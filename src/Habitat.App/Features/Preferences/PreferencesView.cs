using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Preferences;

internal sealed class PreferencesView : Grid
{
    internal ListView Categories { get; } = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false };
    internal ComboBox Theme { get; }
    internal ComboBox InterfaceLanguage { get; }
    internal Slider BackgroundOpacity { get; }
    internal WallpaperView Wallpaper { get; }
    internal ToggleSwitch TextOnly { get; }
    internal ToggleSwitch GlobalHotkey { get; }
    internal ToggleSwitch UseDeviceLocation { get; }
    internal TextBox WeatherCity { get; }
    internal HyperlinkButton LocationSettings { get; }
    internal DestinationsView Destinations { get; }
    internal MarketsPreferencesView Markets { get; }
    internal string Category => ((ListViewItem)Categories.SelectedItem).Tag as string ?? "Appearance";
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    public PreferencesView(Habitat.Preferences saved, Func<Task<IReadOnlyList<WindowsAppDestination>>>? discoverApps = null,
        Func<Task<string?>>? browseApp = null, WallpaperService? wallpapers = null,
        Func<Task<IReadOnlyList<string>>>? pickWallpapers = null)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnSpacing = 20;
        UiLanguage.SetName(Categories, () => UiText.Get("PreferenceCategories"));
        Children.Add(Categories);
        Grid.SetColumn(_scroll, 1);
        Children.Add(_scroll);

        InterfaceLanguage = UiLanguage.Watch(new ComboBox { Header = UiText.Get("InterfaceLanguage"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "English", "Español" }, SelectedIndex = saved.InterfaceLanguage == "es" ? 1 : 0 }, ("Header", () => (object?)(UiText.Get("InterfaceLanguage"))));
        UiLanguage.SetName(InterfaceLanguage, () => UiText.Get("InterfaceLanguage"));

        Theme = UiLanguage.Watch(new ComboBox { Header = UiText.Get("Theme"), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { UiText.Get("Day"), UiText.Get("Night"), UiText.Get("Hybrid") }, SelectedIndex = saved.Theme switch { "night" => 1, "hybrid" => 2, _ => 0 } }, ("Header", () => (object?)(UiText.Get("Theme"))));
        UiLanguage.SetName(Theme, () => UiText.Get("Theme"));
        BackgroundOpacity = new Slider { Minimum = 0, Maximum = 100, Value = saved.Opacity, StepFrequency = 1 };
        UiLanguage.SetName(BackgroundOpacity, () => UiText.Get("BackgroundOpacity"));
        var opacityValue = UiLanguage.Watch(new TextBlock { Text = UiText.Format("BackgroundOpacityMessage", saved.Opacity) }, ("Text", () => (object?)(UiText.Format("BackgroundOpacityMessage", saved.Opacity))));
        BackgroundOpacity.ValueChanged += (_, args) => opacityValue.Text = UiLanguage.Value(opacityValue, "Text", () => UiText.Format("BackgroundOpacityMessage", args.NewValue));
        Wallpaper = new WallpaperView(saved.Wallpaper, wallpapers, pickWallpapers);
        Wallpaper.RestoreOriginal += () => { Theme.SelectedIndex = 2; BackgroundOpacity.Value = 15; };
        Wallpaper.Changed += () => BackgroundOpacity.IsEnabled = Wallpaper.Settings.SelectedId == WallpaperSettings.Original;
        BackgroundOpacity.IsEnabled = Wallpaper.Settings.SelectedId == WallpaperSettings.Original;
        AddCategory("Appearance", "\uE790", UiText.Get("PersonalizeTheLookAndFeelOfHABITAT"),
            Card(InterfaceLanguage, Description(UiText.Get("LanguageApplyOnClose"))),
            Card(Theme, Description(UiText.Get("HybridAdjustsTheContrastToTheBackgroundBehind"))), Card(opacityValue, BackgroundOpacity),
            Card(Wallpaper));

        UseDeviceLocation = UiLanguage.Watch(new ToggleSwitch { Header = UiText.Get("UseCurrentLocationGPSWindows"), IsOn = saved.UseDeviceLocation,
            OnContent = UiText.Get("On"), OffContent = UiText.Get("Off") }, ("Header", () => (object?)(UiText.Get("UseCurrentLocationGPSWindows"))), ("OnContent", () => (object?)(UiText.Get("On"))), ("OffContent", () => (object?)(UiText.Get("Off"))));
        WeatherCity = UiLanguage.Watch(new TextBox { Header = saved.UseDeviceLocation ? UiText.Get("FallbackCity") : UiText.Get("WeatherLocation"), Text = saved.WeatherCity,
            PlaceholderText = UiText.Get("CityCountryCodeForExamplePalmaES"), MaxLength = 100 }, ("Header", () => (object?)(saved.UseDeviceLocation ? UiText.Get("FallbackCity") : UiText.Get("WeatherLocation"))), ("PlaceholderText", () => (object?)(UiText.Get("CityCountryCodeForExamplePalmaES"))));
        UseDeviceLocation.Toggled += (_, _) => WeatherCity.Header = UiLanguage.Value(WeatherCity, "Header", () => UseDeviceLocation.IsOn ? UiText.Get("FallbackCity") : UiText.Get("WeatherLocation"));
        LocationSettings = UiLanguage.Watch(new HyperlinkButton { Content = UiText.Get("WindowsLocationSettings"), NavigateUri = new Uri("ms-settings:privacy-location"),
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) }, ("Content", () => (object?)(UiText.Get("WindowsLocationSettings"))));
        AddCategory("Location", "\uE707", UiText.Get("ChooseWhereYourWeatherForecastComesFrom"),
            Card(UseDeviceLocation, Description(UiText.Get("UsesGPSWhenAvailableWindowsAsksForLocation"))),
            Card(WeatherCity, LocationSettings), Description(UiText.Get("WeatherDataOpenMeteoRefreshesEvery15Minutes")));

        TextOnly = UiLanguage.Watch(new ToggleSwitch { Header = UiText.Get("TextonlyReplies"), IsOn = saved.TextOnly,
            OnContent = UiText.Get("On"), OffContent = UiText.Get("Off") }, ("Header", () => (object?)(UiText.Get("TextonlyReplies"))));
        AddCategory("ChatGPT", "\uE8F2", UiText.Get("CustomizeRepliesFromTheLinkedChatGPTConversation"), Card(TextOnly,
            Description(UiText.Get("TellChatGPTThatMessagesComeFromHABITATAnd"))));

        GlobalHotkey = UiLanguage.Watch(new ToggleSwitch { Header = UiText.Get("GlobalShortcutCtrlH"), IsOn = saved.GlobalHotkeyEnabled,
            OnContent = UiText.Get("On"), OffContent = UiText.Get("Off") }, ("Header", () => (object?)(UiText.Get("GlobalShortcutCtrlH"))), ("OnContent", () => (object?)(UiText.Get("On"))), ("OffContent", () => (object?)(UiText.Get("Off"))));
        UiLanguage.SetName(GlobalHotkey, () => UiText.Get("GlobalShortcutCtrlH"));
        AddCategory("Shortcuts", "\uE765", UiText.Get("AccessHABITATFromAnyApplication"), Card(GlobalHotkey,
            Description(UiText.Get("OpenHABITATFromAnyAppAndPlaceSelected"))));
        Destinations = new(saved, discoverApps ?? (() => Task.FromResult<IReadOnlyList<WindowsAppDestination>>([])),
            browseApp ?? (() => Task.FromResult<string?>(null)));
        AddCategory("Destinations", "\uE8A7", UiText.Get("ChooseAndOrderYourDestinationsRemovingOneKeeps"), Destinations);
        Markets = new MarketsPreferencesView(saved.Markets);
        AddCategory("Markets", "\uE9D2", UiText.Get("MarketsPreferencesDescription"), Markets);
        Categories.SelectionChanged += (_, _) =>
        {
            _scroll.Content = ((ListViewItem)Categories.SelectedItem).Content is StackPanel label ? label.Tag : null;
            _scroll.ChangeView(null, 0, null, true);
        };
        Categories.SelectedIndex = 0;
    }

    internal void SelectCategory(string name)
    {
        Categories.SelectedItem = Categories.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Tag as string == name) ?? Categories.Items[0];
    }

    private void AddCategory(string name, string glyph, string description, params UIElement[] controls)
    {
        var title = UiText.Get(name);
        var page = new StackPanel { Spacing = 16, Padding = new Thickness(0, 0, 8, 8) };
        page.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        page.Children.Add(Description(description));
        foreach (var control in controls) page.Children.Add(control);
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Tag = page };
        label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        label.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var item = new ListViewItem { Content = label, Tag = name, Padding = new Thickness(10, 12, 8, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, title);
        Categories.Items.Add(item);
    }

    private static TextBlock Description(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 13 };
    private static Border Card(params UIElement[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var control in controls) panel.Children.Add(control);
        return new Border { Child = panel, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(8, 128, 128, 128)) };
    }
}
