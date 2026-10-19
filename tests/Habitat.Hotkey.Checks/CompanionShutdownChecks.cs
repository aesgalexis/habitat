using System.Diagnostics;
using Habitat.Activation;
using Habitat.Hotkey;

internal static class CompanionShutdownChecks
{
    internal static async Task RunAsync()
    {
        static string Channel() => "HABITAT.Shutdown.Check." + Guid.NewGuid().ToString("N");
        static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
        }

        Check(await CompanionShutdown.StopAsync(Channel()) == 0, "Absent companion should be harmless.");
        var foreignName = Channel();
        var foreignStops = 0;
        await using (var server = new ActivationChannel(foreignName, request =>
        {
            if (request.Kind == "stop") foreignStops++;
            return Task.FromResult(new ActivationReply(true, Executable: @"C:\another-installation\HABITAT.Hotkey.exe", ProcessId: Environment.ProcessId));
        }))
        {
            Check(await CompanionShutdown.StopAsync(foreignName) == 0 && foreignStops == 0,
                "Uninstall must not stop another installation's companion.");
        }
        var incompleteName = Channel();
        await using (var server = new ActivationChannel(incompleteName, _ =>
            Task.FromResult(new ActivationReply(true, Executable: Environment.ProcessPath))))
        {
            Check(await CompanionShutdown.StopAsync(incompleteName) == 2, "Missing process identity must fail closed.");
        }
        var readyFile = Path.Combine(AppContext.BaseDirectory, "companion-" + Guid.NewGuid().ToString("N") + ".ready");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--wait-fixture");
        start.ArgumentList.Add(readyFile);
        using var fixture = Process.Start(start)!;
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(readyFile))
            {
                if (fixture.HasExited) throw new Exception("Synthetic companion exited before readiness.");
                await Task.Delay(20, startup.Token);
            }
            var matchingName = Channel();
            var matchingStops = 0;
            await using (var server = new ActivationChannel(matchingName, request =>
            {
                if (request.Kind == "stop") { matchingStops++; fixture.Kill(); }
                return Task.FromResult(new ActivationReply(true, Executable: Environment.ProcessPath, ProcessId: fixture.Id));
            }))
            {
                var result = await CompanionShutdown.StopAsync(matchingName);
                Check(result == 0 && matchingStops == 1 && fixture.HasExited,
                    $"Matching companion should receive one stop request and exit before uninstall continues. Result: {result}; stops: {matchingStops}; exited: {fixture.HasExited}.");
            }
        }
        finally
        {
            if (!fixture.HasExited) { fixture.Kill(); await fixture.WaitForExitAsync(); }
            File.Delete(readyFile);
        }
        Console.WriteLine("Companion shutdown checks passed with isolated channels and a synthetic child process.");
    }
}
