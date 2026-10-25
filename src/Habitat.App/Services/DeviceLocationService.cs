using Habitat.Models;
using System.Runtime.InteropServices;
using Windows.Devices.Geolocation;

namespace Habitat.Services;

internal sealed class DeviceLocationService(AppDataPaths paths)
{
    private Geolocator? _locator;
    private GeolocationAccessStatus? _access;

    public bool NeedsPermission => _access is null;

    public void ResetAccess() => _access = null;

    // This entry point is called by MainPage on the UI thread. Windows consent
    // may only be requested while HABITAT is in the foreground.
    public async Task<DeviceLocationResult> GetAsync(bool isForeground, CancellationToken cancellationToken)
    {
        try
        {
            if (_access != GeolocationAccessStatus.Allowed)
            {
                if (!isForeground)
                    return Failed(UiText.Get("OpenHABITATToAllowWindowsLocationAccess"));
                _access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken);
                if (_access != GeolocationAccessStatus.Allowed)
                    return Failed(UiText.Get("WindowsLocationAccessIsDisabledOrDeniedCheck"));
            }

            _locator ??= new Geolocator { DesiredAccuracy = PositionAccuracy.High, DesiredAccuracyInMeters = 100 };
            var position = await _locator.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10))
                .AsTask(cancellationToken);
            var coordinate = position.Coordinate;
            var point = coordinate.Point.Position;
            if (!double.IsFinite(point.Latitude) || !double.IsFinite(point.Longitude) ||
                Math.Abs(point.Latitude) > 90 || Math.Abs(point.Longitude) > 180)
                return Failed(UiText.Get("WindowsReturnedAnInvalidLocation"));
            var source = coordinate.PositionSource switch
            {
                PositionSource.Satellite => "GPS",
                PositionSource.Default => UiText.Get("WindowsDefaultLocation"),
                _ => UiText.Get("WindowsLocation")
            };
            WriteStatus($"Available: {source}; accuracy {coordinate.Accuracy:0} m.");
            return new DeviceLocationResult(new DeviceLocation(point.Latitude, point.Longitude, source, coordinate.Accuracy), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException)
        {
            _access = GeolocationAccessStatus.Denied;
            return Failed(UiText.Get("WindowsLocationAccessIsDisabledOrDeniedCheck"));
        }
        catch (Exception exception) when (exception is COMException or TimeoutException or
            OperationCanceledException or InvalidOperationException or NotSupportedException)
        {
            return Failed(UiText.Get("WindowsCouldNotProvideALocationUsingThe"));
        }
    }

    private DeviceLocationResult Failed(string message)
    {
        WriteStatus(message);
        return new DeviceLocationResult(null, message);
    }

    private void WriteStatus(string message)
    {
        try
        {
            var directory = paths.LocalDirectory;
            Directory.CreateDirectory(directory);
            // Diagnostic status only: never write the user's coordinates.
            File.WriteAllText(Path.Combine(directory, "location-status.log"), $"{DateTimeOffset.Now:O}\n{message}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
