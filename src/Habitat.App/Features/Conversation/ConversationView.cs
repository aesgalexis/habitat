using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Conversation;

// Owns the correspondence between retained messages and their native controls.
// The page decides placement; this view owns rendering, trimming and coloring.
internal sealed class ConversationView(ConversationSession session, Func<MessageTone, Brush> toneBrush, Func<Brush> textBrush)
{
    public StackPanel Messages { get; } = new() { Spacing = 22 };
    public ScrollViewer Scroll { get; } = new();
    private readonly TextBlock _notice = UiLanguage.Watch(new TextBlock()
    {
        Text = UiText.Get("EarlierMessagesRemainAvailableInChatGPT"), FontSize = 12, TextWrapping = TextWrapping.Wrap
    }, ("Text", () => (object?)(UiText.Get("EarlierMessagesRemainAvailableInChatGPT"))));

    public void Add(string author, string body, MessageTone tone)
    {
        var removed = session.Add(author, body, tone);
        for (var index = 0; index < removed; index++)
            Messages.Children.RemoveAt(Messages.Children.Count > 0 && ReferenceEquals(Messages.Children[0], _notice) ? 1 : 0);
        if (session.HasTrimmedMessages && !Messages.Children.Contains(_notice)) Messages.Children.Insert(0, _notice);
        var group = new StackPanel { Spacing = 5, Tag = tone };
        group.Children.Add(new TextBlock
        {
            Text = author, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = toneBrush(tone)
        });
        group.Children.Add(new TextBlock
        {
            Text = body, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = textBrush()
        });
        _notice.Foreground = toneBrush(MessageTone.Muted);
        Messages.Children.Add(group);
    }

    public void ScrollToEnd()
    {
        Scroll.UpdateLayout();
        Scroll.ChangeView(null, Scroll.ScrollableHeight, null);
    }

    public void RefreshAppearance()
    {
        _notice.Foreground = toneBrush(MessageTone.Muted);
        foreach (var child in Messages.Children)
        {
            if (child is not StackPanel group || group.Tag is not MessageTone tone) continue;
            if (group.Children.Count > 0 && group.Children[0] is TextBlock author) author.Foreground = toneBrush(tone);
            if (group.Children.Count > 1 && group.Children[1] is TextBlock body) body.Foreground = textBrush();
        }
    }

    public void RefreshLanguage()
    {
        foreach (var child in Messages.Children.OfType<StackPanel>())
        {
            if (child.Tag is not MessageTone tone || child.Children[0] is not TextBlock author) continue;
            if (tone == MessageTone.User) author.Text = UiLanguage.Value(author, "Text", () => UiText.Get("YOU"));
            else if (tone == MessageTone.Muted)
            {
                author.Text = UiLanguage.Value(author, "Text", () => UiText.Get("SYSTEM"));
                if (child.Children[1] is TextBlock body) body.Text = UiLanguage.Value(body, "Text", () => UiText.LocalizeMessage(body.Text));
            }
        }
    }
}
