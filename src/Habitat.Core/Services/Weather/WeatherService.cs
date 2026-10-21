using Habitat.Models;
using System.Globalization;
using System.Text.Json;

namespace Habitat.Services;

internal sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private string? _locationQuery;
    private WeatherLocation? _location;

    public WeatherService(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<WeatherSnapshot> GetAsync(string city, CancellationToken cancellationToken)
    {
        city = city.Trim();
        if (city.Length == 0) throw new InvalidDataException(UiText.Get("SetAWeatherLocationInPreferences"));
        if (_location is null || !string.Equals(_locationQuery, city, StringComparison.OrdinalIgnoreCase))
        {
            _location = await ResolveLocationAsync(city, cancellationToken).ConfigureAwait(false);
            _locationQuery = city;
        }

        return await GetAsync(_location.Latitude, _location.Longitude, _location.Label, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WeatherSnapshot> GetAsync(double latitude, double longitude, string location, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            throw new InvalidDataException("The weather location has invalid coordinates.");
        // Weather forecasts do not need sub-meter device precision.
        var latitudeText = latitude.ToString("0.###", CultureInfo.InvariantCulture);
        var longitudeText = longitude.ToString("0.###", CultureInfo.InvariantCulture);
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitudeText}&longitude={longitudeText}" +
            "&current=temperature_2m,weather_code,is_day,rain,showers,snowfall" +
            "&hourly=precipitation_probability,rain,showers,snowfall" +
            "&forecast_hours=7&timeformat=unixtime&timezone=auto";
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseForecast(document.RootElement, location);
    }

    private async Task<WeatherLocation> ResolveLocationAsync(string city, CancellationToken cancellationToken)
    {
        var parts = city.Split(',', 2, StringSplitOptions.TrimEntries);
        var countryCode = parts.Length == 2 && parts[1].Length == 2 && parts[1].All(char.IsAsciiLetter)
            ? parts[1].ToUpperInvariant() : string.Empty;
        var name = countryCode.Length > 0 ? parts[0] : city;
        // Open-Meteo indexes Mallorca's capital under its official name Palma.
        if (name.Equals("Palma de Mallorca", StringComparison.OrdinalIgnoreCase))
        {
            name = "Palma";
            if (countryCode.Length == 0) countryCode = "ES";
        }
        var url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name) +
            "&count=1&language=en&format=json" + (countryCode.Length > 0 ? "&countryCode=" + countryCode : string.Empty);
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new InvalidDataException(UiText.Get("CityNotFoundCheckTheWeatherLocationIn"));
        var result = results[0];
        var label = result.GetProperty("name").GetString() ?? name;
        if (result.TryGetProperty("country_code", out var country)) label += ", " + country.GetString();
        var latitude = Number(result.GetProperty("latitude"));
        var longitude = Number(result.GetProperty("longitude"));
        if (Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            throw new InvalidDataException("The weather location has invalid coordinates.");
        return new WeatherLocation(label, latitude, longitude);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static WeatherSnapshot ParseForecast(JsonElement root, string location)
    {
        try
        {
            var current = root.GetProperty("current");
            var updatedAt = DateTimeOffset.FromUnixTimeSeconds(current.GetProperty("time").GetInt64());
            var temperature = Number(current.GetProperty("temperature_2m"));
            var code = current.GetProperty("weather_code").GetInt32();
            var condition = code switch
            {
                0 or 1 => WeatherCondition.Clear,
                2 or 3 => WeatherCondition.Cloudy,
                45 or 48 => WeatherCondition.Fog,
                71 or 73 or 75 or 77 or 85 or 86 => WeatherCondition.Snow,
                95 or 96 or 99 => WeatherCondition.Storm,
                >= 51 and <= 67 or >= 80 and <= 82 => WeatherCondition.Rain,
                _ => WeatherCondition.Cloudy
            };
            var hourly = root.GetProperty("hourly");
            var times = hourly.GetProperty("time");
            var probabilities = hourly.GetProperty("precipitation_probability");
            var rain = hourly.GetProperty("rain");
            var showers = hourly.GetProperty("showers");
            var snowfall = hourly.GetProperty("snowfall");
            var length = times.GetArrayLength();
            if (new[] { probabilities, rain, showers, snowfall }.Any(array => array.GetArrayLength() != length))
                throw new InvalidDataException("Incomplete weather forecast.");
            var chance = 0;
            var liquid = 0d;
            var snow = 0d;
            var hours = 0;
            for (var index = 0; index < length; index++)
            {
                var time = DateTimeOffset.FromUnixTimeSeconds(times[index].GetInt64());
                if (time <= updatedAt || time > updatedAt.AddHours(6)) continue;
                chance = Math.Max(chance, (int)Math.Clamp(Number(probabilities[index]), 0, 100));
                liquid += Number(rain[index]) + Number(showers[index]);
                snow += Number(snowfall[index]);
                hours++;
            }
            if (hours < 6) throw new InvalidDataException("Incomplete six-hour weather forecast.");
            var raining = Number(current.GetProperty("rain")) + Number(current.GetProperty("showers")) > 0;
            var snowing = Number(current.GetProperty("snowfall")) > 0;
            var outlook = raining ? UiText.Get("RainNow") : snowing ? UiText.Get("SnowNow") : snow > 0 && liquid == 0 ?
                (chance >= 50 ? UiText.Get("SnowLikely") : UiText.Get("SnowPossible")) :
                chance >= 50 ? UiText.Get("RainLikely") : liquid > 0 || chance >= 20 ? UiText.Get("RainPossible") : UiText.Get("NoRainExpected");
            return new WeatherSnapshot(location, temperature, condition,
                current.GetProperty("is_day").GetInt32() == 1, outlook, chance, updatedAt);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("Invalid weather data received.", exception);
        }
    }

    private static double Number(JsonElement element)
    {
        if (!element.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException("Missing weather data.");
        return value;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    private sealed record WeatherLocation(string Label, double Latitude, double Longitude);
}
