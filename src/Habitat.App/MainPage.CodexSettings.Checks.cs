#if HABITAT_UI_CHECKS
using Habitat.Features.Codex;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckCodexSettingsUiAsync(string logPath)
    {
        var language = UiText.Language;
        var service = new SettingsCodexService();
        using var section = new Habitat.Features.Codex.CodexSection(() => service, exception => throw exception, ToneBrush);
        try
        {
            section.SetActive(true);
            await section.RefreshAsync();
            if (section.HasSettingsLine || section.SettingsText.Length != 0 || section.HasContent)
                throw new InvalidOperationException("Codex settings must not add content to the initial writing screen.");

            foreach (var currentLanguage in new[] { "en", "es" })
            {
                UiText.SetLanguage(currentLanguage);
                var a = new CodexChatTarget("11111111-1111-1111-1111-111111111111", "My unchanged title")
                    { Settings = new("test-model-a", "high") };
                service.Visible = a;
                await section.RefreshAsync();
                var expected = currentLanguage == "es"
                    ? "Última configuración conocida · Modelo: test-model-a · Razonamiento: Alto"
                    : "Last known configuration · Model: test-model-a · Reasoning: High";
                var header = ((Grid)section.View).Children.OfType<Grid>().Single();
                var heading = header.Children.OfType<StackPanel>().Single(panel => panel.Children[0] is TextBlock);
                if (!section.HasSettingsLine || section.SettingsText != expected ||
                    ((TextBlock)heading.Children[0]).Text != "Codex · My unchanged title" ||
                    ((TextBlock)heading.Children[1]).Text != expected ||
                    Grid.GetRow(header) != 0 || section.StatusText != UiText.Get("FollowingACodexChatStartedFromHABITAT"))
                    throw new InvalidOperationException("Codex settings must sit below the title and leave progress notices separate.");
                if (AutomationProperties.GetName(heading.Children[1]) != expected)
                    throw new InvalidOperationException("Codex settings accessibility must follow the interface language.");
                service.Visible = a with { Settings = new("test-model-changed", "xhigh") };
                await section.RefreshAsync();
                if (section.SettingsText != UiText.Format("CodexChatLastKnownSettings", "test-model-changed", UiText.Get("CodexReasoningExtraHigh")) ||
                    AutomationProperties.GetName(heading.Children[1]) != section.SettingsText)
                    throw new InvalidOperationException("Refreshing an unchanged chat and message history must update its settings.");
                service.Visible = new("22222222-2222-2222-2222-222222222222", "Chat B")
                    { Settings = new("test-model-b", null) };
                await section.RefreshAsync();
                if (section.SettingsText != UiText.Format("CodexChatLastKnownSettings", "test-model-b", UiText.Get("CodexSettingUnavailable")))
                    throw new InvalidOperationException("Switching chats must replace the model and cannot borrow another chat's reasoning effort.");
                service.Visible = service.Visible with { Settings = null };
                await section.RefreshAsync();
                if (section.SettingsText != UiText.Format("CodexChatSettings", UiText.Get("CodexSettingUnavailable"), UiText.Get("CodexSettingUnavailable")))
                    throw new InvalidOperationException("Unavailable metadata must not keep stale model settings.");
                UiText.SetLanguage(currentLanguage == "es" ? "en" : "es");
                UiLanguage.Refresh();
                if (section.SettingsText != CodexChatSettings.Format(null) ||
                    AutomationProperties.GetName(heading.Children[1]) != section.SettingsText)
                    throw new InvalidOperationException("Language switching must refresh settings text and accessibility without reloading history.");
                service.Visible = null;
                await section.RefreshAsync();
                if (section.HasSettingsLine || section.SettingsText.Length != 0 || section.HasContent ||
                    AutomationProperties.GetName(heading.Children[1]).Length != 0)
                    throw new InvalidOperationException("Leaving a linked chat must hide its settings and restore the initial writing screen.");
            }
            File.AppendAllText(logPath, "PASS: Codex model and reasoning appear below the title, refresh without message changes, stay isolated across chats, handle missing values and localize labels and accessibility in both languages\n");
        }
        finally
        {
            UiText.SetLanguage(language);
            UiLanguage.Refresh();
            await section.StopAsync();
        }
    }

    private sealed class SettingsCodexService : ICodexChatService
    {
        public CodexChatTarget? Visible;
        public Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => Task.FromResult(new CodexChatSelection(Visible, Visible is null));
        public Task<CodexChatSelection> GetLinkedAsync(CancellationToken token) => GetVisibleAsync(token);
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) =>
            Task.FromResult(new CodexChatHistory(target, [new("turn", "completed", [new("reply", "Codex", "Unchanged reply")])]));
        public Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token) =>
            throw new InvalidOperationException("Settings checks cannot send messages.");
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) =>
            throw new InvalidOperationException("Settings checks cannot navigate Codex.");
        public void ReturnFocus(nint ownerWindow) => throw new InvalidOperationException("Settings checks cannot change focus.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
