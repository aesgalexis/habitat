using Habitat.Models;
using Habitat.Services;

namespace Habitat.Features.Drive;

// One owner for the existing Drive credential store and refresh-token state.
// Dataset services borrow the connection; they never dispose or replace it.
internal sealed class GoogleDriveConnection(string workspacePath, string legacyContactsPath,
    Func<GmailAuthService> createAuth, HttpClient http, GmailClientConfiguration? bundledClient = null,
    string? legacyTasksPath = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private GmailAuthService? _auth;
    internal GmailAuthService Auth => _auth ??= createAuth();
    internal DriveWorkspace Workspace { get; } = new(workspacePath, legacyContactsPath, legacyTasksPath);
    internal bool Busy => _gate.CurrentCount == 0;
    internal bool Connected => Auth.IsSignedIn;
    internal string? Email => Auth.Email;
    internal bool CanConnect => Auth.IsConfigured || bundledClient is not null;
    internal string? Status { get; private set; }

    internal async Task<bool> ConnectAsync(CancellationToken token, Func<Task<GmailClientConfiguration?>>? pickClient = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            var client = pickClient is null ? Auth.Client ?? bundledClient ??
                throw new InvalidOperationException("This build has no Google Drive sign-in configuration.") : await pickClient();
            if (client is null) return false;
            if (Workspace.ExpectedClientId is { } expected && client.ClientId != expected)
                throw new DriveIdentityException();
            Auth.Configure(client);
            await Auth.SignInAsync(token);
            await Workspace.EnsureAsync(new(Auth, http), client.ClientId, token);
            Status = "Google Drive is connected.";
            return true;
        }
        catch (DriveIdentityException)
        { await Auth.DisconnectAsync(token); throw; }
        finally { _gate.Release(); }
    }

    internal async Task<T> RunAsync<T>(Func<DriveWorkspaceContext, Task<T>> action, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Auth.IsSignedIn) throw new GmailSignInRequiredException("Reconnect Google Drive to resume syncing.");
            var context = await Workspace.EnsureAsync(new(Auth, http), Auth.Client!.ClientId, token);
            return await action(context);
        }
        catch (DriveIdentityException)
        { await Auth.DisconnectAsync(token); throw; }
        finally { _gate.Release(); }
    }

    internal async Task SelectFolderAsync(string id, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Connected) throw new GmailSignInRequiredException("Reconnect Google Drive before choosing a folder.");
            await Workspace.SelectAsync(new(Auth, http), Auth.Client!.ClientId, id, token);
        }
        finally { _gate.Release(); }
    }

    internal async Task DisconnectAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await Auth.DisconnectAsync(token); Status = "Google Drive is disconnected on this PC. Your local data was kept."; }
        finally { _gate.Release(); }
    }
    public void Dispose() => _auth?.Dispose();
}
