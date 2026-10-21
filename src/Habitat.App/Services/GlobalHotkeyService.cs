using Habitat.Activation;
using Microsoft.Win32;
using System.Diagnostics;

namespace Habitat.Services;

internal sealed class GlobalHotkeyService(AppDataPaths paths)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HABITAT.CtrlH";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> ApplyAsync(bool enabled)
    {
        if (paths.IsIsolated) return null;
        await _gate.WaitAsync();
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "HABITAT.Hotkey.exe");
            var status = await ActivationChannel.SendAsync(ActivationChannel.Name("Hotkey"), new(Guid.NewGuid(), "status"));
            if (!enabled || status?.Executable != executable)
            {
                if (status is not null)
                {
                    await ActivationChannel.SendAsync(ActivationChannel.Name("Hotkey"), new(Guid.NewGuid(), "stop"));
                    if (status.Executable is { } oldPath && status.ProcessId is { } processId)
                    {
                        // Wait for this user's exact companion to release RegisterHotKey and its mutex.
                        try
                        {
                            using var process = Process.GetProcessById(processId);
                            if (string.Equals(process.MainModule?.FileName, oldPath, StringComparison.OrdinalIgnoreCase))
                            {
                                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                                await process.WaitForExitAsync(timeout.Token);
                            }
                        }
                        catch (ArgumentException) { } // It exited after acknowledging stop.
                    }
                }
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!enabled) { key.DeleteValue(ValueName, false); return null; }
            if (!File.Exists(executable)) return UiText.Get("TheShortcutComponentIsMissingReinstallHABITATAnd");
            var launcher = FindStableLauncher();
            key.SetValue(ValueName, launcher is null ? $"\"{executable}\"" :
                $"\"{Environment.GetFolderPath(Environment.SpecialFolder.Windows)}\\System32\\wscript.exe\" \"{launcher}\"", RegistryValueKind.String);
            if (status?.Executable == executable) return status.Accepted ? null : UiText.LocalizeMessage(status.Error ?? "");
            using var started = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true });
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(100);
                var ready = await ActivationChannel.SendAsync(ActivationChannel.Name("Hotkey"), new(Guid.NewGuid(), "status"), 100);
                if (ready is not null) return ready.Accepted ? null : UiText.LocalizeMessage(ready.Error ?? "");
            }
            return UiText.Get("TheShortcutCouldNotStartDisableCtrlHAnd");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return UiText.Get("TheShortcutSettingsCouldNotBeAppliedTry"); }
        finally { _gate.Release(); }
    }

    private static string? FindStableLauncher()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "HABITAT-Hotkey.vbs");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
