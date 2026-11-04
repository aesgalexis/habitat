using System.Diagnostics;
using Habitat.Features.Contacts;
using Windows.System;

namespace Habitat.Services;

internal sealed class PhoneLinkLauncher
{
    internal const string PackageFamily = PhoneLinkDesktopAutomation.PackageFamily;
    private readonly PhoneLinkDesktopAutomation _desktop = new();
    private readonly PhoneLinkDelivery _delivery;
    internal PhoneLinkLauncher() => _delivery = new(_desktop, OpenAsync);
    internal Task<PhoneLinkMessageResult> SendAsync(string address, string text, CancellationToken token) =>
        _delivery.SendAsync(address, text, token);

    internal async Task<IReadOnlyList<PhoneLinkConversationMessage>?> ReadConversationAsync(string address, bool openIfNeeded, CancellationToken token)
    {
        var messages = await _desktop.ReadConversationAsync(address, token);
        if (messages is not null || !openIfNeeded) return messages;

        var composers = await _desktop.ReadComposersAsync(token);
        if (composers.Any(c => c.Blocked || c.Text.Length > 0) || !await OpenAsync(address, token)) return null;
        var elapsed = Stopwatch.StartNew();
        do
        {
            await Task.Delay(250, token);
            messages = await _desktop.ReadConversationAsync(address, token);
        } while (messages is null && elapsed.Elapsed < TimeSpan.FromSeconds(6));
        return messages;
    }

    internal async Task<bool> OpenAsync(string address, CancellationToken token)
    {
        var uri = PhoneLinkMessage.RecipientUri(address);
        token.ThrowIfCancellationRequested();
        var support = await Launcher.QueryUriSupportAsync(new Uri("sms:"), LaunchQuerySupportType.Uri, PackageFamily).AsTask(token);
        if (support != LaunchQuerySupportStatus.Available) return false;
        token.ThrowIfCancellationRequested();
        // Navigation carries no body, so an existing destination draft is preserved.
        return await Launcher.LaunchUriAsync(uri, new LauncherOptions { TargetApplicationPackageFamilyName = PackageFamily });
    }
}
