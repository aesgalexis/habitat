using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Conversation;

// Owns the correspondence between retained messages and their native controls.
// The page decides placement; this view owns rendering, trimming and coloring.
internal sealed class ConversationView
{
    private readonly ConversationSession _session;
    private readonly Func<MessageTone, Brush> _toneBrush;
    private readonly Func<Brush> _textBrush;
    private bool _scrollPending, _scrollQueued;
    private double _lastScrollableHeight;
    public StackPanel Messages { get; } = new() { Spacing = 22 };
    public ScrollViewer Scroll { get; } = new();
    private readonly TextBlock _notice = new()
    {
        Text = "Earlier messages remain available in ChatGPT.", FontSize = 12, TextWrapping = TextWrapping.Wrap
    };

    public ConversationView(ConversationSession session, Func<MessageTone, Brush> toneBrush, Func<Brush> textBrush)
    {
        _session = session;
        _toneBrush = toneBrush;
        _textBrush = textBrush;
        Messages.SizeChanged += (_, _) => ScrollToEnd();
        Scroll.SizeChanged += (_, _) => ScrollToEnd();
        Scroll.Loaded += (_, _) => ScrollToEnd();
        // A request made while another destination is visible must survive
        // until this viewer has a viewport again.
        Scroll.LayoutUpdated += (_, _) => FollowExtentChanges();
        Scroll.ViewChanged += (_, _) => FollowExtentChanges();
    }

    public void Add(string author, string body, MessageTone tone)
    {
        var removed = _session.Add(author, body, tone);
        for (var index = 0; index < removed; index++)
            Messages.Children.RemoveAt(Messages.Children.Count > 0 && ReferenceEquals(Messages.Children[0], _notice) ? 1 : 0);
        if (_session.HasTrimmedMessages && !Messages.Children.Contains(_notice)) Messages.Children.Insert(0, _notice);
        var group = new StackPanel { Spacing = 5, Tag = tone };
        group.Children.Add(new TextBlock
        {
            Text = author, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = _toneBrush(tone)
        });
        group.Children.Add(new TextBlock
        {
            Text = body, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = _textBrush()
        });
        _notice.Foreground = _toneBrush(MessageTone.Muted);
        Messages.Children.Add(group);
    }

    public void ScrollToEnd()
    {
        _scrollPending = true;
        QueueScrollToEnd();
    }

    private void FollowExtentChanges()
    {
        // Native extent updates can arrive after content SizeChanged and even
        // LayoutUpdated, particularly when old messages are trimmed.
        if (_lastScrollableHeight != Scroll.ScrollableHeight)
        {
            _lastScrollableHeight = Scroll.ScrollableHeight;
            ScrollToEnd();
        }
        else if (_scrollPending) QueueScrollToEnd();
    }

    private void QueueScrollToEnd()
    {
        if (_scrollQueued || !Scroll.IsLoaded || Scroll.Visibility != Visibility.Visible) return;
        _scrollQueued = true;
        if (!Scroll.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _scrollQueued = false;
            if (!_scrollPending || !Scroll.IsLoaded || Scroll.Visibility != Visibility.Visible) return;
            // Sending also clears/resizes the editor and can collapse the
            // sidebar. Wait for those layouts before reading the final extent.
            Scroll.UpdateLayout();
            if (Scroll.ViewportHeight <= 0) return;
            Scroll.ChangeView(null, Scroll.ScrollableHeight, null, disableAnimation: true);
            _scrollPending = Math.Abs(Scroll.ScrollableHeight - Scroll.VerticalOffset) > 1;
        })) _scrollQueued = false;
    }

    public void RefreshAppearance()
    {
        _notice.Foreground = _toneBrush(MessageTone.Muted);
        foreach (var child in Messages.Children)
        {
            if (child is not StackPanel group || group.Tag is not MessageTone tone) continue;
            if (group.Children.Count > 0 && group.Children[0] is TextBlock author) author.Foreground = _toneBrush(tone);
            if (group.Children.Count > 1 && group.Children[1] is TextBlock body) body.Foreground = _textBrush();
        }
    }
}
