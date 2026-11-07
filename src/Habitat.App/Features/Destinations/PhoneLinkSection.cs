using Habitat.Services;
using Habitat.Features.Contacts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Security.Cryptography;
using System.Text;

namespace Habitat.Features.Destinations;

internal sealed class PhoneLinkSection : IDestinationSection
{
    private readonly Grid _view = new() { RowSpacing = 12 };
    private readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly Button _clear = new() { Content = "Clear", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button _review = new() { Content = "Review in Phone Link", Visibility = Visibility.Collapsed };
    private readonly Button _checked = new() { Content = "I checked Phone Link", Visibility = Visibility.Collapsed };
    private readonly StackPanel _conversation = new() { Spacing = 12 };
    private readonly ScrollViewer _scroll;
    private readonly Func<string, CancellationToken, Task<PhoneLinkMessageResult>> _send;
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<PhoneLinkConversationMessage>?>> _read;
    private readonly Func<CancellationToken, Task<bool>> _openReview;
    private readonly Action<Exception> _reportError;
    private readonly AsyncOperationScope _operations = new();
    private readonly DispatcherQueueTimer _refreshTimer;
    private IReadOnlyList<PhoneLinkConversationMessage> _messages = [];
    private readonly List<string> _localSent = [];
    private CancellationTokenSource? _conversationRead;
    private bool _active;
    private bool _following;
    private bool _showingLoadStatus;
    private bool _disposed;
    private readonly HashSet<string> _unconfirmedMessages;
    private string? _reviewMessage;
    public string Destination { get; }
    public FrameworkElement View => _view;
    public bool IsBusy { get; private set; }
    public bool HasContent { get; private set; }
    public event Action? StateChanged;
    internal event Action? ClearRequested;
    internal string StatusText => _status.Text;
    internal bool WasSubmitted { get; private set; }
    internal bool RequiresReview => _reviewMessage is not null && _unconfirmedMessages.Contains(_reviewMessage);
    internal bool HasUnconfirmedMessages => _unconfirmedMessages.Count > 0;
    internal string[] CopyUnconfirmedMessages() => _unconfirmedMessages.ToArray();
    private static string MessageKey(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))));

    internal PhoneLinkSection(string destination, Func<string, CancellationToken, Task<PhoneLinkMessageResult>> send,
        Func<bool, CancellationToken, Task<IReadOnlyList<PhoneLinkConversationMessage>?>> read,
        Func<CancellationToken, Task<bool>> openReview, Action<Exception> reportError, string[]? unconfirmedMessages = null)
    {
        Destination = destination; _send = send; _read = read; _openReview = openReview; _reportError = reportError;
        _unconfirmedMessages = unconfirmedMessages is null ? [] : new(unconfirmedMessages);
        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(10);
        _refreshTimer.Tick += async (_, _) => await RefreshConversationAsync();
        var actions = new StackPanel { Spacing = 12 };
        actions.Children.Add(_clear); actions.Children.Add(_status); actions.Children.Add(_review); actions.Children.Add(_checked);
        _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _view.Children.Add(actions);
        _scroll = new ScrollViewer { Content = _conversation, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_scroll, 1);
        _view.Children.Add(_scroll);
        AutomationProperties.SetName(_scroll, "Phone Link conversation");
        AutomationProperties.SetName(_clear, "Clear");
        AutomationProperties.SetName(_review, "Review the selected conversation in Phone Link");
        AutomationProperties.SetName(_checked, "I checked Phone Link before allowing another send attempt");
        ToolTipService.SetToolTip(_clear, "Clear the HABITAT view and draft. Messages in Phone Link are kept.");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _clear.Click += (_, _) => RequestClear();
        _review.Click += async (_, _) => await _operations.RunAsync(async () =>
        {
            if (_disposed || IsBusy) return;
            SetBusy(true);
            try { if (!await _openReview(_operations.Token) && !_disposed) _status.Text = "Could not open Phone Link. Open it from Windows and check the conversation before trying again."; }
            catch (Exception exception) { _reportError(exception); }
            finally { if (!_disposed) SetBusy(false); }
        });
        _checked.Click += (_, _) => ConfirmReviewed();
    }

    public Task SubmitAsync(string text) => _operations.RunAsync(async () =>
    {
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return;
        _conversationRead?.Cancel();
        _showingLoadStatus = false;
        HasContent = true;
        WasSubmitted = false;
        var key = MessageKey(text);
        if (_unconfirmedMessages.Contains(key))
        {
            _reviewMessage = key;
            _review.Visibility = _checked.Visibility = Visibility.Visible;
            _status.Text = "A previous attempt with this text was not confirmed. Check Phone Link before trying this message again.";
            SetBusy(false);
            return;
        }
        _reviewMessage = null;
        _review.Visibility = _checked.Visibility = Visibility.Collapsed;
        SetBusy(true);
        _status.Text = "Sending message…";
        try
        {
            var result = await _send(text, _operations.Token);
            if (_disposed)
            {
                // The result cannot update a retired view or consume its draft.
                // Keep the fingerprint if delivery may have happened.
                if (result.Status is "sent" or "submitted" or "uncertain") _unconfirmedMessages.Add(key);
                return;
            }
            WasSubmitted = result.Status is "submitted" or "sent";
            if (result.Status == "uncertain") { _unconfirmedMessages.Add(key); _reviewMessage = key; }
            _review.Visibility = _checked.Visibility = RequiresReview ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = result.Status switch
            {
                "sent" => "Sent via Phone Link.",
                "submitted" => result.MessageType == PhoneLinkMessageType.IMessage ? "iMessage submitted." : "Message submitted.",
                "uncertain" => "The send result could not be confirmed. Check Phone Link, then choose I checked Phone Link before trying again. Your draft was kept.",
                _ => result.Error switch
                {
                    "sms-disabled" => "SMS sending is disabled. Your message was not sent and your draft was kept.",
                    "draft" => "Phone Link already has a different draft. Review or finish it there, then try again. Your text was kept.",
                    "disconnected" => "Connect your phone in Phone Link, then try again. Your text was kept.",
                    "unavailable" => "Open or update Phone Link and connect your phone, then try again. Your text was kept.",
                    "busy" => "Wait for the current Phone Link message to finish. Your text was kept.",
                    "blocked" => "Complete any open Phone Link dialog, then try again. Your text was kept.",
                    "recipient" => "The phone number could not be verified in Phone Link. Check the selected conversation and try again. Your text was kept.",
                    _ => "Phone Link's recipient or editor could not be verified. Your text was kept. Check Phone Link before trying again."
                }
            };
            if (WasSubmitted)
            {
                if (_localSent.Count >= 100) _localSent.RemoveAt(0);
                _localSent.Add(text);
                RenderConversation();
            }
            if (WasSubmitted || RequiresReview) FollowConversation();
        }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _reportError(exception);
            if (exception is not InvalidDataException) _unconfirmedMessages.Add(key);
            if (!_disposed)
            {
                if (exception is not InvalidDataException) _reviewMessage = key;
                _review.Visibility = _checked.Visibility = RequiresReview ? Visibility.Visible : Visibility.Collapsed;
                _status.Text = exception is InvalidDataException ? exception.Message
                    : "The send result could not be confirmed. Check Phone Link before trying again. Your draft was kept.";
            }
        }
        finally
        {
            if (!_disposed)
            {
                SetBusy(false);
                _ = RefreshConversationAsync();
            }
        }
    });

    internal Task OpenConversationAsync() => _operations.RunAsync(async () =>
    {
        if (_disposed || IsBusy || !_active) return;
        HasContent = true;
        _showingLoadStatus = true;
        _status.Text = "Loading conversation…";
        SetBusy(true);
        FollowConversation();
        try
        {
            var messages = await ReadConversationAsync(openIfNeeded: true);
            if (_disposed) return;
            if (messages is null)
                _status.Text = "Could not read this conversation. Open it in Phone Link, then choose View conversation again.";
            else ApplyConversation(messages);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed) _status.Text = "Open this conversation in Phone Link to load its messages.";
        }
        catch (Exception exception)
        {
            _reportError(exception);
            if (!_disposed) _status.Text = "Could not read this conversation. Check Phone Link and try again.";
        }
        finally { if (!_disposed) SetBusy(false); }
    });

    internal void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active;
        if (!active) _conversationRead?.Cancel();
        UpdateConversationTimer();
        if (active) _ = RefreshConversationAsync();
    }

    private void FollowConversation()
    {
        _following = true;
        UpdateConversationTimer();
    }

    private void UpdateConversationTimer()
    {
        if (!_active || !_following || _disposed) _refreshTimer.Stop();
        else if (!_refreshTimer.IsRunning) _refreshTimer.Start();
    }

    private Task RefreshConversationAsync() => _operations.RunAsync(async () =>
    {
        if (_disposed || !_active || !_following || IsBusy || _conversationRead is not null) return;
        try
        {
            var messages = await ReadConversationAsync(openIfNeeded: false);
            if (messages is not null && !_disposed) ApplyConversation(messages);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _reportError(exception); }
    });

    private async Task<IReadOnlyList<PhoneLinkConversationMessage>?> ReadConversationAsync(bool openIfNeeded)
    {
        _conversationRead?.Cancel();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        _conversationRead = read;
        try
        {
            var messages = await _read(openIfNeeded, read.Token);
            read.Token.ThrowIfCancellationRequested();
            return messages;
        }
        finally { if (ReferenceEquals(_conversationRead, read)) _conversationRead = null; }
    }

    private void ApplyConversation(IReadOnlyList<PhoneLinkConversationMessage> messages)
    {
        if (_showingLoadStatus)
            _status.Text = messages.Count == 0 ? "No messages are available in Phone Link." : "";
        if (_messages.SequenceEqual(messages)) return;
        var previous = _messages.ToHashSet();
        foreach (var message in messages.Where(message => message.Outgoing && !previous.Contains(message)))
        {
            var index = _localSent.FindIndex(text => PhoneLinkDelivery.SameText(text, message.Text));
            if (index >= 0) _localSent.RemoveAt(index);
        }
        _messages = messages;
        RenderConversation();
    }

    private void RenderConversation()
    {
        var nearBottom = _scroll.ScrollableHeight - _scroll.VerticalOffset < 60;
        _conversation.Children.Clear();
        var rows = _messages.Select(message => (message.Text, message.Outgoing))
            .Concat(_localSent.Select(text => (Text: text, Outgoing: true))).TakeLast(100);
        foreach (var (text, outgoing) in rows)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock { Text = outgoing ? "You" : "Reply", FontSize = 12, Opacity = 0.75 });
            row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            _conversation.Children.Add(row);
        }
        if (nearBottom) _scroll.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_disposed || !_active || !_scroll.IsLoaded) return;
            _scroll.UpdateLayout();
            _scroll.ChangeView(null, _scroll.ScrollableHeight, null, disableAnimation: true);
        });
    }

    internal void RequestClear()
    {
        if (_disposed || IsBusy) return;
        HasContent = false; _status.Text = "";
        _reviewMessage = null;
        _review.Visibility = _checked.Visibility = Visibility.Collapsed;
        WasSubmitted = false;
        _following = _showingLoadStatus = false;
        _conversationRead?.Cancel();
        UpdateConversationTimer();
        _messages = [];
        _localSent.Clear();
        _conversation.Children.Clear();
        ClearRequested?.Invoke(); StateChanged?.Invoke();
    }
    internal void ConfirmReviewed()
    {
        if (_disposed || IsBusy || !RequiresReview) return;
        _unconfirmedMessages.Remove(_reviewMessage!);
        _reviewMessage = null;
        _review.Visibility = _checked.Visibility = Visibility.Collapsed;
        _status.Text = "Conversation reviewed. Send again only if the previous message was not sent.";
        SetBusy(false);
    }
    private void SetBusy(bool busy)
    { IsBusy = busy; _clear.IsEnabled = !busy; _review.IsEnabled = _checked.IsEnabled = !busy; StateChanged?.Invoke(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsBusy = false;
        _refreshTimer.Stop();
        StateChanged = null;
        ClearRequested = null;
        _ = _operations.StopAsync();
    }
    public Task StopAsync() { Dispose(); return _operations.StopAsync(); }
}
