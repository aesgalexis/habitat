using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly AsyncOperationScope _operations = new();
    private Task? _stopTask;
    private readonly IWindowHost? _windowHost;
    private Microsoft.UI.Xaml.Controls.ContentDialog? _activeDialog;

    private async Task<Microsoft.UI.Xaml.Controls.ContentDialogResult> ShowDialogAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        if (_operations.IsStopping) return Microsoft.UI.Xaml.Controls.ContentDialogResult.None;
        dialog.Language = UiText.Culture.Name;
        _activeDialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_activeDialog, dialog)) _activeDialog = null; }
    }

    private async Task RunOperationAsync(Func<Task> action)
    {
        try { await _operations.RunAsync(action); }
        catch (OperationCanceledException) when (_operations.IsStopping) { }
        catch (Exception exception) { _services.ReportError("Background operation", exception); }
    }

    private void SavePreferences() => _preferences.Save(_services.Paths.PreferencesPath,
        exception => _services.ReportError("Save preferences", exception));

    internal Task StopAsync() => _stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        _timer.Stop();
        _composer.Stop();
        CancelEmptyYouTubeReset();
        CancelPendingCodexSend();
        _activeDialog?.Hide();
        _marketNewsCancellation?.Cancel();
        var sectionStops = _destinationSections.Values.Select(section => section.StopAsync()).ToArray();
        if (_bridge is not null)
        {
            _bridge.EventReceived -= Bridge_EventReceived;
            _bridge.StartFailed -= Bridge_StartFailed;
        }
        foreach (var cancellation in new[] { _weatherCancellation, _gmailCancellation, _gmailSignInCancellation })
        {
            try { cancellation?.Cancel(); }
            catch (Exception exception) { _services.ReportError("Cancel operation", exception); }
        }
        try { await Task.WhenAll(sectionStops.Append(_operations.StopAsync())); }
        finally
        {
            _gmailSession.Dispose();
            _weatherCancellation.Dispose();
            _gmailCancellation.Dispose();
        }
    }
}
