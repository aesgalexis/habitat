using Habitat.Models;
using Habitat.Features.Translation;
using Habitat.Features.Search;
using Habitat.Features.Codex;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    public GlobalHotkeyService GlobalHotkey { get; }
    public BridgeService Bridge { get; }
    public TaskStore Tasks { get; }
    public TaskSession TaskSession { get; }
    public WeatherService Weather { get; }
    public Func<Uri, Task<bool>> LaunchBrowser { get; }
    internal BrowserLauncher Browsers { get; }
    internal void UseBrowser(string browser)
    {
        Browsers.SelectedBrowser = browser;
        RefreshBrowserPolicy();
    }
    internal void RefreshBrowserPolicy()
    {
        if (!Paths.IsIsolated && !Bridge.HasMessageInFlight) Bridge.UseBrowser(Browsers.BridgeFamily);
    }
    public Func<string, Task<bool>> LaunchNotepad { get; }
    public Func<string, CancellationToken, Task<CodexDeliveryResult>> LaunchCodex { get; }
    internal ICodexChatService Codex { get; set; }
    public PowerShellLauncher PowerShell { get; }
    public RunLauncher Run { get; }
    public VSCodeService VSCode { get; }
    private readonly VSCodeFolderLauncher _vsCodeFolders;
    internal ITranslationService Translation { get; set; }
    internal ISearchService Search { get; set; }
    internal ISearchService YouTubeSearch { get; set; }
    internal Func<Uri, ImageSource?> SearchThumbnail { get; set; }
    internal bool CopyText(string text)
    {
        if (Paths.IsIsolated) return false;
        try
        {
            var content = new Windows.ApplicationModel.DataTransfer.DataPackage();
            content.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(content);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception exception) { ReportError("Copy translation", exception); return false; }
    }
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
        GlobalHotkey = new GlobalHotkeyService(paths);
        Bridge = new BridgeService(paths.IsIsolated ? 0 : 8765);
        Tasks = paths.IsIsolated ? new TaskStore(paths.TasksPath) : new TaskStore();
        TaskSession = new TaskSession(Tasks);
        if (paths.IsIsolated) _isolatedHttp = new HttpClient(new DisabledNetwork());
        Weather = new WeatherService(_isolatedHttp);
        Translation = Bridge.Translation;
        Search = Bridge.Search;
        YouTubeSearch = Bridge.YouTubeSearch;
        SearchThumbnail = paths.IsIsolated ? _ => null : CreateSearchThumbnail;
        Browsers = paths.IsIsolated ? new BrowserLauncher(() => [], () => null, _ => false)
            : new BrowserLauncher(WindowsBrowsers.Discover, WindowsBrowsers.SystemDefault);
        UseBrowser(Preferences.Load(paths.PreferencesPath).Browser);
        LaunchBrowser = paths.IsIsolated ? _ => Task.FromResult(false) : uri =>
        {
            // Also follows changes to the Windows default browser between launches.
            RefreshBrowserPolicy();
            return Task.FromResult(Browsers.Open(uri));
        };
        LaunchNotepad = paths.IsIsolated ? _ => Task.FromResult(false) : new NotepadLauncher().OpenAsync;
        var codex = paths.IsIsolated ? null : new CodexLauncher(paths: paths);
        LaunchCodex = codex is null ? (_, _) => Task.FromResult(CodexDeliveryResult.NotSent) : codex.OpenAsync;
        Codex = codex is null ? new DisabledCodexChatService() : codex;
        PowerShell = paths.IsIsolated ? new PowerShellLauncher(paths.LocalDirectory, _ => false) : new PowerShellLauncher();
        Run = paths.IsIsolated ? new RunLauncher(paths.LocalDirectory, _ => false) : new RunLauncher();
        VSCode = new VSCodeService(Path.Combine(paths.LocalDirectory, "vscode"), paths.IsIsolated ? new DisabledNetwork() : null);
        _vsCodeFolders = paths.IsIsolated ? new VSCodeFolderLauncher("code.exe", _ => false) : new VSCodeFolderLauncher();
    }

    public Task<VSCodeProject?> EnsureVSCodeProjectAsync(VSCodeFolder folder, CancellationToken token) =>
        VSCode.EnsureProjectAsync(folder, _vsCodeFolders.Open, token);

    public async Task<string?> PickVSCodeFolderAsync(nint owner)
    {
        if (Paths.IsIsolated || owner == 0) return null;
        var picker = new Windows.Storage.Pickers.FolderPicker { CommitButtonText = "Select project" };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    internal static BitmapImage CreateSearchThumbnail(Uri uri) => new()
    {
        DecodePixelWidth = 320, DecodePixelHeight = 180, AutoPlay = false, UriSource = uri
    };

    public GmailAuthService CreateGmailAuth() => Paths.IsIsolated
        ? new GmailAuthService(new MemoryCredentials(), _isolatedHttp, _ => throw new InvalidOperationException("Browser sign-in is disabled in checks."))
        : new GmailAuthService(new GmailCredentialStore(), openBrowser: async url =>
        {
            if (!await LaunchBrowser(new Uri(url))) throw new InvalidOperationException("Could not open the selected browser. Check Preferences > Browser.");
        });
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
        finally { Weather.Dispose(); VSCode.Dispose(); _isolatedHttp?.Dispose(); await Codex.DisposeAsync(); }
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
