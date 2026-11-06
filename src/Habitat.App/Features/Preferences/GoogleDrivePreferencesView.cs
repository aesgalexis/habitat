using Habitat.Features.Drive;
using Habitat.Features.Contacts;
using Habitat.Features.Tasks;
using Habitat.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed class GoogleDrivePreferencesView : StackPanel
{
    private readonly GoogleDriveSync _sync;
    private readonly Func<Task<GmailClientConfiguration?>> _pickClient;
    private readonly Func<Uri, Task<bool>> _open;
    private readonly CancellationTokenSource _stop = new();
    private Task _pending = Task.CompletedTask;
    private bool _busy, _advanced;
    private string? _error;
    internal event Action? Changed;

    internal GoogleDrivePreferencesView(GoogleDriveSync sync, Func<Task<GmailClientConfiguration?>> pickClient, Func<Uri, Task<bool>> open)
    {
        _sync = sync; _pickClient = pickClient; _open = open; Spacing = 12;
        _sync.Changed += SyncChanged;
        Refresh();
    }
    private void SyncChanged()
    {
        if (_stop.IsCancellationRequested) return;
        if (DispatcherQueue.HasThreadAccess) Refresh();
        else DispatcherQueue.TryEnqueue(() => { if (!_stop.IsCancellationRequested) Refresh(); });
    }
    internal void Refresh()
    {
        if (_stop.IsCancellationRequested) return;
        var settings = _sync.Settings;
        var connection = _sync.Connection;
        Children.Clear();
        Children.Add(Text("Keep contacts and tasks together in the HABITAT folder in My Drive."));
        if (connection.Email is { } email) Children.Add(Text(email));
        Status(_error ?? _sync.Status ?? connection.Status);
        if (!connection.Connected)
        {
            Button(this, "Connect Google Drive",
                () => _sync.ConnectAsync(_stop.Token), connection.CanConnect);
            if (!connection.CanConnect) Children.Add(Text("This build needs a Google Drive sign-in configuration."));
            Button(this, _advanced ? "Hide advanced setup" : "Advanced setup", () =>
            { _advanced = !_advanced; return Task.CompletedTask; });
            if (_advanced)
            {
                Children.Add(Text("Use the same custom Google OAuth client as your existing HABITAT sync."));
                Button(this, "Choose custom OAuth client…", () => _sync.ConnectAsync(_stop.Token, _pickClient));
            }
        }
        else
        {
            Button(this, "Sync now", () => _sync.SyncNowAsync(_stop.Token));
            if (connection.Workspace.FolderUrl is { } url) Button(this, "Open HABITAT folder", () => OpenAsync(url));
            Button(this, "Disconnect Google Drive", () => _sync.DisconnectAsync(_stop.Token));
        }
        foreach (var (folder, index) in connection.Workspace.Choices.Select((folder, index) => (folder, index)))
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(Text($"Folder {index + 1}: {folder.Name}"));
            if (folder.CreatedAt is { } at) row.Children.Add(Text("Created: " + at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            Button(row, "Open folder", () => OpenAsync(folder.Url));
            Button(row, "Use this HABITAT folder", () => _sync.SelectFolderAsync(folder.Id, _stop.Token));
            Children.Add(row);
        }

        var contacts = Toggle("Sync contacts", settings.Contacts);
        var tasks = Toggle("Sync tasks", settings.Tasks);
        contacts.Toggled += async (_, _) => await RunAsync(() =>
        { _sync.SetEnabled(contacts.IsOn, _sync.Settings.Tasks); return Task.CompletedTask; });
        tasks.Toggled += async (_, _) => await RunAsync(() =>
        { _sync.SetEnabled(_sync.Settings.Contacts, tasks.IsOn); return Task.CompletedTask; });
        Children.Add(contacts);
        Status(_sync.Contacts.Status);
        LastSync(_sync.Contacts.LastSyncedAt);
        if (settings.Contacts && _sync.Contacts.HasConflict)
        {
            Button(this, "Keep local conflicting contacts", () => _sync.Contacts.SyncAsync(_stop.Token, ContactDriveConflictChoice.Local));
            Button(this, "Keep Drive conflicting contacts", () => _sync.Contacts.SyncAsync(_stop.Token, ContactDriveConflictChoice.Drive));
        }
        Children.Add(tasks);
        Children.Add(Text("Tasks are saved on this PC first, including while you are offline."));
        Status(_sync.Tasks.Status);
        LastSync(_sync.Tasks.LastSyncedAt);
        if (settings.Tasks && _sync.Tasks.NeedsRecoveryReview)
            Button(this, "Sync recovered tasks", () => _sync.Tasks.ConfirmRecoveryAsync(_stop.Token));
        foreach (var conflict in settings.Tasks ? _sync.Tasks.Conflicts : [])
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(Text("Task: " + conflict.Local.Text));
            row.Children.Add(Text("This PC: " + conflict.Local.Text + (conflict.Local.IsCompleted ? " (Completed)" : " (Pending)")));
            row.Children.Add(Text("Google Drive: " + conflict.Drive.Text + (conflict.Drive.IsCompleted ? " (Completed)" : " (Pending)")));
            if (conflict.Selected is { } selected)
                row.Children.Add(Text(selected == TaskDriveChoice.Local ? "Local version selected." : "Drive version selected."));
            Button(row, "Keep local task", () => _sync.Tasks.SyncAsync(_stop.Token, (conflict.Local.Id, TaskDriveChoice.Local)));
            Button(row, "Keep Drive task", () => _sync.Tasks.SyncAsync(_stop.Token, (conflict.Local.Id, TaskDriveChoice.Drive)));
            Children.Add(row);
        }
    }
    private ToggleSwitch Toggle(string label, bool enabled)
    {
        var toggle = new ToggleSwitch { Header = label, OnContent = "On", OffContent = "Off", IsOn = enabled, IsEnabled = !_busy && !_sync.Busy };
        AutomationProperties.SetName(toggle, label);
        return toggle;
    }
    private void Button(Panel panel, string label, Func<Task> action, bool enabled = true)
    {
        var button = new Button { Content = label, IsEnabled = enabled && !_busy && !_sync.Busy };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await RunAsync(action);
        panel.Children.Add(button);
    }
    private void Status(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var status = Text(value);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Children.Add(status);
    }
    private void LastSync(DateTimeOffset? value)
    { if (value is { } at) Children.Add(Text("Last synced: " + at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))); }
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private async Task OpenAsync(Uri url)
    { if (!await _open(url)) throw new InvalidOperationException("Could not open the Google Drive folder. Try again."); }
    private Task RunAsync(Func<Task> action)
    {
        if (_busy || _stop.IsCancellationRequested || _sync.Busy) return Task.CompletedTask;
        _busy = true; _error = null;
        _pending = Work();
        return _pending;
        async Task Work()
        {
            Refresh();
            try { await action(); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or HttpRequestException)
            { _error = error is InvalidOperationException or InvalidDataException ? error.Message : "Could not update Google Drive sync. Your local data was kept. Try again."; }
            finally { _busy = false; if (!_stop.IsCancellationRequested) { Refresh(); Changed?.Invoke(); } }
        }
    }
    internal async Task StopAsync()
    {
        _sync.Changed -= SyncChanged;
        _stop.Cancel();
        try { await _pending; }
        finally { _stop.Dispose(); }
    }
}
