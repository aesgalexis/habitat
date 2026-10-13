using Habitat.Models;
using Habitat.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Contains("--live"))
{
    using var service = new WeatherService();
    var weather = await service.GetAsync("Palma de Mallorca, ES", CancellationToken.None);
    Console.WriteLine($"{weather.Location}: {weather.Temperature:0}°C · {weather.Condition} · {weather.Outlook} · {weather.PrecipitationChance}% · next 6h");
    return;
}

var checks = 0;
var now = new DateTimeOffset(2026, 10, 13, 17, 15, 0, TimeSpan.Zero);
string Forecast(int chance = 5, double rain = 0, double snow = 0, double currentRain = 0, int code = 0)
{
    var start = now.AddMinutes(-15);
    return JsonSerializer.Serialize(new
    {
        current = new { time = now.ToUnixTimeSeconds(), temperature_2m = 22.4, weather_code = code, is_day = 1,
            rain = currentRain, showers = 0, snowfall = 0 },
        hourly = new
        {
            time = Enumerable.Range(0, 8).Select(index => start.AddHours(index).ToUnixTimeSeconds()).ToArray(),
            precipitation_probability = new[] { 100, chance, chance, chance, chance, chance, chance, 100 },
            rain = new[] { 5, rain, rain, rain, rain, rain, rain, 5 },
            showers = new double[8],
            snowfall = new[] { 0, snow, snow, snow, snow, snow, snow, 0 }
        }
    });
}
WeatherSnapshot Parse(string json)
{
    using var document = JsonDocument.Parse(json);
    return WeatherService.ParseForecast(document.RootElement, "Palma, ES");
}
void Check(bool success, string message)
{
    if (!success) throw new Exception(message);
    checks++;
}

var dry = Parse(Forecast());
Check(dry.Outlook == "No rain expected" && dry.PrecipitationChance == 5,
    "Past rain and rain beyond six hours must not affect the forecast.");
Check(Parse(Forecast(chance: 30)).Outlook == "Rain possible", "Show a moderate rain chance.");
Check(Parse(Forecast(chance: 70, rain: 0.2)).Outlook == "Rain likely", "Show likely future rain.");
Check(Parse(Forecast(currentRain: 0.1, code: 61)).Outlook == "Rain now", "Distinguish current rain from future risk.");
var snow = Parse(Forecast(chance: 85, snow: 0.5, code: 71));
Check(snow.Outlook == "Snow likely" && snow.Condition == WeatherCondition.Snow,
    "Snow precipitation must not be described as rain.");
Check(Parse(Forecast(code: 95)).Condition == WeatherCondition.Storm, "Recognize a thunderstorm.");

var missing = JsonNode.Parse(Forecast())!;
missing["hourly"]!["precipitation_probability"]![1] = null;
try { Parse(missing.ToJsonString()); throw new Exception("Missing weather data must not appear as a zero rain chance."); }
catch (InvalidDataException) { checks++; }
var incomplete = JsonNode.Parse(Forecast())!;
((JsonArray)incomplete["hourly"]!["rain"]!).RemoveAt(0);
try { Parse(incomplete.ToJsonString()); throw new Exception("Incomplete hourly data must be rejected."); }
catch (InvalidDataException) { checks++; }

var requests = new List<Uri>();
using var handler = new FakeHandler(request =>
{
    var uri = request.RequestUri!;
    requests.Add(uri);
    var json = uri.Host == "geocoding-api.open-meteo.com" ?
        "{\"results\":[{\"name\":\"Palma\",\"country_code\":\"ES\",\"latitude\":39.56939,\"longitude\":2.65024}]}" : Forecast();
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
});
using var client = new HttpClient(handler);
using var testService = new WeatherService(client);
await testService.GetAsync("Palma de Mallorca, ES", CancellationToken.None);
await testService.GetAsync("Palma de Mallorca, ES", CancellationToken.None);
Check(requests.Count == 3 && requests[0].Query.Contains("name=Palma&") && requests[0].Query.Contains("countryCode=ES"),
    "Resolve the correct Palma in Spain and reuse its coordinates between refreshes.");
Check(requests[1].Query.Contains("latitude=39.569") && requests[1].Query.Contains("forecast_hours=7"),
    "Request a six-hour forecast with culture-independent coordinates.");

var beforeDevice = requests.Count;
var deviceWeather = await testService.GetAsync(39.56939, 2.65024, "Current location · GPS", CancellationToken.None);
Check(requests.Count == beforeDevice + 1 && requests[^1].Host == "api.open-meteo.com" &&
    deviceWeather.Location == "Current location · GPS",
    "Device coordinates must go directly to the forecast without resolving or reusing the saved city.");
Check(requests[^1].Query.Contains("latitude=39.569&longitude=2.65&"),
    "The forecast should not transmit unnecessary device-coordinate precision.");
foreach (var latitude in new[] { double.NaN, double.PositiveInfinity, 91d })
{
    try { await testService.GetAsync(latitude, 2.65, "Current location", CancellationToken.None); throw new Exception("Invalid device coordinates must be rejected."); }
    catch (InvalidDataException) { checks++; }
}
Check(requests.Count == beforeDevice + 1, "Invalid device coordinates must not make an HTTP request.");

using var failedClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
using var failedService = new WeatherService(failedClient);
try { await failedService.GetAsync("Palma, ES", CancellationToken.None); throw new Exception("HTTP failures must not produce a forecast."); }
catch (HttpRequestException) { checks++; }
Console.WriteLine($"Weather checks passed: {checks}");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(response(request));
}
