using Habitat.Services;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ReleaseUpdateHandoffChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var fixture = Path.Combine(root, "release helper fixtures");
        Directory.CreateDirectory(fixture);
        var helperSource = Path.Combine(AppContext.BaseDirectory, "release-updater");
        var inheritedModules = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            // Reproduce a desktop launched from PowerShell 7 (or another host)
            // with module paths Windows PowerShell 5.1 cannot use.
            Environment.SetEnvironmentVariable("PSModulePath", Path.Combine(fixture, "incompatible modules"));
            var launch = AppUpdateInstall.CreateHelperStartInfo(
                Path.Combine(fixture, "install-release-update.ps1"), "-RequestPath", Path.Combine(fixture, "request.json"));
            check(!launch.Environment.ContainsKey("PSModulePath") && !launch.UseShellExecute && launch.CreateNoWindow &&
                launch.WindowStyle == ProcessWindowStyle.Hidden && launch.WorkingDirectory == fixture &&
                launch.ArgumentList.TakeLast(2).SequenceEqual(new[] { "-RequestPath", Path.Combine(fixture, "request.json") }),
                "The production updater uses a hidden structured launch and lets Windows PowerShell initialise compatible modules.");
            if (!OperatingSystem.IsWindows()) return;

            var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            if (!File.Exists(compiler)) compiler = compiler.Replace("Framework64", "Framework");
            var source = Path.Combine(fixture, "SyntheticParent.cs");
            var executable = Path.Combine(fixture, "SyntheticParent.exe");
            await File.WriteAllTextAsync(source,
                "public static class SyntheticParent { public static void Main() { System.Threading.Thread.Sleep(120000); } }");
            using (var compile = Process.Start(new ProcessStartInfo(compiler)
            {
                UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "/nologo", "/target:winexe", "/out:" + executable, source }
            })!)
            {
                await compile.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                check(compile.ExitCode == 0, "Compile the isolated parent used to exercise the production updater's readiness handshake.");
            }

            foreach (var scenario in new[] { "clean-launch", "inherited-modules", "invalid-hash" })
            {
                var validHash = scenario != "invalid-hash";
                var installation = Path.Combine(fixture, scenario + " installation");
                var work = Path.Combine(fixture, "updates", "update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(installation);
                Directory.CreateDirectory(work);
                var parentExe = Path.Combine(installation, "HABITAT.exe");
                File.Copy(executable, parentExe);
                await File.WriteAllTextAsync(Path.Combine(installation, "unins000.exe"), "synthetic marker");
                foreach (var name in new[] { "install-release-update.ps1", "Habitat.Update.psm1", "Habitat.Release.psm1" })
                    File.Copy(Path.Combine(helperSource, name), Path.Combine(work, name));
                var package = Path.Combine(work, "HABITAT-0.16.3-x64-setup.exe");
                var bytes = "Synthetic package, never executed by this readiness check."u8.ToArray();
                await File.WriteAllBytesAsync(package, bytes);
                using var parent = Process.Start(new ProcessStartInfo(parentExe) { UseShellExecute = false, CreateNoWindow = true })!;
                Process? helper = null;
                try
                {
                    var request = Path.Combine(work, "request.json");
                    await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new
                    {
                        version = "0.16.3", kind = "Installer", installRoot = installation, currentExe = parentExe,
                        processId = parent.Id, processStartTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                        packagePath = package, sha256 = validHash ? Convert.ToHexString(SHA256.HashData(bytes)) : new string('0', 64),
                        extractedDirectory = (string?)null
                    }));
                    var helperStart = AppUpdateInstall.CreateHelperStartInfo(
                        Path.Combine(work, "install-release-update.ps1"), "-RequestPath", request);
                    // Exercise the script's bootstrap too: older callers can
                    // still pass incompatible paths despite the fixed launcher.
                    if (scenario == "inherited-modules")
                        helperStart.Environment["PSModulePath"] = Path.Combine(fixture, "incompatible modules");
                    helper = Process.Start(helperStart)!;
                    var ready = Path.Combine(work, "ready");
                    var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
                    while (!File.Exists(ready) && !helper.HasExited && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
                    if (validHash && !File.Exists(ready))
                    {
                        var error = Path.Combine(work, "update-error.log");
                        throw new IOException("The production updater did not become ready: " +
                            (File.Exists(error) ? await File.ReadAllTextAsync(error) : "No helper error log."));
                    }
                    check(File.Exists(ready) == validHash && !parent.HasExited && (!validHash || !helper.HasExited),
                        validHash ? "The real production helper verifies a valid request and waits without closing the app: " + scenario :
                            "The real production helper rejects an invalid hash before readiness and leaves the parent running.");
                    if (!validHash)
                    {
                        await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        var error = await File.ReadAllTextAsync(Path.Combine(work, "update-error.log"));
                        check(helper.ExitCode == 1 && error.Contains("The update package failed verification."),
                            "A failed production-helper verification keeps its concrete diagnostic for recovery.");
                    }
                }
                finally
                {
                    // The parent must outlive the helper so no fixture package
                    // can reach installation or touch any real application.
                    if (helper is { HasExited: false }) { helper.Kill(); await helper.WaitForExitAsync(); }
                    helper?.Dispose();
                    if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
                }
            }
        }
        finally { Environment.SetEnvironmentVariable("PSModulePath", inheritedModules); }
    }
}
