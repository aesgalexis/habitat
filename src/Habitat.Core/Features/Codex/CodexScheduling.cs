using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Habitat.Services;

namespace Habitat.Features.Codex;

internal sealed record CodexScheduleTarget(CodexChatTarget? Target, bool UsageLimited, DateTimeOffset? ResumeAt = null);
internal sealed record CodexScheduledAttempt(CodexDeliveryResult Result, DateTimeOffset? RetryAt = null);
internal enum CodexScheduleState { Pending, Sending, Sent, NeedsAttention, Cancelled }
internal sealed record CodexScheduledMessage(Guid Id, CodexChatTarget? Target, string Text, DateTimeOffset SendAt,
    CodexScheduleState State = CodexScheduleState.Pending, string? Issue = null, DateTimeOffset? RetryAt = null,
    bool StartNewChat = false);

internal static class CodexUsageLimit
{
    internal static bool IsLimited(CodexChatHistory history)
    {
        var last = history.Turns.LastOrDefault();
        if (last is null || last.IsRunning) return false;
        var code = last.ErrorCode?.Replace("_", "").Replace("-", "");
        if (string.Equals(code, "UsageLimitExceeded", StringComparison.OrdinalIgnoreCase)) return true;
        // Only the final turn's own error/assistant notice counts. Never inspect
        // instructions or old failures, and never derive a timestamp from prose.
        var notice = last.Error ?? last.Messages.LastOrDefault(item => item.Author == "Codex")?.Text ?? "";
        return Regex.IsMatch(notice, @"(?i)(?:you(?:'|’)ve (?:hit|reached) (?:your |the )?(?:usage )?limit|usage limit (?:reached|exceeded)|(?:has |ha )?(?:alcanzado|llegado)(?:s)? (?:al |el |tu )?l[ií]mite|llegaste al l[ií]mite)",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}

// A durable one-shot outbox. Persist Sending before invoking anything that can
// submit; an uncertain outcome always needs a human retry, including a crash.
internal sealed class CodexScheduleQueue(string path)
{
    private sealed record Document(int Version, CodexScheduledMessage[] Messages);
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _gate = new();
    private List<CodexScheduledMessage>? _messages;
    public IReadOnlyList<CodexScheduledMessage> Messages { get { lock (_gate) { Load(); return _messages!.ToArray(); } } }
    public bool IsSending { get; private set; }

    private void Load()
    {
        if (_messages is not null) return;
        if (!File.Exists(path)) { _messages = []; return; }
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("The Codex outbox is too large.");
        var doc = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path), Json);
        if (doc is not { Version: 1 or 2, Messages: not null } || doc.Messages.Length > 500 ||
            doc.Messages.Any(item => item is null || item.Id == Guid.Empty || !Valid(item.Target, item.Text) ||
                item.StartNewChat != (item.Target is null) || doc.Version == 1 && item.StartNewChat ||
                !Enum.IsDefined(item.State) || item.Issue is not (null or "CodexScheduleUnconfirmed" or "CodexScheduleNotSent" or "CodexScheduleMissed")) ||
            doc.Messages.Select(item => item.Id).Distinct().Count() != doc.Messages.Length)
            throw new InvalidDataException("Invalid Codex outbox.");
        var recovered = doc.Messages.Select(item => item.State == CodexScheduleState.Sending
            ? item with { State = CodexScheduleState.NeedsAttention, Issue = "CodexScheduleUnconfirmed" } : item).ToList();
        if (doc.Messages.Any(item => item.State == CodexScheduleState.Sending))
            Save(recovered);
        _messages = recovered;
    }

