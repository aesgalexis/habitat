using Habitat.Features.Composer;
using Habitat.Features.Contacts;
using Habitat.Services;

namespace Habitat.Features.Destinations;

// The desktop half of contact lifetime. Removal disables dispatch immediately,
// then drains the old provider before releasing its recipient and controls.
internal sealed class ContactDestinationViews(ContactDestinationSession contacts,
    Dictionary<string, IDestinationSection> sections, ComposerCoordinator composer,
    ComposerDrafts drafts, Action<Exception> report)
{
    private readonly Dictionary<string, Task> _retiring = new();
    private readonly Dictionary<string, string[]> _phoneReviews = new();
    private readonly AsyncOperationScope _operations = new();
    internal bool IsRetiring(string id) => _retiring.ContainsKey(id);
    internal Task WaitForRetirementAsync(string id) => _retiring.GetValueOrDefault(id, Task.CompletedTask);
    internal string[] TakePhoneReview(string id) => _phoneReviews.Remove(id, out var review) ? review : [];

    internal Task RetireAsync(string id)
    {
        if (_operations.IsStopping) return Task.CompletedTask;
        if (_retiring.TryGetValue(id, out var pending)) return pending;
        contacts.Invalidate(id);
        composer.Unregister(id);
        sections.Remove(id, out var section);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retiring[id] = completion.Task;
        _ = _operations.RunAsync(async () =>
        {
            try { if (section is not null) await section.StopAsync(); }
            catch (Exception exception) { report(exception); }
            finally
            {
                try { section?.Dispose(); }
                catch (Exception exception) { report(exception); }
                finally
                {
                    if (section is PhoneLinkSection { HasUnconfirmedMessages: true } phone)
                        _phoneReviews[id] = phone.CopyUnconfirmedMessages();
                    contacts.Forget(id);
                    drafts.Retire(id);
                    _retiring.Remove(id);
                    completion.TrySetResult();
                }
            }
        });
        return completion.Task;
    }

    internal void Trim(string selected)
    {
        contacts.Touch(selected);
        foreach (var id in contacts.EvictionCandidates(selected, id => drafts.Text(id).Length > 0 ||
            sections.GetValueOrDefault(id) is { IsBusy: true } or PhoneLinkSection { HasUnconfirmedMessages: true }))
            _ = RetireAsync(id);
    }

    internal Task StopAsync() => _operations.StopAsync();
}
