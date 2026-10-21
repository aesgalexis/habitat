using Habitat.Services;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;

internal static class UpdateInstallChecks
{
    internal static async Task RunAsync(string root, DownloadedUpdate downloaded, Action<bool, string> check)
    {
        var fixture = Path.Combine(root, "update install with spaces");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "HABITAT.exe"), "synthetic");
        check(UpdateInstallation.Find(fixture) is null, "A development executable is not mistaken for an installed EXE or ZIP.");
        File.WriteAllText(Path.Combine(fixture, "unins000.exe"), "synthetic marker");
        var exe = UpdateInstallation.Find(fixture)!;
        check(exe.Kind == "exe" && exe.Directory == fixture, "EXE updates target the exact current installation directory.");
        var plan = AppUpdateInstall.Prepare(downloaded, exe, Environment.ProcessId, Path.Combine(root, "update-result.json"));
        check(plan.CreateNoWindow && !plan.UseShellExecute && plan.WindowStyle == ProcessWindowStyle.Hidden &&
            plan.ArgumentList.Contains("-NonInteractive") && plan.ArgumentList.Last().EndsWith("update-plan.json") &&
            !plan.Environment.ContainsKey("PSModulePath"),
            "Installation handoff uses a hidden non-interactive helper with structured arguments.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var observed = false;
            try { await AppUpdateInstall.WaitUntilReadyAsync(plan, cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed, "Cancelling helper preparation keeps the original app open.");
        }
        var failedMarker = Path.Combine(plan.WorkingDirectory, "helper-failed");
        File.WriteAllText(failedMarker, "synthetic failure");
        var failedReady = false;
        try { await AppUpdateInstall.WaitUntilReadyAsync(plan, CancellationToken.None); }
        catch (IOException) { failedReady = true; }
        check(failedReady, "A helper initialization failure is detected before closing the app.");
        File.Delete(failedMarker);
        var wrongFormat = false;
        try { AppUpdateInstall.Prepare(downloaded, exe with { Kind = "zip" }, Environment.ProcessId, "unused"); }
        catch (InvalidDataException) { wrongFormat = true; }
        check(wrongFormat, "The installer never switches an existing EXE installation to ZIP or vice versa.");

        var zipRoot = Path.Combine(root, "zip install");
        var active = Path.Combine(zipRoot, "artifacts", "releases", "current");
        Directory.CreateDirectory(active);
        Directory.CreateDirectory(Path.Combine(zipRoot, "scripts"));
        File.WriteAllText(Path.Combine(zipRoot, "scripts", "Habitat.Release.psm1"), "synthetic marker");
        File.WriteAllText(Path.Combine(zipRoot, "HABITAT.vbs"), "synthetic marker");
        File.WriteAllText(Path.Combine(active, "HABITAT.exe"), "synthetic marker");
        var pointer = Path.Combine(zipRoot, "artifacts", "current-release.txt");
        File.WriteAllText(pointer, "releases/current");
        check(UpdateInstallation.Find(active) is { Kind: "zip" } found && found.Directory == zipRoot,
            "ZIP updates resolve the stable installation root from the exact active release.");
        File.WriteAllText(pointer, "../../outside");
        check(UpdateInstallation.Find(active) is null, "An escaping ZIP release pointer is never accepted.");

        if (OperatingSystem.IsWindows()) await CheckHelperAsync(root, check);
    }

    private static async Task CheckHelperAsync(string root, Action<bool, string> check)
    {
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        if (!File.Exists(compiler)) compiler = compiler.Replace("Framework64", "Framework");
        if (!File.Exists(compiler)) throw new InvalidOperationException("The Windows fixture compiler is unavailable.");
        var fixtureRoot = Path.Combine(root, "helper fixtures");
        Directory.CreateDirectory(fixtureRoot);
        var source = Path.Combine(fixtureRoot, "Fixture.cs");
        var assembly = typeof(AppUpdateService).Assembly.Location.Replace("\"", "\"\"");
        File.WriteAllText(source, """
            using System; using System.IO; using System.Threading;
            public static class Fixture {
                public static int Main(string[] args) {
                    if (args.Length > 0 && args[0] == "--parent") { Thread.Sleep(1400); return 0; }
                    foreach (var arg in args) if (arg.StartsWith("/DIR=")) {
                        var dir = arg.Substring(5).Trim('"');
                        if (File.Exists(Path.Combine(dir, "fail"))) return 42;
                        File.Copy(@"ASSEMBLY_PATH", Path.Combine(dir, "HABITAT.dll"), true);
                        File.WriteAllText(Path.Combine(dir, "installed"), "yes"); return 0;
                    }
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restarted"), "yes"); return 0;
                }
            }
            """.Replace("ASSEMBLY_PATH", assembly));
        var fixtureExe = Path.Combine(fixtureRoot, "Fixture.exe");
        var compile = new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "/nologo", "/target:exe", "/out:" + fixtureExe, source } };
        using (var process = Process.Start(compile)!)
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            check(process.ExitCode == 0, "Compile a standalone synthetic installer without using the real installation.");
        }
        var version = FileVersionInfo.GetVersionInfo(typeof(AppUpdateService).Assembly.Location).FileVersion!;
        version = string.Join('.', version.Split('.').Take(3));
        foreach (var failure in new[] { false, true })
        {
            var install = Path.Combine(fixtureRoot, failure ? "failed install" : "successful install");
            var cache = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(install); Directory.CreateDirectory(cache);
            var target = Path.Combine(install, "HABITAT.exe");
            File.Copy(fixtureExe, target);
            if (failure) File.WriteAllText(Path.Combine(install, "fail"), "synthetic installer failure");
            var name = $"HABITAT-{version}-x64-setup.exe";
            var file = Path.Combine(cache, name);
            File.Copy(fixtureExe, file);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            var package = new UpdatePackage(version, "v" + version, name, new FileInfo(file).Length, new("https://example.invalid"), new("https://example.invalid"));
            using var parent = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--parent" } })!;
            var result = Path.Combine(cache, "result.json");
            var launch = AppUpdateInstall.Prepare(new(package, file, hash), new("exe", install, target), parent.Id, result);
            using var helper = Process.Start(launch)!;
            await AppUpdateInstall.WaitUntilReadyAsync(launch, CancellationToken.None);
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            for (var attempt = 0; attempt < 80 && !File.Exists(Path.Combine(install, "restarted")); attempt++) await Task.Delay(50);
            check(helper.ExitCode == (failure ? 1 : 0) && File.Exists(Path.Combine(install, "restarted")) &&
                File.Exists(Path.Combine(install, "installed")) != failure && File.Exists(result) == failure,
                failure ? "A failed installer records recovery and reopens the existing app, without touching user data." :
                    "The real helper waits for its synthetic parent, runs the verified installer, checks the version and restarts.");
        }
        foreach (var scenario in new[] { "ok", "traversal", "wrong-version" })
        {
            var install = Path.Combine(fixtureRoot, "zip " + scenario);
            var cache = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(install); Directory.CreateDirectory(cache);
            var target = Path.Combine(install, "HABITAT.exe");
            File.Copy(fixtureExe, target);
            var launcher = Path.Combine(install, "HABITAT.vbs");
            File.WriteAllText(launcher, "Set fs = CreateObject(\"Scripting.FileSystemObject\")\nSet f = fs.CreateTextFile(\"" +
                Path.Combine(install, "restarted").Replace("\"", "\"\"") + "\", True)\nf.Write \"yes\"\nf.Close\n");
            var name = $"HABITAT-{version}-win-x64.zip";
            var file = Path.Combine(cache, name);
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("release.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new { version = scenario == "wrong-version" ? "0.0.0" : version, runtime = "win-x64" }));
                using (var writer = new StreamWriter(archive.CreateEntry("Install-HABITAT.ps1").Open()))
                    writer.Write("param([string]$InstallDirectory,[switch]$NoShortcuts)\nif (-not $NoShortcuts) { throw 'Shortcut state changed.' }\n[IO.File]::WriteAllText((Join-Path $InstallDirectory 'zip-updated'), 'yes')\n");
                if (scenario == "traversal")
                {
                    using var writer = new StreamWriter(archive.CreateEntry("../escaped.txt").Open());
                    writer.Write("must never be extracted");
                }
            }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            var package = new UpdatePackage(version, "v" + version, name, new FileInfo(file).Length, new("https://example.invalid"), new("https://example.invalid"));
            using var parent = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--parent" } })!;
            var result = Path.Combine(cache, "result.json");
            using var helper = Process.Start(AppUpdateInstall.Prepare(new(package, file, hash), new("zip", install, target, launcher), parent.Id, result))!;
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            check(helper.ExitCode == (scenario == "ok" ? 0 : 1) &&
                File.Exists(Path.Combine(install, "zip-updated")) == (scenario == "ok") &&
                File.Exists(result) == (scenario != "ok") && !File.Exists(Path.Combine(cache, "escaped.txt")),
                "ZIP helper validates paths/metadata and invokes only the matching package installer: " + scenario);
        }
    }
}