    private static bool Valid(CodexChatTarget? target, string? text) =>
        (target is null || CodexReadClient.ValidId(target.Id) && target.Title is { Length: > 0 and <= 2000 }) &&
        text is { Length: > 0 and <= 200_000 } && !string.IsNullOrWhiteSpace(text) && !text.Contains('\0');
    private void Save(List<CodexScheduledMessage> next)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(2, next.ToArray()), Json);
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("The Codex outbox is full.");
        AtomicFile.Write(path, bytes);
    }
    private void Change(CodexScheduledMessage message)
    {
        lock (_gate)
        {
            Load();
            var next = _messages!.Select(item => item.Id == message.Id ? message : item).ToList();
            Save(next); _messages = next;
        }
    }

    public CodexScheduledMessage Add(CodexChatTarget? target, string text, DateTimeOffset when, DateTimeOffset now)
    {
        if (!Valid(target, text) || when <= now || when > now.AddYears(1)) throw new ArgumentException("Invalid scheduled Codex message.");
        lock (_gate)
        {
            Load();
            if (_messages!.Count >= 500) throw new InvalidDataException("The Codex outbox is full.");
            var item = new CodexScheduledMessage(Guid.NewGuid(), target, text, when.ToUniversalTime(), StartNewChat: target is null);
            var next = new List<CodexScheduledMessage>(_messages) { item };
            Save(next); _messages = next;
            return item;
        }
    }

    public bool Cancel(Guid id)
    {
        lock (_gate)
        {
            Load();
            var item = _messages!.SingleOrDefault(item => item.Id == id);
            if (item is null || item.State is not (CodexScheduleState.Pending or CodexScheduleState.NeedsAttention)) return false;
            Change(item with { State = CodexScheduleState.Cancelled, Issue = null });
            return true;
        }
    }

    public void Reschedule(Guid id, CodexChatTarget? target, string text, DateTimeOffset when, DateTimeOffset now)
    {
        if (!Valid(target, text) || when <= now || when > now.AddYears(1)) throw new ArgumentException("Invalid scheduled Codex message.");
        lock (_gate)
        {
            Load();
            var item = _messages!.SingleOrDefault(item => item.Id == id);
            if (item is null || item.State is not (CodexScheduleState.Pending or CodexScheduleState.NeedsAttention))
                throw new InvalidOperationException("This scheduled message can no longer be edited.");
            Change(new(id, target, text, when.ToUniversalTime(), StartNewChat: target is null));
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            Load();
            if (_messages!.Any(item => item.Id == id && item.State is CodexScheduleState.Pending or CodexScheduleState.Sending)) return false;
            var next = _messages!.Where(item => item.Id != id).ToList();
            if (next.Count == _messages!.Count) return false;
            Save(next); _messages = next; return true;
        }
    }

    public async Task RunDueAsync(DateTimeOffset now, Func<CodexScheduledMessage, CancellationToken, Task<CodexScheduledAttempt>> send,
        CancellationToken token)
    {
        if (!await _running.WaitAsync(0, token)) return;
        try
        {
            var item = await Task.Run(() =>
            {
                lock (_gate)
                {
                    Load();
                    var due = _messages!.Where(item => item.State == CodexScheduleState.Pending && (item.RetryAt ?? item.SendAt) <= now)
                        .OrderBy(item => item.SendAt).FirstOrDefault();
                    if (due is null) return null;
                    if (now - (due.RetryAt ?? due.SendAt) > TimeSpan.FromMinutes(5))
                    { Change(due with { State = CodexScheduleState.NeedsAttention, Issue = "CodexScheduleMissed" }); return null; }
                    token.ThrowIfCancellationRequested();
                    Change(due with { State = CodexScheduleState.Sending });
                    IsSending = true;
                    return due;
                }
            }, token);
            if (item is null) return;
            CodexScheduledAttempt attempt;
            try { attempt = await send(item, token); }
            catch { attempt = new(CodexDeliveryResult.Unconfirmed); }
            var next = attempt.Result == CodexDeliveryResult.Sent
                ? item with { State = CodexScheduleState.Sent, Issue = null, RetryAt = null }
                : attempt.RetryAt is { } retry && retry > now
                ? item with { RetryAt = retry, State = CodexScheduleState.Pending }
                : item with { State = CodexScheduleState.NeedsAttention,
                    Issue = attempt.Result == CodexDeliveryResult.NotSent
                        ? "CodexScheduleNotSent" : "CodexScheduleUnconfirmed" };
            await Task.Run(() => Change(next));
        }
        finally { IsSending = false; _running.Release(); }
    }
}
