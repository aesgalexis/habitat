using Habitat.Features.Codex;

namespace Habitat.Services;

internal sealed class CodexChatService(ICodexChatDesktop desktop, ICodexHistoryReader reader,
    Func<string, CancellationToken, Task<CodexDeliveryResult>> startNew, CodexChatOrigins origins) : ICodexChatService
{
    public async Task<CodexChatSelection> GetVisibleAsync(CancellationToken token)
    {
        var visible = await desktop.ReadVisibleAsync(token);
        if (visible is null) return new(null, true);
        if (visible.Composer.IsNewChat) return new(null, true);
        var target = await reader.ResolveAsync(visible.SelectedTitles, token);
        return target is null ? new(null, false, UiText.Get("SelectALocalCodexChatInTheSidebar")) :
            origins.Contains(target) ? new(target, false) : new(null, true);
    }

    public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) =>
        origins.Contains(target) ? reader.ReadAsync(target, token) : Task.FromResult(new CodexChatHistory(target, []));

    public async Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return new(CodexDeliveryResult.NotSent);
        var visible = await desktop.ReadVisibleAsync(token);
        var target = visible is null || visible.Composer.IsNewChat ? null : await reader.ResolveAsync(visible.SelectedTitles, token);
        if (visible is not null && !visible.Composer.IsNewChat && target is null) return new(CodexDeliveryResult.NotSent);
        if (target is null || !origins.Contains(target))
        {
            if (expected is not null || target is null && visible is { IsEmpty: false } && !CodexDelivery.SameText(visible.Composer.Text, text))
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
                    var current = await desktop.ReadVisibleAsync(timeout.Token);
                    var created = current is null || current.Composer.IsNewChat ? null :
                        await reader.ResolveAsync(current.SelectedTitles, timeout.Token);
                    if (created is not null)
                    {
                        if (created.Id == target?.Id || created.CreatedAt is not { } createdAt || createdAt < startedAt - 1 ||
                            createdAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1 || origins.Contains(created))
                            return new(CodexDeliveryResult.Unconfirmed);
                        var history = await reader.ReadAsync(created, timeout.Token);
                        // A recent timestamp alone could belong to a different
                        // chat opened while delivery was settling. Verify its
                        // initial instruction before recording provenance.
                        var first = history.Turns.FirstOrDefault()?.Messages.FirstOrDefault(message => message.Author == "YOU");
                        if (first is not null)
                        {
                            if (history.Target.Id != created.Id || !CodexDelivery.SameText(first.Text, text))
                                return new(CodexDeliveryResult.Unconfirmed);
                            origins.Register(created);
                            return new(delivery, created);
                        }
                    }
                    await Task.Delay(150, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(CodexDeliveryResult.Unconfirmed); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { return new(CodexDeliveryResult.Unconfirmed); }
        }
        if (target.Id != expected?.Id || !visible!.CanAccept ||
            !visible.IsEmpty && !CodexDelivery.SameText(visible.Composer.Text, text)) return new(CodexDeliveryResult.NotSent);
        var before = await reader.ReadAsync(target, token);
        if (before.IsRunning) return new(CodexDeliveryResult.NotSent);
        return new(await desktop.SubmitVisibleAsync(visible, text, token), target, before.LatestTurnId);
    }

    public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => desktop.OpenChatAsync(target, token);
    public void ReturnFocus(nint ownerWindow) => desktop.ReturnFocus(ownerWindow);
    public async ValueTask DisposeAsync() { if (reader is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
}
