using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Button UpdateButton = new()
    {
        Content = new FontIcon { Glyph = "\uE72C", FontSize = 20 },
        Width = 40, Height = 40, MinWidth = 0, MinHeight = 0,
        Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 24), Visibility = Visibility.Collapsed
    };
    private bool _openingUpdate;
    private bool _checkingUpdate;
    private bool _updateFailed;

    private void BuildUpdateInterface(Grid surface)
    {
        UseStableWidgetButton(UpdateButton);
        UiLanguage.SetName(UpdateButton, () => UiText.Get("UpdateHABITAT"));
        AutomationProperties.SetLiveSetting(UpdateButton, AutomationLiveSetting.Polite);
        UpdateButton.Click += async (_, _) =>
        {
            var restart = false;
            await RunOperationAsync(async () => restart = await OpenUpdateAsync());
            // Closing only after the tracked operation finishes avoids waiting
            // for this same operation while MainWindow drains shutdown work.
            if (restart) _windowHost?.CloseForUpdate();
        };
        surface.Children.Add(UpdateButton);
        RenderUpdateNotice();
    }

    private void TickUpdates(DateTimeOffset now)
    {
        if (_services.Paths.IsIsolated || _operations.IsStopping || _checkingUpdate || now < _services.Updates.NextCheck) return;
        _ = RunOperationAsync(async () =>
        {
            _checkingUpdate = true;
            try
            {
                await _services.Updates.CheckIfDueAsync(now, _operations.Token);
                if (!_operations.IsStopping) RenderUpdateNotice();
            }
            finally { _checkingUpdate = false; }
        });
    }

    private void RenderUpdateNotice(bool openFailed = false)
    {
        if (openFailed) _updateFailed = true;
        var available = _services.Updates.Available;
        UpdateButton.Visibility = available is null ? Visibility.Collapsed : Visibility.Visible;
        var text = available is null ? UiText.Get("UpdateHABITAT") : _updateFailed
            ? UiText.Get("UpdateFailedRetry") : UiText.Format("UpdateDirectAvailable", available.Version);
        UiLanguage.SetName(UpdateButton, () => available is null ? UiText.Get("UpdateHABITAT") : UiText.Format("UpdateHABITATTo", available.Version));
        AutomationProperties.SetHelpText(UpdateButton, text);
        UiLanguage.SetItemStatus(UpdateButton, () => _updateFailed ? UiText.Get("UpdateFailedRetry") : available is null ? "" : UiText.Get("UpdateAvailable"));
        ToolTipService.SetToolTip(UpdateButton, text);
        if (Content is Grid surface) SizeTopWidgets(surface);
    }

    private async Task<bool> OpenUpdateAsync()
    {
        if (_openingUpdate || HasAppearanceOverlay || _activeDialog is not null || _operations.IsStopping ||
            _services.Updates.Available is not { } update) return false;
        _openingUpdate = true;
        UpdateButton.IsEnabled = false;
        _updateFailed = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        var status = new TextBlock { Text = UiText.Get("UpdateChecking"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var content = new StackPanel { Spacing = 16, MinWidth = 280, MaxWidth = 440 };
        content.Children.Add(status);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = UiText.Format("UpdateHABITATTo", update.Version),
            Content = content, CloseButtonText = UiText.Get("Cancel"), DefaultButton = ContentDialogButton.Close };
        StyleDialog(dialog);
        var handedOff = false;
        var savedDrafts = false;
        var downloading = true;
        DownloadedUpdate? downloaded = null;
        dialog.Closed += (_, _) => { if (!handedOff) cancellation.Cancel(); };
        _appearanceDialogOpen = true;
        var showing = ShowDialogAsync(dialog);
        try
        {
            await _services.Updates.CheckIfDueAsync(DateTimeOffset.UtcNow, cancellation.Token, force: true);
            if (_services.Updates.Available is not { } fresh)
            {
                RenderUpdateNotice();
                SetStatus(UiText.Get("UpdateNotAvailable"), StatusTone.Active);
                return false;
            }
            update = fresh;
            dialog.Title = UiText.Format("UpdateHABITATTo", update.Version);
            status.Text = UiText.Get("UpdateDownloading");
            var installation = _services.FindUpdateInstallation();
            if (installation is null)
            {
                status.Text = UiText.Get("UpdateManualRequired");
                dialog.PrimaryButtonText = UiText.Get("UpdateOpenDownloads");
                if (await showing == ContentDialogResult.Primary && !await _services.LaunchBrowser(update.ReleasePage))
                    SetStatus(UiText.Get("UpdateFailedRetry"), StatusTone.Warning);
                return false;
            }
            var package = (installation.Kind == "exe" ? update.Installer : update.Zip)
                ?? throw new InvalidDataException("The matching update package is not ready.");
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (!downloading || handedOff || cancellation.IsCancellationRequested) return;
                status.Text = UiText.Format("UpdateDownloadProgress", value.Percent);
            });
            downloaded = await _services.UpdateDownload.DownloadAsync(package, _services.Paths.UpdatesDirectory, progress, cancellation.Token);
            downloading = false;
            status.Text = UiText.Get("UpdateWaitingForActions");
            while (_bridge?.HasMessageInFlight == true || _composer.IsExclusiveBusy || CodexChat.IsBusy ||
                _codexSendCancellation is not null || _destinationSections.Values.Any(section => section.IsBusy))
                await Task.Delay(250, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            status.Text = UiText.Get("UpdateInstalling");
            var launch = AppUpdateInstall.Prepare(downloaded, installation, Environment.ProcessId, _services.Paths.UpdateResultPath);
            var restartState = new UpdateRestartState(1, _destination, _composerDrafts.SaveForRestart());
            await Task.Run(() => restartState.Save(_services.Paths.UpdateDraftsPath), cancellation.Token);
            savedDrafts = true;
            cancellation.Token.ThrowIfCancellationRequested();
            if (File.Exists(_services.Paths.UpdateResultPath)) File.Delete(_services.Paths.UpdateResultPath);
            if (!_services.StartUpdateHelper(launch)) throw new InvalidOperationException("The update helper could not start.");
            await AppUpdateInstall.WaitUntilReadyAsync(launch, cancellation.Token);
            handedOff = true;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_operations.IsStopping) SetStatus(UiText.Get("UpdateCancelled"), StatusTone.Active);
            return false;
        }
        catch (Exception exception)
        {
            _services.ReportError("Install update", exception);
            if (!_operations.IsStopping)
            {
                RenderUpdateNotice(openFailed: true);
                SetStatus(UiText.Get("UpdateFailedRetry"), StatusTone.Warning);
            }
            return false;
        }
        finally
        {
            downloading = false;
            if (downloaded is not null && !handedOff) AppUpdateDownload.Discard(downloaded, _services.Paths.UpdatesDirectory);
            if (savedDrafts && !handedOff)
            {
                try { File.Delete(_services.Paths.UpdateDraftsPath); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            try { dialog.Hide(); await showing; }
            finally
            {
                _appearanceDialogOpen = false;
                _openingUpdate = false;
                UpdateButton.IsEnabled = true;
                if (!handedOff) FocusComposer();
            }
        }
    }

    private void RestoreUpdateRestartState()
    {
        try
        {
            if (UpdateRestartState.Consume(_services.Paths.UpdateDraftsPath) is { } state)
            {
                _composerDrafts.RestoreAfterRestart(state.Drafts);
                SelectDestination(_destinationCatalog.Select(state.Destination));
                RestoreComposerDraft();
            }
            if (File.Exists(_services.Paths.UpdateResultPath))
            {
                File.Delete(_services.Paths.UpdateResultPath);
                RenderUpdateNotice(openFailed: true);
                SetStatus(UiText.Get("UpdateFailedRetry"), StatusTone.Warning);
            }
        }
        catch (Exception exception) { _services.ReportError("Restore update state", exception); }
    }

    private void PaintUpdateButton()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));
        UpdateButton.Foreground = accent;
        UpdateButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            UpdateButton.Resources[key] = accent;
        UpdateButton.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(dark ? Color(0x24, 255, 255, 255) : Color(0x12, 0, 0, 0));
    }
}
