using System.Globalization;
using System.Text.Json;

namespace Habitat;

// Only application-owned messages use these catalogs. User content and wire values
// never pass through the localizer, and numeric protocols keep their own culture.
internal static class UiText
{
    private static readonly IReadOnlyDictionary<string, string> English = Load("en");
    private static readonly IReadOnlyDictionary<string, string> Spanish = Load("es");
    private static string _language = "en";
    public static string Language => Volatile.Read(ref _language);
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "es" ? "es-ES" : "en-US");
    public static string NormalizeLanguage(string? language) => language is "es" ? "es" : "en";
    public static void SetLanguage(string? language) => Volatile.Write(ref _language, NormalizeLanguage(language));
    public static string Get(string key) => Resolve(key, Language);
    internal static string Resolve(string key, string? language) =>
        NormalizeLanguage(language) == "es" && Spanish.TryGetValue(key, out var translated) ? translated :
        English.TryGetValue(key, out var english) ? english : key;
    public static string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);
    // Used only for retained application messages; never for user/provider content.
    internal static string LocalizeMessage(string value) => KeyFor(value) is { } key ? Get(key) : value;
    internal static string BridgeMessage(string value) => value.StartsWith("ChatGPT editor missing:", StringComparison.Ordinal)
        ? Get("ChatGPTEditorUnavailable")
        : value.StartsWith("ChatGPT received the text but did not accept the send action.", StringComparison.Ordinal)
            ? Get("ChatGPTSendUnconfirmed")
            : LocalizeMessage(value);
    internal static IReadOnlyDictionary<string, string> Catalog(string language) => language == "es" ? Spanish : English;
    internal static string? KeyFor(string value) => English.FirstOrDefault(pair => pair.Value == value).Key ??
        Spanish.FirstOrDefault(pair => pair.Value == value).Key;
    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"Habitat.Localization.{language}.json")
            ?? throw new InvalidOperationException("Missing interface language catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Invalid interface language catalog.");
    }
}
