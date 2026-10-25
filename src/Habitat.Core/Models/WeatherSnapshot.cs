namespace Habitat.Models;

internal enum WeatherCondition { Clear, Cloudy, Fog, Rain, Snow, Storm }

internal sealed record WeatherSnapshot(
    string Location,
    double Temperature,
    WeatherCondition Condition,
    bool IsDay,
    string Outlook,
    int PrecipitationChance,
    DateTimeOffset UpdatedAt)
{
    public string? DeviceSource { get; init; }
    public string? SavedCityName { get; init; }
    public string DisplayLocation => DeviceSource is { } source
        ? UiText.Get("CurrentLocation") + " · " + UiText.LocalizeMessage(source)
        : SavedCityName is { } city ? city + " · " + UiText.Get("SavedCity") : Location;
}
