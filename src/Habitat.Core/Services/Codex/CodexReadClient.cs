using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Habitat.Features.Codex;

namespace Habitat.Services;

// A read-only app-server connection. No thread/resume, turn/start, login,
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

    public async Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> selectedTitles, CancellationToken token)
    {
        var matches = new Dictionary<string, CodexChatTarget>(StringComparer.Ordinal);
        foreach (var title in selectedTitles.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            var result = await RequestAsync("thread/list", new { limit = 100, searchTerm = title, archived = false, useStateDbOnly = true }, token);
            if (result.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                throw new InvalidDataException("Too many Codex chats match this title. Rename the visible chat to link it.");
            foreach (var thread in result.GetProperty("data").EnumerateArray())
            {
                if (String(thread, "name") != title) continue;
                var id = String(thread, "id");
                if (ValidId(id)) matches[id!] = new(id!, title,
                    thread.TryGetProperty("createdAt", out var created) && created.ValueKind == JsonValueKind.Number &&
                    created.TryGetInt64(out var timestamp) ? timestamp : null);
            }
        }
        if (matches.Count > 1) throw new InvalidDataException("More than one Codex chat has this title. Rename the chat in Codex to link it.");
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
            turns.Add(new(String(turn, "id") ?? "", String(turn, "status") ?? "unknown", messages,
                turn.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? String(error, "message") : null));
        }
        return new(target, turns);
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
            _process = Process.Start(info) ?? throw new IOException("Could not start the Codex reader.");
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
        if (method is not ("thread/list" or "thread/turns/list")) throw new InvalidOperationException("Only Codex history reads are allowed.");
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
            if (_process is null || _process.HasExited) throw new IOException("The Codex reader disconnected.");
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
                if (root.TryGetProperty("error", out _)) request.TrySetException(new IOException("Codex could not read this chat. Open it in Codex and reload."));
                else if (root.TryGetProperty("result", out var result)) request.TrySetResult(result.Clone());
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException or InvalidDataException)
        {
            _readerFailed = true;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("The Codex reader disconnected.", exception));
        }
        finally
        {
            _readerFailed = true;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("The Codex reader disconnected."));
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
