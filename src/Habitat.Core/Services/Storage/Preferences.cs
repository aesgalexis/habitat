using System.Text.Json;
using Habitat.Services;

namespace Habitat;

internal sealed record Preferences(string Theme, double Opacity)
{
    public ComposerDestination Destination { get; init; } = ComposerDestination.OpenAI;
    public bool TextOnly { get; init; } = true;
    public string Browser { get; init; } = BrowserLauncher.Default;
    public bool GlobalHotkeyEnabled { get; init; } = true;
    public string WeatherCity { get; init; } = "Palma, ES";
    public bool UseDeviceLocation { get; init; } = true;
    public bool GmailInboxOnly { get; init; } = true;
    public string[] VSCodeFolders { get; init; } = [];
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }

    private static string FilePath => AppDataPaths.Production.PreferencesPath;

    public static Preferences Load(string? storagePath = null)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(storagePath ?? FilePath));
            if (saved is not null && saved.Theme is "day" or "night" or "hybrid")
            {
                return saved with
                {
                    Opacity = Math.Clamp(saved.Opacity, 0, 100), Destination = ComposerDestinations.Normalize(saved.Destination),
                    Browser = string.IsNullOrWhiteSpace(saved.Browser) ? BrowserLauncher.Default : saved.Browser
                };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new Preferences("hybrid", 15);
    }

    public bool Save(string? storagePath = null, Action<Exception>? reportError = null)
    {
        try
        {
            var path = storagePath ?? FilePath;
            AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reportError?.Invoke(exception);
            return false;
        }
    }
}
