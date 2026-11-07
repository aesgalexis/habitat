using Habitat.Features.Contacts;
using Habitat.Models;
using Habitat.Services;

internal static class ReleasePayloadChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var client = ReleasePayload.ReadDriveClient(typeof(ReleasePayloadChecks).Assembly.Location);
        check(client?.ClientId == "synthetic-only.apps.googleusercontent.com", "Read and validate the exact embedded Drive resource through PE metadata without loading a WinUI app.");
        check(!ReleasePayload.VerifyDrive(typeof(ReleasePayload).Assembly.Location, false), "A custom build without an embedded client remains an explicit supported case.");
        var rejected = false;
        try { ReleasePayload.VerifyDrive(typeof(ReleasePayload).Assembly.Location, true); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Official payload verification rejects a missing Drive resource before packaging.");
        using var http = new HttpClient(new DisabledHttp());
        using var drive = new ContactDriveSync(new(new ContactStore(Path.Combine(root, "payload-contacts.json"))),
            Path.Combine(root, "payload-drive.json"), () => new(new EmptyCredentials(), http, driveOnly: true), http, client);
        check(drive.CanConnect && !drive.Connected && !drive.HasLink, "A fresh isolated profile can offer built-in Drive sign-in without a saved OAuth client or account.");
    }
    private sealed class EmptyCredentials : IGmailCredentialStore
    {
        public GmailAccount Load() => new(null, null, null);
        public void Save(GmailAccount account) => throw new InvalidOperationException("Verification never signs in.");
    }
    private sealed class DisabledHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("Payload verification never uses the network.");
    }
}
