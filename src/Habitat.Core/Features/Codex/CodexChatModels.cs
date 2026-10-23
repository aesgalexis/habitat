using Habitat.Services;
using System.Text.Json.Serialization;

namespace Habitat.Features.Codex;

internal sealed record CodexChatTarget(string Id, string Title, long? CreatedAt = null)
{
    // Display metadata refreshed by the reader, never part of a scheduled delivery.
    [JsonIgnore]
    public CodexChatSettings? Settings { get; init; }
}

internal sealed record CodexChatSettings(string? Model, string? ReasoningEffort)
{
    internal static string Format(CodexChatSettings? settings)
    {
        var model = string.IsNullOrWhiteSpace(settings?.Model) ? UiText.Get("CodexSettingUnavailable") : settings.Model;
        var effort = settings?.ReasoningEffort switch
        {
            "none" => UiText.Get("CodexReasoningNone"),
            "minimal" => UiText.Get("CodexReasoningMinimal"),
            "low" => UiText.Get("CodexReasoningLow"),
            "medium" => UiText.Get("CodexReasoningMedium"),
            "high" => UiText.Get("CodexReasoningHigh"),
            "xhigh" => UiText.Get("CodexReasoningExtraHigh"),
            "max" => UiText.Get("CodexReasoningMax"),
            "ultra" => UiText.Get("CodexReasoningUltra"),
            "auto" => UiText.Get("CodexReasoningAuto"),
            { } value when !string.IsNullOrWhiteSpace(value) => value,
            _ => UiText.Get("CodexSettingUnavailable")
        };
        return UiText.Format(string.IsNullOrWhiteSpace(settings?.Model) && string.IsNullOrWhiteSpace(settings?.ReasoningEffort)
            ? "CodexChatSettings" : "CodexChatLastKnownSettings", model, effort);
    }
}
internal sealed record CodexChatMessage(string Id, string Author, string Text);
internal sealed record CodexChatTurn(string Id, string Status, IReadOnlyList<CodexChatMessage> Messages, string? Error = null,
    string? ErrorCode = null)
{
    public bool IsRunning => Status is "inProgress" or "in_progress";
}
internal sealed record CodexChatHistory(CodexChatTarget Target, IReadOnlyList<CodexChatTurn> Turns)
{
    public string? LatestTurnId => Turns.LastOrDefault()?.Id;
    public bool IsRunning => Turns.LastOrDefault()?.IsRunning is true;
}
internal sealed record CodexChatSelection(CodexChatTarget? Target, bool CanStartNew, string? Issue = null);
internal sealed record CodexChatSubmission(CodexDeliveryResult Result, CodexChatTarget? Target = null, string? PreviousTurnId = null);

internal interface ICodexChatService : IAsyncDisposable
{
    Task<IReadOnlyList<CodexScheduleTarget>> ListScheduleTargetsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<CodexScheduleTarget>>([]);
    Task<CodexScheduledAttempt> SendScheduledAsync(CodexChatTarget? target, string text, CancellationToken token, string? interruptedTurnId = null) =>
        Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.NotSent));
    Task<CodexChatSelection> GetVisibleAsync(CancellationToken token);
    Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token);
    Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token);
    Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token);
    void ReturnFocus(nint ownerWindow);
}

internal interface ICodexHistoryReader
{
    Task<IReadOnlyList<CodexChatTarget>> ListAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CodexChatTarget>>([]);
    Task<DateTimeOffset?> ReadLimitResetAsync(CancellationToken token) => Task.FromResult<DateTimeOffset?>(null);
    Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> selectedTitles, CancellationToken token);
    Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token);
}

internal sealed record CodexVisibleChat(CodexComposer Composer, IReadOnlyList<string> SelectedTitles, bool IsEmpty, bool CanAccept);
internal interface ICodexChatDesktop
{
    Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token);
    Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token);
    Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token);
    void ReturnFocus(nint ownerWindow);
}

internal sealed class DisabledCodexChatService : ICodexChatService
{
    public Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => Task.FromResult(new CodexChatSelection(null, false));
    public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) => Task.FromResult(new CodexChatHistory(target, []));
    public Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token) =>
        Task.FromResult(new CodexChatSubmission(CodexDeliveryResult.NotSent));
    public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => Task.FromResult(false);
    public void ReturnFocus(nint ownerWindow) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
