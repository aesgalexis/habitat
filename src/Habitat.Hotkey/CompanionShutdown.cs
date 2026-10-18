using System.Diagnostics;
using Habitat.Activation;

namespace Habitat.Hotkey;

internal static class CompanionShutdown
{
    internal static async Task<int> StopAsync(string? channelName = null)
    {
        var name = channelName ?? ActivationChannel.Name("Hotkey");
        var status = await ActivationChannel.SendAsync(name, new(Guid.NewGuid(), "status"));
        if (status is null) return 0;
        if (status.Executable is null) return 2;
        // An uninstaller must never stop a companion from another installation.
        if (!string.Equals(status.Executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 0;
        if (status.ProcessId is not { } id) return 2;
        try
        {
            using var process = Process.GetProcessById(id);
            if (!string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 2;
            var reply = await ActivationChannel.SendAsync(name, new(Guid.NewGuid(), "stop"));
            if (reply?.Accepted != true) return 2;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            return 0;
        }
        catch (ArgumentException) { return 0; } // It already exited.
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return 2; }
    }
}
