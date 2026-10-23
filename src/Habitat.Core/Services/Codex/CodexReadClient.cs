using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Habitat.Features.Codex;

namespace Habitat.Services;

// A read-only app-server connection for history and usage. No thread/resume, turn/start, login,
// configuration writes, or model/tool execution is allowed on this connection.
internal sealed class CodexReadClient(string executable) : ICodexHistoryReader, IAsyncDisposable
{
    private readonly SemaphoreSlim _start = new(1, 1), _write = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _read;
    private Task? _diagnostics;
    private long _sequence;
    private bool _disposed;
    private volatile bool _readerFailed;

    public async Task<IReadOnlyList<CodexChatTarget>> ListAsync(CancellationToken token)
    {
        var targets = new Dictionary<string, CodexChatTarget>(StringComparer.Ordinal);
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var result = await RequestAsync("thread/list", new { limit = 100, cursor, archived = false,
                sortKey = "updated_at", sourceKinds = new[] { "cli", "vscode", "appServer" }, useStateDbOnly = true }, token);
            foreach (var thread in result.GetProperty("data").EnumerateArray())
            {
                var id = String(thread, "id");
                var title = String(thread, "name") ?? String(thread, "preview");
                if (ValidId(id) && !string.IsNullOrWhiteSpace(title))
                    targets[id!] = ParseTarget(thread, id!, title);
            }
            cursor = String(result, "nextCursor");
            if (cursor is not null && (!seen.Add(cursor) || targets.Count >= 1000))
                throw new InvalidDataException(UiText.Get("CodexScheduleTooManyChats"));
        } while (cursor is not null);
        return targets.Values.ToArray();
    }

    public async Task<DateTimeOffset?> ReadLimitResetAsync(CancellationToken token)
    {
        try
        {
            var result = await RequestAsync("account/rateLimits/read", new { }, token);
            return ParseLimitReset(result, DateTimeOffset.UtcNow);
        }
        catch (IOException) { return null; } // Older readers may not expose this optional read.
    }

    internal static DateTimeOffset? ParseLimitReset(JsonElement result, DateTimeOffset now)
    {
        var resets = new List<DateTimeOffset>();
        void Add(JsonElement bucket)
        {
            foreach (var name in new[] { "primary", "secondary" })
                if (bucket.ValueKind == JsonValueKind.Object && bucket.TryGetProperty(name, out var window) && window.ValueKind == JsonValueKind.Object &&
                    window.TryGetProperty("usedPercent", out var used) && used.ValueKind == JsonValueKind.Number && used.TryGetDouble(out var percent) && percent >= 100 &&
                    window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var seconds) &&
                    seconds is >= 0 and <= 253402300799)
                {
                    var at = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    if (at > now) resets.Add(at);
                }
        }
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
            foreach (var bucket in buckets.EnumerateObject()) Add(bucket.Value);
        else if (result.TryGetProperty("rateLimits", out var bucket)) Add(bucket);
        return resets.Count == 0 ? null : resets.Max();
    }

    public async Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> selectedTitles, CancellationToken token)
    {
        var matches = new Dictionary<string, CodexChatTarget>(StringComparer.Ordinal);
        foreach (var title in selectedTitles.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            var result = await RequestAsync("thread/list", new { limit = 100, searchTerm = title, archived = false,
                sourceKinds = new[] { "cli", "vscode", "appServer" }, useStateDbOnly = true }, token);
            if (result.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                throw new InvalidDataException(UiText.Get("TooManyCodexChatsMatchThisTitleRename"));
            foreach (var thread in result.GetProperty("data").EnumerateArray())
            {
                if (String(thread, "name") != title) continue;
                var id = String(thread, "id");
                if (ValidId(id)) matches[id!] = ParseTarget(thread, id!, title);
            }
        }
        if (matches.Count > 1) throw new InvalidDataException(UiText.Get("MoreThanOneCodexChatHasThisTitle"));
        return matches.Values.SingleOrDefault();
    }

    public async Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token)
    {
        if (!ValidId(target.Id)) throw new InvalidDataException("Invalid Codex chat identifier.");
        var result = await RequestAsync("thread/turns/list", new { threadId = target.Id, limit = 20, sortDirection = "desc", itemsView = "full" }, token);
        return ParseHistory(target, result);
    }

    internal static CodexChatHistory ParseHistory(CodexChatTarget target, JsonElement result)
    {
        var turns = new List<CodexChatTurn>();
        foreach (var turn in result.GetProperty("data").EnumerateArray().Reverse())
        {
            var messages = new List<CodexChatMessage>();
            if (turn.TryGetProperty("items", out var items))
                foreach (var item in items.EnumerateArray())
                {
                    var type = String(item, "type");
                    if (type == "userMessage" && item.TryGetProperty("content", out var content))
                    {
                        var text = string.Join("\n", content.EnumerateArray().Where(part => String(part, "type") == "text")
                            .Select(part => String(part, "text") ?? ""));
                        messages.Add(new(String(item, "id") ?? "", "YOU", text));
                    }
                    else if (type == "agentMessage" && String(item, "phase") is null or "final_answer")
                        messages.Add(new(String(item, "id") ?? "", "Codex", String(item, "text") ?? ""));
                }
            turn.TryGetProperty("error", out var error);
            string? code = null;
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("codexErrorInfo", out var info))
                code = info.ValueKind == JsonValueKind.String ? info.GetString() : info.ValueKind == JsonValueKind.Object
                    ? String(info, "type") ?? info.EnumerateObject().FirstOrDefault().Name : null;
            turns.Add(new(String(turn, "id") ?? "", String(turn, "status") ?? "unknown", messages,
                error.ValueKind == JsonValueKind.Object ? String(error, "message") : null, code));
        }
        return new(target, turns);
    }

    private static CodexChatTarget ParseTarget(JsonElement thread, string id, string title) => new(id, title,
        thread.TryGetProperty("createdAt", out var created) && created.ValueKind == JsonValueKind.Number &&
        created.TryGetInt64(out var timestamp) ? timestamp : null)
    {
        Settings = ParseSettings(thread)
    };

    internal static CodexChatSettings? ParseSettings(JsonElement thread)
    {
        // This private reader does not load/resume chats. Treat returned metadata
        // as the latest persisted configuration, not live per-turn telemetry.
        var model = String(thread, "model");
        var effort = String(thread, "reasoningEffort");
        return string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(effort) ? null : new(model, effort);
    }

    internal static bool ValidId(string? value) => Guid.TryParseExact(value, "D", out _);
    private static string? String(JsonElement value, string key) =>
        value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        await _start.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is { HasExited: false } && !_readerFailed) return;
            if (_read is not null) await _read;
            if (_diagnostics is not null) await _diagnostics;
            _process?.Dispose();
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--stdio");
            _process = Process.Start(info) ?? throw new IOException(UiText.Get("CouldNotStartTheCodexReader"));
            _readerFailed = false;
            _read = ReadLoopAsync(_process, _lifetime.Token);
            _diagnostics = DrainDiagnosticsAsync(_process, _lifetime.Token);
            await SendRequestAsync("initialize", new { clientInfo = new { name = "habitat_reader", title = "HABITAT", version = "0.1.0" } }, token);
            await WriteAsync(new { method = "initialized", @params = new { } }, token);
        }
        catch
        {
            StopProcess();
            throw;
        }
        finally { _start.Release(); }
    }

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken token)
    {
        if (method is not ("thread/list" or "thread/turns/list" or "account/rateLimits/read")) throw new InvalidOperationException("Only Codex history and usage reads are allowed.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await EnsureStartedAsync(timeout.Token);
        return await SendRequestAsync(method, parameters, timeout.Token);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken token)
    {
        var id = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, token);
            return await completion.Task.WaitAsync(token);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task WriteAsync(object message, CancellationToken token)
    {
        await _write.WaitAsync(token);
        try
        {
            if (_process is null || _process.HasExited) throw new IOException(UiText.Get("TheCodexReaderDisconnected"));
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token);
            await _process.StandardInput.FlushAsync(token);
        }
        finally { _write.Release(); }
    }

    private async Task ReadLoopAsync(Process process, CancellationToken token)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (line.Length > 16 * 1024 * 1024) throw new InvalidDataException("Codex history exceeded the reader limit.");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Invalid Codex reader frame.");
                if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                    !id.TryGetInt64(out var number) || !_pending.TryGetValue(number, out var request)) continue;
                if (root.TryGetProperty("error", out _)) request.TrySetException(new IOException(UiText.Get("CodexCouldNotReadThisChatOpenIt")));
                else if (root.TryGetProperty("result", out var result)) request.TrySetResult(result.Clone());
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException or InvalidDataException)
        {
            _readerFailed = true;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException(UiText.Get("TheCodexReaderDisconnected"), exception));
        }
        finally
        {
            _readerFailed = true;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException(UiText.Get("TheCodexReaderDisconnected")));
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    private void StopProcess()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static async Task DrainDiagnosticsAsync(Process process, CancellationToken token)
    {
        // Discard diagnostics with bounded memory. ReadToEndAsync would retain
        // the helper's entire stderr for as long as HABITAT remains open.
        var buffer = new char[4096];
        try { while (await process.StandardError.ReadAsync(buffer.AsMemory(), token) > 0) { } }
        catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); StopProcess();
        if (_read is not null) { try { await _read; } catch (OperationCanceledException) { } }
        if (_diagnostics is not null) await _diagnostics;
        _process?.Dispose(); _lifetime.Dispose();
    }
}
