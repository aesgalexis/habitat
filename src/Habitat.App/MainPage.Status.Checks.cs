#if HABITAT_UI_CHECKS
using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckDestinationStatusAsync(string log)
    {
        var paths = AppDataPaths.Isolated(Path.Combine(_services.Paths.LocalDirectory, "status-checks", Guid.NewGuid().ToString("N")));
        new Preferences("night", 15)
        {
            Destination = ComposerDestination.Google, SetupGuideSeen = true, GlobalHotkeyEnabled = false
        }.Save(paths.PreferencesPath);
        await using var services = new AppServices(paths);
        var host = new StatusCheckHost();
        var page = new MainPage(services, host);
        try
        {
            await page._initialTaskLoad;
            page.MainPage_Loaded(page, new RoutedEventArgs());
            page._timer.Stop();
            if (host.Text != "Ready" || host.Tone != "Success")
                throw new InvalidOperationException("Starting on Google must report Ready without waiting for ChatGPT.");
            foreach (var destination in ComposerDestinations.Ordered.Where(id => id != ComposerDestination.OpenAI))
            {
                page.SelectDestination(destination);
                if (host.Text != "Ready") throw new InvalidOperationException("An idle destination inherited another destination's status.");
            }
            page.SelectDestination(ComposerDestination.OpenAI);
            if (host.Text != services.Bridge.ConnectionIssue || host.Tone != "Warning")
                throw new InvalidOperationException("OpenAI must retain its actual disconnected bridge status.");
            page.SetOpenAIStatus("ChatGPT is responding…", StatusTone.Active);
            if (host.Text != "ChatGPT is responding…") throw new InvalidOperationException("Selected OpenAI progress was hidden.");
            page.SelectDestination(ComposerDestination.Google);
            foreach (var type in new[] { "received", "started", "response", "error", "status" })
                page.Bridge_EventReceived(null, new BridgeEvent(type, "synthetic", "Synthetic bridge event", null));
            await Task.Delay(80);
            if (host.Text != "Ready" || page._conversation.Messages.Count != 2)
                throw new InvalidOperationException("Background ChatGPT events must retain history without replacing another destination's status.");
            page.Bridge_StartFailed(null, "Synthetic startup failure");
            await Task.Delay(40);
            if (host.Text != "Ready") throw new InvalidOperationException("An unused bridge failure replaced the active destination status.");
            page.SelectDestination(ComposerDestination.OpenAI);
            if (host.Text != "Could not start the bridge" || host.Tone != "Danger")
                throw new InvalidOperationException("Returning to OpenAI lost its startup failure.");
            page._preferences = page._preferences with { EnabledDestinations = [] };
            page.ApplyDestinationPreferences();
            if (host.Text != "Ready") throw new InvalidOperationException("An empty profile retained a bridge status.");
            File.AppendAllText(log, "PASS: Google startup and idle destinations show Ready; ChatGPT connection, progress and failures remain scoped to OpenAI while background history is retained\n");
        }
        finally { await page.StopAsync(); }
    }

    private sealed class StatusCheckHost : IWindowHost
    {
        public bool IsActive => false;
        public nint Handle => 0;
        public string Text { get; private set; } = "";
        public string Tone { get; private set; } = "";
        public void SetBackgroundAppearance(double percent, bool dark) { }
        public bool DetectBackdropIsDark(bool current) => current;
        public void SetStatusPresentation(string text, string status) { Text = text; Tone = status; }
    }
}
#endif
