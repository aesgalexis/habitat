using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Habitat.Services;

namespace Habitat;

internal enum PreferencesLoadState { Loaded, New, Damaged, Unavailable }

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
    public string FacebookCardSize { get; init; } = "small";
    public Features.Contacts.BrowserContactSettings BrowserContacts { get; init; } = new();
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

    internal PreferencesLoadState LoadState { get; private init; }
    internal bool CanSave => LoadState is PreferencesLoadState.New or PreferencesLoadState.Loaded;
    private static string FilePath => AppDataPaths.Production.PreferencesPath;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Preferences Load(string? storagePath = null) => Read(storagePath ?? FilePath, out _);

    private static Preferences Read(string path, out string? text)
    {
        text = null;
        try
        {
            text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var saved = document.Deserialize<Preferences>();
            if (saved is not null && saved.Theme is "day" or "night" or "hybrid")
            {
                var catalog = new DestinationCatalog(saved.EnabledDestinations, saved.WindowsApps);
                return saved with
                {
                    LoadState = PreferencesLoadState.Loaded,
                    InterfaceLanguage = UiText.NormalizeLanguage(saved.InterfaceLanguage),
                    Wallpaper = (saved.Wallpaper ?? new()).Normalize(),
                    Markets = (saved.Markets ?? new()).Normalize(),
                    FacebookCardSize = saved.FacebookCardSize == "large" ? "large" : "small",
                    BrowserContacts = saved.BrowserContacts ?? new(),
                    // Existing profiles keep their four widgets; fresh profiles omit Markets.
                    SidebarWidgets = new SidebarCatalog(document.RootElement.TryGetProperty(nameof(SidebarWidgets), out _)
                        ? saved.SidebarWidgets : SidebarCatalog.Available).Enabled.ToArray(),
                    Opacity = Math.Clamp(saved.Opacity, 0, 100), Destination = ComposerDestinations.Normalize(saved.Destination),
                    EnabledDestinations = catalog.Enabled.ToArray(), WindowsApps = catalog.WindowsApps.ToArray()
                };
            }
        }
        catch (FileNotFoundException) { return Defaults(PreferencesLoadState.New); }
        catch (DirectoryNotFoundException) { return Defaults(PreferencesLoadState.New); }
        catch (IOException) { return Defaults(PreferencesLoadState.Unavailable); }
        catch (UnauthorizedAccessException) { return Defaults(PreferencesLoadState.Unavailable); }
        catch (JsonException) { }
        return Defaults(PreferencesLoadState.Damaged);
    }

    private static Preferences Defaults(PreferencesLoadState state) => new("hybrid", 15)
    {
        LoadState = state, EnabledDestinations = ComposerDestinations.Ordered.ToArray()
    };

    internal static bool HasBackup(string path) => Load(path + ".previous").LoadState == PreferencesLoadState.Loaded;

    internal static Preferences RecoverDamaged(string path, bool restoreBackup, Action<Exception>? reportError = null)
    {
        var current = Load(path);
        if (current.LoadState != PreferencesLoadState.Damaged) return current;
        var replacement = Defaults(PreferencesLoadState.Loaded);
        if (restoreBackup)
        {
            replacement = Load(path + ".previous");
            if (replacement.LoadState != PreferencesLoadState.Loaded) return current;
        }
        try
        {
            // Keep the exact file that was replaced, even if another program changed it after the read.
            AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(replacement, Json), path + ".damaged-" + Guid.NewGuid().ToString("N"));
            return replacement;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reportError?.Invoke(exception);
            return current;
        }
    }

    public bool Save(string? storagePath = null, Action<Exception>? reportError = null)
    {
        try
        {
            if (!CanSave) throw new IOException("Reload unreadable preferences before saving.");
            var path = storagePath ?? FilePath;
            var existing = Read(path, out var previousText);
            if (!existing.CanSave) throw new IOException("The preferences file could not be read safely.");
            var text = JsonSerializer.Serialize(this, Json);
            if (text != previousText)
                AtomicFile.Write(path, Encoding.UTF8.GetBytes(text), existing.LoadState == PreferencesLoadState.Loaded ? path + ".previous" : null);
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
