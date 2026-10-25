using System.Text.Json;
using Habitat;
using Habitat.Features.Codex;
using Habitat.Services;

internal static class CodexSettingsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static CodexChatSettings? Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return CodexReadClient.ParseSettings(document.RootElement);
        }

        var settings = Parse("""{"model":"test-model","reasoningEffort":"high"}""");
        check(settings == new CodexChatSettings("test-model", "high"),
            "Codex reads model and reasoning metadata from its own structured fields");
        check(Parse("{}") is null && Parse("""{"model":null,"reasoningEffort":null}""") is null &&
            Parse("""{"model":12,"reasoningEffort":true}""") is null &&
            Parse("""{"model":" ","reasoningEffort":""}""") is null,
            "Older readers, nulls, malformed types and blank settings remain unavailable without guessing defaults");
        check(Parse("""{"model":"test-model","reasoningEffort":null}""") == new CodexChatSettings("test-model", null) &&
            Parse("""{"reasoningEffort":"xhigh"}""") == new CodexChatSettings(null, "xhigh"),
            "A missing model or reasoning field cannot discard the other available setting");
        var target = new CodexChatTarget("11111111-1111-1111-1111-111111111111", "Keep my title") { Settings = settings };
        check(!JsonSerializer.Serialize(target).Contains("Settings", StringComparison.Ordinal),
            "Display settings do not become persisted parameters of scheduled Codex messages");

        var language = UiText.Language;
        try
        {
            foreach (var current in new[] { "en", "es" })
            {
                UiText.SetLanguage(current);
                var expected = UiText.Language == "es"
                    ? "Última configuración conocida · Modelo: test-model · Razonamiento: Alto"
                    : "Last known configuration · Model: test-model · Reasoning: High";
                check(CodexChatSettings.Format(settings) == expected,
                    "Saved Codex metadata is labeled as last known and localized without changing the model: " + current);
                var missing = UiText.Language == "es" ? "Modelo: No disponible · Razonamiento: No disponible"
                    : "Model: Not available · Reasoning: Not available";
                check(CodexChatSettings.Format(null) == missing && CodexChatSettings.Format(new(" ", "")) == missing,
                    "Unavailable Codex settings are localized instead of implying a model default: " + current);
                foreach (var (effort, key) in new[] { ("none", "CodexReasoningNone"), ("minimal", "CodexReasoningMinimal"),
                    ("low", "CodexReasoningLow"), ("medium", "CodexReasoningMedium"), ("high", "CodexReasoningHigh"),
                    ("xhigh", "CodexReasoningExtraHigh"), ("max", "CodexReasoningMax"), ("ultra", "CodexReasoningUltra"),
                    ("auto", "CodexReasoningAuto") })
                    check(CodexChatSettings.Format(new(null, effort)) == UiText.Format("CodexChatLastKnownSettings",
                        UiText.Get("CodexSettingUnavailable"), UiText.Get(key)), "Reasoning label " + effort + " in " + current);
                check(CodexChatSettings.Format(new("test-model", "future-effort")).EndsWith("future-effort", StringComparison.Ordinal),
                    "A future reasoning value remains exactly as reported by Codex");
            }
        }
        finally { UiText.SetLanguage(language); }
    }
}
