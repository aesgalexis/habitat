#if HABITAT_UI_CHECKS
using Habitat.Features.Composer;
using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckContactSectionLifetimeAsync(string log)
    {
        var book = ContactBook.Empty.SaveContact([new("name", "Synthetic lifecycle"), new("messenger", "https://www.facebook.com/synthetic.lifecycle")]);
        var state = new ContactDestinationSession();
        var id = state.RegisterMessenger(new("Synthetic lifecycle", "", "https://www.facebook.com/synthetic.lifecycle"), null, book.People.Single().Id);
        var drafts = new ComposerDrafts();
        drafts.Isolate(id); drafts.Edit(id, "Unsent text must survive retirement 雪");
        var composer = new ComposerCoordinator(drafts, () => { });
        var result = new TaskCompletionSource<FacebookMessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        using var section = new MessengerSection(id, (_, _) => { sends++; return result.Task; });
        var sections = new Dictionary<string, IDestinationSection> { [id] = section };
        var views = new ContactDestinationViews(state, sections, composer, drafts, exception => throw exception);
        composer.Register(id, false, request => section.SubmitAsync(request.Text), _ => section.IsBusy);
        var running = section.SubmitAsync("First submission");
        await section.SubmitAsync("Duplicate while busy");
        if (sends != 1) throw new InvalidOperationException("A duplicate Messenger request must not submit again.");
        var callbacks = 0;
        section.StateChanged += () => callbacks++;
        var retirement = views.RetireAsync(id);
        if (retirement.IsCompleted || !ReferenceEquals(retirement, views.RetireAsync(id)) || state.Contains(id) ||
            sections.ContainsKey(id) || composer.CanSend(id, "retry"))
            throw new InvalidOperationException("Retirement must disable routes immediately and wait for the original provider, even after a duplicate call.");
        result.SetResult(new("submitted"));
        await running; await retirement;
        if (state.Messenger.ContainsKey(id) || callbacks != 0 || drafts.Text(id) != "Unsent text must survive retirement 雪")
            throw new InvalidOperationException("Retired controls must release recipient state, ignore late callbacks and retain unsent text.");
        var stop = section.StopAsync();
        if (!ReferenceEquals(stop, section.StopAsync())) throw new InvalidOperationException("Messenger shutdown must be idempotent.");
        await stop; await views.StopAsync();
        section.Dispose();
        var draftResult = new TaskCompletionSource<Habitat.Models.GmailDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gmail = new GmailDraftSection((_, _) => draftResult.Task, () => Task.CompletedTask,
            () => "synthetic@example.com", _ => Task.FromResult(true), _ => { });
        var saving = gmail.SubmitAsync("Original draft");
        await gmail.SubmitAsync("Duplicate draft");
        var gmailStop = gmail.StopAsync();
        if (gmailStop.IsCompleted || !ReferenceEquals(gmailStop, gmail.StopAsync()))
            throw new InvalidOperationException("Gmail shutdown must retain the original provider after a duplicate request.");
        draftResult.SetResult(new("synthetic", "Late subject", "synthetic@example.com"));
        await saving; await gmailStop;
        if (gmail.IsBusy || gmail.StatusText.Contains("Late subject"))
            throw new InvalidOperationException("A stopped Gmail section must ignore late saves and release its busy state.");
        var phoneResult = new TaskCompletionSource<PhoneLinkMessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var phone = new PhoneLinkSection("synthetic-lifetime", (_, _) => phoneResult.Task,
            (_, _) => Task.FromResult<IReadOnlyList<PhoneLinkConversationMessage>?>(null), _ => Task.FromResult(false), _ => { });
        var sendingPhone = phone.SubmitAsync("Original phone message");
        await phone.SubmitAsync("Duplicate phone message");
        var phoneStop = phone.StopAsync();
        if (phoneStop.IsCompleted || !ReferenceEquals(phoneStop, phone.StopAsync()))
            throw new InvalidOperationException("Phone Link shutdown must retain the original provider after a duplicate request.");
        phoneResult.SetResult(new("submitted"));
        await sendingPhone; await phoneStop;
        if (phone.IsBusy || phone.WasSubmitted || !phone.HasUnconfirmedMessages)
            throw new InvalidOperationException("A stopped Phone Link section must retain the duplicate guard without applying a late result to the view.");
        File.AppendAllText(log, "PASS: contact retirement disables routes, drains duplicate/late Messenger submissions, releases controls and preserves unsent drafts; repeated shutdown is idempotent\n");
    }
}
#endif
