using System.Diagnostics;
using System.Text.Json;

namespace Habitat.Services;

internal sealed record UpdateInstallation(string Kind, string Directory, string Executable, string? Launcher = null)
{
    internal static UpdateInstallation? Find(string executableDirectory)
    {
        var current = Path.GetFullPath(executableDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var executable = Path.Combine(current, "HABITAT.exe");
        if (!File.Exists(executable)) return null;
        if (File.Exists(Path.Combine(current, "unins000.exe"))) return new("exe", current, executable);
        for (var root = new DirectoryInfo(current); root is not null; root = root.Parent)
        {
            var launcher = Path.Combine(root.FullName, "HABITAT.vbs");
            var pointer = Path.Combine(root.FullName, "artifacts", "current-release.txt");
            if (!File.Exists(launcher) || !File.Exists(pointer)) continue;
            try
            {
                var artifacts = Path.Combine(root.FullName, "artifacts");
                var active = Path.GetFullPath(Path.Combine(artifacts, File.ReadAllText(pointer).Trim()));
                if (active.StartsWith(artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(active.TrimEnd(Path.DirectorySeparatorChar), current, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(root.FullName, "scripts", "Habitat.Release.psm1")))
                    return new("zip", root.FullName, executable, launcher);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        return null;
    }
}

internal static class AppUpdateInstall
{
    internal static async Task WaitUntilReadyAsync(ProcessStartInfo start, CancellationToken token)
    {
        var ready = Path.Combine(start.WorkingDirectory, "helper-ready");
        var failed = Path.Combine(start.WorkingDirectory, "helper-failed");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!File.Exists(ready))
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(failed) || DateTimeOffset.UtcNow >= deadline)
                throw new IOException("The update helper did not become ready.");
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
    }

    internal static ProcessStartInfo Prepare(DownloadedUpdate update, UpdateInstallation installation, int processId,
        string resultPath)
    {
        if (installation.Kind is not ("exe" or "zip") || processId <= 0 ||
            !update.Package.Name.EndsWith(installation.Kind == "exe" ? "-x64-setup.exe" : "-win-x64.zip", StringComparison.Ordinal))
            throw new InvalidDataException("The update format does not match this installation.");
        var directory = Path.GetDirectoryName(update.File)!;
        var helper = Path.Combine(directory, "UpdateHelper.ps1");
        using (var resource = typeof(AppUpdateInstall).Assembly.GetManifestResourceStream("Habitat.UpdateHelper.ps1")
            ?? throw new InvalidOperationException("The update helper is missing."))
        using (var output = File.Create(helper)) resource.CopyTo(output);
        var plan = Path.Combine(directory, "update-plan.json");
        AtomicFile.Write(plan, JsonSerializer.SerializeToUtf8Bytes(new
        {
            update.Package.Version, installation.Kind, installation.Directory, installation.Executable, installation.Launcher,
            ParentId = processId, Package = update.File, update.Sha256, ResultPath = resultPath
        }));
        return CreateHelperStartInfo(helper, "-PlanPath", plan);
    }

    internal static ProcessStartInfo CreateHelperStartInfo(string helper, string requestOption, string requestPath)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(helper)!,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper, requestOption, requestPath }
        };
        // PowerShell 7 module paths are incompatible with Windows PowerShell.
        // Let the installation helper initialise its own compatible module path.
        start.Environment.Remove("PSModulePath");
        return start;
    }
}
