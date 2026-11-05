#if HABITAT_UI_CHECKS
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckCodexDesktopSchedulingUiAsync(string log)
    {
        var otherEditor = new SchedulingTestEditor { Text = "Unrelated private draft" };
        var targetEditor = new SchedulingTestEditor();
        var other = SchedulingTestWindow("Other chat", otherEditor, () => throw new InvalidOperationException("Wrong window submitted."));
        var sends = 0;
        var chosen = SchedulingTestWindow("Scheduled target", targetEditor, () => { sends++; targetEditor.Text = ""; });
        try
        {
            other.AppWindow.Show(false); chosen.AppWindow.Show(false);
            await Task.Delay(150);
            var traces = new List<string>();
            var desktop = new CodexDesktopAutomation("HABITAT_TEST_ONLY", _ => throw new InvalidOperationException("No real navigation allowed."),
                Environment.ProcessPath, traces.Add);
            var chats = await desktop.ReadChatsAsync(default);
            var target = chats.Single(chat => chat.SelectedTitles.Contains("Scheduled target"));
            if (!target.IsEmpty || !target.CanAccept || target.IsForeground || chats.Count(chat => chat.SelectedTitles.Contains("Other chat")) != 1)
                throw new InvalidOperationException("Native accessibility must distinguish both synthetic Codex windows and accept the empty target.");
            var result = await desktop.SubmitVisibleAsync(target, "Synthetic scheduled instruction ☕", default);
            if (result != CodexDeliveryResult.Sent || sends != 1 || otherEditor.Text != "Unrelated private draft" ||
                traces.Any(phase => phase.StartsWith("Codex Enter:")) || !traces.Contains("Codex Send control invoked; waiting for acknowledgement."))
                throw new InvalidOperationException($"Native submission: result={result}; sends={sends}; otherDraftKept={otherEditor.Text == "Unrelated private draft"}; phases={string.Join(" | ", traces)}");
            targetEditor.Text = "Protected target draft";
            if (await desktop.SubmitVisibleAsync(target, "Do not overwrite", default) != CodexDeliveryResult.NotSent ||
                targetEditor.Text != "Protected target draft" || sends != 1)
                throw new InvalidOperationException("A draft changed after selection must prevent native transfer and submission.");
            File.AppendAllText(log, "PASS: native accessibility with two synthetic windows; exact background target pinned through Send invocation and acknowledgement; no keyboard input, other draft mutation or external messages.\n");
        }
        finally { chosen.Close(); other.Close(); }
    }

    private static Window SchedulingTestWindow(string title, SchedulingTestEditor editor, Action submit)
    {
        AutomationProperties.SetName(editor, "Synthetic Codex editor");
        var selected = new ListView { SelectionMode = ListViewSelectionMode.Single };
        selected.Items.Add(new ListViewItem { Content = title });
        selected.SelectedIndex = 0;
        var send = new Button { Content = "Send", IsEnabled = editor.Text.Length > 0 };
        send.Click += (_, _) => submit();
        editor.TextChanged += (_, _) => send.IsEnabled = editor.Text.Length > 0;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(selected); content.Children.Add(editor); content.Children.Add(send);
        var window = new Window { Title = title, Content = new SchedulingTestConversation { Content = content } };
        window.AppWindow.Resize(new(380, 240));
        return window;
    }
    private sealed class SchedulingTestEditor : TextBox
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new EditorPeer(this);
        private sealed class EditorPeer(TextBox owner) : TextBoxAutomationPeer(owner)
        { protected override string GetClassNameCore() => "ProseMirror"; }
    }
    private sealed class SchedulingTestConversation : ContentControl
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ConversationPeer(this);
        private sealed class ConversationPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetNameCore() => "Conversation";
        }
    }
}
#endif
