#if HABITAT_UI_CHECKS
using Habitat.Features.Preferences;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckPreferencesUiAsync(string log)
    {
        var original = _preferences;
        Task? showing = null;
        try
        {
            showing = ShowPreferencesAsync();
            await Task.Delay(150);
            if (_activeDialog?.Content is not PreferencesView view || !_appearanceDialogOpen)
                throw new InvalidOperationException("Preferences did not open the categorized view.");
            var names = view.Categories.Items.Cast<ListViewItem>().Select(item => item.Tag as string).ToArray();
            if (!names.SequenceEqual(new[] { "Appearance", "Location", "ChatGPT", "Shortcuts", "Destinations" }))
                throw new InvalidOperationException("Preference categories changed or mixed UI languages.");
            foreach (var index in new[] { 1, 2, 3, 4, 0 })
            {
                view.Categories.SelectedIndex = index;
                await Task.Delay(30);
                if (view.Category != names[index]) throw new InvalidOperationException("Sidebar selection did not change categories.");
            }
            view.WeatherCity.Text = "Ciudad de prueba";
            view.UseDeviceLocation.IsOn = false;
            view.Theme.SelectedIndex = 1;
            view.BackgroundOpacity.Value = 37;
            view.TextOnly.IsOn = false;
            view.Categories.SelectedIndex = 1;
            view.Categories.SelectedIndex = 2;
            if (view.WeatherCity.Text != "Ciudad de prueba" || view.WeatherCity.Header as string != "Weather location")
                throw new InvalidOperationException("Switching categories lost edits.");
            _activeDialog.Hide();
            await showing;
            showing = null;
            var saved = Preferences.Load(_services.Paths.PreferencesPath);
            if (saved.Theme != "night" || saved.Opacity != 37 || saved.TextOnly ||
                saved.WeatherCity != "Ciudad de prueba" || saved.UseDeviceLocation || _appearanceDialogOpen)
                throw new InvalidOperationException("Closing Preferences did not persist the edits.");
            File.AppendAllText(log, "PASS: Preferences sidebar categories, retained edits, English labels and location state without browser selection\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            _preferences = original;
            ApplyDestinationPreferences();
            SavePreferences();
            ApplyPreferences();
        }
    }
}
#endif
