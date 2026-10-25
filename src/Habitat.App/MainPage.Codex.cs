using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private CancellationTokenSource? _codexSendCancellation;

    private void CancelPendingCodexSend()
    {
        var pending = _codexSendCancellation;
        _codexSendCancellation = null;
        pending?.Cancel();
        if (_destinationSections.TryGetValue(ComposerDestination.Codex, out var section))
            ((Features.Codex.CodexSection)section).CancelPendingSend();
        if (pending is not null)
        {
            if (_destination == ComposerDestination.Codex && !_operations.IsStopping)
                SetStatus(UiText.Get("ReadyToSendToCodex"), StatusTone.Active);
            UpdateSendState();
        }
    }

    private async Task SendToCodexAsync(string draft, ComposerDrafts.Submission submission, bool openExternally = false)
    {
        if (_codexSendCancellation is not null || CodexChat.IsBusy || _services.CodexSchedules.IsSending) return;
        using var cancellation = new CancellationTokenSource();
        _codexSendCancellation = cancellation;
        _composerDrafts.Accept(submission);
        RestoreComposerDraft();
        SetStatus(UiText.Get("SendingToCodex"), StatusTone.Active);
        UpdateSendState();
        try
        {
            var delivery = await _launchCodex(draft, cancellation.Token).WaitAsync(cancellation.Token);
            if (!ReferenceEquals(_codexSendCancellation, cancellation) || cancellation.IsCancellationRequested) return;
            if (delivery == CodexDeliveryResult.Sent)
            {
                _composerDrafts.Clear(submission);
                RestoreComposerDraft();
                SetStatus(UiText.Get("SentToCodex"), StatusTone.Success);
                if (!openExternally && _windowHost is { Handle: not 0 })
                {
                    CodexChat.ReturnFocus(_windowHost.Handle);
                    if (_destination == ComposerDestination.Codex) FocusComposer();
                }
            }
            else if (delivery != CodexDeliveryResult.Cancelled)
                SetStatus(delivery == CodexDeliveryResult.Unconfirmed
                    ? UiText.Get("CodexSubmissionCouldNotBeConfirmedCheckCodex")
                    : UiText.Get("CodexWasNotReadyToSendYourInstruction"), StatusTone.Warning);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _services.ReportError("Send to Codex", exception);
            if (ReferenceEquals(_codexSendCancellation, cancellation))
                SetStatus(UiText.Get("CouldNotSendToCodexYourInstructionWas"), StatusTone.Warning);
        }
        finally
        {
            // A cancelled older send must not unlock or clear a newer one.
            if (ReferenceEquals(_codexSendCancellation, cancellation))
            {
                _codexSendCancellation = null;
                UpdateSendState();
            }
        }
    }
}
