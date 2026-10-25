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
    public WallpaperService Wallpapers { get; }
    public GlobalHotkeyService GlobalHotkey { get; }
    public BridgeService Bridge { get; }
    public TaskStore Tasks { get; }
    public TaskSession TaskSession { get; }
    public WeatherService Weather { get; }
    internal MarketsService Markets { get; set; }
    internal IAppUpdateService Updates { get; set; }
    internal AppUpdateDownload UpdateDownload { get; set; }
    internal Func<UpdateInstallation?> FindUpdateInstallation { get; set; }
    internal Func<System.Diagnostics.ProcessStartInfo, bool> StartUpdateHelper { get; set; }
    public Func<Uri, Task<bool>> LaunchBrowser { get; }
    internal bool OpenSetupFolder(string? path) => !Paths.IsIsolated && path is not null && Directory.Exists(path) &&
        StartSetupProcess(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    // Bootstrap Chrome through Windows only when Chrome is unavailable.
    internal bool OpenChromeDownload() => !Paths.IsIsolated &&
        StartSetupProcess(new System.Diagnostics.ProcessStartInfo(InstallationFiles.DownloadChrome.AbsoluteUri) { UseShellExecute = true });
    private bool StartSetupProcess(System.Diagnostics.ProcessStartInfo info)
    {
        try { using var process = System.Diagnostics.Process.Start(info); return true; }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { ReportError("Open setup resource", exception); return false; }
    }
    public Func<string, Task<bool>> LaunchNotepad { get; }
    public Func<string, CancellationToken, Task<CodexDeliveryResult>> LaunchCodex { get; }
    internal ICodexChatService Codex { get; set; }
    internal CodexScheduleQueue CodexSchedules { get; }
    public PowerShellLauncher PowerShell { get; }
    public RunLauncher Run { get; }
    internal Func<WindowsAppDestination, string, bool> LaunchWindowsApp { get; set; }
    internal Task<IReadOnlyList<WindowsAppDestination>> DiscoverWindowsAppsAsync() => Paths.IsIsolated
        ? Task.FromResult<IReadOnlyList<WindowsAppDestination>>([]) : WindowsApps.DiscoverAsync();
    internal async Task<string?> PickWindowsAppAsync(nint owner)
    {
        if (Paths.IsIsolated || owner == 0) return null;
        var picker = new Windows.Storage.Pickers.FileOpenPicker { CommitButtonText = UiText.Get("SelectApplication") };
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".lnk");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    internal async Task<IReadOnlyList<string>> PickWallpapersAsync(nint owner)
    {
        if (Paths.IsIsolated || owner == 0) return [];
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
            CommitButtonText = UiText.Get("WallpaperAdd")
        };
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        return (await picker.PickMultipleFilesAsync()).Select(file => file.Path).ToArray();
    }
    public VSCodeService VSCode { get; }
    private readonly VSCodeFolderLauncher _vsCodeFolders;
    internal ITranslationService Translation { get; set; }
    internal ISearchService Search { get; set; }
    internal ISearchService YouTubeSearch { get; set; }
    internal Habitat.Features.Facebook.IFacebookContactsService Facebook { get; set; }
    internal Func<Uri, ImageSource?> FacebookPhoto { get; set; }
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
        Wallpapers = new WallpaperService(paths);
        GlobalHotkey = new GlobalHotkeyService(paths);
        Bridge = new BridgeService(paths.IsIsolated ? 0 : 8765);
        Tasks = paths.IsIsolated ? new TaskStore(paths.TasksPath) : new TaskStore();
        TaskSession = new TaskSession(Tasks);
        if (paths.IsIsolated) _isolatedHttp = new HttpClient(new DisabledNetwork());
        Weather = new WeatherService(_isolatedHttp);
        Markets = new MarketsService(_isolatedHttp);
        Updates = new DesktopUpdateService(paths);
        UpdateDownload = new AppUpdateDownload(_isolatedHttp);
        FindUpdateInstallation = paths.IsIsolated ? () => null : () => UpdateInstallation.Find(AppContext.BaseDirectory);
        StartUpdateHelper = paths.IsIsolated ? _ => false : info =>
        {
            using var process = System.Diagnostics.Process.Start(info);
            return process is not null;
        };
        Translation = Bridge.Translation;
        Search = Bridge.Search;
        YouTubeSearch = Bridge.YouTubeSearch;
        Facebook = Bridge.Facebook;
        FacebookPhoto = paths.IsIsolated ? _ => null : uri => new BitmapImage { DecodePixelWidth = 80, DecodePixelHeight = 80, AutoPlay = false, UriSource = uri };
        SearchThumbnail = paths.IsIsolated ? _ => null : CreateSearchThumbnail;
        var browser = paths.IsIsolated ? new BrowserLauncher(() => null, _ => false)
            : new BrowserLauncher(WindowsChrome.FindExecutable);
        LaunchBrowser = uri => Task.FromResult(browser.Open(uri));
        LaunchNotepad = paths.IsIsolated ? _ => Task.FromResult(false) : new NotepadLauncher().OpenAsync;
        var codex = paths.IsIsolated ? null : new CodexLauncher(paths: paths);
        LaunchCodex = codex is null ? (_, _) => Task.FromResult(CodexDeliveryResult.NotSent) : codex.OpenAsync;
        Codex = codex is null ? new DisabledCodexChatService() : codex;
        CodexSchedules = new(paths.CodexSchedulesPath);
        PowerShell = paths.IsIsolated ? new PowerShellLauncher(paths.LocalDirectory, _ => false) : new PowerShellLauncher();
        Run = paths.IsIsolated ? new RunLauncher(paths.LocalDirectory, _ => false) : new RunLauncher();
        LaunchWindowsApp = paths.IsIsolated ? (_, _) => false : new WindowsAppLauncher().Open;
        VSCode = new VSCodeService(Path.Combine(paths.LocalDirectory, "vscode"), paths.IsIsolated ? new DisabledNetwork() : null);
        _vsCodeFolders = paths.IsIsolated ? new VSCodeFolderLauncher("code.exe", _ => false) : new VSCodeFolderLauncher();
    }

    public Task<VSCodeProject?> EnsureVSCodeProjectAsync(VSCodeFolder folder, CancellationToken token) =>
        VSCode.EnsureProjectAsync(folder, _vsCodeFolders.Open, token);

    public async Task<string?> PickVSCodeFolderAsync(nint owner)
    {
        if (Paths.IsIsolated || owner == 0) return null;
        var picker = new Windows.Storage.Pickers.FolderPicker { CommitButtonText = UiText.Get("SelectProject") };
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
            if (!await LaunchBrowser(new Uri(url))) throw new InvalidOperationException(UiText.Get("CouldNotOpenChromeCheckThatGoogleChromeMessage"));
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
        finally { Updates.Dispose(); UpdateDownload.Dispose(); Weather.Dispose(); Markets.Dispose(); VSCode.Dispose(); _isolatedHttp?.Dispose(); await Codex.DisposeAsync(); }
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
