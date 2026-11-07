using Habitat.Features.Contacts;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Preferences;

// Manual contact editing is the current flow. Legacy imports remain in Core checks.
internal sealed partial class ContactsPreferencesView : StackPanel
{
    private readonly ContactSession _session;
    private readonly ContactDriveSync? _drive;
    private readonly Func<Task<Habitat.Models.GmailClientConfiguration?>>? _pickDriveClient;
    private readonly AsyncOperationScope _operations = new();
    private bool _busy;
    private string _query = "";
    private readonly TextBlock _status = Text("", 13);
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly ContentControl _bodyHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal bool IsBusy => _busy;
    internal bool IsEditingContact => _contactEditorOpen;
    internal Action? OpenDriveSettings { get; set; }

    internal ContactsPreferencesView(ContactSession session, ContactDriveSync? drive = null,
        Func<Task<Habitat.Models.GmailClientConfiguration?>>? pickDriveClient = null)
    {
        _session = session;
        _drive = drive;
        _pickDriveClient = pickDriveClient;
        Spacing = 12;
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _bodyHost.Content = _body;
        Children.Add(_status);
        Children.Add(_bodyHost);
        Refresh();
    }

    internal Task StopAsync() => _operations.StopAsync();
    internal void SelectPerson(string id) => BeginContactEdit(id);
    internal Task InitializeAsync() => _operations.RunAsync(async () =>
    {
        if (!_session.Loaded) await _session.LoadAsync(_operations.Token);
        if (!_operations.IsStopping) Refresh();
    });

    private void Action(StackPanel panel, string label, Func<Task> action, bool enabled = true)
    {
        var button = new Button { Content = label, IsEnabled = !_busy && enabled };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await RunAsync(action);
        panel.Children.Add(button);
    }

    private Task RunAsync(Func<Task> action) => _operations.RunAsync(async () =>
    {
        if (_busy) return;
        CaptureContactForm();
        _busy = true;
        _status.Text = "Working…";
        _bodyHost.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { if (!_operations.IsStopping) _status.Text = "Action cancelled. Your contacts were kept."; }
        catch (Exception exception)
        {
            if (!_operations.IsStopping)
                _status.Text = exception is InvalidDataException or InvalidOperationException or Habitat.Models.GmailSignInRequiredException
                    ? exception.Message : "Could not complete this action. Your existing contacts were kept. Try again.";
        }
        finally
        {
            _busy = false;
            if (!_operations.IsStopping)
            {
                _bodyHost.IsEnabled = true;
                if (_status.Text == "Working…") _status.Text = "";
                Refresh();
            }
        }
    });

    internal void Refresh()
    {
        if (_operations.IsStopping) return;
        _body.Children.Clear();
        if (_session.Error is not null || !_session.Loaded)
        {
            _body.Children.Add(Text(_session.Error ?? "Load your contacts to get started."));
            Action(_body, "Reload contacts", () => _session.LoadAsync(_operations.Token));
            return;
        }
        ShowManualContacts();
    }

    private static TextBlock Text(string value, double size = 13) => new()
        { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static StackPanel Panel(string title, string description)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text(title, 15));
        panel.Children.Add(Text(description, 12));
        return panel;
    }
    private static Border Card(UIElement content) => new() { Child = content, Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)) };
}
