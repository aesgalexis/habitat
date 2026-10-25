#if HABITAT_UI_CHECKS
using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckConversationScrollUiAsync(Window window, string logPath)
    {
        var savedDestination = _destination;
        var savedText = Composer.Text;
        var savedSize = window.AppWindow.Size;
        var longText = string.Join('\n', Enumerable.Range(1, 45).Select(index =>
            $"Synthetic conversation line {index}: enough text to wrap when the conversation becomes narrow."));
        try
        {
            SelectDestination(ComposerDestination.OpenAI);
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
            Composer.Text = "";
            await Task.Delay(100);
            if (ConversationScroll.Visibility != Visibility.Collapsed || Composer.VerticalAlignment != VerticalAlignment.Top ||
                SendButton.VerticalAlignment != VerticalAlignment.Top || ComposerHint.Visibility != Visibility.Visible)
                throw new InvalidOperationException("An empty OpenAI conversation must retain the centered welcome editor.");

            // Match the send order: append, then clear/refocus the multiline
            // editor. Its final height is only known in a later layout pass.
            Composer.Text = longText;
            await Task.Delay(80);
            AddMessage("YOU", longText, MessageTone.User);
            Composer.Text = "";
            FocusComposer();
            await CheckBottomAsync("first send and welcome-to-history layout");

            for (var index = 0; index < 5; index++)
            {
                ConversationScroll.ChangeView(null, 0, null, true);
                await Task.Delay(80);
                if (ConversationScroll.VerticalOffset > 1)
                    throw new InvalidOperationException("Idle history must allow scrolling up to read earlier messages.");
                Composer.Text = longText;
                await Task.Delay(60);
                AddMessage("YOU", $"Send {index}\n{longText}", MessageTone.User);
                Composer.Text = "";
                FocusComposer();
                await CheckBottomAsync($"send {index} from earlier history after clearing a multiline draft");

                Bridge_EventReceived(this, new BridgeEvent("response", null, longText, null));
                await CheckBottomAsync($"long response {index}");
            }

            Composer.Text = longText;
            await CheckBottomAsync("editor growth");
            Composer.Text = "";
            await CheckBottomAsync("editor shrink and restored hint");
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 540));
            await CheckBottomAsync("narrower and shorter viewport");
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 820));
            await CheckBottomAsync("wider and taller viewport");

            SelectDestination(ComposerDestination.Notepad);
            Bridge_EventReceived(this, new BridgeEvent("response", null, longText, null));
            await Task.Delay(100);
            if (ConversationScroll.Visibility != Visibility.Collapsed || Composer.VerticalAlignment != VerticalAlignment.Top)
                throw new InvalidOperationException("A hidden OpenAI response must preserve the other destination's welcome layout.");
            SelectDestination(ComposerDestination.OpenAI);
            await CheckBottomAsync("return to a response received in another destination");

            // Multiple arrivals in one dispatch must coalesce to the final
            // extent, including removal of the oldest retained messages.
            for (var index = 0; index < 205; index++)
                AddMessage("OpenAI", $"Synthetic retained message {index}", MessageTone.Assistant);
            await CheckBottomAsync("rapid arrivals and history trimming");
            if (!_conversation.HasTrimmedMessages)
                throw new InvalidOperationException("The scroll regression must exercise retained-history trimming.");
            File.AppendAllText(logPath, "PASS: OpenAI follows every send, long response, editor/viewport resize, hidden reply and trimmed history; idle history remains scrollable and welcome layouts stay centered\n");
        }
        finally
        {
            _conversation.Clear();
            Conversation.Children.Clear();
            Composer.Text = "";
            window.AppWindow.Resize(savedSize);
            SelectDestination(savedDestination);
            Composer.Text = savedText;
        }

        async Task CheckBottomAsync(string stage)
        {
            // Large batches can span several native layout/render passes.
            // Require a settled bottom, with a bounded wait for those passes.
            await Task.Delay(100);
            var previousBottom = -1d;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var bottom = ConversationScroll.ScrollableHeight;
                if (bottom > 0 && Math.Abs(bottom - ConversationScroll.VerticalOffset) <= 1 && bottom == previousBottom)
                    break;
                previousBottom = bottom;
                await Task.Delay(25);
            }
            if (ConversationScroll.ViewportHeight <= 0 || ConversationScroll.ScrollableHeight <= 0 ||
                Math.Abs(ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset) > 1)
                throw new InvalidOperationException($"OpenAI did not reach the bottom after {stage}: " +
                    $"offset={ConversationScroll.VerticalOffset}, bottom={ConversationScroll.ScrollableHeight}, viewport={ConversationScroll.ViewportHeight}.");
            File.AppendAllText(logPath, $"PASS: bottom after {stage} (offset={ConversationScroll.VerticalOffset:0.0}, bottom={ConversationScroll.ScrollableHeight:0.0})\n");
        }
    }
}
#endif
