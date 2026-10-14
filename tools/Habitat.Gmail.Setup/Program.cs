using Habitat.Models;
using Habitat.Services;

// Uses exactly the same OAuth flow and encrypted account store as the desktop app.
// No message content or tokens are printed by this setup utility.
if (args.Length == 0 || args[0] is not ("connect" or "check"))
{
    Console.WriteLine("Usage: Habitat.Gmail.Setup connect <desktop-client.json> [authorization-url-file] | check");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
string? authorizationFile = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
try
{
    using var auth = new GmailAuthService(new GmailCredentialStore(), openBrowser: authorizationFile is null ? null : url =>
    {
        File.WriteAllText(authorizationFile, url);
        Console.WriteLine("Authorization URL ready. Complete Google sign-in in your browser within five minutes.");
        return Task.CompletedTask;
    });
    if (args[0] == "connect")
    {
        if (args.Length < 2) throw new ArgumentException("A Desktop app client JSON is required.");
        var file = new FileInfo(args[1]);
        if (file.Length > 65536) throw new InvalidDataException("The Google client file is too large.");
        auth.Configure(GmailClientConfiguration.FromJson(File.ReadAllText(file.FullName)));
        await auth.SignInAsync(cancellation.Token);
    }
    using var gmail = new GmailService(auth);
    var inbox = await gmail.GetUnreadAsync(true, null, cancellation.Token);
    Console.WriteLine($"Gmail connected. Inbox unread messages: {inbox.UnreadCount}. Credentials encrypted for this Windows user.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Google sign-in cancelled or timed out. Run connect again to retry.");
    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
finally
{
    if (authorizationFile is not null && File.Exists(authorizationFile)) File.Delete(authorizationFile);
}
