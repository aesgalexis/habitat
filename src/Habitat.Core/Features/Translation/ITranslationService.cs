namespace Habitat.Features.Translation;

internal sealed record TranslationResult(string Text, string? DetectedLanguage);

internal interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken);
}
