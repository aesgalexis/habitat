using Habitat.Features.Codex;

namespace Habitat.Services;

internal sealed class CodexChatService(ICodexChatDesktop desktop, ICodexHistoryReader reader,
    Func<string, CancellationToken, Task<CodexDeliveryResult>> startNew) : ICodexChatService
{
    public async Task<CodexChatSelection> GetVisibleAsync(CancellationToken token)
    {
        var visible = await desktop.ReadVisibleAsync(token);
        if (visible is null) return new(null, true);
        if (visible.Composer.IsNewChat) return new(null, true);
        var target = await reader.ResolveAsync(visible.SelectedTitles, token);
        return target is null ? new(null, false, "Select a local Codex chat in the sidebar, then reload.") : new(target, false);
    }

    public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) => reader.ReadAsync(target, token);

    public async Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return new(CodexDeliveryResult.NotSent);
        var visible = await desktop.ReadVisibleAsync(token);
        if (visible is null || visible.Composer.IsNewChat)
        {
            if (expected is not null || visible is { IsEmpty: false } && !CodexDelivery.SameText(visible.Composer.Text, text))
                return new(CodexDeliveryResult.NotSent);
            var startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var delivery = await startNew(text, token);
            if (delivery != CodexDeliveryResult.Sent) return new(delivery);
            // New-chat naming and its sidebar row can settle after submission.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                while (true)
                {
                    var selection = await GetVisibleAsync(timeout.Token);
                    if (selection.Target is { CreatedAt: { } createdAt } created && createdAt >= startedAt - 1)
                        return new(delivery, created);
                    if (selection.Target is not null) return new(CodexDeliveryResult.Unconfirmed);
                    await Task.Delay(150, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(CodexDeliveryResult.Unconfirmed); }
        }
        var target = await reader.ResolveAsync(visible.SelectedTitles, token);
        if (target is null || target.Id != expected?.Id || !visible.CanAccept ||
            !visible.IsEmpty && !CodexDelivery.SameText(visible.Composer.Text, text)) return new(CodexDeliveryResult.NotSent);
        var before = await reader.ReadAsync(target, token);
        if (before.IsRunning) return new(CodexDeliveryResult.NotSent);
        return new(await desktop.SubmitVisibleAsync(visible, text, token), target, before.LatestTurnId);
    }

    public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => desktop.OpenChatAsync(target, token);
    public void ReturnFocus(nint ownerWindow) => desktop.ReturnFocus(ownerWindow);
    public async ValueTask DisposeAsync() { if (reader is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
}
