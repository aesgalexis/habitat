using Windows.Management.Deployment;
using Windows.System;
using Habitat.Features.Codex;

namespace Habitat.Services;

internal sealed class CodexLauncher(Action<string>? trace = null, AppDataPaths? paths = null) : ICodexChatService
{
    private sealed record CodexPackage(string Family, string Executable, string Reader);
    private Task<CodexPackage?>? _package;
    private CodexChatService? _chat;
    private CodexDesktopAutomation? _desktop;

    private async Task<CodexPackage?> GetPackageAsync(CancellationToken cancellationToken)
    {
        var package = await (_package ??= Task.Run(() =>
        {
            var installed = new PackageManager().FindPackagesForUser(string.Empty)
                .FirstOrDefault(item => item.Id.Name.Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase));
            return installed is null ? null : new CodexPackage(installed.Id.FamilyName,
                Path.Combine(installed.InstalledLocation.Path, "app", "ChatGPT.exe"),
                Path.Combine(installed.InstalledLocation.Path, "app", "resources", "codex.exe"));
        })).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        if (package is null) _package = null;
        return package;
    }

    private async Task<CodexChatService> GetChatAsync(CancellationToken token)
    {
        if (_chat is not null) return _chat;
        var package = await GetPackageAsync(token) ?? throw new IOException("Install Codex to use this destination.");
        _desktop ??= new CodexDesktopAutomation(package.Family, async uri =>
            await Launcher.LaunchUriAsync(uri, new LauncherOptions { TargetApplicationPackageFamilyName = package.Family }),
            package.Executable, trace);
        return _chat ??= new CodexChatService(_desktop, new CodexReadClient(package.Reader),
            (text, cancellation) => new CodexDelivery(_desktop, trace: trace).SendAsync(text, cancellation),
            new CodexChatOrigins((paths ?? AppDataPaths.Production).CodexChatsPath));
    }

    public async Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => await (await GetChatAsync(token)).GetVisibleAsync(token);
    public async Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) => await (await GetChatAsync(token)).ReadAsync(target, token);
    public async Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token) =>
        await (await GetChatAsync(token)).SendAsync(expected, text, token);
    public async Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => await (await GetChatAsync(token)).OpenChatAsync(target, token);
    public void ReturnFocus(nint ownerWindow) => _desktop?.ReturnFocus(ownerWindow);
    public async ValueTask DisposeAsync() { if (_chat is not null) await _chat.DisposeAsync(); }

    public async Task<CodexDeliveryResult> OpenAsync(string text, CancellationToken cancellationToken)
    {
        // Package discovery may be slow; keep it off the UI thread and reuse
        // the stable family name across submissions and app updates.
        var package = await GetPackageAsync(cancellationToken);
        if (package is null) { _package = null; return CodexDeliveryResult.NotSent; }
        var desktop = new CodexDesktopAutomation(package.Family, async uri =>
            await Launcher.LaunchUriAsync(uri, new LauncherOptions { TargetApplicationPackageFamilyName = package.Family }),
            package.Executable, trace);
        return await new CodexDelivery(desktop, trace: trace).SendAsync(text, cancellationToken);
    }
}
