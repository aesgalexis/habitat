#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckContactPhoneUiAsync(string log)
    {
        await _services.Contacts.LoadAsync();
        var original = _services.Contacts.Book;
        var destination = _destination;
        var draft = Composer.Text;
        var send = _services.SendPhoneLink;
        var read = _services.ReadPhoneLinkConversation;
        var focus = _services.KeepPhoneLinkBackground;
        var focusStarted = 0; var focusReleased = 0;
        var previousIds = _phoneRecipients.Keys.ToHashSet();
        var calls = 0;
        string? recipient = null, body = null;
        IReadOnlyList<PhoneLinkConversationMessage>? messages = null;
        TaskCompletionSource<bool>? nextRead = null;
        var readCalls = 0;
        var explicitReads = 0;
        var unexpectedRead = false;
        var pending = new TaskCompletionSource<PhoneLinkMessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _services.KeepPhoneLinkBackground = _ => { focusStarted++; return new CheckedPhoneFocus(() => focusReleased++); };
            _services.SendPhoneLink = (address, text, _) =>
            { calls++; recipient = address; body = text; return pending.Task; };
            _services.ReadPhoneLinkConversation = (address, openIfNeeded, _) =>
            {
                readCalls++;
                nextRead?.TrySetResult(true);
                if (openIfNeeded) explicitReads++;
                unexpectedRead |= address != "+34600111222";
                return Task.FromResult(messages);
            };
            var source = new ContactSource(ContactBook.NewId(), "vcard", "Synthetic phone", "synthetic-phone", false);
            await _services.Contacts.ChangeAsync(b => b.Import(source, [
                new("one", [new("name", "Phone friend ☕"), new("phone", "+34 600 111 222")], "one"),
                new("two", [new("name", "Second phone friend"), new("phone", "+34 600 333 444")], "two")
            ]));
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            await InvokeAsync((Button)FacebookAvatars.Children.Single(c => AutomationProperties.GetName(c) == "Phone friend ☕"));
            var first = _destination;
            CheckInitial();
            if (calls != 0 || readCalls != 0) throw new InvalidOperationException("Choosing a phone contact must keep the initial writing view without reading or sending.");
            Composer.Text = "Phone draft · café ☕\nKeep the exact text & recipient";
            var firstText = Composer.Text;
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            await InvokeAsync((Button)FacebookAvatars.Children.Single(c => AutomationProperties.GetName(c) == "Second phone friend"));
            var second = _destination;
            if (Composer.Text != "" || second == first || !IsPhoneContactDestination(second))
                throw new InvalidOperationException("Phone recipients must have independent drafts.");
            Composer.Text = "Second recipient's private draft";
            SelectDestination(first);
            if (Composer.Text != firstText) throw new InvalidOperationException("Returning to a phone contact must restore its own draft.");
            var submitting = LaunchDestinationAsync(first, Composer.Text);
            await Task.Delay(40);
            await LaunchDestinationAsync(first, Composer.Text);
            if (calls != 1 || recipient != "+34600111222" || body != firstText || focusStarted != 1 || focusReleased != 0)
                throw new InvalidOperationException("A pending Phone Link handoff must target the fixed phone once with the exact text.");
            pending.SetResult(new("sent", MessageType: PhoneLinkMessageType.IMessage)); await submitting;
            var section = (PhoneLinkSection)_destinationSections[first];
            await WaitForUpdateUiAsync(() => Descendants(section.View).OfType<TextBlock>().Any(t => t.Text == firstText));
            if (!section.HasContent || !section.WasSubmitted || section.StatusText != "Sent via Phone Link." || Composer.Text != "" || focusReleased != 1 ||
                !Descendants(section.View).OfType<TextBlock>().Any(t => t.Text == firstText))
                throw new InvalidOperationException("A confirmed send must clear the composer and retain the submitted text in the result view.");
            const string reply = "sí · café ☕";
            messages = [new("outgoing", firstText, true), new("reply", reply, false)];
            nextRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await nextRead.Task.WaitAsync(TimeSpan.FromSeconds(15));
            nextRead = null;
            await WaitForUpdateUiAsync(() => CountMessage(reply) == 1);
            if (CountMessage(firstText) != 1 || explicitReads != 0 || unexpectedRead ||
                !Descendants(section.View).OfType<TextBlock>().Any(t => t.Text == "Reply"))
                throw new InvalidOperationException("The active phone thread must show the reply once and reconcile the submitted message.");
            section.RequestClear(); CheckInitial();
            var beforeOpen = calls;
            var contact = _services.Contacts.Book.People.Single(p => _services.Contacts.Book.Name(p) == "Phone friend ☕");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            var phoneCard = FacebookAvatars.Children.OfType<Button>().Single(c => AutomationProperties.GetName(c) == "Phone friend ☕");
            await WaitForUpdateUiAsync(() => phoneCard.XamlRoot is not null);
            ShowContactProfile(contact.Id, phoneCard);
            Button? viewConversation = null;
            await WaitForUpdateUiAsync(() => ContactProfile.IsOpen &&
                (viewConversation = Descendants((FrameworkElement)ContactProfile.Content).OfType<Button>()
                    .FirstOrDefault(button => button.Content as string == "View conversation")) is not null);
            await InvokeAsync(viewConversation!);
            await WaitForUpdateUiAsync(() => section.HasContent && CountMessage(reply) == 1);
            if (calls != beforeOpen || explicitReads != 1 || unexpectedRead)
                throw new InvalidOperationException("Viewing a phone conversation must load that contact's existing replies without sending.");
            section.RequestClear(); CheckInitial();
            messages = null;
            using (var unknownService = new PhoneLinkSection("unknown-service-check", (_, _) => Task.FromResult(new PhoneLinkMessageResult("submitted")),
                (_, _) => Task.FromResult<IReadOnlyList<PhoneLinkConversationMessage>?>(null),
                _ => Task.FromResult(false), _ => { }))
            {
                await unknownService.SubmitAsync("Synthetic unknown-service message");
                if (!unknownService.WasSubmitted || unknownService.StatusText != "Message submitted.")
                    throw new InvalidOperationException("Phone Link submission must not be labelled as confirmed iMessage when the service is unknown.");
            }
            _services.SendPhoneLink = (_, _, _) => Task.FromResult(new PhoneLinkMessageResult("not-sent", "unavailable"));
            Composer.Text = "Keep this text when Phone Link cannot open";
            await LaunchDestinationAsync(first, Composer.Text);
            if (!section.StatusText.StartsWith("Open or update Phone Link", StringComparison.Ordinal) ||
                Composer.Text != "Keep this text when Phone Link cannot open")
                throw new InvalidOperationException("A failed Phone Link launch must keep the draft and offer recovery.");
            section.RequestClear(); CheckInitial();
            _services.SendPhoneLink = (_, _, _) => { calls++; return Task.FromResult(new PhoneLinkMessageResult("uncertain")); };
            Composer.Text = "Keep this draft until the uncertain send is reviewed";
            await LaunchDestinationAsync(first, Composer.Text);
            var callsBeforeRetry = calls;
            await LaunchDestinationAsync(first, Composer.Text);
            if (!section.RequiresReview || SendButton.IsEnabled || calls != callsBeforeRetry ||
                Composer.Text != "Keep this draft until the uncertain send is reviewed")
                throw new InvalidOperationException("Uncertain sends must preserve the draft and block repeat submission until review.");
            var clear = FindButton(section.View, "Clear");
            if (!clear.IsEnabled) throw new InvalidOperationException("Clear must stay enabled after an uncertain send has finished.");
            var uncertainText = Composer.Text;
            await InvokeAsync(clear);
            CheckInitial();
            if (calls != callsBeforeRetry || section.RequiresReview || !section.HasUnconfirmedMessages)
                throw new InvalidOperationException("Clear must hide the review and retain its duplicate guard for cache eviction.");
            Composer.Text = uncertainText.Replace("\n", "\r\n", StringComparison.Ordinal);
            await LaunchDestinationAsync(first, Composer.Text);
            if (!section.RequiresReview || !section.HasContent || calls != callsBeforeRetry || SendButton.IsEnabled ||
                !FindButton(section.View, "Clear").IsEnabled)
                throw new InvalidOperationException("Re-entering the uncertain text after Clear must reopen recovery without a duplicate send or another Clear lock.");
            section.ConfirmReviewed();
            if (section.RequiresReview || calls != callsBeforeRetry || !SendButton.IsEnabled)
                throw new InvalidOperationException("Review must allow a later explicit action without sending automatically.");
            section.RequestClear(); CheckInitial();
            _services.SendPhoneLink = (_, _, _) => { calls++; return Task.FromResult(new PhoneLinkMessageResult("uncertain")); };
            Composer.Text = "First unconfirmed message";
            await LaunchDestinationAsync(first, Composer.Text);
            section.RequestClear(); CheckInitial();
            _services.SendPhoneLink = (_, _, _) => { calls++; return Task.FromResult(new PhoneLinkMessageResult("not-sent", "unavailable")); };
            Composer.Text = "A different new message";
            await LaunchDestinationAsync(first, Composer.Text);
            if (section.RequiresReview || !FindButton(section.View, "Clear").IsEnabled || calls != callsBeforeRetry + 2)
                throw new InvalidOperationException("Clear must allow a different message without treating an earlier uncertain attempt as a permanent lock.");
            section.RequestClear(); Composer.Text = "First unconfirmed message";
            await LaunchDestinationAsync(first, Composer.Text);
            if (!section.RequiresReview || calls != callsBeforeRetry + 2)
                throw new InvalidOperationException("A different message must not erase the duplicate guard for the earlier uncertain text.");
            section.RequestClear(); CheckInitial();

            var readBeforeRetirement = explicitReads;
            var lateRead = new TaskCompletionSource<IReadOnlyList<PhoneLinkConversationMessage>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _services.ReadPhoneLinkConversation = (address, openIfNeeded, _) =>
            {
                if (!openIfNeeded) return Task.FromResult<IReadOnlyList<PhoneLinkConversationMessage>?>(null);
                unexpectedRead |= address != "+34600111222";
                return ++explicitReads == readBeforeRetirement + 1 ? lateRead.Task : Task.FromResult(messages);
            };
            var opening = section.OpenConversationAsync();
            SelectDestination(second);
            var retirement = _contactDestinationViews.RetireAsync(first);
            var reopening = ViewPhoneConversationAsync(contact.Id, "+34 600 111 222");
            try
            {
                if (retirement.IsCompleted || reopening.IsCompleted || _destination != second || unexpectedRead)
                    throw new InvalidOperationException("Reopening a retiring phone view must wait without reading the currently selected recipient.");
            }
            finally { lateRead.TrySetResult(null); }
            await Task.WhenAll(opening, retirement, reopening);
            if (_destination != first || explicitReads != readBeforeRetirement + 2 || unexpectedRead)
                throw new InvalidOperationException("Reopening must load only the requested contact after its previous view drains.");
            var reopened = (PhoneLinkSection)_destinationSections[first];
            Composer.Text = "First unconfirmed message";
            await LaunchDestinationAsync(first, Composer.Text);
            if (!reopened.RequiresReview || calls != callsBeforeRetry + 2)
                throw new InvalidOperationException("A re-created phone destination must still block a previously unconfirmed send.");
            reopened.ConfirmReviewed(); reopened.RequestClear();
            File.AppendAllText(log, "PASS: reopening a phone contact waits for the retired view, reads the requested recipient and keeps the duplicate guard.\n");
            File.AppendAllText(log, "PASS: Phone Link exact recipient/text and one send, live and existing replies without duplicate messages, Clear restores the centered editor, and uncertain text remains guarded. All real reads and sends intercepted.\n");

            int CountMessage(string text) => Descendants(section.View).OfType<TextBlock>().Count(t => t.Text == text);

            void CheckInitial()
            {
                if (!IsPhoneContactDestination(_destination) || _facebookPanelOpen || ContactProfile.IsOpen || Composer.Text != "" ||
                    ComposerHint.Text != "Message Phone friend ☕…" || SendButton.Content as string != "Send" ||
                    _destinationSections[_destination].HasContent || _destinationContent.Visibility != Visibility.Collapsed ||
                    Composer.TextAlignment != TextAlignment.Center || ContactMessageName.Text != "Phone friend ☕" ||
                    SelectedContactChannelLabel != "iMessage" || ContactChannelPicker.Visibility != Visibility.Visible || ContactMessageHeader.Visibility != Visibility.Visible ||
                    AutomationProperties.GetName(Composer) != ComposerHint.Text)
                    throw new InvalidOperationException("A phone contact must start/reset to the shared centered writing view and English Phone Link action without result controls.");
            }
        }
        finally
        {
            pending.TrySetResult(new("not-sent"));
            _services.SendPhoneLink = send; _services.ReadPhoneLinkConversation = read; _services.KeepPhoneLinkBackground = focus;
            SetFacebookOpen(false); SelectDestination(destination); Composer.Text = draft;
            await _services.Contacts.ChangeAsync(_ => original);
            foreach (var id in _phoneRecipients.Keys.Where(id => !previousIds.Contains(id)).ToArray())
            {
                await _contactDestinationViews.RetireAsync(id);
            }
        }
    }
    private sealed class CheckedPhoneFocus(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
#endif
