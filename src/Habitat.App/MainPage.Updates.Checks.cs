#if HABITAT_UI_CHECKS
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckUpdatesUiAsync(string log)
    {
        var original = _services.Updates;
        var sidebar = _sidebarManualExpanded;
        var theme = RequestedTheme;
        var draft = Composer.Text;
        var fake = new CheckedUpdates();
        try
        {
            File.AppendAllText(log, "Checking update modal states\n");
            _services.Updates = fake;
            await CheckUpdatesAsync();
            if (UpdateButton.Visibility != Visibility.Collapsed || UpdateMenuItem.Visibility != Visibility.Collapsed ||
                _activeDialog is not null || !fake.UsedCancelableToken)
                throw new InvalidOperationException("An up-to-date installation must not show Update.");
            var initialEditorMargin = Composer.Margin;
            var initialRootMargin = _root.Margin;
            var initialTaskHeight = _taskArea.MaxHeight;
            fake.Available = new("0.11.0", UpdatePackageKind.Zip,
                new("synthetic.zip", new Uri("https://github.com/synthetic.zip"), 12),
                new("synthetic.zip.sha256", new Uri("https://github.com/synthetic.zip.sha256"), 64));
            _appearanceDialogOpen = true;
            await CheckUpdatesAsync();
            if (_activeDialog is not null)
                throw new InvalidOperationException("An update must wait for another modal to close.");
            _appearanceDialogOpen = false;
            TickUpdates();
            await WaitForUpdateUiAsync(() => UpdateButton.ActualWidth > 0 && _activeDialog is not null);
            File.AppendAllText(log, "Update notice opened\n");
            var notice = _activeDialog!;
            if (UpdateButton.Visibility != Visibility.Visible || UpdateButton.Content as string != "Update" ||
                AutomationProperties.GetName(UpdateButton) != "Update HABITAT" || !UpdateButton.IsTabStop ||
                notice.Title as string != "Update HABITAT" || notice.CloseButtonText != "Later" ||
                !HasAppearanceOverlay || UpdateMenuItem.Visibility != Visibility.Visible ||
                Composer.Margin != initialEditorMargin || _root.Margin != initialRootMargin ||
                _taskArea.MaxHeight != initialTaskHeight)
                throw new InvalidOperationException("The ready update must open a modal English action without shifting the workspace.");
            await InstallUpdateAsync();
            File.AppendAllText(log, "Duplicate update ignored\n");
            TickUpdates();
            if (!ReferenceEquals(notice, _activeDialog))
                throw new InvalidOperationException("Repeated clicks and ticks must not open another update dialog.");
            var destination = _destination;
            SelectDestination(ComposerDestinations.CycleFromWheel(destination, 1));
            await ReceiveActivationAsync("Synthetic captured text must not replace the draft");
            FocusComposer();
            await Task.Delay(60);
            if (_destination != destination || Composer.Text != draft ||
                !ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), UpdateButton))
                throw new InvalidOperationException("The update modal must retain keyboard focus and block destination changes and global capture.");
            File.AppendAllText(log, "Update input guards verified\n");
            foreach (var dark in new[] { false, true })
            foreach (var expanded in new[] { false, true })
            {
                RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
                StyleDialog(notice);
                PaintUpdateButton();
                _sidebarManualExpanded = expanded;
                UpdateWritingLayout();
                await Task.Delay(60);
                var center = UpdateButton.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(UpdateButton.ActualWidth / 2, 0));
                if (Math.Abs(center.X - XamlRoot.Size.Width / 2) > 1 ||
                    UpdateButton.HorizontalAlignment != HorizontalAlignment.Center)
                    throw new InvalidOperationException("Update must stay centered in the modal in both themes and sidebar layouts.");
            }
            notice.Hide();
            await WaitForUpdateUiAsync(() => !_updateDialogOpen);
            File.AppendAllText(log, "Update notice deferred\n");
            TickUpdates();
            if (fake.Preparations != 0 || fake.Launches != 0 || Composer.Text != draft || HasAppearanceOverlay)
                throw new InvalidOperationException("Deferring an update must not download, launch or change the draft.");
            var retry = InstallUpdateAsync();
            File.AppendAllText(log, "Update retry requested\n");
            await WaitForUpdateUiAsync(() => _activeDialog?.Title as string == "Update HABITAT" &&
                ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), UpdateButton));
            File.AppendAllText(log, "Update retry focused\n");
            ((IInvokeProvider)new ButtonAutomationPeer(UpdateButton).GetPattern(PatternInterface.Invoke)).Invoke();
            File.AppendAllText(log, "Update action invoked\n");
            await WaitForUpdateUiAsync(() => fake.Preparations == 1 && _activeDialog?.Title as string == "Updating HABITAT");
            // ShowAsync returns its task before the native popup finishes opening.
            // Wait for that transition before simulating a user dismissing it.
            await Task.Delay(250);
            File.AppendAllText(log, "Synthetic update downloading\n");
            if (!HasAppearanceOverlay || UpdateMenuItem.IsEnabled || _activeDialog!.CloseButtonText != "Cancel")
                throw new InvalidOperationException("Downloading must remain modal and prevent duplicate updates while allowing cancellation.");
            _activeDialog.Hide();
            await WaitForUpdateUiAsync(() => fake.Canceled && !_updateDialogOpen);
            await retry;
            if (!fake.Canceled || fake.Launches != 0 || HasAppearanceOverlay || Composer.Text != draft || !UpdateMenuItem.IsEnabled)
                throw new InvalidOperationException("Canceling must preserve the draft, unlock the workspace and allow a menu retry.");
            fake.Fail = true;
            await CheckUpdatesAsync();
            if (UpdateButton.Visibility != Visibility.Visible || _nextUpdateCheck > DateTimeOffset.UtcNow.AddMinutes(16))
                throw new InvalidOperationException("A failed check must retain the known update and retry later.");
            fake.Fail = false;
            fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = CheckUpdatesAsync();
            var requests = fake.Requests;
            await CheckUpdatesAsync();
            if (fake.Requests != requests) throw new InvalidOperationException("Concurrent update checks must be coalesced.");
            fake.Pending.SetResult(null);
            await pending;
            if (UpdateButton.Visibility != Visibility.Collapsed || _nextUpdateCheck < DateTimeOffset.UtcNow.AddHours(5))
                throw new InvalidOperationException("An up-to-date result must hide Update and retain the six-hour schedule.");
            File.AppendAllText(log, "PASS: automatic centered update modal in both themes/sidebar layouts, deferred behind existing modals, keyboard focus, blocked destination changes/global capture, duplicate prevention, unchanged workspace, Later, cancelable download, menu retry, offline retry and coalesced checks; no network or installer\n");
        }
        finally
        {
            _activeDialog?.Hide();
            await WaitForUpdateUiAsync(() => !_updateDialogOpen);
            _services.Updates = original;
            _appearanceDialogOpen = false;
            _sidebarManualExpanded = sidebar;
            RequestedTheme = theme;
            _offeredUpdateVersion = null;
            _availableUpdate = null;
            RenderUpdateButton();
            UpdateWritingLayout();
        }
    }

    private static async Task WaitForUpdateUiAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (ready()) return;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("The update UI did not reach the expected state.");
    }

    private sealed class CheckedUpdates : IAppUpdateService
    {
        public AppUpdate? Available;
        public bool Fail, UsedCancelableToken, Canceled;
        public int Requests, Preparations, Launches;
        public TaskCompletionSource<AppUpdate?>? Pending;
        public Task<AppUpdate?> CheckAsync(CancellationToken token)
        {
            Requests++; UsedCancelableToken = token.CanBeCanceled;
            if (Fail) throw new HttpRequestException("Synthetic offline check");
            return Pending?.Task.WaitAsync(token) ?? Task.FromResult(Available);
        }
        public async Task<PreparedAppUpdate> PrepareAsync(AppUpdate release, IProgress<int> progress, CancellationToken token)
        {
            Preparations++;
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Canceled = true; throw; }
            throw new InvalidOperationException("Installation is disabled in checks.");
        }
        public Task LaunchInstallerAsync(PreparedAppUpdate prepared, CancellationToken token)
        { Launches++; throw new InvalidOperationException("Installation is disabled in checks."); }
        public void Dispose() { }
    }
}
#endif
