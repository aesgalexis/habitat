using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Habitat.Services;

internal sealed class DesktopUpdateService : IAppUpdateService
{
    private readonly ReleaseUpdateInstallation? _installation;
    private readonly ReleaseUpdateClient? _client;
    private readonly bool _enabled;
    public string? FailedVersion { get; }

    internal DesktopUpdateService(AppDataPaths paths)
    {
        _enabled = !paths.IsIsolated && !AppDataPaths.HasPackageIdentity() &&
            !paths.LocalDirectory.Contains("\\sandbox.", StringComparison.OrdinalIgnoreCase);
        if (!_enabled) return;
        try
        {
            _installation = ReleaseUpdateInstallation.Find(AppContext.BaseDirectory);
            var resultPath = Path.Combine(paths.LocalDirectory, "updates", "last-result.json");
            if (File.Exists(resultPath))
            {
                using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
                if (!result.RootElement.GetProperty("succeeded").GetBoolean()) FailedVersion = result.RootElement.GetProperty("version").GetString();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        if (_installation is not null)
            _client = new(typeof(App).Assembly.GetName().Version!, _installation.Kind, Path.Combine(paths.LocalDirectory, "updates"));
    }

    public Task<ReleaseAppUpdate?> CheckAsync(CancellationToken token) => _client?.CheckAsync(token) ?? Task.FromResult<ReleaseAppUpdate?>(null);
    public Task<PreparedAppUpdate> PrepareAsync(ReleaseAppUpdate release, IProgress<int> progress, CancellationToken token) =>
        _client?.PrepareAsync(release, progress, token) ?? throw new InvalidOperationException("Updates are unavailable for this installation.");

    public async Task LaunchInstallerAsync(PreparedAppUpdate prepared, CancellationToken token)
    {
        if (!_enabled || _installation is null) throw new InvalidOperationException("Updates are disabled for this session.");
        ReleaseUpdateClient.EnsureNoLinks(prepared.Directory);
        foreach (var name in new[] { "install-release-update.ps1", "Habitat.Update.psm1", "Habitat.Release.psm1" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "updater", name), Path.Combine(prepared.Directory, name));
        using var current = Process.GetCurrentProcess();
        var request = new
        {
            version = prepared.Release.Version, kind = prepared.Release.Kind.ToString(),
            installRoot = _installation.Root, currentExe = _installation.Executable,
            processId = Environment.ProcessId, processStartTicks = current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            packagePath = prepared.PackagePath, sha256 = prepared.Sha256, extractedDirectory = prepared.ExtractedDirectory
        };
        var requestPath = Path.Combine(prepared.Directory, "request.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), token);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = prepared.Directory };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(prepared.Directory, "install-release-update.ps1"), "-RequestPath", requestPath }) info.ArgumentList.Add(argument);
        using var helper = Process.Start(info) ?? throw new IOException("The update helper could not start.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            while (!File.Exists(Path.Combine(prepared.Directory, "ready")))
            {
                if (helper.HasExited) throw new IOException("The update helper could not prepare the installation.");
                await Task.Delay(100, timeout.Token);
            }
        }
        catch
        {
            if (!helper.HasExited) helper.Kill();
            throw;
        }
    }

    public void Dispose() => _client?.Dispose();
}
