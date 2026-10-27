using Habitat.LegacyUpdates;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal static class LegacyUpdateLifecycleChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var native = Path.Combine(root, "native-fixture");
        Directory.CreateDirectory(native);
        var source = Path.Combine(native, "Fixture.cs");
        await File.WriteAllTextAsync(source, """
            using System; using System.IO; using System.Net; using System.Text; using System.Diagnostics; using System.Threading;
            public static class Fixture {
              public static int Main(string[] args) {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                if (args.Length > 0 && args[0] == "--parent") {
                  while (!File.Exists(Path.Combine(dir, "close-parent"))) Thread.Sleep(50);
                  return 0;
                }
                File.WriteAllText(Path.Combine(dir, "started-pid"), Process.GetCurrentProcess().Id.ToString());
                if (File.Exists(Path.Combine(dir, "fail-startup"))) return 23;
                var port = File.ReadAllText(Path.Combine(dir, "port"));
                var listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start();
                while (true) {
                  var context = listener.GetContext();
                  var text = context.Request.Url.AbsolutePath == "/pair" ? "{\"key\":\"synthetic\"}" :
                    "{\"protocolVersion\":1,\"processId\":" + Process.GetCurrentProcess().Id + "}";
                  var bytes = Encoding.UTF8.GetBytes(text); context.Response.ContentType = "application/json";
                  context.Response.OutputStream.Write(bytes, 0, bytes.Length); context.Response.Close();
                }
              }
            }
            """);
        var executable = Path.Combine(native, "Fixture.exe");
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework64/v4.0.30319/csc.exe");
        using (var compile = Process.Start(new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "/nologo", "/target:winexe", "/out:" + executable, source } })!)
        {
            await compile.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            check(compile.ExitCode == 0, "Compile a private native parent and startup health server for old-updater lifecycle checks.");
        }
        var wrapper = Path.Combine(native, "Run-Helper.ps1");
        await File.WriteAllTextAsync(wrapper, """
            param([string]$Helper, [string]$Request, [int]$Port)
            # Keep the historical helper byte-for-byte unchanged. Redirect only
            # health requests away from the user's bridge to the private fixture.
            function Invoke-RestMethod {
                param([string]$Uri, $Headers, [int]$TimeoutSec)
                if ($Uri -notmatch '^http://127\.0\.0\.1:8765/(pair|status)$') { throw 'Unexpected fixture health request.' }
                Microsoft.PowerShell.Utility\Invoke-RestMethod -Uri ($Uri.Replace(':8765/', ':' + $Port + '/')) -Headers $Headers -TimeoutSec $TimeoutSec
            }
            & $Helper -RequestPath $Request
            exit $LASTEXITCODE
            """);
        foreach (var modern in new[] { false, true })
        foreach (var failStartup in new[] { false, true })
        {
            var scenario = Path.Combine(root, (modern ? "fixed-" : "legacy-") + (failStartup ? "startup-rollback" : "success"));
            var old = Path.Combine(scenario, "artifacts/releases/release-e464b7ac0eb54f75a0b9e0e57c50197f");
            Directory.CreateDirectory(old);
            var oldExe = Path.Combine(old, "HABITAT.exe");
            File.Copy(executable, oldExe);
            var pointer = Path.Combine(scenario, "artifacts/current-release.txt");
            const string oldPointer = "releases\\release-e464b7ac0eb54f75a0b9e0e57c50197f";
            await File.WriteAllTextAsync(pointer, oldPointer);
            await File.WriteAllTextAsync(Path.Combine(scenario, "HABITAT.vbs"), "synthetic launcher; never executed");
            var protectedFiles = new[] { "src/user-code.cs", "profile/tasks.json", "profile/preferences.json", "profile/credentials-marker.bin" };
            foreach (var name in protectedFiles)
            {
                var path = Path.Combine(scenario, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "Synthetic state to preserve: " + name);
            }
            var port = FreePort();
            await File.WriteAllTextAsync(Path.Combine(old, "port"), port.ToString());
            var package = UpdateMigrationChecks.Package(false, await File.ReadAllBytesAsync(executable), failStartup, port);
            using var http = new HttpClient(new UpdateMigrationChecks.DownloadFixture(package));
            using var client = new ReleaseUpdateClient(new(0, 16, 1), UpdatePackageKind.Zip, Path.Combine(scenario, "updates"), http);
            var prepared = await client.PrepareAsync((await client.CheckAsync(default))!, new UpdateMigrationChecks.Progress(), default);
            // DesktopUpdateService copies these files from the active old release,
            // not from the newly downloaded package.
            var oldUpdaters = Path.Combine(old, "updater"); Directory.CreateDirectory(oldUpdaters);
            foreach (var name in new[] { "install-release-update.ps1", "Habitat.Update.psm1", "Habitat.Release.psm1" })
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, modern ? "release-updater" : "legacy-updater", name), Path.Combine(oldUpdaters, name));
                File.Copy(Path.Combine(oldUpdaters, name), Path.Combine(prepared.Directory, name));
            }
            using var parent = Process.Start(new ProcessStartInfo(oldExe) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--parent" } })!;
            var request = Path.Combine(prepared.Directory, "request.json");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new { version = "0.16.4", kind = "Zip", installRoot = scenario,
                currentExe = oldExe, processId = parent.Id,
                processStartTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                packagePath = prepared.PackagePath, sha256 = prepared.Sha256, extractedDirectory = prepared.ExtractedDirectory }));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32/WindowsPowerShell/v1.0/powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = prepared.Directory,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", wrapper,
                    "-Helper", Path.Combine(prepared.Directory, "install-release-update.ps1"), "-Request", request, "-Port", port.ToString() }
            };
            // Exactly the unfiltered environment inherited by the old launcher.
            start.Environment["PSModulePath"] = Path.Combine(scenario, "incompatible module paths");
            using var helper = Process.Start(start)!;
            var output = helper.StandardOutput.ReadToEndAsync();
            var error = helper.StandardError.ReadToEndAsync();
            try
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
                while (!File.Exists(Path.Combine(prepared.Directory, "ready")) && !helper.HasExited && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(50);
                if (!File.Exists(Path.Combine(prepared.Directory, "ready")))
                    throw new Exception("Legacy helper did not prepare: " + await error +
                        (File.Exists(Path.Combine(prepared.Directory, "update-error.log")) ? await File.ReadAllTextAsync(Path.Combine(prepared.Directory, "update-error.log")) : ""));
                check(!parent.HasExited && File.ReadAllText(pointer) == oldPointer,
                    "The unchanged old helper prepares while keeping the parent and active pointer intact, even with inherited module paths on this host.");
                await File.WriteAllTextAsync(Path.Combine(old, "close-parent"), "normal synthetic shutdown");
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                await File.WriteAllTextAsync(Path.Combine(scenario, "helper-output.log"), await output + await error);
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(scenario, "updates/last-result.json")));
                check(result.RootElement.GetProperty("succeeded").GetBoolean() != failStartup && (helper.ExitCode == 0) != failStartup,
                    "The old helper reports the exact synthetic installation/startup result.");
                var currentPointer = (await File.ReadAllTextAsync(pointer)).Trim();
                check(failStartup ? currentPointer == oldPointer : currentPointer != oldPointer,
                    failStartup ? "Failed startup restores the previous active pointer." : "After parent shutdown, the old helper activates a new release.");
                var active = Path.Combine(scenario, "artifacts", currentPointer);
                check(File.Exists(Path.Combine(active, "started-pid")),
                    failStartup ? "Rollback reopens the old native executable and verifies its private health endpoint." : "The updated native executable starts and passes the process-identity health check.");
                foreach (var name in protectedFiles)
                    check(await File.ReadAllTextAsync(Path.Combine(scenario, name)) == "Synthetic state to preserve: " + name,
                        "The ZIP lifecycle preserves source and synthetic user state: " + name);
                check(File.Exists(oldExe), "Both successful installation and recovery retain the original release.");
                if (modern)
                {
                    var diagnostics = await File.ReadAllTextAsync(Path.Combine(scenario, "updates/diagnostics", Path.GetFileName(prepared.Directory) + ".jsonl"));
                    check(diagnostics.Contains("wait-parent") && diagnostics.Contains("activation") && diagnostics.Contains("startup") &&
                        diagnostics.Contains(failStartup ? "rollback" : "complete"),
                        "The production helper records preparation, activation, startup and recovery phases independently of the old application.");
                }
            }
            finally
            {
                if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(); }
                if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
                foreach (var marker in Directory.EnumerateFiles(Path.Combine(scenario, "artifacts/releases"), "started-pid", SearchOption.AllDirectories))
                {
                    try
                    {
                        using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker)));
                        if (child.MainModule!.FileName == Path.Combine(Path.GetDirectoryName(marker)!, "HABITAT.exe"))
                        { child.Kill(); await child.WaitForExitAsync(); }
                    }
                    catch (ArgumentException) { }
                }
            }
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); }
    }
}
