using System.Text.Json;
using System.Text.Json.Serialization;
using Habitat.Services;

namespace Habitat;

internal sealed record Preferences(string Theme, double Opacity)
{
    [JsonConverter(typeof(DestinationPreferenceConverter))]
    public string Destination { get; init; } = ComposerDestination.OpenAI;
    public string[]? EnabledDestinations { get; init; }
    public WindowsAppDestination[] WindowsApps { get; init; } = [];
    public string? DefaultDestination { get; init; }
    public string InterfaceLanguage { get; init; } = "en";
    public WallpaperSettings Wallpaper { get; init; } = new();
    public MarketsSettings Markets { get; init; } = new();
    public string[]? SidebarWidgets { get; init; } = SidebarCatalog.Defaults;
    public bool TextOnly { get; init; } = true;
    public bool GlobalHotkeyEnabled { get; init; } = true;
    public bool SetupGuideSeen { get; init; }
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
            using var document = JsonDocument.Parse(File.ReadAllText(storagePath ?? FilePath));
            var saved = document.Deserialize<Preferences>();
            if (saved is not null && saved.Theme is "day" or "night" or "hybrid")
            {
                var catalog = new DestinationCatalog(saved.EnabledDestinations, saved.WindowsApps);
                return saved with
                {
                    InterfaceLanguage = UiText.NormalizeLanguage(saved.InterfaceLanguage),
                    Wallpaper = (saved.Wallpaper ?? new()).Normalize(),
                    Markets = (saved.Markets ?? new()).Normalize(),
                    // Existing profiles keep their four widgets; fresh profiles omit Markets.
                    SidebarWidgets = new SidebarCatalog(document.RootElement.TryGetProperty(nameof(SidebarWidgets), out _)
                        ? saved.SidebarWidgets : SidebarCatalog.Available).Enabled.ToArray(),
                    Opacity = Math.Clamp(saved.Opacity, 0, 100), Destination = ComposerDestinations.Normalize(saved.Destination),
                    EnabledDestinations = catalog.Enabled.ToArray(), WindowsApps = catalog.WindowsApps.ToArray()
                };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new Preferences("hybrid", 15) { EnabledDestinations = ComposerDestinations.Ordered.ToArray() };
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

internal sealed class DestinationPreferenceConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var legacy)
            ? ComposerDestinations.FromLegacy(legacy)
            : reader.TokenType == JsonTokenType.String ? ComposerDestinations.Normalize(reader.GetString())
            : throw new JsonException("Invalid destination preference.");
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
