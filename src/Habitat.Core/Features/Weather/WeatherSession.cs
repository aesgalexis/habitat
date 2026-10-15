using Habitat.Models;
using Habitat.Services;

namespace Habitat;

internal sealed class WeatherSession(WeatherService weather,
    Func<bool, CancellationToken, Task<DeviceLocationResult>> locate, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public bool IsRefreshing { get; private set; }
    public DateTimeOffset NextRefresh { get; set; } = DateTimeOffset.MinValue;

    public async Task<WeatherSnapshot?> RefreshAsync(string city, bool useDeviceLocation, bool foreground,
        Func<bool> stillCurrent, CancellationToken token)
    {
        if (IsRefreshing) return null;
        IsRefreshing = true;
        NextRefresh = _clock.GetUtcNow().AddMinutes(15);
        try
        {
            if (useDeviceLocation)
            {
                var result = await locate(foreground, token);
                if (token.IsCancellationRequested || !stillCurrent()) return null;
                if (result.Position is DeviceLocation position)
                    return await weather.GetAsync(position.Latitude, position.Longitude, "Current location · " + position.Source, token);
                var saved = await weather.GetAsync(city, token);
                return saved with { Location = saved.Location + " · saved city" };
            }
            return await weather.GetAsync(city, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { NextRefresh = _clock.GetUtcNow().AddMinutes(5); throw; }
        finally { IsRefreshing = false; }
    }
}
