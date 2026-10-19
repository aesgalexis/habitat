using Habitat.Features.Setup;
using Habitat.Services;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private bool _setupDialogOpen;

    private async Task ShowSetupAsync()
    {
        if (HasAppearanceOverlay || _activeDialog is not null || _operations.IsStopping) return;
        var files = InstallationFiles.Find(AppContext.BaseDirectory);
        var view = new SetupView(files)
        {
            Width = Math.Max(280, Math.Min(600, XamlRoot.Size.Width - 96)),
            MaxHeight = Math.Max(180, Math.Min(520, XamlRoot.Size.Height - 180))
        };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Set up HABITAT", Content = view,
            CloseButtonText = "Done", DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 700d;
        StyleDialog(dialog);
        async Task OpenLink(Uri uri)
        {
            view.ActionStatus.Text = await _launchBrowser(uri) ? "Opened in Chrome." : "Could not open Chrome. Install Google Chrome, then try again.";
        }
        void OpenFolder(string? path) => view.ActionStatus.Text = _services.OpenSetupFolder(path)
            ? "Folder opened." : "Could not open the folder. Reinstall HABITAT or read the setup guide.";
        view.InstallExtension.Click += async (_, _) => await RunOperationAsync(() => OpenLink(InstallationFiles.ChromeStore));
        view.OpenChat.Click += async (_, _) => await RunOperationAsync(() => OpenLink(new Uri("https://chatgpt.com/")));
        view.OpenExtensions.Click += async (_, _) => await RunOperationAsync(() => OpenLink(new Uri("chrome://extensions/")));
        view.ReadGuide.Click += async (_, _) => await RunOperationAsync(() => OpenLink(InstallationFiles.SetupGuide));
        view.GetChrome.Click += (_, _) => view.ActionStatus.Text = _services.OpenChromeDownload()
            ? "Chrome download page opened. Install Chrome, then return here." : "Open www.google.com/chrome in your browser to install Chrome.";
        view.OpenExtensionFolder.Click += (_, _) => OpenFolder(files.ChromeExtensionDirectory);
        view.OpenVSCodeFolder.Click += (_, _) => OpenFolder(files.VSCodeExtensionDirectory);
        var gmail = false;
        view.SetupGmail.Click += (_, _) => { gmail = true; dialog.Hide(); };
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        void Refresh() => view.Refresh(!_services.Paths.IsIsolated && WindowsChrome.FindExecutable() is not null,
            _bridge?.Search.IsConnected == true, _bridge?.YouTubeSearch.IsConnected == true,
            _bridge?.Translation.IsConnected == true, _bridge?.IsConnected == true);
        timer.Tick += (_, _) => Refresh();
        _setupDialogOpen = true;
        try
        {
            Refresh();
            timer.Start();
            await ShowDialogAsync(dialog);
            if (!_operations.IsStopping)
            {
                _preferences = _preferences with { SetupGuideSeen = true };
                SavePreferences();
            }
        }
        finally { timer.Stop(); _setupDialogOpen = false; FocusComposer(); }
        if (gmail && !_operations.IsStopping) await ShowGmailSetupCoreAsync();
    }
}
