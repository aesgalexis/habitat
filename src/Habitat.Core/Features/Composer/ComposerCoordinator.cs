using Habitat.Services;

namespace Habitat.Features.Composer;

// Routes are composed by the desktop host. Admission, exclusive launchers and
// draft ownership are independent of controls and can be exercised headlessly.
internal sealed class ComposerCoordinator(ComposerDrafts drafts, Action draftChanged)
{
    private sealed record Route(Func<Submission, Task> Execute, Func<string, bool> IsBusy, bool Exclusive, bool ExclusiveForEmpty, bool AllowEmpty);
    private readonly Dictionary<(string, bool), Route> _routes = new();
    public bool IsExclusiveBusy { get; private set; }
    public bool IsStopping { get; private set; }
    public event Action? StateChanged;
    public Func<string, bool> IsEnabled { get; set; } = _ => true;

    public void Register(string destination, bool external, Func<Submission, Task> execute,
        Func<string, bool> isBusy, bool exclusive = false, bool exclusiveForEmpty = false, bool allowEmpty = false) =>
        _routes[(destination, external)] = new(execute, isBusy, exclusive, exclusiveForEmpty, allowEmpty);

    public bool CanSend(string destination, string text, bool external = false) =>
        !IsStopping && !IsExclusiveBusy && IsEnabled(destination) &&
        _routes.TryGetValue((destination, external), out var route) && !route.IsBusy(text);

    public async Task SubmitAsync(string destination, string text, bool external = false)
    {
        if (!CanSend(destination, text, external)) return;
        var route = _routes[(destination, external)];
        if (!external && !route.AllowEmpty && string.IsNullOrWhiteSpace(text)) return;
        var exclusive = route.Exclusive || route.ExclusiveForEmpty && string.IsNullOrWhiteSpace(text);
        var submission = new Submission(destination, text, external, drafts.Capture(destination), drafts, draftChanged, () => !IsStopping);
        if (exclusive) { IsExclusiveBusy = true; StateChanged?.Invoke(); }
        try { await route.Execute(submission); }
        finally
        {
            if (exclusive) { IsExclusiveBusy = false; StateChanged?.Invoke(); }
        }
    }

    public void Stop() { IsStopping = true; StateChanged?.Invoke(); }

    public static string OpenAIText(string text, bool textOnly) => textOnly
        ? text.Trim() + "\n\n[HABITAT: Reply in plain text unless asked otherwise.]" : text.Trim();

    internal sealed class Submission(string destination, string text, bool external,
        ComposerDrafts.Submission draft, ComposerDrafts drafts, Action changed, Func<bool> canProceed)
    {
        public string Destination { get; } = destination;
        public string Text { get; } = text;
        public bool External { get; } = external;
        public ComposerDrafts.Submission Draft { get; } = draft;
        public bool CanProceed => canProceed();
        public void Accept() { drafts.Accept(Draft); changed(); }
        public void Clear() { drafts.Clear(Draft); changed(); }
    }
}
