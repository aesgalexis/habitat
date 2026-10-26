using Habitat.Features.Facebook;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Habitat.Features.Destinations;

internal sealed class MessengerSection : IDestinationSection
{
    private readonly StackPanel _view = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly StackPanel _submitted = new() { Spacing = 12 };
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, CancellationToken, Task<FacebookMessageResult>> _send;
    private Task _pending = Task.CompletedTask;
    public string Destination { get; }
    public FrameworkElement View => _view;
    public bool HasContent { get; private set; }
    public bool IsBusy { get; private set; }
    internal bool WasSubmitted { get; private set; }
    public event Action? StateChanged;
    internal event Action? ClearRequested;

    internal MessengerSection(string destination, Func<string, CancellationToken, Task<FacebookMessageResult>> send)
    {
        Destination = destination; _send = send;
        var clear = new Button { Content = "Clear", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(clear, "Clear messages");
        clear.Click += (_, _) =>
        {
            if (IsBusy) return;
            HasContent = false; _submitted.Children.Clear(); _status.Text = "";
            ClearRequested?.Invoke(); StateChanged?.Invoke();
        };
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _view.Children.Add(clear); _view.Children.Add(_status); _view.Children.Add(_submitted);
    }

    public Task SubmitAsync(string text) => _pending = SubmitCoreAsync(text);
    private async Task SubmitCoreAsync(string text)
    {
        if (_stop.IsCancellationRequested || IsBusy || string.IsNullOrWhiteSpace(text)) return;
        IsBusy = true; HasContent = true; WasSubmitted = false;
        _status.Text = "Sending to Messenger…"; StateChanged?.Invoke();
        try
        {
            var result = await _send(text, _stop.Token);
            if (_stop.IsCancellationRequested) return;
            WasSubmitted = result.Status == "submitted";
            _status.Text = result.Status switch
            {
                "submitted" => "Message submitted to Messenger.",
                "uncertain" => "Submission could not be confirmed. Check Messenger before trying again. Your draft has been kept.",
                _ => result.Error switch
                {
                    "permission" => "Enable Facebook access in the HABITAT extension before sending.",
                    "login" => "Sign in to Facebook in Chrome, then try again.",
                    "blocked" => "Complete Facebook's verification in Chrome, then try again.",
                    "draft" => "Messenger already has a draft. Review it there before sending from HABITAT.",
                    "recipient" => "The recipient could not be verified. Nothing was sent.",
                    "composer" => "Messenger's editor could not be verified. Nothing was sent.",
                    _ => "This Facebook conversation could not be opened safely. Nothing was sent."
                }
            };
            if (WasSubmitted)
            {
                if (_submitted.Children.Count >= 100) _submitted.Children.RemoveAt(0);
                _submitted.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) { if (!_stop.IsCancellationRequested) _status.Text = exception is InvalidOperationException ? exception.Message : "Could not submit to Messenger. Your draft has been kept."; }
        finally { IsBusy = false; StateChanged?.Invoke(); }
    }
    public async Task StopAsync() { _stop.Cancel(); await _pending; }
    public void Dispose() { _stop.Cancel(); }
}
