#if HABITAT_UI_CHECKS
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Security.Cryptography;
using System.Text.Json;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckUpdatesUiAsync(string logPath)
    {
        if (UpdateButton.Visibility != Visibility.Collapsed || UpdateButton.Parent != Content)
            throw new InvalidOperationException("Updates must stay hidden until a newer release is found.");
        var original = _services.Updates;
        var originalDownload = _services.UpdateDownload;
        var originalFind = _services.FindUpdateInstallation;
        var originalStart = _services.StartUpdateHelper;
        var originalLanguage = UiText.Language;
        var originalText = Composer.Text;
        var theme = RequestedTheme;
        using var handler = new UpdateCheckHandler();
        using var http = new HttpClient(handler);
        using var updates = new AppUpdateService(http, new Version(0, 10, 0));
        using var downloads = new AppUpdateDownload(http);
        _services.Updates = updates;
        _services.UpdateDownload = downloads;
        _services.FindUpdateInstallation = () => new("exe", _services.Paths.LocalDirectory,
            Path.Combine(_services.Paths.LocalDirectory, "HABITAT.exe"));
        var launches = 0;
        var launchSucceeds = false;
        _services.StartUpdateHelper = info =>
        {
            launches++;
            if (info.UseShellExecute || !info.CreateNoWindow || !info.ArgumentList.Contains("-NonInteractive"))
                throw new InvalidOperationException("Update handoff must use a hidden helper and structured arguments.");
            if (launchSucceeds) File.WriteAllText(Path.Combine(info.WorkingDirectory, "helper-ready"), "synthetic ready");
            return launchSucceeds; // Never execute a real installer in UI checks.
        };
        try
        {
            await updates.CheckIfDueAsync(DateTimeOffset.UtcNow);
            RenderUpdateNotice();
            foreach (var currentTheme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                RequestedTheme = currentTheme;
                PaintSurface();
                foreach (var expanded in new[] { false, true })
                {
                    _sidebarManualExpanded = expanded;
                    UpdateWritingLayout();
                    UpdateLayout();
                    if (UpdateButton.Visibility != Visibility.Visible || UpdateButton.ActualWidth != 40 || UpdateButton.ActualHeight != 40 ||
                        UpdateButton.VerticalAlignment != VerticalAlignment.Bottom ||
                        AutomationProperties.GetName(UpdateButton) != "Update HABITAT to 0.11.0" ||
                        ToolTipService.GetToolTip(UpdateButton) as string != UiText.Format("UpdateDirectAvailable", "0.11.0") ||
                        _widgets.RowDefinitions.Count != 4)
                        throw new InvalidOperationException("The accessible update icon must stay at the bottom in both sidebar states and themes.");
                    var origin = UpdateButton.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
                    var sidebar = _widgets.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
                    if (Math.Abs(origin.X - sidebar.X) > 1 || origin.Y <= sidebar.Y + _widgets.ActualHeight)
                        throw new InvalidOperationException("The update icon overlaps the sidebar widgets or is misaligned.");
                    SetTasksOpen(true);
                    UpdateLayout();
                    var taskTop = _taskArea.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;
                    if (taskTop + _taskArea.MaxHeight > origin.Y)
                        throw new InvalidOperationException("The task drawer must reserve space above the update icon.");
                    SetTasksOpen(false);
                }
            }
            foreach (var language in new[] { "en", "es" })
            {
                UiText.SetLanguage(language);
                UiLanguage.Refresh();
                _updateFailed = false;
                RenderUpdateNotice();
                if (AutomationProperties.GetName(UpdateButton) != UiText.Format("UpdateHABITATTo", "0.11.0") ||
                    ToolTipService.GetToolTip(UpdateButton) as string != UiText.Format("UpdateDirectAvailable", "0.11.0"))
                    throw new InvalidOperationException("Update accessibility must follow the chosen language.");
                Composer.Text = "Borrador original — keep this unchanged";
                var before = launches;
                handler.Mode = "bad-hash";
                if (await OpenUpdateAsync() || launches != before || Composer.Text != "Borrador original — keep this unchanged" ||
                    AutomationProperties.GetItemStatus(UpdateButton) != UiText.Get("UpdateFailedRetry"))
                    throw new InvalidOperationException("Checksum errors must preserve the draft, expose retry and never launch.");
                handler.Mode = "cancel";
                handler.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = OpenUpdateAsync();
                await handler.Started.Task;
                if (UpdateButton.IsEnabled || _activeDialog?.CloseButtonText != UiText.Get("Cancel") || await OpenUpdateAsync())
                    throw new InvalidOperationException("Pending updates must be cancellable and reject duplicate clicks.");
                _activeDialog.Hide();
                if (await pending || launches != before || !UpdateButton.IsEnabled)
                    throw new InvalidOperationException("Cancellation must drain the download without starting an installer.");
                handler.Mode = "ok";
                if (await OpenUpdateAsync() || launches != before + 1 || File.Exists(_services.Paths.UpdateDraftsPath))
                    throw new InvalidOperationException("Failed helper startup must retain the app and remove obsolete restart data.");
                launchSucceeds = true;
                if (!await OpenUpdateAsync() || launches != before + 2 ||
                    UpdateRestartState.Consume(_services.Paths.UpdateDraftsPath) is not { } state ||
                    state.Drafts.Shared != "Borrador original — keep this unchanged" &&
                    !state.Drafts.Local.Values.Contains("Borrador original — keep this unchanged"))
                    throw new InvalidOperationException("A verified update must hand off once and preserve drafts for the restart.");
                launchSucceeds = false;
                var installed = _services.FindUpdateInstallation;
                _services.FindUpdateInstallation = () => null;
                var manual = OpenUpdateAsync();
                for (var attempt = 0; attempt < 40 && _activeDialog?.PrimaryButtonText != UiText.Get("UpdateOpenDownloads"); attempt++)
                    await Task.Delay(25);
                if (_activeDialog?.PrimaryButtonText != UiText.Get("UpdateOpenDownloads") ||
                    _activeDialog.Content is not StackPanel panel || panel.Children[0] is not TextBlock message ||
                    message.Text != UiText.Get("UpdateManualRequired"))
                    throw new InvalidOperationException("Unsupported copies must explain the manual installation in the chosen language.");
                _activeDialog.Hide();
                if (await manual || launches != before + 2)
                    throw new InvalidOperationException("Cancelling manual installation must never start an update helper.");
                _services.FindUpdateInstallation = installed;
                File.AppendAllText(logPath, $"PASS: direct updates, cancellation, duplicates, checksum and helper failures, handoff and draft recovery in {language}; isolated data and fake process launcher.\n");
            }
        }
        finally
        {
            _services.Updates = original;
            _services.UpdateDownload = originalDownload;
            _services.FindUpdateInstallation = originalFind;
            _services.StartUpdateHelper = originalStart;
            _updateFailed = false;
            UiText.SetLanguage(originalLanguage);
            UiLanguage.Refresh();
            Composer.Text = originalText;
            RequestedTheme = theme;
            _sidebarManualExpanded = null;
            SetTasksOpen(false);
            RenderUpdateNotice();
            PaintSurface();
        }
    }

    private sealed class UpdateCheckHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes("Synthetic update; never executable");
        internal string Mode = "ok";
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            const string name = "HABITAT-0.11.0-x64-setup.exe";
            if (request.RequestUri == AppUpdateService.LatestRelease)
                return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = "v0.11.0", draft = false, prerelease = false,
                    assets = new[] { new { name, state = "uploaded", size = _bytes.Length },
                        new { name = name + ".sha256", state = "uploaded", size = 100 } }
                })) };
            if (Mode == "cancel") { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            return new(System.Net.HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                    ? new StringContent((Mode == "bad-hash" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(_bytes))) + "  " + name)
                    : new ByteArrayContent(_bytes)
            };
        }
    }
}
#endif
