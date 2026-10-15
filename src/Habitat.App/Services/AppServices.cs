using Habitat.Models;
using Windows.System;

namespace Habitat.Services;

// The only composition root for desktop infrastructure. Checks use the same
// controls with explicit storage and fail-closed network/launcher adapters.
internal sealed class AppServices : IAsyncDisposable
{
    private readonly HttpClient? _isolatedHttp;
    private DeviceLocationService? _location;
    private Task _bridgeStart = Task.CompletedTask;
    private Task? _dispose;
    public AppDataPaths Paths { get; }
    public BridgeService Bridge { get; }
    public TaskStore Tasks { get; }
    public TaskSession TaskSession { get; }
    public WeatherService Weather { get; }
    public Func<Uri, Task<bool>> LaunchBrowser { get; }
    public Func<string, Task<bool>> LaunchNotepad { get; }
    public Func<string, Task<bool>> LaunchCodex { get; }
    public PowerShellLauncher PowerShell { get; }
    public Task StartAsync()
    {
        if (Paths.IsIsolated) throw new InvalidOperationException("The desktop bridge is disabled in isolated checks.");
        return _bridgeStart = Bridge.StartAsync();
    }

    public void ReportError(string operation, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Paths.LocalDirectory);
            File.AppendAllText(Path.Combine(Paths.LocalDirectory, "operations.log"),
                $"{DateTimeOffset.Now:O} {operation}: {exception.GetType().Name} {exception.HResult:X8}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public AppServices(AppDataPaths paths)
    {
#if HABITAT_UI_CHECKS
        if (!paths.IsIsolated) throw new InvalidOperationException("UI checks require isolated infrastructure.");
#endif
        Paths = paths;
        Bridge = new BridgeService(paths.IsIsolated ? 0 : 8765);
        Tasks = paths.IsIsolated ? new TaskStore(paths.TasksPath) : new TaskStore();
        TaskSession = new TaskSession(Tasks);
        if (paths.IsIsolated) _isolatedHttp = new HttpClient(new DisabledNetwork());
        Weather = new WeatherService(_isolatedHttp);
        LaunchBrowser = paths.IsIsolated ? _ => Task.FromResult(false) : async uri => await Launcher.LaunchUriAsync(uri);
        LaunchNotepad = paths.IsIsolated ? _ => Task.FromResult(false) : new NotepadLauncher().OpenAsync;
        LaunchCodex = paths.IsIsolated ? _ => Task.FromResult(false) : new CodexLauncher().OpenAsync;
        PowerShell = paths.IsIsolated ? new PowerShellLauncher(paths.LocalDirectory, _ => false) : new PowerShellLauncher();
    }

    public GmailAuthService CreateGmailAuth() => Paths.IsIsolated
        ? new GmailAuthService(new MemoryCredentials(), _isolatedHttp, _ => throw new InvalidOperationException("Browser sign-in is disabled in checks."))
        : new GmailAuthService(new GmailCredentialStore());
    public GmailService CreateGmail(GmailAuthService auth) => new(auth, _isolatedHttp);
    public bool NeedsLocationPermission => !Paths.IsIsolated && (_location?.NeedsPermission ?? true);
    public void ResetLocationAccess() => _location?.ResetAccess();
    public Task<DeviceLocationResult> GetLocationAsync(bool foreground, CancellationToken token) => Paths.IsIsolated
        ? Task.FromResult(new DeviceLocationResult(null, "Location is disabled in checks."))
        : (_location ??= new DeviceLocationService(Paths)).GetAsync(foreground, token);

    public ValueTask DisposeAsync() => new(_dispose ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        try
        {
            try { await _bridgeStart; }
            finally { await Bridge.DisposeAsync(); }
        }
        finally { Weather.Dispose(); _isolatedHttp?.Dispose(); }
    }

    private sealed class MemoryCredentials : IGmailCredentialStore
    {
        private GmailAccount _account = new(null, null, null);
        public GmailAccount Load() => _account;
        public void Save(GmailAccount account) => _account = account;
    }
    private sealed class DisabledNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Network access is disabled in checks.");
    }
}
