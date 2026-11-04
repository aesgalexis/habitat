using System.Diagnostics;

namespace Habitat.Features.Contacts;

internal sealed record PhoneLinkMessageResult(string Status, string? Error = null, PhoneLinkMessageType MessageType = PhoneLinkMessageType.Unknown);
internal sealed record PhoneLinkConversationMessage(string Id, string Text, bool Outgoing);
internal enum PhoneLinkMessageType { Unknown, Sms, IMessage }
internal enum PhoneLinkSendOutcome { NotInvoked, Unconfirmed, Submitted, Sent }
internal sealed record PhoneLinkComposer(nint Window, string EditorId, string SendId, string[] Recipients,
    string Text, bool Connected, bool EditorEnabled, bool SendEnabled, bool Blocked,
    PhoneLinkMessageType MessageType = PhoneLinkMessageType.Unknown);
internal interface IPhoneLinkDesktop
{
    Task<IReadOnlyList<PhoneLinkComposer>> ReadComposersAsync(CancellationToken token);
    Task<bool> SetTextAsync(PhoneLinkComposer expected, string address, string text, CancellationToken token);
    Task<PhoneLinkSendOutcome> InvokeSendAsync(PhoneLinkComposer expected, string address, string text,
        TimeSpan acknowledgementTimeout, CancellationToken token);
}

// Navigation never carries the body. Only a verified empty editor (or the same
// existing draft) can be used. There is exactly one invocation per user action.
internal sealed class PhoneLinkDelivery(IPhoneLinkDesktop desktop, Func<string, CancellationToken, Task<bool>> open,
    TimeSpan? preparationTimeout = null, TimeSpan? acknowledgementTimeout = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _preparationTimeout = preparationTimeout ?? TimeSpan.FromSeconds(12);
    private readonly TimeSpan _acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(8);

    internal static bool SameText(string left, string right) =>
        left.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') ==
        right.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    internal static bool RecipientMatches(PhoneLinkComposer composer, string address)
    {
        var phone = ContactBook.NormalizePhone(address);
        var recipients = composer.Recipients.Select(ContactBook.NormalizePhone).Where(p => p.Length > 0).Distinct().ToArray();
        return phone.Length > 0 && recipients.Length == 1 && recipients[0] == phone;
    }
    internal static bool Ready(PhoneLinkComposer composer, string address, string text) => composer.MessageType != PhoneLinkMessageType.Sms && composer.Connected &&
        !composer.Blocked && composer.EditorEnabled && composer.SendEnabled && composer.EditorId.Length > 0 &&
        composer.SendId.Length > 0 && RecipientMatches(composer, address) && SameText(composer.Text, text);

    internal static PhoneLinkSendOutcome WaitForAcknowledgement(PhoneLinkComposer prepared, string address,
        Func<(PhoneLinkComposer? Composer, bool Sent)> observe, bool watchingNotifications, TimeSpan timeout, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan? submittedAt = null;
        while (elapsed.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            var (current, sent) = observe();
            if (current is not null && current.Window == prepared.Window && RecipientMatches(current, address) &&
                current.Connected && !current.Blocked && current.MessageType != PhoneLinkMessageType.Sms)
            {
                if (sent) return PhoneLinkSendOutcome.Sent;
                if (current.EditorId == prepared.EditorId && current.Text.Length == 0 && !current.SendEnabled)
                    submittedAt ??= elapsed.Elapsed;
            }
            // A consumed editor is enough for submission. Give an active listener a short chance to confirm sending too.
            if (submittedAt is { } at && (!watchingNotifications || elapsed.Elapsed - at >= TimeSpan.FromMilliseconds(600)))
                break;
            token.WaitHandle.WaitOne(50);
        }
        token.ThrowIfCancellationRequested();
        return submittedAt.HasValue ? PhoneLinkSendOutcome.Submitted : PhoneLinkSendOutcome.Unconfirmed;
    }

    internal async Task<PhoneLinkMessageResult> SendAsync(string address, string text, CancellationToken token)
    {
        _ = PhoneLinkMessage.DraftUri(address, text); // Validate before navigation or a send attempt.
        if (!await _gate.WaitAsync(0, token)) return new("not-sent", "busy");
        var attempted = false;
        try
        {
            var before = await desktop.ReadComposersAsync(token);
            if (before.Any(c => c.Blocked)) return new("not-sent", "blocked");
            if (before.Any(c => c.Text.Length > 0 && (!RecipientMatches(c, address) || !SameText(c.Text, text))))
                return new("not-sent", "draft");
            var existing = before.Where(c => RecipientMatches(c, address) && c.Connected && c.EditorEnabled &&
                c.EditorId.Length > 0 && c.SendId.Length > 0).ToArray();
            // Reuse a verified open conversation without activating the other app.
            if (existing.Length != 1 && !await open(address, token)) return new("not-sent", "unavailable");
            var elapsed = Stopwatch.StartNew();
            PhoneLinkComposer? prepared = null;
            while (elapsed.Elapsed < _preparationTimeout)
            {
                token.ThrowIfCancellationRequested();
                var states = await desktop.ReadComposersAsync(token);
                if (states.Any(c => c.Blocked)) return new("not-sent", "blocked");
                var matches = states.Where(c => RecipientMatches(c, address)).ToArray();
                if (matches.Length == 1)
                {
                    prepared = matches[0];
                    if (!prepared.Connected) return new("not-sent", "disconnected");
                    if (prepared.MessageType == PhoneLinkMessageType.Sms) return new("not-sent", "sms-disabled");
                    if (prepared.Text.Length > 0 && !SameText(prepared.Text, text)) return new("not-sent", "draft");
                    if (prepared.EditorEnabled && prepared.EditorId.Length > 0 && prepared.SendId.Length > 0) break;
                }
                await Task.Delay(50, token);
                prepared = null;
            }
            if (prepared is null) return new("not-sent", "recipient");
            if (prepared.Text.Length == 0)
            {
                if (!await desktop.SetTextAsync(prepared, address, text, token)) return new("not-sent", "changed");
                var expected = prepared;
                prepared = null;
                elapsed.Restart();
                while (elapsed.Elapsed < _preparationTimeout)
                {
                    var states = await desktop.ReadComposersAsync(token);
                    var current = states.SingleOrDefault(c => c.Window == expected.Window && c.EditorId == expected.EditorId &&
                        c.SendId == expected.SendId && RecipientMatches(c, address));
                    if (current is null || current.Blocked || !current.Connected || current.MessageType == PhoneLinkMessageType.Sms ||
                        current.Text.Length > 0 && !SameText(current.Text, text)) return new("not-sent", "changed");
                    if (Ready(current, address, text)) { prepared = current; break; }
                    await Task.Delay(50, token);
                }
            }
            if (prepared is null || !Ready(prepared, address, text)) return new("not-sent", "changed");
            token.ThrowIfCancellationRequested();
            attempted = true;
            var outcome = await desktop.InvokeSendAsync(prepared, address, text, _acknowledgementTimeout, token);
            return outcome switch
            {
                PhoneLinkSendOutcome.NotInvoked => new("not-sent", "changed"),
                PhoneLinkSendOutcome.Sent => new("sent", MessageType: prepared.MessageType),
                PhoneLinkSendOutcome.Submitted => new("submitted", MessageType: prepared.MessageType),
                _ => new("uncertain", "acknowledgement")
            };
        }
        catch (OperationCanceledException) when (attempted) { return new("uncertain", "cancelled"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(attempted ? "uncertain" : "not-sent", "automation"); }
        finally { _gate.Release(); }
    }
}
