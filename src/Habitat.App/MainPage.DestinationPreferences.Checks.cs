#if HABITAT_UI_CHECKS
using Habitat.Features.Preferences;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckDestinationPreferencesUiAsync(string log)
    {
        var original = _preferences;
        var launch = _services.LaunchWindowsApp;
        Task? showing = null;
        var application = Path.Combine(_services.Paths.LocalDirectory, "Synthetic Windows app.exe");
        Directory.CreateDirectory(_services.Paths.LocalDirectory);
        File.WriteAllText(application, "Synthetic executable. Launch is intercepted by the test.");
        try
        {
            showing = ShowPreferencesAsync("Destinations");
            await Task.Delay(100);
            if (_activeDialog?.Content is not PreferencesView view || view.Category != "Destinations")
                throw new InvalidOperationException("Destination preferences did not open directly.");
            var settings = view.Destinations;
            await InvokeAsync(FindButton(settings, "Move OpenAI down"));
            await InvokeAsync(FindButton(settings, "Remove Terminal"));
            if (settings.EnabledDestinations[0] != ComposerDestination.Google || settings.EnabledDestinations.Contains(ComposerDestination.Terminal))
                throw new InvalidOperationException("Move or remove did not update the personal order.");
            var startup = Descendants(settings).OfType<ComboBox>().Single(control => control.Header as string == "On startup");
            startup.SelectedItem = startup.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == ComposerDestination.Google);
            await InvokeAsync(FindButton(settings, "Add Windows application…"));
            Descendants(settings).OfType<TextBox>().Single(control => control.Header as string == "Destination name").Text = "Synthetic Paint";
            Descendants(settings).OfType<TextBox>().Single(control => control.Header as string == "Application or shortcut").Text = application;
            await InvokeAsync(FindButton(settings, "Add application"));
            var app = settings.Applications.Single();
            if (!settings.EnabledDestinations.Contains(app.Id) || app.PassText || settings.DefaultDestination != ComposerDestination.Google)
                throw new InvalidOperationException("Adding an application lost its settings or startup choice.");
            _activeDialog!.Hide();
            await showing;
            showing = null;
            var saved = Preferences.Load(_services.Paths.PreferencesPath);
            if (saved.DefaultDestination != ComposerDestination.Google || saved.WindowsApps.Single() != app ||
                !saved.EnabledDestinations!.SequenceEqual(_destinationCatalog.Enabled) || _destinationButtons.ContainsKey(ComposerDestination.Terminal))
                throw new InvalidOperationException("Destination preferences were not persisted and applied to navigation.");
            var runs = 0;
            _services.LaunchWindowsApp = (destination, text) => { if (destination.Id != app.Id) throw new InvalidOperationException(); runs++; return true; };
            SelectDestination(app.Id);
            Composer.Text = "";
            await Task.Delay(80);
            var hint = ComposerHint.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
            var editor = Composer.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
            if (HasDestinationSection || Composer.VerticalAlignment != VerticalAlignment.Top ||
                ComposerHint.Text != "Open Synthetic Paint…" || SendButton.Content as string != "Open Synthetic Paint" ||
                hint.Y + ComposerHint.ActualHeight > editor.Y - 15)
                throw new InvalidOperationException("A Windows application must retain the common initial editor layout.");
            SendCurrentMessage();
            if (runs != 1) throw new InvalidOperationException("An application must open from an empty editor.");
            Composer.Text = "Keep this draft ☕";
            SendCurrentMessage();
            if (runs != 2 || Composer.Text != "Keep this draft ☕")
                throw new InvalidOperationException("Open-only applications must keep the editor draft.");
            _preferences = _preferences with { EnabledDestinations = [] };
            ApplyDestinationPreferences();
            await _composer.SubmitAsync(app.Id, "This must not run");
            if (runs != 2 || _destination != "" || _destinationButtons.Count != 0 ||
                SendButton.Content as string != "Add destination" || !SendButton.IsEnabled || Composer.Text != "Keep this draft ☕")
                throw new InvalidOperationException("An empty profile must block removed routes and offer Add destination without losing the draft.");
            _preferences = _preferences with { EnabledDestinations = [app.Id], WindowsApps = [app with { PassText = true }] };
            ApplyDestinationPreferences();
            _services.LaunchWindowsApp = (_, _) => false;
            SendCurrentMessage();
            if (Composer.Text != "Keep this draft ☕") throw new InvalidOperationException("Application launch failures must preserve the draft.");
            _services.LaunchWindowsApp = (_, text) => text == "Keep this draft ☕";
            SendCurrentMessage();
            if (Composer.Text != "") throw new InvalidOperationException("A successful text argument launch must clear only its submitted draft.");
            File.AppendAllText(log, "PASS: destination management, application creation, startup persistence, empty profiles, disabled dispatch, common initial layout and Windows draft handling\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            _services.LaunchWindowsApp = launch;
            _preferences = original;
            ApplyDestinationPreferences();
            SavePreferences();
            SelectDestination(original.Destination);
            Composer.Text = "";
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Button FindButton(DependencyObject parent, string name) => Descendants(parent).OfType<Button>().First(button =>
        AutomationProperties.GetName(button) == name || button.Content as string == name);

    private static async Task InvokeAsync(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(80);
    }
}
#endif
