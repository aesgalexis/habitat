#if HABITAT_UI_CHECKS
using Habitat.Features.Translation;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckTranslationUiAsync(string logPath, string original)
    {
        var savedService = _services.Translation;
        var savedLauncher = _launchBrowser;
        var fake = new TranslationCheckService();
        var section = (TranslateSection)_destinationSections[ComposerDestination.Translate];
        var savedTarget = section.TargetLanguage;
        try
        {
            _services.Translation = fake;
            var externalCalls = 0;
            Uri? openedTranslation = null;
            _launchBrowser = uri => { openedTranslation = uri; externalCalls++; return Task.FromResult(true); };
            SelectDestination(ComposerDestination.Translate);
            Composer.Text = original;
            await Task.Delay(80);
            if (fake.Calls != 0 || section.HasContent || section.TargetLanguage != "en" || _destinationContent.Content is not null ||
                _destinationContent.Visibility != Visibility.Collapsed || Composer.VerticalAlignment != VerticalAlignment.Top)
                throw new InvalidOperationException("Translate must keep the original centered writing view, hide the selector until submission and default to English.");
            if (FindWebContent(section.View)) throw new InvalidOperationException("Translate must not contain a WebView or website.");
            SendCurrentMessage();
            if (fake.Calls != 1 || fake.LastText != original || fake.LastTarget != "en" || section.ResultText != "Traducción ☕\nsegunda línea" || !section.HasContent ||
                _destinationContent.Visibility != Visibility.Visible ||
                Composer.Text != original || !section.CanCopy || externalCalls != 0 || Conversation.Children.Count != 0 || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("Translate must display plain translated text and preserve the original without chat or browser launches.");
            section.View.UpdateLayout();
            if (!HasVisibleText(section.View, section.ResultText) || !HasVisibleText(section.View, "English"))
                throw new InvalidOperationException("The translated text and selected language name must actually render in the native section.");
            Composer.Text = "A revised source draft";
            await section.OpenTranslationAsync();
            if (externalCalls != 1 || openedTranslation?.Host != "translate.google.com" ||
                !openedTranslation.Query.Contains("tl=en") || !openedTranslation.Query.Contains("text=" + Uri.EscapeDataString(original)) ||
                Composer.Text != "A revised source draft" || fake.Calls != 1 || !section.CanCopy)
                throw new InvalidOperationException("Open in browser must use the submitted translation source and language, preserve a revised draft and avoid another translation request.");
            Composer.Text = original;
            foreach (var destination in new[] { ComposerDestination.Google, ComposerDestination.YouTube, ComposerDestination.OpenAI })
            {
                SelectDestination(destination);
                if (Composer.Text != "") throw new InvalidOperationException("A translated source must not travel to another mode.");
            }
            SelectDestination(ComposerDestination.Translate);
            if (Composer.Text != original) throw new InvalidOperationException("Returning to Translate must restore its original source.");
            section.SelectTarget("el");
            if (section.CanCopy || section.ResultText.Length != 0)
                throw new InvalidOperationException("Language selection must invalidate the previous result.");

            fake.Pending = new TaskCompletionSource<TranslationResult>();
            var pending = LaunchDestinationAsync(ComposerDestination.Translate, original);
            SendCurrentMessage();
            if (SendButton.IsEnabled || fake.Calls != 2) throw new InvalidOperationException("Translation must gate duplicate submissions.");
            SelectDestination(ComposerDestination.Google);
            if (!SendButton.IsEnabled) throw new InvalidOperationException("A running translation must not block other destinations.");
            Composer.Text = "New draft written while translating";
            fake.Pending.SetResult(new TranslationResult("Bonjour", "en"));
            await pending;
            fake.Pending = null;
            SelectDestination(ComposerDestination.Translate);
            if (Composer.Text != original || section.ResultText != "Bonjour" || fake.Calls != 2)
                throw new InvalidOperationException("Mode switching must restore the translation's own source and result.");
            AddMessage("YOU", "Existing isolated conversation", MessageTone.User);
            SelectDestination(ComposerDestination.OpenAI);
            if (ConversationScroll.Visibility != Visibility.Visible || _conversation.Messages.Single().Body != "Existing isolated conversation" ||
                Composer.Text != "New draft written while translating")
                throw new InvalidOperationException("Returning to OpenAI must recover the conversation and preserve the new shared draft.");
            _conversation.Clear();
            Conversation.Children.Clear();
            SelectDestination(ComposerDestination.Translate);

            fake.Error = new BrowserTranslationException("Open your selected browser and reconnect the HABITAT extension, then retry.");
            await section.SubmitAsync(original);
            if (!section.StatusText.StartsWith("Open your selected browser") || section.CanCopy || section.IsBusy || Composer.Text != original)
                throw new InvalidOperationException("Service failure must be recoverable without altering the draft or exposing stale results.");
            fake.Error = null;
            await section.RetryAsync();
            if (fake.LastText != original || fake.LastTarget != "el" || !section.CanCopy)
                throw new InvalidOperationException("Retry must repeat the failed input using the selected language.");

            // A provider that ignores cancellation must still never publish a stale response.
            fake.Pending = new TaskCompletionSource<TranslationResult>();
            pending = section.SubmitAsync(original);
            section.SelectTarget("es");
            fake.Pending.SetResult(new TranslationResult("Stale result", "en"));
            await pending;
            if (section.ResultText.Length != 0 || section.CanCopy || section.IsBusy)
                throw new InvalidOperationException("Changing language must discard late responses.");
            fake.Pending = new TaskCompletionSource<TranslationResult>();
            pending = section.SubmitAsync(original);
            section.Cancel();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            if (section.ResultText.Length != 0 || section.StatusText != "Translation canceled." || section.IsBusy)
                throw new InvalidOperationException("Cancel must immediately release the section even when the browser never completes.");
            fake.Pending.SetResult(new TranslationResult("Canceled result", "en"));

            fake.Pending = new TaskCompletionSource<TranslationResult>();
            var staleTranslation = fake.Pending;
            pending = section.SubmitAsync(original);
            section.RequestClear();
            if (section.HasContent || section.IsBusy || section.ResultText.Length != 0 || section.CanCopy || section.StatusText.Length != 0 ||
                Composer.Text.Length != 0 || _destinationContent.Visibility != Visibility.Collapsed || Composer.VerticalAlignment != VerticalAlignment.Top)
                throw new InvalidOperationException("Clear translation must cancel pending work, clear the original and result and return to the centered welcome editor.");
            fake.Pending = new TaskCompletionSource<TranslationResult>();
            var freshTranslation = section.SubmitAsync("Fresh source");
            await pending;
            staleTranslation.SetResult(new TranslationResult("Must stay cleared", "en"));
            await Task.Yield();
            if (!section.IsBusy || section.ResultText.Length != 0 || section.CanCopy)
                throw new InvalidOperationException("A cleared translation must never restore stale text or release a newer translation request.");
            fake.Pending.SetResult(new TranslationResult("Fresh result", "en"));
            await freshTranslation;
            if (section.IsBusy || section.ResultText != "Fresh result" || !section.CanCopy)
                throw new InvalidOperationException("Translating again after Clear must display only the new result.");
            section.RequestClear();

            string? copied = null;
            using var copySection = new TranslateSection((_, _, _) => Task.FromResult(new TranslationResult("<b>plain text</b> & ☕", "en")),
                text => { copied = text; return true; }, _ => { });
            await copySection.SubmitAsync(original);
            if (!copySection.CopyResult() || copied != "<b>plain text</b> & ☕" || copySection.CopyStatusText != "Copied." || copySection.StatusText.Length != 0)
                throw new InvalidOperationException("Copy must transfer only the exact plain translated text.");
            using var failedCopy = new TranslateSection((_, _, _) => Task.FromResult(new TranslationResult("Text", "en")),
                _ => false, _ => { });
            await failedCopy.SubmitAsync(original);
            if (failedCopy.CopyResult() || !failedCopy.CopyStatusText.StartsWith("Could not copy.") || failedCopy.StatusText.Length != 0)
                throw new InvalidOperationException("Clipboard failures must retain selectable text and explain recovery.");

            fake.Pending = new TaskCompletionSource<TranslationResult>();
            using var delayedSection = new TranslateSection(fake.TranslateAsync, _ => false, _ => { });
            pending = delayedSection.SubmitAsync(original);
            delayedSection.Dispose();
            if (!fake.LastToken.IsCancellationRequested) throw new InvalidOperationException("Closing a section must cancel its request.");
            fake.Pending.SetResult(new TranslationResult("Late result", "en"));
            await pending;
            if (delayedSection.ResultText.Length != 0) throw new InvalidOperationException("Disposed sections must ignore late translations.");
            File.AppendAllText(logPath, "PASS: original writing view and hidden selector before Enter; English default; three languages; native results, exact source, copy, browser opening, clear, independent modes, retry, cancel and stale-response/shutdown protection (browser simulated)\n");
        }
        finally
        {
            section.SelectTarget(savedTarget);
            _services.Translation = savedService;
            _launchBrowser = savedLauncher;
        }

        static bool FindWebContent(DependencyObject parent)
        {
            if (parent is WebView2) return true;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (FindWebContent(VisualTreeHelper.GetChild(parent, index))) return true;
            return false;
        }

        static bool HasVisibleText(DependencyObject parent, string text)
        {
            if (parent is TextBlock block && block.Text == text && block.ActualHeight > 0 && block.Visibility == Visibility.Visible) return true;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (HasVisibleText(VisualTreeHelper.GetChild(parent, index), text)) return true;
            return false;
        }
    }

    private sealed class TranslationCheckService : ITranslationService
    {
        public int Calls { get; private set; }
        public string? LastText { get; private set; }
        public string? LastTarget { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Exception? Error { get; set; }
        public TaskCompletionSource<TranslationResult>? Pending { get; set; }
        public Task<TranslationResult> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            Calls++;
            LastText = text;
            LastTarget = targetLanguage;
            LastToken = cancellationToken;
            return Error is { } error ? Task.FromException<TranslationResult>(error)
                : Pending?.Task ?? Task.FromResult(new TranslationResult("Traducción ☕\nsegunda línea", "en"));
        }
    }
}
#endif
