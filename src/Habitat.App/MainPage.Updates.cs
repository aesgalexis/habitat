using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private Button UpdateButton = CreateUpdateButton();

    private static Button CreateUpdateButton()
    {
        var button = new Button
        {
            Content = "Update", FontSize = 16, MinWidth = 160, MinHeight = 44,
            Padding = new Thickness(24, 10, 24, 10), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(button, "Update HABITAT");
        return button;
    }
    private readonly MenuFlyoutItem UpdateMenuItem = new() { Text = "Update…", Visibility = Visibility.Collapsed };
    private ReleaseAppUpdate? _availableUpdate;
    private string? _offeredUpdateVersion;
    private bool _checkingUpdates, _updateBusy, _updateDialogOpen, _updateFailed, _updateHandoff;
    private string _updateStatus = "";
    private bool _openingUpdate => _updateDialogOpen || _updateHandoff;
    private int _updatePercent;
    private DateTimeOffset _nextUpdateCheck = DateTimeOffset.MinValue;

    private void BuildUpdateInterface()
    {
        UpdateMenuItem.Click += async (_, _) => await RunOperationAsync(InstallUpdateAsync);
        OptionsMenu.Items.Insert(Math.Max(0, OptionsMenu.Items.Count - 2), UpdateMenuItem);
    }

    private void TickUpdates()
    {
        if (!_checkingUpdates && !_updateBusy && !_updateDialogOpen && !_updateHandoff && DateTimeOffset.UtcNow >= _nextUpdateCheck)
            _ = RunOperationAsync(CheckUpdatesAsync);
        OfferAvailableUpdate();
    }

    private void OfferAvailableUpdate()
    {
        if (_availableUpdate is not { } release || release.Version == _offeredUpdateVersion ||
            HasAppearanceOverlay || _activeDialog is not null || _receivingActivation ||
            _updateHandoff || _operations.IsStopping || XamlRoot is null) return;
        _ = RunOperationAsync(InstallUpdateAsync);
    }

    private async Task CheckUpdatesAsync()
    {
        if (_checkingUpdates || _operations.IsStopping) return;
        _checkingUpdates = true;
        _nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(6);
        try
        {
            var available = await _services.Updates.CheckAsync(_operations.Token);
            if (!_operations.IsStopping)
            {
                _availableUpdate = available;
                _updateFailed = available is not null && _services.Updates.FailedVersion == available.Version;
                RenderUpdateButton();
            }
        }
        catch (OperationCanceledException) when (_operations.IsStopping) { }
        catch (Exception exception)
        {
            _nextUpdateCheck = DateTimeOffset.UtcNow.AddMinutes(15);
            _services.ReportError("Check for updates", exception);
        }
        finally { _checkingUpdates = false; }
        OfferAvailableUpdate();
    }

    private void RenderUpdateButton()
    {
        UpdateButton.Visibility = _availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateMenuItem.Visibility = UpdateButton.Visibility;
        UpdateButton.IsEnabled = !_updateBusy && !_updateHandoff;
        UpdateMenuItem.IsEnabled = UpdateButton.IsEnabled;
        UpdateButton.Content = _updateBusy ? $"Updating… {_updatePercent}%" : "Update";
        var help = _updateBusy ? _updateStatus : _updateFailed ? "Update failed. Click to try again." :
            $"Version {_availableUpdate?.Version} is available. Update and restart HABITAT.";
        ToolTipService.SetToolTip(UpdateButton, help);
        AutomationProperties.SetHelpText(UpdateButton, help);
        AutomationProperties.SetItemStatus(UpdateButton, help);
    }

    private bool HasWorkBeforeUpdate => _bridge?.HasMessageInFlight == true || _composer.IsExclusiveBusy ||
        _destinationSections.Values.Any(section => section.IsBusy) || _gmailBusy || _taskAdding ||
        CodexChat.IsBusy || _codexSendCancellation is not null || _services.CodexSchedules.IsSending;

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { } release || _updateBusy || HasAppearanceOverlay ||
            _activeDialog is not null || _updateHandoff || _operations.IsStopping) return;
        _offeredUpdateVersion = release.Version;
        _updateDialogOpen = true;
        ContentDialog? progressDialog = null;
        Task<ContentDialogResult>? showing = null;
        var committing = false;
        var savedDrafts = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        try
        {
            OptionsMenu.Hide();
            GmailFlyout.Hide();
            // Keep each native dialog's visual tree intact while it closes.
            UpdateButton = CreateUpdateButton();
            RenderUpdateButton();
            PaintUpdateButton();
            var prompt = new StackPanel { Spacing = 24 };
            prompt.Children.Add(new TextBlock
            {
                Text = $"Install version {release.Version} and restart HABITAT? Saved tasks and preferences will be kept. Drafts will be restored after restarting.",
                TextWrapping = TextWrapping.Wrap
            });
            prompt.Children.Add(UpdateButton);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            progressDialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Update HABITAT",
                Content = prompt, CloseButtonText = "Later", DefaultButton = ContentDialogButton.None
            };
            void ConfirmUpdate(object sender, RoutedEventArgs args) => decision.TrySetResult(true);
            UpdateButton.Click += ConfirmUpdate;
            progressDialog.Opened += (_, _) => UpdateButton.Focus(FocusState.Keyboard);
            progressDialog.CloseButtonClick += (_, _) => cancellation.Cancel();
            progressDialog.Closing += (_, args) =>
            {
                if (committing && !_updateHandoff && !_operations.IsStopping) args.Cancel = true;
                else if (!_updateHandoff) cancellation.Cancel();
                decision.TrySetResult(false);
            };
            StyleDialog(progressDialog);
            showing = ShowDialogAsync(progressDialog);
            try
            {
                if (await Task.WhenAny(decision.Task, showing) == showing) { await showing; return; }
                if (!await decision.Task || _operations.IsStopping) return;
            }
            finally { UpdateButton.Click -= ConfirmUpdate; }
            _updateBusy = true;
            _updatePercent = 0;
            _updateFailed = false;
            var status = new TextBlock { Text = "Downloading update…", TextWrapping = TextWrapping.Wrap };
            // ProgressBar's template fails in the bundled WinUI runtime. Keep
            // progress as accessible text, without loading that native template.
            AutomationProperties.SetName(status, "Update download progress");
            AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(status);
            progressDialog.Title = "Updating HABITAT";
            progressDialog.Content = content;
            progressDialog.CloseButtonText = "Cancel";
            var progress = new Progress<int>(percent =>
            {
                if (_operations.IsStopping || !_updateBusy) return;
                _updatePercent = percent;
                _updateStatus = status.Text = percent == 100 ? "Verifying update…" : $"Downloading update… {percent}%";
                RenderUpdateButton();
            });
            _updateStatus = status.Text;
            RenderUpdateButton();
            var prepared = await _services.Updates.PrepareAsync(release, progress, cancellation.Token);
            _updateStatus = status.Text = "Waiting for current work to finish…";
            RenderUpdateButton();
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            waiting.CancelAfter(TimeSpan.FromMinutes(15));
            while (HasWorkBeforeUpdate) await Task.Delay(250, waiting.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _updateStatus = status.Text = "Preparing to restart…";
            committing = true;
            progressDialog.CloseButtonText = "";
            RenderUpdateButton();
            var restartState = new UpdateRestartState(1, _destination, _composerDrafts.SaveForRestart());
            await Task.Run(() => restartState.Save(_services.Paths.UpdateDraftsPath), cancellation.Token);
            savedDrafts = true;
            await _services.Updates.LaunchInstallerAsync(prepared, cancellation.Token);
            _updateHandoff = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _updateFailed = true;
            _services.ReportError("Install update", exception);
            if (progressDialog is not null && showing is { IsCompleted: false } && !_operations.IsStopping)
            {
                committing = false;
                progressDialog.Title = "Update unavailable";
                progressDialog.Content = new TextBlock
                {
                    Text = "The update could not be installed. HABITAT is still running. Open the HABITAT menu and choose Update to try again.",
                    TextWrapping = TextWrapping.Wrap
                };
                progressDialog.CloseButtonText = "Done";
                await showing;
            }
        }
        finally
        {
            committing = false;
            progressDialog?.Hide();
            if (showing is not null) await showing;
            if (savedDrafts && !_updateHandoff)
            {
                try { File.Delete(_services.Paths.UpdateDraftsPath); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            _updateBusy = false;
            _updateDialogOpen = false;
            RenderUpdateButton();
            if (_updateHandoff)
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _windowHost?.CloseForUpdate());
            else FocusComposer();
        }
    }

    private void PaintUpdateButton()
    {
        var accent = new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));
        UpdateButton.Foreground = accent;
        UpdateButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        UpdateButton.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(0x24, 255, 255, 255) : Color(0x12, 0, 0, 0));
    }
}
