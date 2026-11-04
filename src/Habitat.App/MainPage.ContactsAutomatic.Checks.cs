#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Habitat.Features.Preferences;
using Microsoft.UI.Xaml;
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
        var browser = _services.Bridge.BrowserContacts;
        var owner = new object();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Task? showing = null;
        try
        {
            await _services.Contacts.ChangeAsync(_ => ContactBook.Empty);
            _preferences = _preferences with { BrowserContacts = new(true, true) };
            browser.Configure(new(true, true)); browser.Attach(owner, true);
            using var packet = JsonDocument.Parse(JsonSerializer.Serialize(new {
                snapshots = new[] {
                    new BrowserContactSnapshot("facebook", "old-facebook-account", "Old account", [
                        new BrowserContactRow(null, "Must never become a contact", ProfileUrl: "https://www.facebook.com/automatic.friend")]),
                    new BrowserContactSnapshot("google", "old@example.com", "Old Google", [
                        new BrowserContactRow(null, "Must never become a suggestion", "old.friend@example.com")]) }
            }, json));
            browser.Accept(owner, packet.RootElement, json);
            TickBrowserContacts();
            await Task.Delay(60);
            if (_services.Contacts.Book.People.Length != 0 || _services.Contacts.Book.Sources.Length != 0 ||
                _services.Contacts.Book.BrowserSuggestions.Length != 0 || browser.Settings.Allows("facebook") || browser.Settings.Allows("google") ||
                browser.TryTake(out _) || SetBrowserContactSource("facebook", true))
                throw new InvalidOperationException("Legacy enabled permissions, pending snapshots and old settings must never populate the manual address book.");
            browser.Accept(owner, packet.RootElement, json);
            if (browser.TryTake(out _)) throw new InvalidOperationException("Late observations must be rejected.");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookAvatars.Children.Count != 0 || ContactsEnableGoogle.Visibility != Visibility.Collapsed ||
                ContactsDuplicates.Visibility != Visibility.Collapsed || ContactsAdd.Parent is null ||
                !FacebookNoMatches.Text.Contains("Add contact"))
                throw new InvalidOperationException("The empty sidebar must offer manual creation without source or duplicate actions.");
            showing = ShowPreferencesAsync("Contacts");
            await WaitForUpdateUiAsync(() => _activeDialog?.Content is PreferencesView);
            var view = ((PreferencesView)_activeDialog!.Content).Contacts!;
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<Button>().Any(b => b.Content as string == "Add contact"));
            if (Descendants(view).OfType<ToggleSwitch>().Any() || Descendants(view).OfType<ComboBox>().Any() ||
                Descendants(view).OfType<Button>().Any(b => (b.Content as string)?.Contains("Google") == true))
                throw new InvalidOperationException("Contacts preferences must expose manual contacts only.");
            _activeDialog.Hide(); await showing; showing = null;
            await _services.Contacts.LoadAsync();
            if (_services.Contacts.Book.People.Length != 0) throw new InvalidOperationException("Reloading must keep the untouched manual agenda empty.");
            File.AppendAllText(log, "PASS: manual agenda remains empty with legacy browser permissions/settings, queued and late Facebook/Google snapshots; automatic sources and import actions are absent.\n");
        }
        finally
        {
            _activeDialog?.Hide(); if (showing is not null) await showing;
            SetFacebookOpen(false); browser.Detach(owner);
            _preferences = preferences; SavePreferences(); browser.Configure(new(false, false));
            await _services.Contacts.ChangeAsync(_ => original);
        }
    }
}
#endif