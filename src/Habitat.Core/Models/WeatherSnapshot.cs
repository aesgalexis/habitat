namespace Habitat.Models;

internal enum WeatherCondition { Clear, Cloudy, Fog, Rain, Snow, Storm }

internal sealed record WeatherSnapshot(
    string Location,
    double Temperature,
    WeatherCondition Condition,
    bool IsDay,
    string Outlook,
    int PrecipitationChance,
    DateTimeOffset UpdatedAt);
