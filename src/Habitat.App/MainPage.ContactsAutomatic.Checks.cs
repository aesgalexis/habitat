#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Habitat.Features.Preferences;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckAutomaticContactsUiAsync(string log)
    {
        await _services.Contacts.LoadAsync();
        var original = _services.Contacts.Book;
        var preferences = _preferences;
        var draft = Composer.Text;
        var destination = _destination;
        var browser = _services.Bridge.BrowserContacts;
        var owner = new object();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Task? showing = null;
        void Push(string provider, string account, string name)
        {
            var person = provider == "facebook" ? new BrowserContactRow(null, name,
                ProfileUrl: "https://www.facebook.com/automatic.friend", PhotoUrl: "https://scontent.fbcdn.net/auto.jpg", Page: "Facebook") :
                new BrowserContactRow(null, name, "automatic@example.com", Page: "Gmail");
            using var packet = JsonDocument.Parse(JsonSerializer.Serialize(new { states = new[] {
                new BrowserContactState(provider, true, true, "ready", 1) }, snapshots = new[] {
                new BrowserContactSnapshot(provider, account, account, [person]) } }, json));
            browser.Accept(owner, packet.RootElement, json);
        }
        try
        {
            browser.Configure(new(Facebook: true, Google: true)); browser.Attach(owner, true);
            Composer.Text = "Automatic agenda keeps my draft ☕";
            var kept = Composer.Text;
            Push("facebook", "https://www.facebook.com/automatic.owner", "Automatic friend ☕");
            TickBrowserContacts();
            await WaitForUpdateUiAsync(() => !_browserContactUpdating);
            if (_services.Contacts.Book.People.Length != 1 || _services.Contacts.Book.Sources.Single().Kind != "facebook")
                throw new InvalidOperationException("An enabled open source must save contacts automatically without opening settings or requesting an import.");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookAvatars.Children.Count != 1 || Composer.Text != kept || _destination != destination)
                throw new InvalidOperationException("The automatically populated agenda must preserve the draft and destination.");
            var revision = _services.Contacts.Book.Revision;
            Push("facebook", "https://www.facebook.com/automatic.owner", "Automatic friend ☕");
            TickBrowserContacts(); await WaitForUpdateUiAsync(() => !_browserContactUpdating);
            if (_services.Contacts.Book.Revision != revision)
                throw new InvalidOperationException("Unchanged browser observations must not rewrite contact history.");
            showing = ShowPreferencesAsync("Contacts");
            await WaitForUpdateUiAsync(() => _activeDialog?.Content is PreferencesView);
            var view = ((PreferencesView)_activeDialog!.Content).Contacts!;
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<ToggleSwitch>().Count() == 2);
            if (Descendants(view).OfType<TextBox>().Any(t => t.Header as string is "Facebook account name" or "Import source name"))
                throw new InvalidOperationException("The normal contact source flow must show automatic switches without account naming or import forms.");
            var facebook = Descendants(view).OfType<ToggleSwitch>().Single(t => t.Header as string == "Facebook");
            if (!facebook.IsOn) throw new InvalidOperationException("An active browser permission must be reflected by the source toggle.");
            facebook.IsOn = false; await Task.Delay(100);
            await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_preferences.BrowserContacts.Facebook != false || Preferences.Load(_services.Paths.PreferencesPath).BrowserContacts.Facebook != false)
                throw new InvalidOperationException("Turning a source off must be persisted immediately.");
            Push("facebook", "https://www.facebook.com/automatic.owner", "Blocked update");
            TickBrowserContacts(); await Task.Delay(40);
            if (_services.Contacts.Book.Name(_services.Contacts.Book.People.Single()) != "Automatic friend ☕")
                throw new InvalidOperationException("An off source must keep saved contacts and reject later browser updates.");
            Push("google", "owner@example.com", "Automatic email friend");
            TickBrowserContacts(); await WaitForUpdateUiAsync(() => !_browserContactUpdating);
            if (_services.Contacts.Book.People.Length != 2 || !_services.Contacts.Book.Sources.Any(s => s.Kind == "google-browser"))
                throw new InvalidOperationException("A separate enabled Gmail source must continue updating while Facebook is off.");
            _activeDialog.Hide(); await showing; showing = null;
            if (Composer.Text != kept || _destination != destination)
                throw new InvalidOperationException("Automatic contact updates must preserve the editor through settings changes.");
            File.AppendAllText(log, "PASS: automatic Facebook and Google browser sources; no import preview; immediate persisted Off; late observations rejected; unchanged scans avoid writes; source provenance and editor preserved. Synthetic snapshots only.\n");
        }
        finally
        {
            _activeDialog?.Hide(); if (showing is not null) await showing;
            SetFacebookOpen(false); browser.Detach(owner);
            _preferences = preferences; SavePreferences(); browser.Configure(preferences.BrowserContacts);
            await _services.Contacts.ChangeAsync(_ => original);
            SelectDestination(destination); Composer.Text = draft;
        }
    }
}
#endif
