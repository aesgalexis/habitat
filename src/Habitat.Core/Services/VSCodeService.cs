using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Habitat.Services;

internal sealed record VSCodeFolder(string Name, string Path);
internal sealed record VSCodeEndpoint(string SessionId, string Token, int Port);
internal sealed record VSCodeContext(string SessionId, string ContextId, string Workspace,
    VSCodeFolder[] Folders, string? ActivePath, long LastFocused, bool Trusted, bool Remote);
internal sealed record VSCodeProject(VSCodeEndpoint Endpoint, VSCodeContext Context, VSCodeFolder Folder)
{
    public string Id => Endpoint.SessionId + "|" + Folder.Path;
    public string Label => $"{Folder.Name} — {Context.Workspace}";
}
internal sealed record VSCodeRunResult(bool Accepted, string Message, string? ExecutionId = null);

internal static class VSCodeProjects
{
    public static VSCodeProject? Active(IReadOnlyList<VSCodeProject> projects)
    {
        var window = projects.OrderByDescending(project => project.Context.LastFocused).FirstOrDefault();
        if (window?.Context.LastFocused == 0 && projects.Select(project => project.Endpoint.SessionId).Distinct().Count() > 1)
            return null;
        return window is null ? null : projects.FirstOrDefault(project => project.Endpoint.SessionId == window.Endpoint.SessionId &&
            project.Folder.Path == window.Context.ActivePath);
    }
}

