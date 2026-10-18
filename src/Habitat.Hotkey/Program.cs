using System.Diagnostics;
using System.Text.Json;
using Habitat.Activation;

namespace Habitat.Hotkey;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        if (args is ["--stop"]) return CompanionShutdown.StopAsync().GetAwaiter().GetResult();
        if (args is ["--inspect-selection"])
        {
            var values = new Dictionary<string, object?>();
            var selected = SelectionProbe.SelectedHash(Native.GetForegroundWindow(), (key, value) => values[key] = value);
            values["HasSelection"] = selected is not null;
            Console.Write(JsonSerializer.Serialize(values));
            return 0;
        }
        if (args is ["--inspect-window", var inspectionHandle] && long.TryParse(inspectionHandle, out var inspectionSource))
        {
            var values = new Dictionary<string, object?>();
            SelectionProbe.SelectedHash((nint)inspectionSource, (key, value) => values[key] = value, inspectBackground: true);
            Console.Write(JsonSerializer.Serialize(values));
            return 0;
        }
        if (args is ["--selection", var handle] && long.TryParse(handle, out var source))
        {
            var hash = SelectionProbe.SelectedHash((nint)source);
            if (hash is null) return 2;
            Console.Write(hash);
            return 0;
        }
        if (!Enabled()) return 0;
        using var instance = new Mutex(true, "Local\\" + ActivationChannel.Name("HotkeyMutex"), out var owns);
        if (!owns) return 0;
        var window = Native.CreateWindowEx(0, "STATIC", "HABITAT hotkey", 0, 0, 0, 0, 0, -3, 0, 0, 0);
        if (window == 0) return 2;
        var registered = Native.RegisterHotKey(window, 1, 0x4002, 0x48);
        var error = registered ? null : "Ctrl+H is already in use. Disable it in the other application and enable it again here.";
        if (error is not null) Log(error);
        var channel = new ActivationChannel(ActivationChannel.Name("Hotkey"), request =>
        {
            if (request.Kind == "stop") Native.PostMessage(window, 0x8001, 0, 0);
            return Task.FromResult(new ActivationReply((request.Kind is "status" or "stop") &&
                (registered || request.Kind == "stop"), error, Environment.ProcessPath, Environment.ProcessId));
        });
        _ = channel.Completion.ContinueWith(failure =>
        {
            Log($"Activation channel stopped: {failure.Exception?.GetBaseException().GetType().Name}");
            Native.PostMessage(window, 0x8001, 0, 0);
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        var delivering = 0;
        try
        {
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Id == 0x8001) break;
                if (message.Id == 0x312 && registered)
                {
                    if (Interlocked.CompareExchange(ref delivering, 1, 0) != 0) continue;
                    try
                    {
                        string? text = null;
                        try { text = new SelectionCapture(new WindowsSelectionDesktop(window, Log)).Capture(); }
                        catch (Exception exception) { Log(exception.GetType().Name); }
                        var delivery = new ActivationDelivery(
                            request => ActivationChannel.SendAsync(ActivationChannel.Name("App"), request),
                            () => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HABITAT.exe")) { UseShellExecute = false }),
                            () => Task.Delay(100));
                        _ = DeliverAsync();
                        async Task DeliverAsync()
                        {
                            try
                            {
                                var result = await delivery.DeliverAsync(text);
                                if (!result.Accepted) Log(result.Error ?? "Activation failed.");
                            }
                            catch (Exception exception) { Log(exception.GetType().Name); }
                            finally { Interlocked.Exchange(ref delivering, 0); }
                        }
                    }
                    catch (Exception exception) { Interlocked.Exchange(ref delivering, 0); Log(exception.GetType().Name); }
                    continue;
                }
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
        }
        finally
        {
            if (registered) Native.UnregisterHotKey(window, 1);
            try { channel.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception exception) { Log(exception.GetType().Name); }
            Native.DestroyWindow(window);
            instance.ReleaseMutex();
        }
        return 0;
    }
    private static bool Enabled()
    {
        try
        {
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HABITAT", "preferences.json")));
            return !saved.RootElement.TryGetProperty("GlobalHotkeyEnabled", out var enabled) || enabled.GetBoolean();
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch { return false; }
    }
    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HABITAT");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "hotkey.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
