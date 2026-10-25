using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly StackPanel WeatherDetails = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock WeatherCompactTemperature = new()
    {
        Text = "—", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center
    };

    private void BuildWeatherInterface(Grid sidebar)
    {
        WeatherBlock.ColumnSpacing = 8;
        WeatherBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        WeatherBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var symbol = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        symbol.Children.Add(WeatherSymbol);
        // Keep the icon in place when the compact temperature is hidden.
        symbol.Children.Add(new Border { Height = 18, Child = WeatherCompactTemperature });
        WeatherBlock.Children.Add(symbol);
        WeatherLocation.Text = UiLanguage.Value(WeatherLocation, "Text", () => _preferences.UseDeviceLocation ? UiText.Get("CurrentLocation") : _preferences.WeatherCity);
        WeatherLocation.TextTrimming = TextTrimming.CharacterEllipsis;
        WeatherOutlook.TextTrimming = TextTrimming.CharacterEllipsis;
        WeatherDetails.Children.Add(WeatherLocation);
        WeatherDetails.Children.Add(WeatherTemperature);
        WeatherDetails.Children.Add(WeatherOutlook);
        Grid.SetColumn(WeatherDetails, 1);
        WeatherBlock.Children.Add(WeatherDetails);
        Grid.SetRow(WeatherBlock, 0);
        sidebar.Children.Add(WeatherBlock);
    }

    private void SizeWeatherWidget(TopWidgetLayout layout)
    {
        WeatherBlock.Width = layout.WidgetWidth;
        WeatherBlock.ColumnSpacing = layout.Expanded ? 8 : 0;
        WeatherDetails.Visibility = layout.Expanded ? Visibility.Visible : Visibility.Collapsed;
        WeatherCompactTemperature.Visibility = layout.Expanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateWeatherSummary()
    {
        WeatherCompactTemperature.Text = WeatherTemperature.Text;
        UiLanguage.SetName(WeatherCompactTemperature, () => UiText.Format("Weather", WeatherLocation.Text, WeatherTemperature.Text, WeatherOutlook.Text));
    }
}
