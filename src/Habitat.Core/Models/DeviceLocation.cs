namespace Habitat.Models;

internal sealed record DeviceLocation(double Latitude, double Longitude, string Source, double Accuracy);

internal sealed record DeviceLocationResult(DeviceLocation? Position, string? Error);
