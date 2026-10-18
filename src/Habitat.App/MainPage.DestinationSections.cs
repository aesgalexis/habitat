using Habitat.Features.Destinations;
using Habitat.Features.Translation;
using Habitat.Features.Search;
using Habitat.Features.Codex;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Dictionary<Services.ComposerDestination, IDestinationSection> _destinationSections = new();
    private CancellationTokenSource? _emptyYouTubeReset;
    private readonly ContentControl _destinationContent = new()
    {
        Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
    };

    private bool HasDestinationSection => _destinationSections.TryGetValue(_destination, out var section) && section.HasContent;
    private bool IsDestinationSectionVisible => HasDestinationSection || _destination is Services.ComposerDestination.VSCode or Services.ComposerDestination.Codex;
    private CodexSection CodexChat => (CodexSection)_destinationSections[Services.ComposerDestination.Codex];

    private void BuildDestinationSections()
    {
        var translate = new TranslateSection((text, target, token) => _services.Translation.TranslateAsync(text, target, token),
            _services.CopyText, exception => _services.ReportError("Translate text", exception));
        translate.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        _destinationSections.Add(translate.Destination, translate);
        var vscode = new VSCodeSection(_services.VSCode.DiscoverAsync, _services.VSCode.RunAsync,
            exception => _services.ReportError("VS Code terminal", exception),
            project => _services.VSCode.OpenAsync(project, uri => _launchBrowser(uri)),
            () => _launchBrowser(new Uri("vscode://")), _services.VSCode.GetExecutionAsync,
            () => _services.PickVSCodeFolderAsync(_windowHost?.Handle ?? 0), _services.EnsureVSCodeProjectAsync,
            _preferences.VSCodeFolders ?? [], path =>
            {
                _preferences = _preferences with { VSCodeFolders = Services.VSCodeFolders.Remember(_preferences.VSCodeFolders, path) };
                SavePreferences();
            });
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
        var codex = new CodexSection(() => _services.Codex, exception => _services.ReportError("Codex chat", exception));
        codex.StateChanged += () =>
        {
            PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState();
            if (_destination == Services.ComposerDestination.Codex && !_operations.IsStopping)
                SetStatus(codex.StatusText, StatusTone.Active);
        };
        _destinationSections.Add(codex.Destination, codex);
        _launchCodex = async (text, token) => (await codex.SendAsync(text, token)).Result;
        Composer.TextChanged += (_, _) => ResetEmptySearch();
        _root.Children.Add(_destinationContent);
    }

    private void ResetSearch(Services.ComposerDestination target)
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
