#if HABITAT_UI_CHECKS
using Microsoft.UI.Xaml;

namespace Habitat;

public sealed partial class MainPage
{
    internal async Task CheckTasksUiAsync(Window window)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tasks-ui-checks.log");
        File.WriteAllText(path, "Opening inline tasks panel\n");
        try
        {
            if (!_services.Paths.IsIsolated || _services.Paths.TasksPath != _taskStore.StoragePath ||
                _gmailAuth?.IsConfigured != false || _gmailAuth?.IsSignedIn != false)
                throw new InvalidOperationException("UI checks did not receive isolated services.");
            File.AppendAllText(path, "PASS: isolated services, empty in-memory Gmail credentials and explicit data paths\n");
            await _initialTaskLoad;
            if (Environment.GetCommandLineArgs().Contains("--scroll-edges-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckScrollEdgesUiAsync(window, path);
            if (Environment.GetCommandLineArgs().Contains("--scroll-edges-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--codex-schedules-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckCodexSchedulesUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--codex-schedules-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--markets-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckMarketsUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--markets-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--wallpaper-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckWallpaperUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--wallpaper-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--language-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckLanguageUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--language-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--about-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckAboutUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--about-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--updates-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckUpdatesUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--updates-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--status-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckDestinationStatusAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--status-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--setup-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
                await CheckSetupUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--setup-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--chrome-store-assets"))
            {
                await ExportChromeStoreAssetsAsync(window);
                File.AppendAllText(path, "PASS: Chrome Web Store screenshots rendered from native controls with isolated sample content\n");
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--preferences-ui-only") || Environment.GetCommandLineArgs().Contains("--all-ui-checks"))
            {
                await CheckPreferencesUiAsync(path);
                await CheckDestinationPreferencesUiAsync(path);
                await CheckSidebarPreferencesUiAsync(path);
            }
            if (Environment.GetCommandLineArgs().Contains("--preferences-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--gmail-ui-only"))
            {
                await CheckGmailDraftUiAsync(path);
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            await CheckActivationUiAsync(path);
            if (Environment.GetCommandLineArgs().Contains("--hotkey-ui-only"))
            {
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (_sidebarExpanded || TasksPreview.Visibility != Visibility.Collapsed ||
                GmailMessageButton.Visibility != Visibility.Collapsed ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(SidebarToggle) != "Expand sidebar")
                throw new InvalidOperationException("The sidebar must start collapsed with an accessible expansion action.");
            CheckWeatherSidebar();
            File.AppendAllText(path, "PASS: sidebar starts collapsed with Gmail, Tasks and compact weather\n");
            if (Environment.GetCommandLineArgs().Contains("--vscode-ui-only"))
            {
                await CheckVSCodeUiAsync(path);
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--search-ui-only"))
            {
                await CheckSearchUiAsync(path);
                await CheckSearchUiAsync(path, Services.ComposerDestination.YouTube);
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--codex-ui-only") ||
                Environment.GetCommandLineArgs().Contains("--codex-conversation-smoke") ||
                Environment.GetCommandLineArgs().Contains("--codex-desktop-smoke"))
            {
                if (Environment.GetCommandLineArgs().Contains("--codex-conversation-smoke"))
                    await CheckCodexConversationDesktopAsync(path);
                else if (Environment.GetCommandLineArgs().Contains("--codex-desktop-smoke"))
                    await CheckCodexDesktopAsync(path);
                else await CheckCodexUiAsync(path);
                await StopAsync();
                await _services.DisposeAsync();
                window.Close();
                Application.Current.Exit();
                return;
            }
            if (!Environment.GetCommandLineArgs().Contains("--sidebar-ui-only"))
            {
                await CheckDestinationsUiAsync(path);
                await CheckSectionShutdownAsync(path);
                CheckConversationRetentionUi(path);
            }
            SetTasksOpen(true);
            // A late destination completion or dialog close cannot steal focus.
            FocusComposer();
            await Task.Delay(700);
            if (!_tasksPanelOpen || _taskArea.Visibility != Visibility.Visible || TasksButton.Flyout is not null ||
                _taskArea.XamlRoot != XamlRoot || _taskArea.Parent != Content)
                throw new InvalidOperationException("Tasks did not open directly on the app surface.");
            if (!ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), TaskInput))
            {
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
                throw new InvalidOperationException($"New task input did not retain focus. Actual: {focused?.GetType().Name ?? "none"}; composer: {ReferenceEquals(focused, Composer)}; input: {TaskInput.ActualWidth}x{TaskInput.ActualHeight}; area: {_taskArea.ActualWidth}x{_taskArea.ActualHeight}, max: {_taskArea.MaxHeight}; modal: {HasAppearanceOverlay}; window: {ActualWidth}x{ActualHeight}.");
            }
            File.AppendAllText(path, "PASS: inline panel rendered on the app surface and input focused\n");
            // Synthetic in-memory tasks only: this check never calls TaskStore.Save.
            _tasks = [new(Guid.NewGuid(), "Pending check", false, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Completed check", true, DateTimeOffset.UtcNow)];
            _tasksLoaded = true;
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                RequestedTheme = theme;
                RenderTasks();
                await Task.Delay(200);
                PaintGmail();
                foreach (var button in new[] { TasksButton, GmailButton, GmailMessageButton, TasksCompletedToggle })
                {
                    button.ApplyTemplate();
                    var presenter = (Microsoft.UI.Xaml.Controls.ContentPresenter)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(button, 0);
                    if (presenter.BackgroundTransition is not null)
                        throw new InvalidOperationException("Widget button still animates its background.");
                    VisualStateManager.GoToState(button, "PointerOver", false);
                    await Task.Delay(50);
                    var hoveredBackground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)presenter.Background).Color;
                    var hoveredForeground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)presenter.Foreground).Color;
                    VisualStateManager.GoToState(button, "Pressed", false);
                    await Task.Delay(50);
                    if (hoveredBackground != ((Microsoft.UI.Xaml.Media.SolidColorBrush)presenter.Background).Color ||
                        hoveredForeground != ((Microsoft.UI.Xaml.Media.SolidColorBrush)presenter.Foreground).Color || presenter.Opacity != 1)
                        throw new InvalidOperationException($"Widget {Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)} changes hover appearance: {hoveredBackground} -> {((Microsoft.UI.Xaml.Media.SolidColorBrush)presenter.Background).Color}; {hoveredForeground} -> {((Microsoft.UI.Xaml.Media.SolidColorBrush)presenter.Foreground).Color}; opacity {presenter.Opacity}.");
                    VisualStateManager.GoToState(button, "Normal", false);
                }
                File.AppendAllText(path, $"PASS: Tasks, Gmail and Completed stay visually steady when pressed in {theme}\n");
                if (_taskArea.ActualWidth > 240 || TaskInput.ActualWidth > 200)
                    throw new InvalidOperationException("Task column or input is too wide.");
                var expectedText = ((Microsoft.UI.Xaml.Media.SolidColorBrush)TextBrush()).Color;
                foreach (var resource in new[] { "TextControlForeground", "TextControlForegroundFocused", "TextControlPlaceholderForeground" })
                    if (((Microsoft.UI.Xaml.Media.SolidColorBrush)TaskInput.Resources[resource]).Color != expectedText)
                        throw new InvalidOperationException("Task input text does not match the theme.");
                var row = (Microsoft.UI.Xaml.Controls.Grid)TasksPending.Children[0];
                var checkbox = (Microsoft.UI.Xaml.Controls.Viewbox)row.Children[0];
                if (checkbox.ActualWidth != 16 || checkbox.ActualHeight != 16)
                    throw new InvalidOperationException("Completion checkbox is not compact.");
                File.AppendAllText(path, $"PASS: compact column, input and checkbox with theme text in {theme}\n");
                TasksCompletedToggle.Focus(FocusState.Keyboard);
                var completedFocusLost = 0;
                void TrackCompletedFocus(object sender, RoutedEventArgs args) => completedFocusLost++;
                TasksCompletedToggle.LostFocus += TrackCompletedFocus;
                var completedContent = TasksCompletedToggle.Content;
                var completedPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(TasksCompletedToggle);
                var completedInvoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)completedPeer.GetPattern(
                    Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
                completedInvoke.Invoke();
                await Task.Delay(300);
                if (TasksCompleted.Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Completed tasks did not expand.");
                File.AppendAllText(path, $"PASS: completed tasks expanded in {theme}\n");
                if (!TasksCompletedLabel.Text.StartsWith("▾")) throw new InvalidOperationException("Expanded triangle is missing.");
                completedInvoke.Invoke();
                await Task.Delay(300);
                if (TasksCompleted.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Completed tasks did not collapse.");
                File.AppendAllText(path, $"PASS: completed tasks collapsed in {theme}\n");
                TasksCompletedToggle.LostFocus -= TrackCompletedFocus;
                if (completedFocusLost != 0 || !ReferenceEquals(completedContent, TasksCompletedToggle.Content) ||
                    !ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), TasksCompletedToggle) ||
                    !TasksCompletedLabel.Text.StartsWith("▸"))
                    throw new InvalidOperationException("Completed toggle lost focus or replaced its label.");
                File.AppendAllText(path, $"PASS: Completed click preserves focus and triangle in {theme}\n");
            }
            SetTasksOpen(false);
            await Task.Delay(200);
            if (_taskArea.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Inline tasks panel did not collapse.");
            SetTasksOpen(true);
            await Task.Delay(300);
            if (!_tasksPanelOpen) throw new InvalidOperationException("Inline tasks panel did not reopen.");
            File.AppendAllText(path, "PASS: tasks panel reopened\n");
            SetTasksOpen(false);
            _tasks = [new(Guid.NewGuid(), "First pending", false, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Completed excluded", true, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Second pending", false, DateTimeOffset.UtcNow)];
            _taskPreviewIndex = 0;
            _taskWidgetHovered = _taskWidgetFocused = false;
            RenderTasks();
            var due = _nextTaskRotation;
            TickTasks(due.AddMilliseconds(-1));
            if (TasksSummary.Text != "First pending") throw new InvalidOperationException("Tasks rotated too early.");
            TickTasks(due);
            if (TasksSummary.Text != "Second pending" || _nextTaskRotation != due.AddSeconds(8))
                throw new InvalidOperationException("Tasks did not rotate after 8 seconds.");
            TickTasks(due.AddSeconds(8));
            if (TasksSummary.Text != "First pending") throw new InvalidOperationException("Task rotation did not wrap.");
            _tasksPanelOpen = true;
            TickTasks(due.AddSeconds(16));
            _tasksPanelOpen = false;
            _taskWidgetHovered = true;
            TickTasks(due.AddSeconds(16));
            _taskWidgetHovered = false;
            _taskWidgetFocused = true;
            TickTasks(due.AddSeconds(16));
            _taskWidgetFocused = false;
            if (TasksSummary.Text != "First pending") throw new InvalidOperationException("Task rotation did not pause.");
            _tasks.RemoveAt(2);
            RenderTasks();
            TickTasks(_nextTaskRotation);
            if (TasksSummary.Text != "First pending") throw new InvalidOperationException("Single pending task rotated.");
            _tasks.Clear();
            RenderTasks();
            TickTasks(_nextTaskRotation);
            if (TasksSummary.Text != "No pending tasks") throw new InvalidOperationException("Empty task preview failed.");
            File.AppendAllText(path, "PASS: 8-second task rotation, wrap, completed exclusion, pauses and empty list\n");
            // The app injects an isolated store for this check. Exercise actual
            // save and completion actions without ever accessing user tasks.
            await LoadTasksAsync();
            TaskInput.Text = "Stored pending task";
            await AddTaskAsync();
            TaskInput.Text = "Stored completed task";
            await AddTaskAsync();
            var completedRow = (Microsoft.UI.Xaml.Controls.Grid)TasksPending.Children[1];
            var completionCheck = (Microsoft.UI.Xaml.Controls.CheckBox)((Microsoft.UI.Xaml.Controls.Viewbox)completedRow.Children[0]).Child;
            var completionPeer = new Microsoft.UI.Xaml.Automation.Peers.CheckBoxAutomationPeer(completionCheck);
            var completionToggle = (Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)completionPeer.GetPattern(
                Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle);
            completionToggle.Toggle();
            await _taskSession.WaitForIdleAsync();
            await LoadTasksAsync();
            if (_tasks.Count != 2 || _tasks.Count(item => item.IsCompleted) != 1 ||
                TasksPending.Children.Count != 1 || TasksCompleted.Children.Count != 1 ||
                TasksCompletedSection.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Saved tasks or completion states did not survive reload.");
            foreach (var savedRow in TasksPending.Children.Concat(TasksCompleted.Children).OfType<Microsoft.UI.Xaml.Controls.Grid>())
                if (savedRow.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Any())
                    throw new InvalidOperationException("A task row still has a delete button.");
            File.AppendAllText(path, "PASS: actual create/complete actions persist both lists, without delete buttons\n");
            var independentStore = new Habitat.Services.TaskStore(_taskStore.StoragePath);
            var independentTasks = independentStore.Load();
            independentStore.Save([.. independentTasks, new(Guid.NewGuid(), "Task from another session", false, DateTimeOffset.UtcNow)]);
            TaskInput.Text = "Draft kept after a conflict";
            await AddTaskAsync();
            if (_tasksLoaded || TaskInput.Text != "Draft kept after a conflict" || TasksRetry.Visibility != Visibility.Visible)
                throw new InvalidOperationException("A conflict did not block stale writes and preserve the draft.");
            await LoadTasksAsync();
            if (!_tasksLoaded || _tasks.Count != 3 || TaskInput.Text != "Draft kept after a conflict")
                throw new InvalidOperationException("Reload did not restore the newer list and keep the draft.");
            File.AppendAllText(path, "PASS: a changed file blocks stale UI writes, exposes reload and keeps the draft\n");
            SetTasksOpen(false);
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
            await Task.Delay(300);
            SelectDestination(Services.ComposerDestination.OpenAI);
            UpdateWritingLayout();
            await Task.Delay(100);
            if (_sidebarExpanded) throw new InvalidOperationException("An empty conversation must keep the sidebar collapsed by default.");
            var togglePeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(SidebarToggle);
            var toggleInvoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)togglePeer.GetPattern(
                Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            toggleInvoke.Invoke();
            await Task.Delay(100);
            if (!_sidebarExpanded || TasksButton.ActualWidth != GmailWidget.ActualWidth ||
                Microsoft.UI.Xaml.Controls.Grid.GetRow(GmailWidget) != 1 || Microsoft.UI.Xaml.Controls.Grid.GetRow(TasksButton) != 2)
                throw new InvalidOperationException("Sidebar order or equal widget widths failed.");
            File.AppendAllText(path, "PASS: Weather above Gmail above Tasks with equal widths in the expanded sidebar\n");
            CheckWeatherSidebar();
            var taskGlyph = (Microsoft.UI.Xaml.Controls.FontIcon)TasksSymbol.Children[0];
            var mailGlyph = (Microsoft.UI.Xaml.Controls.FontIcon)((Microsoft.UI.Xaml.Controls.StackPanel)GmailButton.Content).Children[0];
            var expandedTaskPosition = taskGlyph.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
            var expandedMailPosition = mailGlyph.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
            AddMessage("YOU", "Synthetic sidebar conversation", MessageTone.User);
            await Task.Delay(300);
            var hintPosition = ComposerHint.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
            var composerPosition = Composer.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
            if (ComposerHint.Visibility != Visibility.Visible || hintPosition.Y + ComposerHint.ActualHeight > composerPosition.Y - 15 ||
                ConversationScroll.Margin.Top + ConversationScroll.ActualHeight > hintPosition.Y)
                throw new InvalidOperationException("Conversation history must leave room for the hint above the composer.");
            File.AppendAllText(path, "PASS: hint remains above the bottom composer without overlapping conversation history\n");
            if (_sidebarExpanded || TasksButton.ActualWidth != 40 || GmailWidget.ActualWidth != 40 ||
                TasksPreview.Visibility != Visibility.Collapsed || GmailMessageButton.Visibility != Visibility.Collapsed ||
                TasksBadge.Visibility != Visibility.Collapsed || GmailBadge.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Conversation did not collapse the sidebar to icons.");
            CheckWeatherSidebar();
            if (taskGlyph.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()) != expandedTaskPosition ||
                mailGlyph.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()) != expandedMailPosition)
                throw new InvalidOperationException("Icon coordinates changed while collapsing the sidebar.");
            var railMargin = _root.Margin.Left;
            var tasksPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(TasksButton);
            var tasksInvoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)tasksPeer.GetPattern(
                Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            tasksInvoke.Invoke();
            await Task.Delay(300);
            if (_root.Margin.Left <= railMargin || _taskArea.Margin.Left < 40 || _sidebarExpanded ||
                _taskArea.Margin.Left + _taskArea.ActualWidth >= _root.Margin.Left)
                throw new InvalidOperationException("Task drawer overlaps the conversation or expands the icon rail.");
            CheckTasksBelowWidgets();
            SetTasksOpen(false);
            await Task.Delay(200);
            if (_root.Margin.Left != railMargin || !HasActiveConversation)
                throw new InvalidOperationException("Closing Tasks did not restore the conversation space.");
            var sidebarLeft = _widgets.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).X;
            toggleInvoke.Invoke();
            await Task.Delay(200);
            if (!_sidebarExpanded || _widgets.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).X != sidebarLeft ||
                TasksButton.ActualWidth != GmailWidget.ActualWidth || TasksSummary.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Left ||
                GmailWidgetSubject.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Left)
                throw new InvalidOperationException("Manual expansion moved the left edge or changed the original widget text alignment.");
            UpdateWritingLayout();
            await Task.Delay(100);
            if (!_sidebarExpanded) throw new InvalidOperationException("Layout changes discarded manual expansion.");
            tasksInvoke.Invoke();
            await Task.Delay(200);
            CheckTasksBelowWidgets();
            toggleInvoke.Invoke();
            await Task.Delay(200);
            CheckTasksBelowWidgets();
            tasksInvoke.Invoke();
            await Task.Delay(200);
            var tasksContent = (Microsoft.UI.Xaml.Controls.Grid)TasksButton.Content;
            var taskSymbol = (Microsoft.UI.Xaml.Controls.StackPanel)tasksContent.Children[0];
            var taskCenter = taskSymbol.TransformToVisual(_widgets).TransformPoint(new Windows.Foundation.Point(taskSymbol.ActualWidth / 2, 0)).X;
            var mailCenter = GmailButton.TransformToVisual(_widgets).TransformPoint(new Windows.Foundation.Point(GmailButton.ActualWidth / 2, 0)).X;
            if (_sidebarExpanded || _widgets.ActualWidth != 40 ||
                Math.Abs(taskCenter - 20) > 0.5 || Math.Abs(mailCenter - 20) > 0.5 ||
                _widgets.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).X != sidebarLeft)
                throw new InvalidOperationException("Collapsed rail icons/toggle are not centered or the left edge moved.");
            if (ReferenceEquals(SidebarToggle.Parent, _widgets) || _widgets.RowDefinitions.Count != _sidebarCatalog.Enabled.Count + 1 ||
                _widgets.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y != 20 || sidebarLeft != 12 ||
                SidebarToggle.Content is not null || SidebarToggle.ActualHeight <= _widgets.ActualHeight ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)SidebarToggle.Background).Color.A != 0)
                throw new InvalidOperationException("Sidebar background must be clickable, invisible and independent of the widget rows.");
            var emptySidebarPoint = SidebarToggle.TransformToVisual(this).TransformPoint(
                new Windows.Foundation.Point(20, SidebarToggle.ActualHeight - 10));
            if (!Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(emptySidebarPoint, this).Contains(SidebarToggle))
                throw new InvalidOperationException("Empty sidebar area does not receive clicks.");
            if (taskGlyph.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()) != expandedTaskPosition ||
                mailGlyph.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()) != expandedMailPosition)
                throw new InvalidOperationException("Manual sidebar toggling moved the icons.");
            File.AppendAllText(path, "PASS: no triangle; clickable sidebar background toggles manually, keeping margins and icon positions\n");
            File.AppendAllText(path, "PASS: conversation collapses to icons; Tasks drawer reserves space and restores it on close\n");
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(440, 720));
            SetTasksOpen(true);
            await Task.Delay(300);
            if (_root.Visibility != Visibility.Collapsed || _taskArea.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Narrow task drawer must preserve and temporarily hide the chat.");
            CheckWeatherSidebar();
            CheckTasksBelowWidgets();
            SetTasksOpen(false);
            await Task.Delay(200);
            if (_root.Visibility != Visibility.Visible || Conversation.Children.Count == 0)
                throw new InvalidOperationException("Narrow drawer lost the conversation.");
            _conversation.Clear();
            Conversation.Children.Clear();
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
            UpdateWritingLayout();
            await Task.Delay(300);
            if (_sidebarExpanded) throw new InvalidOperationException("An empty conversation must restore the collapsed sidebar default.");
            File.AppendAllText(path, "PASS: narrow drawer preserves the conversation; empty chat keeps the sidebar collapsed\n");

            toggleInvoke.Invoke();
            SetTasksOpen(true);
            _tasks = Enumerable.Range(0, 30).Select(index => new Models.TaskItem(
                Guid.NewGuid(), $"Weather clearance task {index}", false, DateTimeOffset.UtcNow)).ToList();
            RenderTasks();
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 480));
            await Task.Delay(300);
            CheckTasksBelowWidgets();
            CheckWeatherSidebar();
            var taskViewportBottom = _taskArea.TransformToVisual(this).TransformPoint(
                new Windows.Foundation.Point(0, _taskArea.ActualHeight)).Y;
            if (taskViewportBottom > ((Microsoft.UI.Xaml.Controls.Grid)Content).ActualHeight - 24 || _taskArea.ScrollableHeight <= 0)
                throw new InvalidOperationException("Long task lists must scroll below the sidebar widgets in short windows.");
            File.AppendAllText(path, "PASS: top weather adapts to both sidebar states; long tasks scroll below the widgets in short windows\n");
            File.AppendAllText(path, "PASS: Tasks icon click opens below enabled widgets in collapsed, expanded and narrow states, including toggling with Tasks open\n");
            await StopAsync();
            await _services.DisposeAsync();
            window.Close();
            Application.Current.Exit();
        }
        catch (Exception exception)
        {
            File.AppendAllText(path, exception + "\n");
            Environment.Exit(1);
        }

        void CheckTasksBelowWidgets()
        {
            if (!_tasksPanelOpen || _taskArea.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Tasks panel did not open from its icon.");
            var panelTop = _taskArea.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;
            var tasksBottom = TasksButton.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, TasksButton.ActualHeight)).Y;
            var gmailBottom = GmailWidget.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, GmailWidget.ActualHeight)).Y;
            var weatherBottom = WeatherBlock.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, WeatherBlock.ActualHeight)).Y;
            var marketsBottom = MarketsButton.Parent is null ? 0 : MarketsButton.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, MarketsButton.ActualHeight)).Y;
            if (panelTop < marketsBottom + 16 || panelTop < tasksBottom + 16 || panelTop < gmailBottom || panelTop < weatherBottom || GmailWidget.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Tasks panel invades the Weather/Gmail/Tasks/Markets widget region.");
            CheckWeatherSidebar();
        }

        void CheckWeatherSidebar()
        {
            var position = WeatherBlock.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
            var iconCenter = WeatherSymbol.TransformToVisual(WeatherBlock).TransformPoint(
                new Windows.Foundation.Point(WeatherSymbol.ActualWidth / 2, 0)).X;
            var gmailTop = GmailWidget.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;
            if (!ReferenceEquals(WeatherBlock.Parent, _widgets) || WeatherBlock.Visibility != Visibility.Visible ||
                Math.Abs(position.X - 12) > 0.5 || Math.Abs(iconCenter - 20) > 0.5 ||
                Math.Abs(position.Y - 20) > 0.5 || position.Y + WeatherBlock.ActualHeight + 12 > gmailTop + 0.5 ||
                Microsoft.UI.Xaml.Controls.Grid.GetRow(WeatherBlock) != 0 ||
                WeatherBlock.ActualWidth != _widgets.ActualWidth)
                throw new InvalidOperationException("Weather must stay at the top of the sidebar above Gmail with aligned icons and matching width.");
            if (WeatherDetails.Visibility != (_sidebarExpanded ? Visibility.Visible : Visibility.Collapsed) ||
                WeatherCompactTemperature.Visibility != (_sidebarExpanded ? Visibility.Collapsed : Visibility.Visible) ||
                WeatherCompactTemperature.Text != WeatherTemperature.Text ||
                !Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(WeatherCompactTemperature).Contains(WeatherOutlook.Text))
                throw new InvalidOperationException("Weather must retain its temperature and accessible forecast in both sidebar states.");
        }
    }
}
#endif
