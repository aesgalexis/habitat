namespace Habitat.Features.Translation;

internal sealed record TranslationLanguage(string Code, string NameKey)
{
    public string Name => UiText.Get(NameKey);
}

internal static class TranslationLanguages
{
    public static IReadOnlyList<TranslationLanguage> All { get; } = new TranslationLanguage[]
    {
        new("en", "English"), new("el", "Greek"), new("es", "Spanish")
    };

    public static bool IsSupported(string? code) => All.Any(language => language.Code == code);
    public const string DefaultCode = "en";
    public static string Normalize(string? code) => IsSupported(code) ? code! : DefaultCode;
}
