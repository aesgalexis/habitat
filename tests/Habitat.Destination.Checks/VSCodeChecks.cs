using System.Net;
using System.Text.Json;
using Habitat;
using Habitat.Services;
using Habitat.Features.VSCode;

internal static class VSCodeChecks
{
    public static async Task SmokeAsync(Action<bool, string> check)
    {
        // Opt-in check from the normal Windows session only. Commands write
        // synthetic receipts under this repository's artifacts directory.
        var repository = Path.GetFullPath(Environment.CurrentDirectory);
        using var service = new VSCodeService(Path.Combine(AppDataPaths.Production.LocalDirectory, "vscode"));
        var project = VSCodeProjects.Active(await service.DiscoverAsync());
        check(project is not null && string.Equals(Path.GetFullPath(project.Folder.Path), repository, StringComparison.OrdinalIgnoreCase),
            "The real VS Code bridge identifies this repository as the active local project");
        var output = Path.Combine(repository, "artifacts", "vscode-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var parents = new List<int>();
        for (var index = 1; index <= 2; index++)
        {
            var receipt = Path.Combine(output, $"terminal-{index}.json");
            var script = "require('node:fs').writeFileSync(" + JsonSerializer.Serialize(receipt) +
                ",JSON.stringify({cwd:process.cwd(),parent:process.ppid,marker:'HABITAT synthetic terminal check'}));" +
                "console.log('HABITAT live output café');console.log('http://localhost:5173/');" +
                (index == 1 ? "setTimeout(()=>{console.log('HABITAT completed');process.exit(0)},1500)" :
                    "console.error('HABITAT synthetic error');process.exit(7)");
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script));
            var command = "node -e \"eval(Buffer.from('" + encoded + "','base64').toString('utf8'))\"";
            var result = await service.RunAsync(project!, command);
            check(result.Accepted, $"Real VS Code accepts synthetic command {index}");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!File.Exists(receipt) && DateTimeOffset.UtcNow < deadline) await Task.Delay(200);
            check(File.Exists(receipt), $"Real VS Code terminal {index} executes the synthetic command");
            using var body = JsonDocument.Parse(await File.ReadAllTextAsync(receipt));
            check(string.Equals(body.RootElement.GetProperty("cwd").GetString(), project!.Folder.Path, StringComparison.OrdinalIgnoreCase),
                $"Real VS Code terminal {index} starts in the selected project directory");
            parents.Add(body.RootElement.GetProperty("parent").GetInt32());
            check(result.ExecutionId is not null, "The installed VS Code extension supports execution monitoring");
            VSCodeExecution? execution = null;
            deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                execution = await service.GetExecutionAsync(project.Endpoint, result.ExecutionId!);
                if (execution?.EndedAt is not null) break;
                await Task.Delay(200);
            }
            check(execution?.State == (index == 1 ? "completed" : "failed") && execution.ExitCode == (index == 1 ? 0 : 7),
                $"Real VS Code terminal {index} reports its actual completion and exit code");
            check(execution!.Output.Contains("HABITAT live output café") && execution.Links.Any(uri => uri.Host == "localhost") &&
                (index == 1 || execution.Output.Contains("HABITAT synthetic error")),
                $"Real VS Code terminal {index} returns Unicode output, links and error text");
        }
        check(parents[0] != parents[1], "Two real VS Code sends use different fresh shell processes");
        Console.WriteLine($"Synthetic receipts: {output}");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-vscode");
        Directory.CreateDirectory(root);
        var endpoint = new VSCodeEndpoint(Guid.NewGuid().ToString(), new string('a', 64), 12345);
        var folder = new VSCodeFolder("café project", Path.Combine(root, "café project"));
        var secondFolder = new VSCodeFolder("other", Path.Combine(root, "other"));
        var context = new VSCodeContext(endpoint.SessionId, "context-one", "Two folders", [folder, secondFolder], folder.Path, 42, true, false);
        File.WriteAllText(Path.Combine(root, "window.json"), JsonSerializer.Serialize(endpoint));
        File.WriteAllText(Path.Combine(root, "corrupt.json"), "{");
        File.WriteAllText(Path.Combine(root, "invalid-port.json"), JsonSerializer.Serialize(endpoint with { Port = 0 }));
        var handler = new Handler(context);
        using var service = new VSCodeService(root, handler);
        var projects = await service.DiscoverAsync();
        check(projects.Count == 2 && projects[0].Folder == folder && projects[0].Endpoint == endpoint,
            "VS Code discovers authenticated local projects and ignores invalid descriptors");
        check(handler.LastUri == new Uri("http://127.0.0.1:12345/context") && handler.LastKey == endpoint.Token,
            "VS Code discovery stays on authenticated loopback endpoints");
        check(VSCodeProjects.Active(projects)?.Folder == folder, "VS Code follows the active folder in a multi-root workspace");
        var newerEndpoint = endpoint with { SessionId = Guid.NewGuid().ToString() };
        var newer = new VSCodeProject(newerEndpoint, context with { SessionId = newerEndpoint.SessionId, LastFocused = 100, ActivePath = secondFolder.Path }, secondFolder);
        check(VSCodeProjects.Active([projects[0], newer]) == newer, "VS Code follows the most recently focused window");
        var selection = new VSCodeProjectSelection();
        selection.Refresh([projects[0]]);
        selection.Resolved(projects[0]);
        selection.Refresh([projects[0], newer]);
        check(selection.Selected?.Project == newer, "Resolving a command's project preserves automatic focus following");
        selection.Select(selection.Choices.First(choice => choice.Project == projects[0]));
        selection.Resolved(projects[0]);
        selection.Refresh([projects[0], newer]);
        check(selection.Selected?.Project == projects[0], "Explicit project selection stays pinned across sends and focus changes");
        selection.Refresh([]);
        check(selection.Selected?.Folder == folder && selection.Selected.Project is null,
            "A pinned project retains its directory after disconnecting without retaining a stale endpoint");
        var reconnected = projects[0] with { Endpoint = newerEndpoint };
        selection.Refresh([reconnected, newer]);
        check(selection.Selected?.Project == reconnected, "A pinned folder reconnects to its new VS Code window session");
        selection.SelectFolder(secondFolder.Path);
        selection.Refresh([projects[0], newer]);
        check(selection.Selected?.Folder?.Path == secondFolder.Path, "The folder picker pins its directory independently of window focus");
        var empty = newer with { Context = newer.Context with { ActivePath = null, Folders = [] }, Folder = new("No folder", "") };
        check(VSCodeProjects.Active([projects[0], empty]) is null, "An empty active VS Code window never falls back to an older project");
        check(VSCodeProjects.Active([projects[0] with { Context = context with { ActivePath = null } }]) is null,
            "VS Code requires an explicit folder when a multi-root workspace has no active editor");
        check(VSCodeProjects.Active([projects[0] with { Context = context with { LastFocused = 0 } },
            newer with { Context = newer.Context with { LastFocused = 0 } }]) is null,
            "VS Code does not guess between windows with unknown focus history");
        const string command = "  npm run dev # café ☕\r\nWrite-Output \"$HOME & quotes\"  ";
        var result = await service.RunAsync(projects[0], command);
        check(result.Accepted && result.Message.Length == 0, "VS Code accepts delivery without adding a success explanation");
        using var body = JsonDocument.Parse(handler.LastBody!);
        var firstId = body.RootElement.GetProperty("requestId").GetString();
        check(result.ExecutionId == firstId, "VS Code delivery returns the exact execution ID for later read-only polling");
        check(body.RootElement.GetProperty("command").GetString() == command &&
            body.RootElement.GetProperty("path").GetString() == folder.Path &&
            body.RootElement.GetProperty("contextId").GetString() == context.ContextId &&
            body.RootElement.GetProperty("sessionId").GetString() == endpoint.SessionId,
            "VS Code sends the exact command, selected directory and captured window context");
        await service.RunAsync(projects[0], "npm run site:publish");
        using var secondBody = JsonDocument.Parse(handler.LastBody!);
        check(firstId != secondBody.RootElement.GetProperty("requestId").GetString(), "Each intentional VS Code send gets a new request ID");
        handler.Snapshot = new VSCodeExecution(firstId!, command, folder.Path, "HABITAT 1", "running", 10, null, null, 4000,
            "Ready café ☕\nhttp://localhost:5173/\n", false);
        var execution = await service.GetExecutionAsync(endpoint, firstId!);
        check(execution?.State == "running" && execution.Output.Contains("café ☕") &&
            handler.LastUri?.AbsolutePath == "/runs/" + firstId && handler.LastKey == endpoint.Token,
            "Read-only VS Code polling preserves live output and stays bound to its authenticated execution");
        check(execution!.IsRunning && execution.Links.Single().AbsoluteUri == "http://localhost:5173/" &&
            execution.Summary.StartsWith("Running"), "A server URL is available while the command remains running");
        handler.Snapshot = handler.Snapshot with { State = "failed", EndedAt = 5010, ElapsedMs = 5000, ExitCode = 7,
            Output = string.Join('\n', Enumerable.Range(0, 30).Select(index => "line " + index)) + "\nError: synthetic failure" };
        execution = await service.GetExecutionAsync(endpoint, firstId!);
        check(execution?.IsRunning == false && execution.Summary.Contains("Exit code 7") &&
            execution.Preview.Contains("synthetic failure") && !execution.Preview.Contains("line 0"),
            "VS Code failure summaries include the exit code and recent error output");
        handler.Snapshot = handler.Snapshot with { State = "completed", ExitCode = null };
        check(await service.GetExecutionAsync(endpoint, firstId!) is null, "An unconfirmed VS Code exit code never becomes completed");
        handler.Snapshot = handler.Snapshot with { Id = Guid.NewGuid().ToString(), ExitCode = 0 };
        check(await service.GetExecutionAsync(endpoint, firstId!) is null, "VS Code rejects output from a different execution");
        handler.Snapshot = handler.Snapshot with { Id = firstId!, Output = new string('x', 65537) };
        check(await service.GetExecutionAsync(endpoint, firstId!) is null, "VS Code rejects oversized execution output");
        handler.Snapshot = handler.Snapshot with { Output = "done", State = "unknown", ExitCode = null };
        check((await service.GetExecutionAsync(endpoint, firstId!))?.Label == "Status unavailable",
            "VS Code reports an unknown exit state without a success indicator");
        handler.Status = HttpStatusCode.Conflict;
        result = await service.RunAsync(projects[0], command);
        check(!result.Accepted && result.Message.Contains("Review the project"), "Stale VS Code contexts require reviewing the project");
        handler.Status = HttpStatusCode.Forbidden;
        check(!(await service.RunAsync(projects[0], command)).Accepted, "VS Code authentication rejection never reports success");
        var count = handler.Count;
        check(!(await service.RunAsync(projects[0] with { Context = context with { Trusted = false } }, command)).Accepted && handler.Count == count,
            "Untrusted VS Code workspaces are blocked before command dispatch");
        check(!(await service.RunAsync(projects[0] with { Context = context with { Remote = true } }, command)).Accepted && handler.Count == count,
            "Remote VS Code workspaces are blocked before command dispatch");
        handler.Throw = true;
        result = await service.RunAsync(projects[0], command);
        check(!result.Accepted && handler.Count == count + 1 && result.Message.Contains("Check VS Code"),
            "An uncertain VS Code delivery is never automatically retried");
        check((await service.DiscoverAsync()).Count == 0, "Disconnected VS Code windows never provide stale project context");
        handler.Throw = false; handler.Status = HttpStatusCode.OK;
        handler.Context = context with { Folders = [], ActivePath = null };
        projects = await service.DiscoverAsync();
        check(projects.Count == 1 && VSCodeProjects.Active(projects) is null,
            "Empty VS Code windows retain focus ordering without becoming executable projects");
        check(ComposerDestinations.Label(ComposerDestination.VSCode) == "VS Code" &&
            ComposerDestinations.SearchUri(ComposerDestination.VSCode, command) is null &&
            ComposerDestinations.FromLegacy(6) == ComposerDestination.Terminal && ComposerDestinations.FromLegacy(8) == ComposerDestination.Codex,
            "VS Code uses its English label and preserves existing destination preference values");
        await CheckFoldersAsync(root, endpoint, context, handler, service, check);
    }

    private static async Task CheckFoldersAsync(string root, VSCodeEndpoint endpoint, VSCodeContext context,
        Handler handler, VSCodeService service, Action<bool, string> check)
    {
        var folder = new VSCodeFolder("closed café & project", Path.Combine(root, "closed café & project"));
        Directory.CreateDirectory(folder.Path);
        var original = new VSCodeProject(endpoint, context, context.Folders[0]);
        var choices = VSCodeFolders.Choices([original], [original.Folder.Path + "\\", folder.Path, folder.Path.ToUpperInvariant(), "relative"]);
        check(choices.Count == 3 && choices[0].Project == original && choices[1].Folder?.Path == folder.Path &&
            choices[1].Project is null && choices[^1].IsPicker && choices[^1].Label == "Choose folder…",
            "The selector merges live and remembered folders without duplicates and always offers Choose folder…");
        var recent = VSCodeFolders.Remember(Enumerable.Range(0, 30).Select(index => Path.Combine(root, "folder" + index)), folder.Path);
        check(recent.Length == 20 && recent[0] == folder.Path && VSCodeFolders.Normalize("relative") is null,
            "Remembered projects stay bounded and use explicit absolute directories");
        var preferencesPath = Path.Combine(root, "folder-preferences.json");
        new Preferences("hybrid", 15) { VSCodeFolders = recent }.Save(preferencesPath);
        check(Preferences.Load(preferencesPath).VSCodeFolders.SequenceEqual(recent),
            "Project folders persist only in HABITAT preferences");
        System.Diagnostics.ProcessStartInfo? launched = null;
        var launcher = new VSCodeFolderLauncher("Code.exe", info => { launched = info; return true; });
        check(launcher.Open(folder.Path) && launched?.FileName == "Code.exe" && launched.WorkingDirectory == folder.Path &&
            launched.ArgumentList.SequenceEqual(new[] { "--new-window", folder.Path }) && launched.Arguments.Length == 0 &&
            !launcher.Open(Path.Combine(root, "missing")) && !launcher.Open("relative"),
            "VS Code receives a literal folder argument, including spaces, Unicode and shell symbols, without a command shell");
        handler.Context = context with { Folders = [folder], ActivePath = folder.Path };
        var opens = 0;
        var resolved = await service.EnsureProjectAsync(folder, _ => { opens++; return true; });
        check(resolved?.Folder == folder && opens == 0, "An already connected folder reuses its VS Code window");
        handler.Context = context;
        var posts = handler.Posts;
        resolved = await service.EnsureProjectAsync(folder, path =>
        {
            opens++;
            check(path == folder.Path, "Only the explicitly selected project is opened");
            handler.Context = context with { Folders = [folder], ActivePath = folder.Path, ContextId = "new-folder" };
            return true;
        }, timeout: TimeSpan.FromSeconds(2));
        check(resolved?.Folder == folder && resolved.Context.ContextId == "new-folder" && opens == 1 && handler.Posts == posts,
            "Opening waits for the selected folder's fresh connection and never dispatches a command itself");
        handler.Context = context;
        resolved = await service.EnsureProjectAsync(folder, _ => { opens++; return true; }, timeout: TimeSpan.FromMilliseconds(350));
        check(resolved is null && opens == 2 && handler.Posts == posts,
            "A different open project is never substituted when the selected folder fails to connect");
        using var cancellation = new CancellationTokenSource();
        var canceled = false;
        try
        {
            await service.EnsureProjectAsync(folder, _ => { cancellation.Cancel(); return true; }, cancellation.Token);
        }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled && handler.Posts == posts, "Closing HABITAT cancels the folder wait without sending or retrying");
        check(!Directory.EnumerateFileSystemEntries(folder.Path).Any(),
            "Selecting and opening a project creates no files, settings or dependencies inside it");
    }

    private sealed class Handler(VSCodeContext context) : HttpMessageHandler
    {
        public VSCodeContext Context = context;
        public Uri? LastUri;
        public string? LastKey, LastBody;
        public int Count, Posts;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Throw;
        public VSCodeExecution? Snapshot;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            LastUri = request.RequestUri;
            LastKey = request.Headers.GetValues("X-Habitat-Key").Single();
            if (Throw) throw new HttpRequestException("Synthetic disconnect");
            if (request.RequestUri!.AbsolutePath.StartsWith("/runs/"))
                return new(Status) { Content = new StringContent(JsonSerializer.Serialize(Snapshot)) };
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Context)) };
            LastBody = await request.Content!.ReadAsStringAsync(token);
            Posts++;
            using var body = JsonDocument.Parse(LastBody);
            return new(Status) { Content = new StringContent(Status == HttpStatusCode.OK ?
                JsonSerializer.Serialize(new { accepted = true, executionId = body.RootElement.GetProperty("requestId").GetString() }) : "{\"error\":\"synthetic\"}") };
        }
    }
}
