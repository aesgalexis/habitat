using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using Windows.System;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Grid _widgets = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 20, 0, 0), RowSpacing = 12
    };
    private readonly Border _sidebarDivider = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 24), IsHitTestVisible = false };
    private bool _sidebarExpanded;
    private bool? _sidebarManualExpanded;
    private bool? _sidebarConversationActive;
    // An invisible button behind the widgets makes the empty sidebar area
    // clickable and keyboard-accessible without another visible control.
    private readonly Button SidebarToggle = new()
    {
        MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(0),
        BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 20, 0, 24)
    };
    private readonly TaskStore _taskStore;
    private readonly TaskSession _taskSession;
    private List<TaskItem> _tasks { get => _taskSession.Items; set => _taskSession.Items = value; }
    private bool _tasksLoaded { get => _taskSession.IsLoaded; set => _taskSession.IsLoaded = value; }
    private bool _taskAdding;
    private Task _initialTaskLoad = Task.CompletedTask;
    private bool _tasksPanelOpen;
    private int _taskPreviewIndex;
    private bool _taskWidgetHovered;
    private bool _taskWidgetFocused;
    private DateTimeOffset _nextTaskRotation = DateTimeOffset.MinValue;
    private static readonly TimeSpan TaskRotationInterval = TimeSpan.FromSeconds(8);
    private readonly Button TasksButton = new();
    private readonly TextBlock TasksTitle = new() { Text = "Tasks", FontSize = 11, Opacity = 0.72 };
    private readonly StackPanel TasksSymbol = new() { Spacing = 3, Width = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock TasksBadge = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel TasksPreview = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock TasksSummary = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock TasksHint = new() { FontSize = 12, Opacity = 0.8 };
    private readonly StackPanel TasksPanel = new() { Spacing = 12 };
    private readonly ScrollViewer _taskArea = new()
    {
        Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly StackPanel TasksPending = new() { Spacing = 6 };
    private readonly StackPanel TasksCompleted = new() { Spacing = 6 };
    // Expander's template crashes in the bundled WinUI runtime.
    // Basic controls provide the same collapsible section without that template.
    private readonly StackPanel TasksCompletedSection = new() { Spacing = 6 };
    private bool _completedExpanded;
    private readonly TextBlock TasksCompletedLabel = new() { Text = "▸ Completed", FontSize = 14 };
    private readonly Button TasksCompletedToggle = new()
    {
        Content = "Completed", HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(0),
        BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0)
    };
    private readonly TextBox TaskInput = new() { PlaceholderText = "New task…", MaxLength = 1000, MinWidth = 0, FontSize = 13, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBlock TasksStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button TasksRetry = new() { Content = "Reload tasks", Visibility = Visibility.Collapsed };
    private readonly Button TaskAdd = new()
    {
        Content = new FontIcon { Glyph = "\uE710", FontSize = 12 },
        Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        CornerRadius = new CornerRadius(0)
    };

    private void BuildTaskInterface(Grid surface)
    {
        _widgets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
        _widgets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
        _widgets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
        surface.Children.Add(SidebarToggle);
        SidebarToggle.Click += (_, _) =>
        {
            _sidebarManualExpanded = !_sidebarExpanded;
            UpdateWritingLayout();
        };
        _widgets.SizeChanged += (_, _) => SizeTopWidgets(surface);
        _taskArea.SizeChanged += (_, _) => UpdateWritingLayout();
        var content = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var symbol = TasksSymbol;
        symbol.Children.Add(new FontIcon { Glyph = "\uE9D5", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        TasksBadge.VerticalAlignment = VerticalAlignment.Center;
        // Reserve the badge line even when its text is hidden. The glyph must
        // not move vertically when sidebar details appear/disappear.
        symbol.Children.Add(new Border { Height = 18, Child = TasksBadge });
        content.Children.Add(symbol);
        TasksPreview.Children.Add(TasksTitle);
        TasksPreview.Children.Add(TasksSummary);
        TasksPreview.Children.Add(TasksHint);
        TasksPreview.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(TasksPreview, 1);
        content.Children.Add(TasksPreview);
        TasksButton.Content = content;
        Grid.SetRow(TasksButton, 2);
        TasksButton.Height = 78;
        TasksButton.Padding = new Thickness(8);
        TasksButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        TasksButton.VerticalContentAlignment = VerticalAlignment.Center;
        TasksButton.VerticalAlignment = VerticalAlignment.Center;
        TasksButton.Click += (_, _) => SetTasksOpen(!_tasksPanelOpen);
        TasksButton.PointerEntered += (_, _) => _taskWidgetHovered = true;
        TasksButton.PointerExited += (_, _) =>
        {
            _taskWidgetHovered = false;
            _nextTaskRotation = DateTimeOffset.UtcNow + TaskRotationInterval;
        };
        TasksButton.GotFocus += (_, _) => _taskWidgetFocused = true;
        TasksButton.LostFocus += (_, _) =>
        {
            _taskWidgetFocused = false;
            _nextTaskRotation = DateTimeOffset.UtcNow + TaskRotationInterval;
        };
        _widgets.Children.Add(TasksButton);
        Grid.SetRow(GmailWidget, 1);

        TasksPanel.Children.Add(new TextBlock { Text = "Tasks", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var inputRow = new Grid { ColumnSpacing = 8 };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.Children.Add(TaskInput);
        Grid.SetColumn(TaskAdd, 1);
        inputRow.Children.Add(TaskAdd);
        AutomationProperties.SetName(TaskInput, "New task");
        AutomationProperties.SetName(TaskAdd, "Add task");
        TaskAdd.Click += async (_, _) => await RunOperationAsync(AddTaskAsync);
        TaskInput.KeyDown += async (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            await RunOperationAsync(AddTaskAsync);
        };
        TasksPanel.Children.Add(inputRow);
        TasksPanel.Children.Add(TasksStatus);
        TasksPanel.Children.Add(TasksRetry);
        TasksRetry.Click += async (_, _) => await RunOperationAsync(LoadTasksAsync);
        var lists = new StackPanel { Spacing = 12 };
        lists.Children.Add(TasksPending);
        TasksCompleted.Visibility = Visibility.Collapsed;
        TasksCompletedToggle.Content = TasksCompletedLabel;
        TasksCompletedToggle.Click += (_, _) => SetCompletedExpanded(!_completedExpanded);
        TasksCompletedSection.Children.Add(TasksCompletedToggle);
        TasksCompletedSection.Children.Add(TasksCompleted);
        lists.Children.Add(TasksCompletedSection);
        TasksPanel.Children.Add(lists);
        _taskArea.Content = TasksPanel;
        surface.Children.Add(_taskArea);
        surface.Children.Add(_sidebarDivider);
        if (_taskSession.HasAttemptedLoad) PresentTaskLoad();
        else _initialTaskLoad = RunOperationAsync(LoadTasksAsync);
    }

    private void SizeTopWidgets(Grid surface)
    {
        if (surface.ActualWidth <= 0) return;
        var contentWidth = surface.ActualWidth - surface.Padding.Left - surface.Padding.Right;
        var active = HasActiveConversation;
        if (_sidebarConversationActive != active)
        {
            _sidebarConversationActive = active;
            _sidebarManualExpanded = null;
        }
        var layout = TopWidgetLayout.Calculate(contentWidth, _tasksPanelOpen, _sidebarManualExpanded);
        _sidebarExpanded = layout.Expanded;
        SizeWeatherWidget(layout);
        _widgets.Margin = new Thickness(0, 20, 0, 0);
        var expanded = layout.Expanded;
        _widgets.Width = layout.WidgetWidth;
        SidebarToggle.Width = layout.WidgetWidth + 16;
        AutomationProperties.SetName(SidebarToggle, expanded ? "Collapse sidebar" : "Expand sidebar");
        TasksPreview.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        TasksTitle.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        GmailMessageButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        GmailWidget.ColumnSpacing = expanded ? 4 : 0;
        TasksBadge.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        GmailBadge.Visibility = expanded && _gmailAuth?.IsSignedIn == true ? Visibility.Visible : Visibility.Collapsed;
        TasksButton.Width = GmailWidget.Width = layout.WidgetWidth;
        TasksButton.MinHeight = GmailWidget.MinHeight = expanded ? 78 : 40;
        GmailWidget.Height = 78;
        // Both sidebar states own the same widget rows. Tasks must always open
        // below those rows, including on the very first layout after a click.
        var widgetRowsHeight = _widgets.RowDefinitions.Sum(row => row.Height.Value) +
            _widgets.RowSpacing * (_widgets.RowDefinitions.Count - 1);
        var tasksTop = _widgets.Margin.Top + widgetRowsHeight + 16;
        _taskArea.Margin = new Thickness(layout.TaskLeft, tasksTop, 0, 0);
        _taskArea.Width = layout.TaskWidth;
        _taskArea.MaxHeight = Math.Max(0, surface.ActualHeight - tasksTop - 24);
        _sidebarDivider.Margin = new Thickness(layout.ContentInset - 12, 20, 0, 24);
        // On narrow windows an open task drawer takes the content slot. Keep
        // the draft/history intact and restore them when the drawer closes.
        var drawerOnly = (_tasksPanelOpen || expanded) && contentWidth - layout.ContentInset - DestinationRailInset < 200;
        _root.Visibility = drawerOnly ? Visibility.Collapsed : Visibility.Visible;
        _root.Margin = new Thickness(layout.ContentInset, 0, DestinationRailInset, 0);
    }

    private void OpenTasks() => DispatcherQueue.TryEnqueue(
        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => SetTasksOpen(true));

    private void SetTasksOpen(bool open)
    {
        _tasksPanelOpen = open;
        _nextTaskRotation = DateTimeOffset.UtcNow + TaskRotationInterval;
        _taskArea.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TasksHint.Text = open ? "Click to collapse the list" : "Click to view your list";
        AutomationProperties.SetName(TasksButton, $"{(open ? "Hide" : "Show")} tasks, {TasksBadge.Text} pending");
        UpdateWritingLayout();
        if (open)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_tasksPanelOpen && !HasAppearanceOverlay)
                {
                    // The first opening must lay out the previously collapsed
                    // drawer before WinUI can give its TextBox keyboard focus.
                    _taskArea.UpdateLayout();
                    TaskInput.Focus(FocusState.Keyboard);
                }
            });
        else FocusComposer();
    }

    private async Task LoadTasksAsync()
    {
        await _taskSession.LoadAsync();
        if (!_operations.IsStopping) PresentTaskLoad();
    }

    private void PresentTaskLoad()
    {
        if (_taskSession.IsLoaded)
        {
            TasksStatus.Visibility = Visibility.Collapsed;
            if (_taskSession.RecoveredFromBackup)
                ShowTaskError("Tasks restored from a backup. Check your pending and completed lists.");
        }
        else
        {
            ShowTaskError(_taskStore.TemporaryProfileBlocked
                ? "Open HABITAT from your Windows shortcut to use your saved tasks. This session uses a different data folder and cannot change them."
                : "Could not load tasks. Your saved file is unchanged; please try again.");
        }
        if (!_operations.IsStopping) RenderTasks();
    }

    private void ShowTaskError(string message)
    {
        TasksStatus.Text = message;
        TasksStatus.Visibility = Visibility.Visible;
    }

    private async Task<bool> SaveTasksAsync(Func<IReadOnlyList<TaskItem>, List<TaskItem>> update)
    {
        var saved = await _taskSession.UpdateAsync(update);
        if (_operations.IsStopping) return saved;
        if (saved) TasksStatus.Visibility = Visibility.Collapsed;
        else if (_taskSession.Error is not null)
            ShowTaskError(_taskSession.Error is TaskStoreConflictException
                ? "Tasks changed on disk. Reload tasks before saving. Your draft is kept."
                : "Could not save the change. Your previous tasks and draft are kept. Reload tasks to try again.");
        RenderTasks();
        return saved;
    }

    private async Task AddTaskAsync()
    {
        if (_taskAdding) return;
        var draft = TaskInput.Text;
        var text = draft.Trim();
        if (text.Length == 0) return;
        _taskAdding = true;
        try
        {
            var item = new TaskItem(Guid.NewGuid(), text, false, DateTimeOffset.UtcNow);
            if (await SaveTasksAsync(items => [.. items, item]) && TaskInput.Text == draft)
                TaskInput.Text = string.Empty;
        }
        finally { _taskAdding = false; if (!_operations.IsStopping) TaskInput.Focus(FocusState.Keyboard); }
    }

    private void RenderTasks()
    {
        var pending = _tasks.Count(task => !task.IsCompleted);
        TasksBadge.Text = _tasksLoaded ? pending.ToString() : "!";
        RenderTaskPreview();
        _nextTaskRotation = DateTimeOffset.UtcNow + TaskRotationInterval;
        TasksHint.Text = _tasksPanelOpen ? "Click to collapse the list" : "Click to view your list";
        AutomationProperties.SetName(TasksButton, $"{(_tasksPanelOpen ? "Hide" : "Show")} tasks, {TasksBadge.Text} pending");
        TaskInput.IsEnabled = TaskAdd.IsEnabled = _tasksLoaded;
        TasksRetry.Visibility = _tasksLoaded ? Visibility.Collapsed : Visibility.Visible;
        TasksPending.Children.Clear();
        TasksCompleted.Children.Clear();
        if (_tasksLoaded && pending == 0)
            TasksPending.Children.Add(new TextBlock { Text = "No pending tasks.", Opacity = 0.7 });
        foreach (var task in _tasks)
            (task.IsCompleted ? TasksCompleted : TasksPending).Children.Add(BuildTaskRow(task));
        UpdateCompletedSection();
        AutomationProperties.SetName(TasksCompletedToggle, $"Show or hide completed tasks, {_tasks.Count - pending}");
        TasksCompletedSection.Visibility = _tasks.Any(task => task.IsCompleted) ? Visibility.Visible : Visibility.Collapsed;
        PaintTasks();
    }

    private void RenderTaskPreview()
    {
        var pending = _tasks.Where(task => !task.IsCompleted).ToArray();
        _taskPreviewIndex = pending.Length == 0 ? 0 : Math.Clamp(_taskPreviewIndex, 0, pending.Length - 1);
        TasksSummary.Text = !_tasksLoaded ? "Tasks unavailable" : pending.ElementAtOrDefault(_taskPreviewIndex)?.Text ?? "No pending tasks";
    }

    private void TickTasks(DateTimeOffset now)
    {
        var count = _tasks.Count(task => !task.IsCompleted);
        if (!_tasksLoaded || count < 2 || _tasksPanelOpen || _taskWidgetHovered || _taskWidgetFocused ||
            _appearanceDialogOpen || now < _nextTaskRotation) return;
        _taskPreviewIndex = (_taskPreviewIndex + 1) % count;
        _nextTaskRotation = now + TaskRotationInterval;
        RenderTaskPreview();
    }

    private void UpdateCompletedSection()
    {
        TasksCompleted.Visibility = _completedExpanded ? Visibility.Visible : Visibility.Collapsed;
        // Update the existing label rather than replacing the button's content
        // while it is pressed; keep its visual tree and focus stable.
        TasksCompletedLabel.Text = $"{(_completedExpanded ? "▾" : "▸")} Completed ({_tasks.Count(task => task.IsCompleted)})";
    }

    private void SetCompletedExpanded(bool expanded)
    {
        _completedExpanded = expanded;
        UpdateCompletedSection();
    }

    private Grid BuildTaskRow(TaskItem task)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var check = new CheckBox
        {
            IsChecked = task.IsCompleted, IsEnabled = _tasksLoaded, Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0)
        };
        AutomationProperties.SetName(check, task.Text);
        check.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            await SaveTasksAsync(items => items.Select(item => item.Id == task.Id ? item with { IsCompleted = !item.IsCompleted } : item).ToList());
            if (!_operations.IsStopping) TaskInput.Focus(FocusState.Keyboard);
        });
        row.Children.Add(new Viewbox { Width = 16, Height = 16, Child = check, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var label = new TextBlock { Text = task.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Opacity = task.IsCompleted ? 0.55 : 1 };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    private void PaintTasks()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        var text = TextBrush();
        var hover = new SolidColorBrush(dark ? Color(0x22, 0xFF, 0xFF, 0xFF) : Color(0x12, 0, 0, 0));
        _sidebarDivider.Background = new SolidColorBrush(dark ? Color(0x22, 0xFF, 0xFF, 0xFF) : Color(0x18, 0, 0, 0));
        UseStableWidgetButton(SidebarToggle);
        SidebarToggle.Foreground = text;
        SidebarToggle.Background = transparent;
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
            SidebarToggle.Resources[key] = transparent;
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            SidebarToggle.Resources[key] = text;
        TasksButton.Foreground = text;
        TasksButton.Background = transparent;
        TasksButton.BorderThickness = new Thickness(0);
        TasksButton.CornerRadius = new CornerRadius(8);
        UseStableWidgetButton(TasksButton);
        TasksButton.Resources["ButtonBackground"] = transparent;
        TasksButton.Resources["ButtonBackgroundPointerOver"] = hover;
        TasksButton.Resources["ButtonBackgroundPressed"] = hover;
        TasksButton.Resources["ButtonForeground"] = text;
        TasksButton.Resources["ButtonForegroundPointerOver"] = text;
        TasksButton.Resources["ButtonForegroundPressed"] = text;
        PaintText(TasksButton);
        TasksPanel.RequestedTheme = RequestedTheme;
        PaintText(TasksPanel);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            TaskInput.Resources[key] = transparent;
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            TaskInput.Resources[key] = text;
        TaskInput.Resources["TextControlForegroundDisabled"] = ToneBrush(MessageTone.Muted);
        TaskInput.Foreground = text;

        void PaintText(DependencyObject element)
        {
            if (element is TextBlock block) block.Foreground = text;
            if (element is Control control) control.Foreground = text;
            if (element is ButtonBase button && button is not CheckBox)
            {
                if (button is Button widgetButton) UseStableWidgetButton(widgetButton);
                button.Background = transparent;
                button.BorderThickness = new Thickness(0);
                foreach (var key in new[] { "ButtonBackground", "ToggleButtonBackground" }) button.Resources[key] = transparent;
                foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ToggleButtonBackgroundPointerOver", "ToggleButtonBackgroundPressed" })
                    button.Resources[key] = ReferenceEquals(button, TasksCompletedToggle) ? transparent : hover;
                foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
                    "ToggleButtonForeground", "ToggleButtonForegroundPointerOver", "ToggleButtonForegroundPressed",
                    "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" }) button.Resources[key] = text;
            }
            if (element is Panel panel) foreach (var child in panel.Children) PaintText(child);
            if (element is ContentControl content && content.Content is DependencyObject childContent) PaintText(childContent);
            if (element is Viewbox viewbox && viewbox.Child is DependencyObject scaledChild) PaintText(scaledChild);
        }
    }
}
