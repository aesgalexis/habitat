using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly ComposerDrafts _composerDrafts = new();
    private bool _restoringComposerDraft;

    private void RestoreComposerDraft()
    {
        var text = _composerDrafts.Text(_destination);
        if (Composer.Text == text) return;
        _restoringComposerDraft = true;
        try { Composer.Text = text; }
        finally { _restoringComposerDraft = false; }
    }
}
