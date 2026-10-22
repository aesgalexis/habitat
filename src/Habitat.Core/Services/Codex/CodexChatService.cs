using Habitat.Features.Codex;

namespace Habitat.Services;

internal sealed class CodexChatService(ICodexChatDesktop desktop, ICodexHistoryReader reader,
    Func<string, CancellationToken, Task<CodexDeliveryResult>> startNew, CodexChatOrigins origins) : ICodexChatService
{
    public async Task<IReadOnlyList<CodexScheduleTarget>> ListScheduleTargetsAsync(CancellationToken token)
    {
        var targets = (await reader.ListAsync(token)).Where(origins.Contains).ToArray();
        using var gate = new SemaphoreSlim(4);
        var candidates = await Task.WhenAll(targets.Select(async target =>
        {
            await gate.WaitAsync(token);
            try { return new CodexScheduleTarget(target, CodexUsageLimit.IsLimited(await reader.ReadAsync(target, token))); }
            finally { gate.Release(); }
        }));
        var reset = candidates.Any(item => item.UsageLimited) ? await reader.ReadLimitResetAsync(token) : null;
        return candidates.Select(item => item.UsageLimited ? item with { ResumeAt = reset } : item)
            .OrderByDescending(item => item.UsageLimited).ToArray();
    }

    public async Task<CodexScheduledAttempt> SendScheduledAsync(CodexChatTarget? target, string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return new(CodexDeliveryResult.NotSent);
        if (target is null)
        {
            var visible = await desktop.ReadVisibleAsync(token);
            if (visible is { Composer.IsNewChat: true, IsEmpty: false }) return new(CodexDeliveryResult.NotSent);
            if (await reader.ReadLimitResetAsync(token) is { } newReset && newReset > DateTimeOffset.UtcNow)
                return new(CodexDeliveryResult.NotSent, newReset.AddSeconds(5));
            return new((await StartNewAsync(text, null, token)).Result);
        }
        if (!CodexReadClient.ValidId(target.Id) || !origins.Contains(target))
            return new(CodexDeliveryResult.NotSent);
        // Apply the same origin boundary to the picker and actual delivery,
        // including messages persisted by an earlier version of HABITAT.
        var before = await reader.ReadAsync(target, token);
        if (before.Target.Id != target.Id) return new(CodexDeliveryResult.NotSent);
        if (before.IsRunning) return new(CodexDeliveryResult.NotSent, DateTimeOffset.UtcNow.AddSeconds(30));
        if (CodexUsageLimit.IsLimited(before) && await reader.ReadLimitResetAsync(token) is { } reset && reset > DateTimeOffset.UtcNow)
            return new(CodexDeliveryResult.NotSent, reset.AddSeconds(5));
        if (!await desktop.OpenChatAsync(target, token)) return new(CodexDeliveryResult.NotSent);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var visible = await desktop.ReadVisibleAsync(timeout.Token);
            if (visible is not null && !visible.Composer.IsNewChat)
            {
                var selected = await reader.ResolveAsync(visible.SelectedTitles, timeout.Token);
                if (selected?.Id == target.Id)
                {
                    if (!visible.CanAccept || !visible.IsEmpty) return new(CodexDeliveryResult.NotSent);
                    if ((await reader.ReadAsync(target, timeout.Token)).IsRunning)
                        return new(CodexDeliveryResult.NotSent, DateTimeOffset.UtcNow.AddSeconds(30));
                    return new(await desktop.SubmitVisibleAsync(visible, text, timeout.Token));
                }
            }
            await Task.Delay(150, timeout.Token);
        }
    }

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
            return await StartNewAsync(text, target?.Id, token);
        }
        if (target.Id != expected?.Id || !visible!.CanAccept ||
            !visible.IsEmpty && !CodexDelivery.SameText(visible.Composer.Text, text)) return new(CodexDeliveryResult.NotSent);
        var before = await reader.ReadAsync(target, token);
        if (before.IsRunning) return new(CodexDeliveryResult.NotSent);
        return new(await desktop.SubmitVisibleAsync(visible, text, token), target, before.LatestTurnId);
    }

    private async Task<CodexChatSubmission> StartNewAsync(string text, string? previousId, CancellationToken token)
    {
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
                    if (created.Id == previousId || created.CreatedAt is not { } createdAt || createdAt < startedAt - 1 ||
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

    public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => desktop.OpenChatAsync(target, token);
    public void ReturnFocus(nint ownerWindow) => desktop.ReturnFocus(ownerWindow);
    public async ValueTask DisposeAsync() { if (reader is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
}
