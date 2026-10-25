using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private void RestoreUpdateRestartState()
    {
        try
        {
            if (UpdateRestartState.Consume(_services.Paths.UpdateDraftsPath) is { } state)
            {
                _composerDrafts.RestoreAfterRestart(state.Drafts);
                SelectDestination(_destinationCatalog.Select(state.Destination));
                RestoreComposerDraft();
            }
            if (File.Exists(_services.Paths.UpdateResultPath))
            {
                File.Delete(_services.Paths.UpdateResultPath);
                _updateFailed = true;
                RenderUpdateButton();
                SetStatus(UiText.Get("UpdateFailedRetry"), StatusTone.Warning);
            }
        }
        catch (Exception exception) { _services.ReportError("Restore update state", exception); }
    }

    private void RenderUpdateNotice() => RenderUpdateButton();
}
