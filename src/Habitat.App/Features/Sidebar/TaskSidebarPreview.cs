using Habitat.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Sidebar;

internal sealed class TaskSidebarPreview
{
    private static readonly TimeSpan RotationInterval = TimeSpan.FromSeconds(8);
    private readonly SidebarDeadline _deadline;
    private readonly Func<bool> _overlayOpen;
    private TaskItem[] _pending = [];
    private bool _loaded, _open;
    internal bool Hovered { get; set; }
    internal bool Focused { get; set; }
    internal int Index { get; set; }
    internal DateTimeOffset NextRotation { get; private set; }
    internal Button Button { get; } = new();
    internal StackPanel Symbol { get; } = new() { Spacing = 3, Width = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock Title { get; } = UiLanguage.Watch(new TextBlock { Text = UiText.Get("Tasks"), FontSize = 11, Opacity = 0.72 },
        ("Text", () => (object?)UiText.Get("Tasks")));
    internal TextBlock Badge { get; } = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    internal StackPanel Details { get; } = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBlock Summary { get; } = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _hint = new() { FontSize = 12, Opacity = 0.8 };

    internal TaskSidebarPreview(DispatcherQueue dispatcher, Func<bool> overlayOpen)
    {
        _overlayOpen = overlayOpen;
        _deadline = new(dispatcher, NextDeadline, Advance);
        var content = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Symbol.Children.Add(new FontIcon { Glyph = "\uE9D5", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        // Preserve the glyph position in both sidebar layouts.
        Symbol.Children.Add(new Border { Height = 18, Child = Badge });
        content.Children.Add(Symbol);
        Details.Children.Add(Title); Details.Children.Add(Summary); Details.Children.Add(_hint);
        Grid.SetColumn(Details, 1);
        content.Children.Add(Details);
        Button.Content = content;
        Button.Height = 78;
        Button.Padding = new Thickness(8);
        Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Button.VerticalContentAlignment = VerticalAlignment.Center;
        Button.VerticalAlignment = VerticalAlignment.Center;
        Button.PointerEntered += (_, _) => { Hovered = true; _deadline.Reschedule(); };
        Button.PointerExited += (_, _) => { Hovered = false; RestartRotation(); };
        Button.GotFocus += (_, _) => { Focused = true; _deadline.Reschedule(); };
        Button.LostFocus += (_, _) => { Focused = false; RestartRotation(); };
    }

    internal void Start() => _deadline.Start();
    internal void Stop() => _deadline.Stop();

    internal void Render(IReadOnlyList<TaskItem> tasks, bool loaded, bool open)
    {
        _loaded = loaded;
        _pending = tasks.Where(task => !task.IsCompleted).ToArray();
        Badge.Text = loaded ? _pending.Length.ToString() : "!";
        Index = _pending.Length == 0 ? 0 : Math.Clamp(Index, 0, _pending.Length - 1);
        RenderMessage();
        SetOpen(open);
    }

    internal void SetOpen(bool open)
    {
        _open = open;
        _hint.Text = UiLanguage.Value(_hint, "Text", () => _open ? UiText.Get("ClickToCollapseTheList") : UiText.Get("ClickToViewYourList"));
        UiLanguage.SetName(Button, () => UiText.Format("TasksPending", _open ? UiText.Get("Hide") : UiText.Get("Show"), Badge.Text));
        RestartRotation();
    }

    private void RestartRotation()
    {
        NextRotation = DateTimeOffset.UtcNow + RotationInterval;
        _deadline.Reschedule();
    }

    private bool CanRotate => _loaded && _pending.Length >= 2 && !_open && !Hovered && !Focused;
    private DateTimeOffset? NextDeadline() => !CanRotate ? null :
        _overlayOpen() ? DateTimeOffset.UtcNow + RotationInterval : NextRotation;

    internal void Advance(DateTimeOffset now)
    {
        if (!CanRotate || _overlayOpen() || now < NextRotation) return;
        Index = (Index + 1) % _pending.Length;
        NextRotation = now + RotationInterval;
        RenderMessage();
    }

    private void RenderMessage() => Summary.Text = !_loaded ? UiText.Get("TasksUnavailable") :
        _pending.ElementAtOrDefault(Index)?.Text ?? UiText.Get("NoPendingTasks");
}
