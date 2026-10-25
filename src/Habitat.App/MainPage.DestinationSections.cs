using Habitat.Features.Destinations;
using Habitat.Features.Translation;
using Habitat.Features.Search;
using Habitat.Features.Codex;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Dictionary<string, IDestinationSection> _destinationSections = new();
    private CancellationTokenSource? _emptyYouTubeReset;
    private readonly ContentControl _destinationContent = new()
    {
        Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
    };

    private bool HasDestinationSection => _destinationSections.TryGetValue(_destination, out var section) && section.HasContent;
    private bool IsDestinationSectionVisible => HasDestinationSection || _destination == Services.ComposerDestination.VSCode;
    private CodexSection CodexChat => (CodexSection)_destinationSections[Services.ComposerDestination.Codex];

    private void BuildDestinationSections()
    {
        var translate = new TranslateSection((text, target, token) => _services.Translation.TranslateAsync(text, target, token),
            _services.CopyText, exception => _services.ReportError("Translate text", exception), uri => _launchBrowser(uri));
        translate.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        translate.ClearRequested += () =>
        {
            _composerDrafts.Edit(Services.ComposerDestination.Translate, "");
            if (_destination == Services.ComposerDestination.Translate) RestoreComposerDraft();
            translate.Reset();
            if (_destination == Services.ComposerDestination.Translate) FocusComposer();
        };
        _destinationSections.Add(translate.Destination, translate);
        var gmailDraft = new GmailDraftSection(_gmailSession.CreateDraftAsync, async () =>
        {
            _gmailSession.Initialize();
            if (_gmailAuth?.IsConfigured != true) { await ShowGmailSetupAsync(); return; }
            if (_gmailAuth?.IsConfigured == true)
                await _gmailSession.SignInAsync(() => _preferences.GmailInboxOnly, _gmailCancellation.Token, authorizeDrafts: true);
        }, () => _gmailAuth?.Email, uri => _launchBrowser(uri), exception => _services.ReportError("Gmail draft", exception));
        gmailDraft.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        gmailDraft.ClearRequested += () =>
        {
            _composerDrafts.Edit(Services.ComposerDestination.Gmail, "");
            if (_destination == Services.ComposerDestination.Gmail) { RestoreComposerDraft(); FocusComposer(); }
        };
        _destinationSections.Add(gmailDraft.Destination, gmailDraft);
        var vscode = new VSCodeSection(_services.VSCode.DiscoverAsync, _services.VSCode.RunAsync,
            exception => _services.ReportError("VS Code terminal", exception),
            project => _services.VSCode.OpenAsync(project, uri => _launchBrowser(uri)),
            () => _launchBrowser(new Uri("vscode://")), _services.VSCode.GetExecutionAsync,
            () => _services.PickVSCodeFolderAsync(_windowHost?.Handle ?? 0), _services.EnsureVSCodeProjectAsync,
            _preferences.VSCodeFolders ?? [], path =>
            {
                _preferences = _preferences with { VSCodeFolders = Services.VSCodeFolders.Remember(_preferences.VSCodeFolders, path) };
                SavePreferences();
            }, openLink: uri => _launchBrowser(uri));
        vscode.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        _destinationSections.Add(vscode.Destination, vscode);
        var search = new SearchSection((query, start, token) => _services.Search.SearchAsync(query, start, token),
            uri => _launchBrowser(uri), exception => _services.ReportError("Search Google", exception));
        search.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        search.ClearRequested += () => ResetSearch(Services.ComposerDestination.Google);
        _destinationSections.Add(search.Destination, search);
        var youtube = new SearchSection((query, start, token) => _services.YouTubeSearch.SearchAsync(query, start, token),
            uri => _launchBrowser(uri), exception => _services.ReportError("Search YouTube", exception), Services.ComposerDestination.YouTube,
            uri => _services.SearchThumbnail(uri));
        youtube.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        youtube.ClearRequested += () => ResetSearch(Services.ComposerDestination.YouTube);
        _destinationSections.Add(youtube.Destination, youtube);
        var codex = new CodexSection(() => _services.Codex, exception => _services.ReportError("Codex chat", exception), ToneBrush);
        codex.NewChatStarted += () =>
        {
            _composerDrafts.Edit(Services.ComposerDestination.Codex, "");
            if (_destination == Services.ComposerDestination.Codex) { RestoreComposerDraft(); FocusComposer(); }
        };
        codex.StateChanged += () =>
        {
            PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState();
            if (_destination == Services.ComposerDestination.Codex && !_operations.IsStopping)
                UpdateDestinationStatus();
        };
        _destinationSections.Add(codex.Destination, codex);
        _launchCodex = async (text, token) => (await codex.SendAsync(text, token)).Result;
        _root.Children.Add(_destinationContent);
        ConfigureComposerRoutes();
    }

    private void ResetSearch(string target)
    {
        if (target == Services.ComposerDestination.YouTube) CancelEmptyYouTubeReset();
        if (!_destinationSections.TryGetValue(target, out var destination) ||
            destination is not SearchSection search || !search.HasContent && !search.IsBusy) return;
        _composerDrafts.Edit(target, "");
        if (_destination == target) RestoreComposerDraft();
        search.Reset();
        (target == Services.ComposerDestination.YouTube ? _services.YouTubeSearch : _services.Search).Reset();
        if (_destination == target) FocusComposer();
    }

    private void ResetEmptySearch()
    {
        if (_restoringComposerDraft || _operations.IsStopping) return;
        if (_destination == Services.ComposerDestination.YouTube)
        {
            CancelEmptyYouTubeReset();
            if (string.IsNullOrWhiteSpace(Composer.Text))
            {
                var reset = new CancellationTokenSource();
                _emptyYouTubeReset = reset;
                _ = RunOperationAsync(() => ResetEmptyYouTubeAsync(reset));
            }
        }
        else if (_destination == Services.ComposerDestination.Google && string.IsNullOrWhiteSpace(Composer.Text))
            ResetSearch(_destination);
    }

    private void CancelEmptyYouTubeReset()
    {
        var reset = _emptyYouTubeReset;
        _emptyYouTubeReset = null;
        reset?.Cancel();
    }

    private async Task ResetEmptyYouTubeAsync(CancellationTokenSource reset)
    {
        using (reset)
        {
            try
            {
                // Replacing the previous query can briefly leave the editor empty.
                // Keep its browser tab unless the field stays empty for a moment.
                await Task.Delay(750, reset.Token);
                if (!ReferenceEquals(_emptyYouTubeReset, reset)) return;
                _emptyYouTubeReset = null;
                if (string.IsNullOrWhiteSpace(_composerDrafts.Text(Services.ComposerDestination.YouTube)))
                    ResetSearch(Services.ComposerDestination.YouTube);
            }
            catch (OperationCanceledException) when (reset.IsCancellationRequested) { }
        }
    }

    private void PresentDestinationSection()
    {
        if (_destinationSections.TryGetValue(Services.ComposerDestination.VSCode, out var vscode))
            ((VSCodeSection)vscode).SetActive(_destination == Services.ComposerDestination.VSCode);
        if (_destinationSections.TryGetValue(Services.ComposerDestination.Codex, out var codex))
            ((CodexSection)codex).SetActive(_destination == Services.ComposerDestination.Codex);
        _destinationContent.Content = IsDestinationSectionVisible ? _destinationSections[_destination].View : null;
        _destinationContent.Visibility = IsDestinationSectionVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
