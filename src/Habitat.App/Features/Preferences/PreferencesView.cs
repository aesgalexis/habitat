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
    internal Slider BackgroundOpacity { get; }
    internal ToggleSwitch TextOnly { get; }
    internal ToggleSwitch GlobalHotkey { get; }
    internal ToggleSwitch UseDeviceLocation { get; }
    internal TextBox WeatherCity { get; }
    internal HyperlinkButton LocationSettings { get; }
    internal string Category => ((ListViewItem)Categories.SelectedItem).Tag as string ?? "Appearance";
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    public PreferencesView(Habitat.Preferences saved)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnSpacing = 20;
        AutomationProperties.SetName(Categories, "Preference categories");
        Children.Add(Categories);
        Grid.SetColumn(_scroll, 1);
        Children.Add(_scroll);

        Theme = new ComboBox { Header = "Theme", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Day", "Night", "Hybrid" }, SelectedIndex = saved.Theme switch { "night" => 1, "hybrid" => 2, _ => 0 } };
        AutomationProperties.SetName(Theme, "Theme");
        BackgroundOpacity = new Slider { Minimum = 0, Maximum = 100, Value = saved.Opacity, StepFrequency = 1 };
        AutomationProperties.SetName(BackgroundOpacity, "Background opacity");
        var opacityValue = new TextBlock { Text = $"Background opacity · {saved.Opacity:0}%" };
        BackgroundOpacity.ValueChanged += (_, args) => opacityValue.Text = $"Background opacity · {args.NewValue:0}%";
        AddCategory("Appearance", "\uE790", "Personalize the look and feel of HABITAT.",
            Card(Theme, Description("Hybrid adjusts the contrast to the background behind the window.")), Card(opacityValue, BackgroundOpacity));

        UseDeviceLocation = new ToggleSwitch { Header = "Use current location (GPS / Windows)", IsOn = saved.UseDeviceLocation };
        WeatherCity = new TextBox { Header = saved.UseDeviceLocation ? "Fallback city" : "Weather location", Text = saved.WeatherCity,
            PlaceholderText = "City, country code (for example Palma, ES)", MaxLength = 100 };
        UseDeviceLocation.Toggled += (_, _) => WeatherCity.Header = UseDeviceLocation.IsOn ? "Fallback city" : "Weather location";
        LocationSettings = new HyperlinkButton { Content = "Windows location settings", NavigateUri = new Uri("ms-settings:privacy-location"),
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) };
        AddCategory("Location", "\uE707", "Choose where your weather forecast comes from.",
            Card(UseDeviceLocation, Description("Uses GPS when available. Windows asks for location permission. Coordinates are sent to Open-Meteo for the forecast; the saved city is used if location is unavailable.")),
            Card(WeatherCity, LocationSettings), Description("Weather data: Open-Meteo · refreshes every 15 minutes."));

        TextOnly = new ToggleSwitch { Header = "Text-only replies", IsOn = saved.TextOnly };
        AddCategory("ChatGPT", "\uE8F2", "Customize replies from the linked ChatGPT conversation.", Card(TextOnly,
            Description("Tell ChatGPT that messages come from HABITAT and avoid tables, images, files and other rich output unless you explicitly request them.")));

        GlobalHotkey = new ToggleSwitch { Header = "Global shortcut (Ctrl+H)", IsOn = saved.GlobalHotkeyEnabled,
            OnContent = "On", OffContent = "Off" };
        AutomationProperties.SetName(GlobalHotkey, "Global shortcut (Ctrl+H)");
        AddCategory("Shortcuts", "\uE765", "Access HABITAT from any application.", Card(GlobalHotkey,
            Description("Open HABITAT from any app and place selected text in the editor. Starts with Windows. Nothing is sent automatically.")));
        Categories.SelectionChanged += (_, _) =>
        {
            _scroll.Content = ((ListViewItem)Categories.SelectedItem).Content is StackPanel label ? label.Tag : null;
            _scroll.ChangeView(null, 0, null, true);
        };
        Categories.SelectedIndex = 0;
    }

    private void AddCategory(string name, string glyph, string description, params UIElement[] controls)
    {
        var page = new StackPanel { Spacing = 16, Padding = new Thickness(0, 0, 8, 8) };
        page.Children.Add(new TextBlock { Text = name, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        page.Children.Add(Description(description));
        foreach (var control in controls) page.Children.Add(control);
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Tag = page };
        label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        label.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ListViewItem { Content = label, Tag = name, Padding = new Thickness(10, 12, 8, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, name);
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