internal sealed class VSCodeService : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly HttpClient _http;
    public VSCodeService(string directory, HttpMessageHandler? handler = null)
    {
        _directory = directory;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<IReadOnlyList<VSCodeProject>> DiscoverAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(_directory)) return [];
        var files = Directory.EnumerateFiles(_directory, "*.json").Take(128).ToArray();
        // Dead descriptors from crashed windows never supply project context.
        var windows = await Task.WhenAll(files.Select(async file =>
        {
            try
            {
                if (new FileInfo(file).Length > 4096) return Array.Empty<VSCodeProject>();
                var endpoint = JsonSerializer.Deserialize<VSCodeEndpoint>(await File.ReadAllTextAsync(file, token), Json);
                if (!Valid(endpoint)) return [];
                using var request = Request(endpoint!, HttpMethod.Get, "/context");
                using var response = await _http.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
                var context = await response.Content.ReadFromJsonAsync<VSCodeContext>(Json, token);
                if (context is null || context.SessionId != endpoint!.SessionId || context.Folders is null ||
                    string.IsNullOrEmpty(context.ContextId)) return [];
                var projects = context.Folders.Where(folder => folder is not null && !string.IsNullOrWhiteSpace(folder.Name) &&
                    Path.IsPathFullyQualified(folder.Path)).Select(folder => new VSCodeProject(endpoint, context, folder)).ToArray();
                // Keep empty/remote windows in focus ordering so an old local
                // project is never mistaken for the active project.
                return projects.Length > 0 ? projects : [new VSCodeProject(endpoint, context, new("No local folder", ""))];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                HttpRequestException or OperationCanceledException or ArgumentException) { return []; }
        }));
        token.ThrowIfCancellationRequested();
        return windows.SelectMany(window => window).OrderByDescending(project => project.Context.LastFocused)
            .ThenByDescending(project => project.Folder.Path == project.Context.ActivePath).ToArray();
    }

    public async Task<VSCodeRunResult> RunAsync(VSCodeProject project, string command, CancellationToken token = default)
    {
        if (!Valid(project.Endpoint) || string.IsNullOrWhiteSpace(command) || !project.Context.Trusted || project.Context.Remote ||
            !Path.IsPathFullyQualified(project.Folder.Path))
            return new(false, "Open a trusted local project in VS Code.");
        using var request = Request(project.Endpoint, HttpMethod.Post, "/run");
        request.Content = JsonContent.Create(new { requestId = Guid.NewGuid().ToString(),
            sessionId = project.Endpoint.SessionId, contextId = project.Context.ContextId, path = project.Folder.Path, command });
        // Node's bridge expects JSON without media-type parameters.
        request.Content.Headers.ContentType = new("application/json");
        try
        {
            using var response = await _http.SendAsync(request, token);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (response.IsSuccessStatusCode && body.RootElement.TryGetProperty("accepted", out var accepted) &&
                accepted.ValueKind == JsonValueKind.True)
                return new(true, "", body.RootElement.TryGetProperty("executionId", out var id) &&
                    Guid.TryParse(id.GetString(), out _) ? id.GetString() : null);
            if (response.StatusCode == HttpStatusCode.Conflict)
                return new(false, "Project changed. Review the project and send again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new(false, "Reconnect VS Code.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException) { }
        return new(false, "Check VS Code before sending again.");
    }

    public async Task<VSCodeProject?> EnsureProjectAsync(VSCodeFolder folder, Func<string, bool> openFolder,
        CancellationToken token = default, TimeSpan? timeout = null)
    {
        var path = VSCodeFolders.Normalize(folder.Path);
        if (path is null || !Directory.Exists(path)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            var projects = await DiscoverAsync(deadline.Token);
            var existing = projects.FirstOrDefault(project => !project.Context.Remote && VSCodeFolders.Same(project.Folder.Path, path));
            if (existing is not null) return existing;
            if (!openFolder(path)) return null;
            while (true)
            {
                await Task.Delay(250, deadline.Token);
                projects = await DiscoverAsync(deadline.Token);
                var opened = projects.FirstOrDefault(project => !project.Context.Remote && VSCodeFolders.Same(project.Folder.Path, path));
                if (opened is not null) return opened;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
    }

    public async Task<bool> OpenAsync(VSCodeProject project, Func<Uri, Task<bool>> launch, CancellationToken token = default)
    {
        if (!Valid(project.Endpoint)) return false;
        try
        {
            using var request = Request(project.Endpoint, HttpMethod.Get, "/focus");
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return false;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!body.RootElement.TryGetProperty("uri", out var value) ||
                !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("vscode" or "vscode-insiders") || uri.Host != "habitat-local.habitat-terminal") return false;
            return await launch(uri);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException) { return false; }
    }

    public async Task<VSCodeExecution?> GetExecutionAsync(VSCodeEndpoint endpoint, string id, CancellationToken token = default)
    {
        if (!Valid(endpoint) || !Guid.TryParse(id, out _)) return null;
        try
        {
            using var request = Request(endpoint, HttpMethod.Get, "/runs/" + Uri.EscapeDataString(id));
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return null;
            var snapshot = await response.Content.ReadFromJsonAsync<VSCodeExecution>(Json, token);
            if (snapshot is null || snapshot.Id != id || snapshot.Command is null || snapshot.Path is null ||
                snapshot.Output is null || snapshot.Output.Length > 65536 || snapshot.ElapsedMs < 0 ||
                snapshot.State is not ("starting" or "running" or "completed" or "failed" or "unknown" or "closed") ||
                snapshot.State == "completed" && snapshot.ExitCode != 0 ||
                snapshot.State == "failed" && (snapshot.ExitCode is null or 0)) return null;
            return snapshot;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException) { return null; }
    }

    private static bool Valid(VSCodeEndpoint? endpoint) => endpoint is not null && Guid.TryParse(endpoint.SessionId, out _) &&
        endpoint.Port is > 0 and <= 65535 && endpoint.Token is { Length: 64 } && endpoint.Token.All(Uri.IsHexDigit);
    private static HttpRequestMessage Request(VSCodeEndpoint endpoint, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"http://127.0.0.1:{endpoint.Port}{path}");
        request.Headers.Add("X-Habitat-Key", endpoint.Token);
        return request;
    }
    public void Dispose() => _http.Dispose();
}
