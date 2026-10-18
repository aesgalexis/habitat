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
        using var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-fixture")
            { UseShellExecute = false, CreateNoWindow = true })!;
        var matchingName = Channel();
        var matchingStops = 0;
        await using (var server = new ActivationChannel(matchingName, request =>
        {
            if (request.Kind == "stop") { matchingStops++; fixture.Kill(); }
            return Task.FromResult(new ActivationReply(true, Executable: Environment.ProcessPath, ProcessId: fixture.Id));
        }))
        {
            Check(await CompanionShutdown.StopAsync(matchingName) == 0 && matchingStops == 1 && fixture.HasExited,
                "Matching companion should receive one stop request and exit before uninstall continues.");
        }
        Console.WriteLine("Companion shutdown checks passed with isolated channels and a synthetic child process.");
    }
}
