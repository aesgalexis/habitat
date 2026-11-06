using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Habitat.Features.Contacts;
using Habitat.Models;
using Habitat.Services;
using Microsoft.AspNetCore.WebUtilities;

internal static class DriveContactChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var sample = new[]
        {
            new ContactValue("name", "Ángela, \"A\""),
            new ContactValue("phone", "+34 600 123 456", "'Personal"),
            new ContactValue("note", "first\nsecond\r\nthird"),
            new ContactValue("other", "=2+2")
        };
        var id = ContactBook.NewId();
        var csv = ContactDriveCsv.Encode(new Dictionary<string, ContactValue[]> { [id] = sample });
        check(ContactDriveCsv.Decode(csv)[id].SequenceEqual(sample), "CSV must round-trip Unicode, multiline notes, quotes and spreadsheet escapes.");
        check(Encoding.UTF8.GetString(csv).Contains("\"'=2+2\""), "Potential spreadsheet formulas must be escaped.");

        var handler = new DriveHandler();
        using var http = new HttpClient(handler);
        var first = new ContactSession(new ContactStore(Path.Combine(root, "drive-first.json")));
        var second = new ContactSession(new ContactStore(Path.Combine(root, "drive-second.json")));
        var firstState = Path.Combine(root, "drive-first-link.json");
        var bundled = new GmailClientConfiguration("drive.apps.googleusercontent.com", "test-secret");
        var otherClient = new GmailClientConfiguration("other.apps.googleusercontent.com", "test-secret");
        var signIns = 0;
        using var callback = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        using var syncFirst = new ContactDriveSync(first, firstState, Auth, http, otherClient);
        using var syncSecond = new ContactDriveSync(second, Path.Combine(root, "drive-second-link.json"), FreshAuth, http, bundled);
        GmailAuthService Auth() => new(new Credentials(), http,
            _ => throw new InvalidOperationException("A saved client should be reused without opening the browser."), driveOnly: true);
        GmailAuthService FreshAuth() => new(new Credentials(fresh: true), http, async url =>
        {
            signIns++;
            var query = QueryHelpers.ParseQuery(new Uri(url).Query);
            check(query["client_id"] == bundled.ClientId && query["code_challenge_method"] == "S256",
                "The normal connect action must start Google sign-in with the bundled desktop client and PKCE.");
            var redirect = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?>
            {
                ["state"] = query["state"].ToString(), ["code"] = "synthetic-code"
            });
            using var response = await callback.GetAsync(redirect);
            response.EnsureSuccessStatusCode();
        }, driveOnly: true);
        await first.LoadAsync();
        await first.ChangeAsync(book => book.SaveContact(sample));
        await syncFirst.ConnectAsync(default);
        check(syncFirst.Connected && syncFirst.HasLink, "The bundled client must not replace a previously configured custom client: " + syncFirst.Status);
        check(syncSecond.CanConnect && !syncSecond.Connected, "A fresh install with a bundled client can connect without choosing a file.");
        await syncSecond.ConnectAsync(default);
        check(signIns == 1 && syncSecond.Connected, "The fresh PC must connect through one Google sign-in.");
        var person = first.Book.People.Single();
        check(second.Book.People.Single().Id == person.Id && second.Book.For(second.Book.People.Single())[0].Current.Values.SequenceEqual(sample),
            "Connecting a second PC must restore details and preserve the contact identifier.");
        check(handler.VisibleName == ContactDriveClient.FileName, "Google Drive must expose a readable contacts CSV in My Drive.");

        await first.ChangeAsync(book => book.SaveContact([new("name", "Local addition")]));
        await second.ChangeAsync(book => book.SaveContact([new("name", "Other PC addition")]));
        handler.RaceNextAppends();
        await Task.WhenAll(syncFirst.SyncAsync(default), syncSecond.SyncAsync(default));
        await syncFirst.SyncAsync(default);
        await syncSecond.SyncAsync(default);
        check(handler.RejectedAppends == 1 && first.Book.People.Length == 3 && second.Book.People.Length == 3 &&
            ContactDriveCsv.Decode(handler.VisibleBytes).Count == 3,
            "Concurrent revision creation must retry the losing writer and keep both additions in the CSV.");

        await first.ChangeAsync(book => book.SaveContact([new("name", "First edit")], person.Id));
        await second.ChangeAsync(book => book.SaveContact([new("name", "Second edit")], person.Id));
        await syncFirst.SyncAsync(default);
        await syncSecond.SyncAsync(default);
        check(syncSecond.HasConflict && second.Book.Name(second.Book.People.Single(p => p.Id == person.Id)) == "Second edit",
            "Concurrent edits to one contact must keep the unsynced local value and report a conflict.");
        await syncSecond.SyncAsync(default, ContactDriveConflictChoice.Local);
        await syncFirst.SyncAsync(default);
        check(first.Book.Name(first.Book.People.Single(p => p.Id == person.Id)) == "Second edit", "Choosing a conflict version must propagate it.");

        await second.ChangeAsync(book => book.RemoveContact(person.Id));
        await syncSecond.SyncAsync(default);
        using var reopened = new ContactDriveSync(first, firstState, Auth, http);
        await reopened.SyncAsync(default);
        check(first.Book.People.All(p => p.Id != person.Id) && !ContactDriveCsv.Decode(handler.VisibleBytes).ContainsKey(person.Id),
            "Deletions must survive restarts and disappear from the current CSV.");

        handler.Owner = "another-account";
        var uploads = handler.Writes;
        await first.ChangeAsync(book => book.SaveContact([new("name", "Private local contact")]));
        await reopened.SyncAsync(default);
        check(reopened.NeedsAttention && handler.Writes == uploads, "A different Google account must never receive contacts linked to another account.");

        var scopes = Uri.UnescapeDataString(GmailAuthService.AuthorizationUrl("desktop", "http://localhost", "state", "challenge", driveOnly: true));
        check(scopes.Contains(GmailAuthService.DriveFileScope + " " + GmailAuthService.DriveAppDataScope) &&
            !scopes.Contains(GmailAuthService.ReadOnlyScope) && !scopes.Contains(GmailAuthService.ContactsScope),
            "Drive authorization must be isolated from Gmail and Google Contacts scopes.");
    }

    private sealed class Credentials(bool fresh = false) : IGmailCredentialStore
    {
        private GmailAccount _account = fresh ? new(null, null, null) : new(new("drive.apps.googleusercontent.com", "test-secret"),
            new("access", "refresh", DateTimeOffset.UtcNow.AddHours(1)), "owner@example.test");
        public GmailAccount Load() => _account;
        public void Save(GmailAccount account) => _account = account;
    }

    private sealed class DriveHandler : DriveMemoryHandler { }
}
