#if HABITAT_UI_CHECKS
using Habitat.Features.Preferences;
using Habitat.Features.Translation;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckLanguageUiAsync(string log)
    {
        var original = _preferences;
        var destination = _destination;
        var draft = Composer.Text;
        Task? showing = null;
        var translateOriginal = _destinationSections[ComposerDestination.Translate];
        var response = new TaskCompletionSource<TranslationResult>();
        var requestCanceled = false;
        var translate = new TranslateSection((text, language, token) =>
        {
            token.Register(() => requestCanceled = true);
            return response.Task;
        }, _ => true, _ => { });
        try
        {
            _destinationSections[ComposerDestination.Translate] = translate;
            translate.SelectTarget("el");
            var pending = translate.SubmitAsync("Keep this source · café");
            SelectDestination(ComposerDestination.Gmail);
            const string userDraft = "Tasks · Keep this user text · mañana";
            Composer.Text = userDraft;
            foreach (var language in new[] { "es", "en", "es" })
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                showing = ShowPreferencesAsync();
                await Task.Delay(100);
                if (_activeDialog?.Content is not PreferencesView view) throw new InvalidOperationException("Language selector is missing.");
                view.InterfaceLanguage.SelectedIndex = 0;
                _activeDialog.Hide();
                await showing;
                showing = null;
                await Task.Delay(60);
                if (UiText.Language != "en" || Preferences.Load(_services.Paths.PreferencesPath).InterfaceLanguage != "en" ||
                    Composer.Text != userDraft || ComposerHint.Text != UiText.Get("DraftANewEmail") || SendButton.Content as string != UiText.Get("DraftEmail") ||
                    TaskInput.PlaceholderText != UiText.Get("NewTaskMessage") || AutomationProperties.GetName(TaskAdd) != UiText.Get("AddTask") ||
                    (OptionsMenu.Items.Last() as MenuFlyoutItem)?.Text != UiText.Get("AboutMenu"))
                    throw new InvalidOperationException("Language switch lost its saved choice, labels, accessibility or user draft.");
                if (translate.TargetLanguage != "el" || requestCanceled || !translate.IsBusy)
                    throw new InvalidOperationException("Interface language changed the translation target or canceled its request.");
                if (HasDestinationSection || Composer.VerticalAlignment != VerticalAlignment.Top || SendButton.HorizontalAlignment != HorizontalAlignment.Center ||
                    ComposerHint.Margin.Top >= Composer.Margin.Top || SendButton.Margin.Top <= Composer.Margin.Top)
                    throw new InvalidOperationException("Language switch changed the shared initial destination layout.");
                showing = ShowPreferencesAsync();
                await Task.Delay(80);
                view = (PreferencesView)_activeDialog!.Content;
                foreach (var item in view.Categories.Items.Cast<ListViewItem>())
                    if (AutomationProperties.GetName(item) != UiText.Get((string)item.Tag)) throw new InvalidOperationException("Translated categories lost their stable identifiers.");
                _activeDialog.Hide();
                await showing;
                showing = null;
            }
            response.SetResult(new TranslationResult("Tasks · provider result stays unchanged", null));
            await pending;
            _preferences = _preferences with { InterfaceLanguage = "en" };
            ApplyInterfaceLanguage();
            if (translate.ResultText != "Tasks · provider result stays unchanged" || translate.TargetLanguage != "el")
                throw new InvalidOperationException("Language switch altered a provider result.");
            var owned = UiLanguage.Watch(new TextBlock { Text = UiText.Get("Tasks") }, ("Text", () => UiText.Get("Tasks")));
            owned.Text = "user-owned result";
            UiText.SetLanguage("es");
            UiLanguage.Refresh();
            if (owned.Text != "user-owned result") throw new InvalidOperationException("A stale label registration overwrote later content.");
            File.AppendAllText(log, "PASS: English interface, persisted preference, accessible labels, stable category IDs, centered initial layout, retained multilingual drafts and pending translation/results\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            response.TrySetResult(new TranslationResult("", null));
            await translate.StopAsync();
            _destinationSections[ComposerDestination.Translate] = translateOriginal;
            _preferences = original;
            ApplyInterfaceLanguage();
            SavePreferences();
            SelectDestination(destination);
            Composer.Text = draft;
        }
    }
}
#endif
