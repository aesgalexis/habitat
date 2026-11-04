using Habitat.Features.Contacts;

internal static class PhoneLinkDeliveryChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        const string phone = "+34600111222";
        const string text = "Exact message · café ☕\nSecond line & + #";
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        PhoneLinkDelivery Delivery(Desktop desktop, Action? onOpen = null) => new(desktop, (_, _) =>
        { desktop.Opens++; onOpen?.Invoke(); return Task.FromResult(true); }, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(80));

        var desktop = new Desktop();
        var result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "submitted" && desktop.Opens == 0 && desktop.Sets == 1 && desktop.Sends == 1 && desktop.Inserted == text,
            "A verified open iMessage conversation must receive the exact text once without activating Phone Link again.");
        Check(desktop.State.Text == "" && !desktop.State.SendEnabled,
            "Submission acknowledgement must require the same editor to be consumed.");

        desktop = new() { State = Desktop.Empty with { Text = text, SendEnabled = true } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "submitted" && desktop.Sets == 0 && desktop.Sends == 1,
            "The same existing draft may be sent without rewriting it.");

        desktop = new() { State = Desktop.Empty with { Text = "Another draft", SendEnabled = true } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "draft" } && desktop.Opens == 0 && desktop.Sets == 0 && desktop.Sends == 0,
            "An existing different draft must be preserved before navigation or sending.");

        desktop = new() { State = Desktop.Empty with { Recipients = ["+34600999888"] } };
        var current = desktop;
        result = await Delivery(desktop, () => current.State = current.State with { Recipients = [phone], Text = "Destination's saved draft", SendEnabled = true })
            .SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "draft" } && desktop.Sets == 0 && desktop.Sends == 0,
            "Recipient-only navigation must allow detecting a destination's existing draft without overwriting it.");

        desktop = new() { State = Desktop.Empty with { Recipients = ["+34600999888"] } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "recipient" } && desktop.Sets == 0 && desktop.Sends == 0,
            "An unverified phone number must never receive text or a Send invocation.");

        desktop = new() { State = Desktop.Empty with { Recipients = [phone, "+34600999888"] } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "not-sent" && desktop.Sends == 0, "Ambiguous or group recipients must not be sent a message.");

        desktop = new() { State = Desktop.Empty with { Connected = false } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "disconnected" } && desktop.Sets == 0 && desktop.Sends == 0,
            "A disconnected phone must be detected before inserting or sending.");

        desktop = new() { State = Desktop.Empty with { Blocked = true } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "blocked" } && desktop.Opens == 0 && desktop.Sends == 0,
            "A modal or verification dialog must stop the send flow.");

        desktop = new() { AfterSet = d => d.State = d.State with { Text = "Changed by the user" } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "changed" } && desktop.Sets == 1 && desktop.Sends == 0,
            "A changed editor must never be overwritten again or sent.");

        desktop = new() { AfterSet = d => d.State = d.State with { Recipients = ["+34600999888"] } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "not-sent" && desktop.Sends == 0, "A recipient change during insertion must stop before Send.");

        desktop = new() { RejectInvoke = true };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result is { Status: "not-sent", Error: "changed" } && desktop.Sends == 0,
            "The desktop must be able to reject a stale send control without invoking it.");

        desktop = new() { Acknowledge = false };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "uncertain" && desktop.Sends == 1,
            "A missing acknowledgement must return uncertain after one invocation, without automatic retries.");

        desktop = new() { ThrowAfterInvoke = true };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "uncertain" && desktop.Sends == 1,
            "An exception after invoking Send must not be reported as definitely unsent or retried.");

        desktop = new() { AfterSend = d => d.State = d.State with { Recipients = ["+34600999888"] } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "uncertain" && desktop.Sends == 1,
            "An empty editor in a different conversation is not acknowledgement of this submission.");

        desktop = new() { SentNotification = true, AfterSend = d => d.State = d.State with { EditorId = "thread-editor" } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "sent" && desktop.Sends == 1,
            "A matched sent notification must confirm the message even when Phone Link replaces the editor.");

        desktop = new() { AfterSend = d => d.State = d.State with { EditorId = "thread-editor" } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "uncertain" && desktop.Sends == 1,
            "An empty replacement editor alone must not confirm the message.");

        desktop = new() { SentNotification = true, AfterSend = d => d.State = d.State with { Recipients = ["+34600999888"] } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "uncertain" && desktop.Sends == 1,
            "A sent notification must be scoped to the selected conversation.");

        desktop = new() { PendingSend = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var delivery = Delivery(desktop);
        var pending = delivery.SendAsync(phone, text, default);
        await desktop.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicate = await delivery.SendAsync(phone, text, default);
        Check(duplicate is { Status: "not-sent", Error: "busy" } && desktop.Sends == 1,
            "Concurrent button presses must not queue a duplicate send.");
        desktop.PendingSend.SetResult(true);
        Check((await pending).Status == "submitted" && desktop.Sends == 1, "The original pending send must complete once.");

        desktop = new() { PendingSend = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cancellation = new CancellationTokenSource();
        pending = Delivery(desktop).SendAsync(phone, text, cancellation.Token);
        await desktop.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        Check((await pending).Status == "uncertain" && desktop.Sends == 1,
            "Cancellation after invocation must preserve an uncertain result and never issue another send.");
        Check(PhoneLinkMessage.RecipientUri("0034 600 111 222").OriginalString == "sms:+34600111222",
            "Navigation must contain the selected number and no message body or send flag.");
        foreach (var type in new[] { PhoneLinkMessageType.Sms, PhoneLinkMessageType.Unknown })
        {
            desktop = new() { State = Desktop.Empty with { MessageType = type } };
            result = await Delivery(desktop).SendAsync(phone, text, default);
            Check(type == PhoneLinkMessageType.Sms
                ? result is { Status: "not-sent", Error: "sms-disabled" } && desktop.Sets == 0 && desktop.Sends == 0
                : result is { Status: "submitted", MessageType: PhoneLinkMessageType.Unknown } && desktop.Sets == 1 && desktop.Sends == 1,
                "An unknown service label must allow the existing verified-recipient route without claiming iMessage; explicit SMS stays blocked.");
        }
        desktop = new() { AfterSet = d => d.State = d.State with { MessageType = PhoneLinkMessageType.Sms } };
        result = await Delivery(desktop).SendAsync(phone, text, default);
        Check(result.Status == "not-sent" && desktop.Sends == 0, "Switching to SMS during preparation must stop before Send.");
        if (OperatingSystem.IsWindows())
        {
            Check(Habitat.Services.PhoneLinkDesktopAutomation.MessageType(["iMessage", "Send"]) == PhoneLinkMessageType.IMessage,
                "An explicit iMessage composer label is valid service evidence.");
            Check(Habitat.Services.PhoneLinkDesktopAutomation.MessageType(["SMS", "iMessage"]) == PhoneLinkMessageType.Unknown &&
                Habitat.Services.PhoneLinkDesktopAutomation.MessageType(["iPhone", "Send a message", "Messages", "Text message", "Mensaje de texto", "sms-gsm"]) == PhoneLinkMessageType.Unknown,
                "Ambiguous labels, generic messages and an iPhone identity do not prove iMessage.");
            Check(Habitat.Services.PhoneLinkDesktopAutomation.IsSentNotification(2, "OutgoingMessageStatusIndicator", "Message Sent") &&
                Habitat.Services.PhoneLinkDesktopAutomation.IsSentNotification(2, "OutgoingMessageStatusIndicator", "Mensaje enviado") &&
                !Habitat.Services.PhoneLinkDesktopAutomation.IsSentNotification(2, "OutgoingMessageStatusIndicator", "Enviando") &&
                !Habitat.Services.PhoneLinkDesktopAutomation.IsSentNotification(2, "OtherNotification", "Message Sent"),
                "Only Phone Link's outgoing completion announcement is a sent notification.");
            Check(Habitat.Services.PhoneLinkDesktopAutomation.MessageDirection("Message from you. Hello. 10:00") == true &&
                Habitat.Services.PhoneLinkDesktopAutomation.MessageDirection("Mensaje tuyo. Hola. 10:00") == true &&
                Habitat.Services.PhoneLinkDesktopAutomation.MessageDirection("Message from Test. Yes. 10:01") == false &&
                Habitat.Services.PhoneLinkDesktopAutomation.MessageDirection("Mensaje de Test. Sí. 10:01") == false &&
                Habitat.Services.PhoneLinkDesktopAutomation.MessageDirection("Today, 10:00") is null,
                "Only recognized Phone Link message rows have a conversation direction.");
        }
        nint foreground = 1;
        var restores = 0;
        var keeper = new PhoneLinkFocusKeeper(1, () => foreground, w => w == 2, w => { restores++; foreground = w; });
        foreground = 2; keeper.ForegroundChanged();
        Check(foreground == 1 && restores == 1, "A Phone Link foreground event must return focus during the operation, not only at the end.");
        foreground = 3; keeper.ForegroundChanged(); foreground = 2; keeper.ForegroundChanged();
        Check(foreground == 2 && restores == 1, "An intentional switch to another app must stop focus restoration.");
        foreground = 1; keeper.ForegroundChanged(); foreground = 2; keeper.ForegroundChanged();
        Check(foreground == 1 && restores == 2, "Returning to HABITAT must resume foreground protection for the remaining operation.");
        foreground = 3;
        keeper = new PhoneLinkFocusKeeper(1, () => foreground, w => w == 2, _ => restores++);
        foreground = 2; keeper.ForegroundChanged();
        Check(restores == 2, "A send started while another app owns focus must not steal focus for HABITAT.");
        return checks;
    }

    private sealed class Desktop : IPhoneLinkDesktop
    {
        internal static PhoneLinkComposer Empty => new(7, "editor", "send", ["+34600111222"], "", true, true, false, false, PhoneLinkMessageType.IMessage);
        internal PhoneLinkComposer State = Empty;
        internal int Opens, Sets, Sends;
        internal string? Inserted;
        internal bool Acknowledge = true, RejectInvoke, ThrowAfterInvoke, SentNotification;
        internal Action<Desktop>? AfterSet, AfterSend;
        internal TaskCompletionSource<bool>? PendingSend;
        internal readonly TaskCompletionSource<bool> SendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<PhoneLinkComposer>> ReadComposersAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<PhoneLinkComposer>>([State]); }
        public Task<bool> SetTextAsync(PhoneLinkComposer expected, string address, string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (State.Text.Length > 0 || !PhoneLinkDelivery.RecipientMatches(State, address)) return Task.FromResult(false);
            Sets++; Inserted = text; State = State with { Text = text, SendEnabled = true }; AfterSet?.Invoke(this);
            return Task.FromResult(true);
        }
        public async Task<PhoneLinkSendOutcome> InvokeSendAsync(PhoneLinkComposer expected, string address, string text,
            TimeSpan acknowledgementTimeout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (RejectInvoke || !PhoneLinkDelivery.Ready(State, address, text)) return PhoneLinkSendOutcome.NotInvoked;
            Sends++; SendStarted.TrySetResult(true);
            if (ThrowAfterInvoke) throw new IOException("Synthetic failure after invocation.");
            if (PendingSend is not null) await PendingSend.Task.WaitAsync(token);
            if (Acknowledge) State = State with { Text = "", SendEnabled = false };
            AfterSend?.Invoke(this);
            return PhoneLinkDelivery.WaitForAcknowledgement(expected, address, () => (State, SentNotification), SentNotification,
                acknowledgementTimeout, token);
        }
    }
}
