using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Habitat.Activation;

public sealed record ActivationRequest(Guid Id, string Kind = "activate", string? Text = null);
public sealed record ActivationReply(bool Accepted, string? Error = null, string? Executable = null, int? ProcessId = null);

// No command lines, disk payloads or network listener carry selected text.
public sealed class ActivationChannel : IAsyncDisposable
{
    public const int MaxTextLength = 128 * 1024;
    private const int MaxPacket = MaxTextLength * 6 + 1024;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    public Task Completion => _listener;
    public static string Name(string kind)
    {
        using var current = Process.GetCurrentProcess();
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}:{current.SessionId}";
        return $"HABITAT.{kind}.{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24]}";
    }

    public ActivationChannel(string name, Func<ActivationRequest, Task<ActivationReply>> receive)
    {
        // Reserve the first instance synchronously, including during app startup.
        var first = Create(name);
        _listener = ListenAsync(first, receive);
    }

    private static NamedPipeServerStream Create(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);

    private async Task ListenAsync(NamedPipeServerStream pipe, Func<ActivationRequest, Task<ActivationReply>> receive)
    {
        var recent = new Dictionary<Guid, ActivationReply>();
        var order = new Queue<Guid>();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var request = await ReadAsync<ActivationRequest>(pipe, deadline.Token);
                    ActivationReply reply;
                    if (request.Id == Guid.Empty || request.Text?.Length > MaxTextLength)
                        reply = new(false, "Invalid activation request.");
                    else if (recent.TryGetValue(request.Id, out var previous)) reply = previous;
                    else
                    {
                        order.Enqueue(request.Id);
                        if (order.Count > 256) recent.Remove(order.Dequeue());
                        reply = await receive(request);
                        recent.Add(request.Id, reply);
                    }
                    await WriteAsync(pipe, reply, deadline.Token);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException or InvalidDataException) { }
                finally { if (pipe.IsConnected) pipe.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { pipe.Dispose(); }
    }

    // A null result means no server was reached. Once written, an uncertain acknowledgement
    // is reported as an error and MUST NOT be retried (draft insertion is not replayed).
    public static async Task<ActivationReply?> SendAsync(string name, ActivationRequest request, int connectMilliseconds = 250)
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try { await pipe.ConnectAsync(connectMilliseconds, deadline.Token); }
        catch (Exception exception) when (exception is TimeoutException or IOException) { return null; }
        catch (UnauthorizedAccessException) { return new(false, "Windows denied access to the activation channel."); }
        if (OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var process))
            _ = AllowSetForegroundWindow(process);
        try
        {
            await WriteAsync(pipe, request, deadline.Token);
            return await ReadAsync<ActivationReply>(pipe, deadline.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException)
        { return new(false, "HABITAT did not acknowledge the request. It was not repeated."); }
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaxPacket) throw new InvalidDataException("Activation is too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    private static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token);
        var length = BitConverter.ToInt32(prefix);
        if (length is <= 0 or > MaxPacket) throw new InvalidDataException("Invalid activation packet.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty activation packet.");
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _listener;
        _stop.Dispose();
    }

    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint process);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
}
