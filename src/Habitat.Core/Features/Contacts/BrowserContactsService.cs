using System.Text.Json;
using Habitat.Features.Facebook;

namespace Habitat.Features.Contacts;

// Null adopts the user's existing Chrome permission. Explicit Off always wins.
internal sealed record BrowserContactSettings(bool? Facebook = null, bool? Google = null)
{
    internal bool Allows(string provider) => provider == "facebook" ? Facebook != false : Google != false;
}
internal sealed record BrowserContactRow(string? Id, string Name, string? Email = null, string? Phone = null,
    string? PhotoUrl = null, string? ProfileUrl = null, string? Page = null);
internal sealed record BrowserContactSnapshot(string Provider, string AccountId, string AccountName, BrowserContactRow[] Contacts);
internal sealed record BrowserContactState(string Provider, bool Allowed, bool Open, string State, int Count = 0);
internal sealed record BrowserContactSetup(string Id, string Provider);

// Shares the authenticated extension connection; names and addresses never enter
// connection diagnostics. Queued snapshots are consumed on the desktop UI thread.
internal sealed class BrowserContactsService(TimeProvider? time = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Queue<BrowserContactSnapshot> _pending = new();
    private object? _owner;
    private bool _supported;
    private DateTimeOffset _seen;
    private BrowserContactSettings _settings = new();
    private BrowserContactState[] _states = [];
    private BrowserContactSetup? _setup;
    internal BrowserContactSettings Settings { get { lock (_gate) return _settings; } }
    internal bool Connected { get { lock (_gate) return _owner is not null && _time.GetUtcNow() - _seen < TimeSpan.FromSeconds(10); } }
    internal bool Supported { get { lock (_gate) return Connected && _supported; } }

    internal void Configure(BrowserContactSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            var keep = _pending.Where(s => settings.Allows(s.Provider)).ToArray();
            _pending.Clear(); foreach (var item in keep) _pending.Enqueue(item);
        }
    }
    internal void Attach(object owner, bool supported)
    {
        lock (_gate) { _owner = owner; _supported = supported; _seen = _time.GetUtcNow(); _states = []; _pending.Clear(); }
    }
    internal void Touch(object owner) { lock (_gate) if (ReferenceEquals(owner, _owner)) _seen = _time.GetUtcNow(); }
    internal void Detach(object owner)
    {
        lock (_gate) if (ReferenceEquals(owner, _owner)) { _owner = null; _states = []; _pending.Clear(); }
    }
    internal object? Tick(object owner)
    {
        lock (_gate) return ReferenceEquals(owner, _owner) && _supported
            ? new { facebook = _settings.Allows("facebook"), google = _settings.Allows("google"), setup = _setup } : null;
    }
    internal void RequestSetup(string provider)
    {
        if (provider is not ("facebook" or "google")) throw new ArgumentException("Unknown contact provider.");
        lock (_gate) _setup = new(ContactBook.NewId(), provider);
    }
    internal BrowserContactState StateFor(string provider)
    {
        lock (_gate)
        {
            if (!_settings.Allows(provider)) return new(provider, false, false, "disabled");
            if (!Connected) return new(provider, false, false, "disconnected");
            if (!_supported) return new(provider, false, false, "extension-update");
            return _states.FirstOrDefault(s => s.Provider == provider) ?? new(provider, false, false, "waiting");
        }
    }
    internal bool TryTake(out BrowserContactSnapshot? snapshot)
    {
        lock (_gate)
        {
            while (_pending.TryDequeue(out var next))
                if (_settings.Allows(next.Provider)) { snapshot = next; return true; }
            snapshot = null; return false;
        }
    }
    internal void Accept(object owner, JsonElement packet, JsonSerializerOptions json)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner) || !_supported) return;
            if (packet.TryGetProperty("setupDone", out var setup) && setup.ValueKind == JsonValueKind.String && setup.GetString() == _setup?.Id) _setup = null;
            if (packet.TryGetProperty("states", out var states))
            {
                var parsed = states.Deserialize<BrowserContactState[]>(json);
                if (parsed is not null && parsed.Length <= 2 && parsed.All(s => s is not null &&
                    s.Provider is "facebook" or "google" && s.Count is >= 0 and <= 10000 &&
                    s.State is "permission" or "waiting-for-tab" or "waiting-for-contacts" or "account-unavailable" or "ready" or "login" or "disabled" or "reading"))
                    _states = parsed;
            }
            if (!packet.TryGetProperty("snapshots", out var snapshots)) return;
            var incoming = snapshots.Deserialize<BrowserContactSnapshot[]>(json);
            if (incoming is null || incoming.Length > 8) return;
            foreach (var snapshot in incoming)
            {
                if (!Valid(snapshot) || !_settings.Allows(snapshot.Provider)) continue;
                if (_pending.Count >= 32) _pending.Dequeue();
                _pending.Enqueue(snapshot);
            }
        }
    }
    private static bool Valid(BrowserContactSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Provider is not ("facebook" or "google") ||
            snapshot.AccountName is not { Length: > 0 and <= 200 } || snapshot.AccountId is not { Length: > 0 and <= 2048 } ||
            snapshot.Contacts is not { Length: <= 200 }) return false;
        if (snapshot.Provider == "google" && !ContactChannels.IsEmail(snapshot.AccountId)) return false;
        if (snapshot.Provider == "facebook" && snapshot.AccountId != "unidentified" &&
            (!FacebookContactsService.SafeLink(snapshot.AccountId) || snapshot.AccountId.Contains("/messages/", StringComparison.Ordinal))) return false;
        return snapshot.Contacts.All(r => r is not null && r.Name is { Length: > 0 and <= 200 } &&
            (r.Id is null || r.Id.Length <= 512) && (r.Email is null || ContactChannels.IsEmail(r.Email)) &&
            (r.Phone is null || r.Phone.Length is > 0 and <= 100) &&
            (r.PhotoUrl is null || FacebookContactsService.SafePhoto(r.PhotoUrl)) &&
            (r.ProfileUrl is null || FacebookContactsService.SafeLink(r.ProfileUrl)) &&
            r.Page is null or "Gmail" or "Google Contacts" or "Facebook");
    }

    internal static ContactSource Source(BrowserContactSnapshot snapshot) => new(
        ContactBook.Hash("browser:" + snapshot.Provider + ":" + snapshot.AccountId.ToUpperInvariant())[..32].ToLowerInvariant(),
        snapshot.Provider == "facebook" ? "facebook" : "google-browser",
        (snapshot.Provider == "facebook" ? "Facebook · " : "Google · ") + snapshot.AccountName,
        snapshot.AccountId, true);

    internal static ContactBook Apply(ContactBook book, BrowserContactSnapshot snapshot)
    {
        var source = Source(snapshot);
        if (book.Sources.FirstOrDefault(s => s.Kind == source.Kind && s.Account.Equals(source.Account, StringComparison.OrdinalIgnoreCase)) is { } known)
            source = source with { Id = known.Id };
        var candidates = new Dictionary<string, ContactCandidate>();
        foreach (var row in snapshot.Contacts)
        {
            if (row.Email is not null && ContactChannels.IsNoReply(row.Email)) continue;
            string remote;
            if (snapshot.Provider == "facebook") remote = row.ProfileUrl ?? "snapshot:" + ContactBook.Hash(row.Name + "\n" + row.PhotoUrl);
            else
            {
                var stableId = string.IsNullOrEmpty(row.Id) ? null : "contact:" + row.Id;
                var knownId = stableId is null ? null : book.Records.FirstOrDefault(r => r.SourceId == source.Id &&
                    (r.RemoteId == stableId || r.Current.Values.Contains(new("other", row.Id!, "Google Contacts ID"))));
                var byEmail = row.Email is null ? [] : book.Records.Where(r => r.SourceId == source.Id && r.Current.Values.Any(v =>
                    v.Kind == "email" && ContactBook.NormalizeEmail(v.Value) == ContactBook.NormalizeEmail(row.Email))).ToArray();
                remote = knownId?.RemoteId ?? (byEmail.Length == 1 ? byEmail[0].RemoteId : stableId ??
                    (row.Email is not null ? "email:" + ContactBook.NormalizeEmail(row.Email) : "snapshot:" + ContactBook.Hash(row.Name + "\n" + row.Phone)));
            }
            var values = new List<ContactValue> { new("name", row.Name) };
            if (row.Email is not null) values.Add(new("email", row.Email));
            if (row.Phone is not null) values.Add(new("phone", row.Phone));
            if (snapshot.Provider == "google" && !string.IsNullOrEmpty(row.Id)) values.Add(new("other", row.Id, "Google Contacts ID"));
            if (row.PhotoUrl is not null) values.Add(new("photo", row.PhotoUrl));
            if (row.ProfileUrl is not null) { values.Add(new("profile", row.ProfileUrl)); values.Add(new("messenger", row.ProfileUrl)); }
            values.Add(new("other", row.Page ?? (snapshot.Provider == "facebook" ? "Facebook" : "Gmail"), "Source page"));
            // Browser lists are partial observations. A later Gmail inbox row
            // must not erase a phone number previously seen in Google Contacts.
            var id = ContactBook.RecordId(source.Id, remote);
            if (book.Records.FirstOrDefault(r => r.Id == id) is { } previous)
                values.AddRange(previous.Current.Values.Where(v => v.Kind != "name" && (v.Kind != "photo" || row.PhotoUrl is null)));
            if (candidates.TryGetValue(remote, out var prior)) values.AddRange(prior.Values.Where(v => v.Kind != "name"));
            var fields = values.Distinct().OrderBy(v => v.Kind, StringComparer.Ordinal).ThenBy(v => v.Value, StringComparer.Ordinal)
                .ThenBy(v => v.Label, StringComparer.Ordinal).ToArray();
            candidates[remote] = new(remote, fields, JsonSerializer.Serialize(new { provider = snapshot.Provider, account = snapshot.AccountId, fields }));
        }
        var existing = book.Sources.FirstOrDefault(s => s.Id == source.Id);
        var changed = candidates.Values.Any(c => book.Records.FirstOrDefault(r => r.Id == ContactBook.RecordId(source.Id, c.RemoteId))?.Current.Raw != c.Raw);
        if (!changed && existing?.Label == source.Label && existing.Connected) return book.DeduplicateFacebookProfiles();
        return (candidates.Count > 0 ? book.Import(source, candidates.Values.ToArray()) : existing is null ? book.WithSource(source) : book)
            .DeduplicateFacebookProfiles();
    }
}
