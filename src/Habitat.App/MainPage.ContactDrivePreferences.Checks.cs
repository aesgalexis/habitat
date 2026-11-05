#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Habitat.Features.Preferences;
using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckContactDrivePreferencesUiAsync(string log)
    {
        await _services.Contacts.LoadAsync();
        using var http = new HttpClient(new DrivePreferencesNoNetwork());
        var link = Path.Combine(_services.Paths.LocalDirectory, "drive-preferences-link.json");
        foreach (var linked in new[] { false, true })
        {
            if (linked) await File.WriteAllTextAsync(link, "{}");
            using var drive = new ContactDriveSync(_services.Contacts, link,
                () => new GmailAuthService(new DrivePreferencesCredentials(), http,
                    _ => throw new InvalidOperationException("UI checks must not open Google sign-in."), driveOnly: true), http);
            var contacts = new ContactsPreferencesView(_services.Contacts, drive: drive,
                pickDriveClient: () => throw new InvalidOperationException("Expanding settings must not choose a client."));
            var view = new PreferencesView(_preferences, contacts: contacts) { Width = 600, Height = 340 };
            view.SelectCategory("Contacts");
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Preferences", Content = view, CloseButtonText = "Close" };
            StyleDialog(dialog);
            Task<ContentDialogResult>? showing = null;
            try
            {
                _appearanceDialogOpen = true;
                showing = ShowDialogAsync(dialog);
                await Task.Delay(100);
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    view.RequestedTheme = theme; view.UpdateLayout();
                    if (Descendants(contacts).OfType<Expander>().Any())
                        throw new InvalidOperationException("Contacts must not load the unstable Expander template.");
                    await InvokeAsync(FindButton(contacts, "Advanced setup"));
                    if (!Descendants(contacts).OfType<Button>().Any(button => button.Content as string == "Choose custom OAuth client…" && button.ActualHeight > 0))
                        throw new InvalidOperationException("The advanced client action must become visible without launching sign-in.");
                    contacts.Refresh(); await Task.Delay(40);
                    await InvokeAsync(FindButton(contacts, "Hide advanced setup"));
                    if (view.Category != "Contacts" || drive.Connected)
                        throw new InvalidOperationException("Toggling Drive setup must preserve the selected category and disconnected state.");
                }
            }
            finally
            {
                dialog.Hide(); if (showing is not null) await showing;
                await contacts.StopAsync(); _appearanceDialogOpen = false;
            }
        }
        File.AppendAllText(log, "PASS: Preferences > Contacts with disconnected and linked Drive; advanced setup expands, survives refresh and collapses in both themes without Expander, credentials or network.\n");
    }

    private sealed class DrivePreferencesCredentials : IGmailCredentialStore
    {
        public GmailAccount Load() => new(null, null, null);
        public void Save(GmailAccount account) => throw new InvalidOperationException("UI checks must not save credentials.");
    }
    private sealed class DrivePreferencesNoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("UI checks must not contact Google Drive.");
    }
}
#endif
