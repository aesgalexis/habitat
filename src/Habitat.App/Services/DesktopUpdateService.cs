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
        var diagnostics = UpdateDiagnosticLog.ForDirectory(prepared.Directory);
        var phase = "helper-files";
        try
        {
            diagnostics.Record("helper-files");
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
            phase = "helper-request";
            diagnostics.Record(phase);
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), token);
            var info = AppUpdateInstall.CreateHelperStartInfo(
                Path.Combine(prepared.Directory, "install-release-update.ps1"), "-RequestPath", requestPath);
            phase = "helper-start";
            await UpdateHelperHandoff.StartAsync(info, Path.Combine(prepared.Directory, "ready"), diagnostics, token);
        }
        catch (Exception exception)
        {
            diagnostics.Record(phase, exception);
            throw;
        }
    }

    public void Dispose() => _client?.Dispose();
}
