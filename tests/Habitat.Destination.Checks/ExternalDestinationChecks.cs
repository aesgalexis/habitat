using System.Net;
using Habitat.Services;

internal static class ExternalDestinationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string text = "  café ☕ & #?\r\nsecond line  ";
        var gmail = ComposerDestinations.ExternalUri(ComposerDestination.Gmail, text, "user+test@example.com")!;
        check(gmail.Fragment == "#drafts" && !gmail.Query.Contains("body=") &&
            gmail.AbsoluteUri.Contains("authuser=user%2Btest%40example.com"), "Opening Gmail shows saved drafts in the selected account without composing another message");
        var translation = ComposerDestinations.ExternalUri(ComposerDestination.Translate, text, targetLanguage: "el")!;
        check(translation.Query.Contains("sl=auto&tl=el&text=" + Uri.EscapeDataString(text)), "External translation preserves source text and selected target language");
        foreach (var destination in new[] { ComposerDestination.Google, ComposerDestination.YouTube, ComposerDestination.GoogleMaps })
            check(ComposerDestinations.ExternalUri(destination, text) == ComposerDestinations.SearchUri(destination, text), $"External {destination} uses the destination action with current text");
        check(ComposerDestinations.ExternalUri(ComposerDestination.GoogleMaps, "ruta de Madrid a Barcelona")!.AbsolutePath.Contains("/dir/"),
            "External Maps preserves directions instead of turning routes into place searches");
        check(ComposerDestinations.ExternalUri(ComposerDestination.Gmail, "")!.Fragment == "#drafts" &&
            ComposerDestinations.ExternalUri(ComposerDestination.Google, " ") == new Uri("https://www.google.com/") &&
            ComposerDestinations.ExternalUri(ComposerDestination.YouTube, "") == new Uri("https://www.youtube.com/") &&
            ComposerDestinations.ExternalUri(ComposerDestination.Codex, "") == new Uri("codex://threads/new"),
            "Empty external actions open starting pages, saved Gmail drafts or a blank Codex chat");
        var project = new VSCodeProject(new(Guid.NewGuid().ToString(), new string('a', 64), 12345),
            new("session", "context", "Workspace", [], null, 1, true, false), new("Project", Path.GetFullPath(".")));
        var handler = new FocusHandler();
        using var vscode = new VSCodeService("unused", handler);
        Uri? opened = null;
        check(await vscode.OpenAsync(project, uri => { opened = uri; return Task.FromResult(true); }) &&
            opened?.Query == "?windowId=17&session=one" && handler.Route == "/focus",
            "VS Code obtains a fresh authenticated window URI and opens it without running another command");
        handler.Json = "{\"uri\":\"https://example.com/\"}";
        check(!await vscode.OpenAsync(project, _ => throw new Exception("Unexpected URI launch")), "VS Code rejects unrelated focus URI schemes and destinations");
        handler.Json = "{\"uri\":\"vscode://another.extension/focus\"}";
        check(!await vscode.OpenAsync(project, _ => throw new Exception("Unexpected URI launch")), "VS Code window handoff stays scoped to HABITAT Terminal");
    }

    private sealed class FocusHandler : HttpMessageHandler
    {
        public string Json = "{\"uri\":\"vscode://habitat-local.habitat-terminal/focus?windowId=17&session=one\"}";
        public string? Route;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Route = request.RequestUri!.AbsolutePath;
            if (!request.Headers.Contains("X-Habitat-Key")) throw new Exception("Unauthenticated focus request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) });
        }
    }
}
