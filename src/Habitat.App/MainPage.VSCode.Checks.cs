#if HABITAT_UI_CHECKS
using Habitat.Features.Destinations;
using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckVSCodeUiAsync(string logPath)
    {
        File.AppendAllText(logPath, "Checking VS Code layout and execution views\n");
        var original = _destinationSections[ComposerDestination.VSCode];
        var folder = new VSCodeFolder("Synthetic project", Path.Combine(_services.Paths.LocalDirectory, "vscode-project"));
        var endpoint = new VSCodeEndpoint(Guid.NewGuid().ToString(), new string('a', 64), 12345);
        var context = new VSCodeContext(endpoint.SessionId, "one", "Synthetic workspace", [folder], folder.Path, 10, true, false);
        var project = new VSCodeProject(endpoint, context, folder);
        IReadOnlyList<VSCodeProject> projects = [project];
        var deliveries = 0;
        TaskCompletionSource<VSCodeRunResult>? pending = null;
        VSCodeProject? deliveredProject = null;
        string? deliveredText = null;
        using var section = new VSCodeSection(_ => Task.FromResult(projects), (target, text, _) =>
        {
            deliveries++; deliveredProject = target; deliveredText = text;
            return pending?.Task ?? Task.FromResult(new VSCodeRunResult(true, ""));
        }, _ => { });
        section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        _destinationSections[ComposerDestination.VSCode] = section;
        try
        {
            SelectDestination(ComposerDestination.VSCode);
            await section.RefreshAsync();
            await Task.Delay(80);
            UpdateWritingLayout();
            var controls = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.ScrollViewer)section.View).Content;
            var directory = (Microsoft.UI.Xaml.Controls.TextBlock)controls.Children[1];
            var directoryPosition = directory.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
            var editorPosition = Composer.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
            if (section.HasContent || _destinationContent.Visibility != Microsoft.UI.Xaml.Visibility.Visible ||
                Composer.VerticalAlignment != Microsoft.UI.Xaml.VerticalAlignment.Top ||
                Composer.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Center ||
                Math.Abs(editorPosition.Y + Composer.ActualHeight / 2 - _root.ActualHeight / 2) > 1 ||
                !ReferenceEquals(_destinationContent.Content, section.View) ||
                section.ProjectSelector.ActualWidth is <= 0 or > 220 || directory.ActualHeight <= 0 ||
                directoryPosition.Y + directory.ActualHeight > _destinationContent.Margin.Top + _destinationContent.ActualHeight ||
                _destinationContent.Margin.Top + _destinationContent.ActualHeight > editorPosition.Y - 16 ||
                section.SelectedProject != project || !section.PathText.Contains(folder.Path) ||
                SendButton.Content.ToString() != "Run in VS Code")
                throw new InvalidOperationException("VS Code must show its compact project selector and directory above the centered editor before the first send.");
            var otherFolder = new VSCodeFolder("Other project", Path.Combine(_services.Paths.LocalDirectory, "other-vscode-project"));
            var otherProject = project with { Folder = otherFolder };
            projects = [project, otherProject];
            await section.RefreshAsync();
            section.SelectProject(otherProject.Id);
            if (section.PathText != otherFolder.Path || section.HasContent || Composer.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Center)
                throw new InvalidOperationException("Choosing a project before sending must update its visible directory and preserve the centered editor.");
            section.SelectProject(project.Id);
            Composer.Text = "npm run dev # café ☕";
            File.AppendAllText(logPath, "PASS: VS Code initial project context and centered layout\n");
            var command = Composer.Text;
            pending = new();
            var first = LaunchDestinationAsync(ComposerDestination.VSCode, command);
            await LaunchDestinationAsync(ComposerDestination.VSCode, command);
            if (deliveries != 1 || deliveredProject != project || deliveredText != command || SendButton.IsEnabled || _terminalConfirmationOpen)
                throw new InvalidOperationException("VS Code must send directly once to the selected project without a confirmation dialog and block pending duplicates.");
            await Task.Delay(80);
            if (!section.HasContent || _destinationContent.Visibility != Microsoft.UI.Xaml.Visibility.Visible ||
                section.ProjectSelector.HorizontalAlignment != Microsoft.UI.Xaml.HorizontalAlignment.Left ||
                section.ProjectSelector.ActualWidth > 220 || section.StatusText.Length != 0 ||
                ((Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.ScrollViewer)section.View).Content).Children.Count != 4)
                throw new InvalidOperationException("VS Code must reveal only a compact selector, path and errors after the first send.");
            pending.SetResult(new(true, ""));
            File.AppendAllText(logPath, "Checking VS Code command result view\n");
            await first;
            if (deliveries != 1 || deliveredProject != project || deliveredText != command || Composer.Text != "" ||
                section.StatusText.Length != 0 || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("VS Code sends exact text to the captured project and clears only on delivery.");
            pending = new();
            Composer.Text = command;
            var second = LaunchDestinationAsync(ComposerDestination.VSCode, command);
            SelectDestination(ComposerDestination.Notepad);
            Composer.Text = "new text";
            pending.SetResult(new(false, "Could not confirm command delivery. Check VS Code before sending again."));
            await second;
            SelectDestination(ComposerDestination.VSCode);
            if (Composer.Text != command || !section.StatusText.Contains("Check VS Code"))
                throw new InvalidOperationException("Uncertain VS Code delivery must retain its own command for review.");
            SelectDestination(ComposerDestination.Notepad);
            if (Composer.Text != "new text") throw new InvalidOperationException("Pending VS Code sends must preserve newer drafts in other modes.");
            SelectDestination(ComposerDestination.VSCode);
            projects = [];
            await section.RefreshAsync();
            var before = deliveries;
            await LaunchDestinationAsync(ComposerDestination.VSCode, command);
            if (deliveries != before || section.SelectedProject is not null || section.SelectedFolder?.Path != folder.Path ||
                !section.StatusText.StartsWith("Could not connect"))
                throw new InvalidOperationException("Disconnected VS Code must retain the selected directory without executing through stale context.");
            projects = [project with { Context = context with { Trusted = false } }];
            await section.RefreshAsync();
            await LaunchDestinationAsync(ComposerDestination.VSCode, command);
            if (deliveries != before || !section.StatusText.Contains("Trust this project"))
                throw new InvalidOperationException("Untrusted VS Code workspaces must remain blocked.");
            await CheckVSCodeExecutionUiAsync(project);
            await CheckVSCodeAutomaticSelectionAsync(project);
            await CheckVSCodeFolderSelectionAsync(project);
            await CheckVSCodeProjectHistoryAsync(project);
            File.AppendAllText(logPath, "PASS: VS Code compact project and directory visible before sending above the centered editor, project selection, direct exact delivery without confirmation, duplicate gating, drafts and recovery\n");
            File.AppendAllText(logPath, "PASS: VS Code independent running/completed/failed states, progress, duration, output preview/expansion, unknown connection and late-result shutdown protection\n");
            File.AppendAllText(logPath, "PASS: choose any folder, retained directory, centered initial editor, canceled selection, direct run after exact project connection and duplicate gating\n");
            File.AppendAllText(logPath, "PASS: project-specific output, links and history; centered unused project; background and late updates stay hidden; reconnecting the same folder restores its history\n");
        }
        finally
        {
            _destinationSections[ComposerDestination.VSCode] = original;
            SelectDestination(ComposerDestination.OpenAI);
            Composer.Text = "";
        }
    }

    private static async Task CheckVSCodeAutomaticSelectionAsync(VSCodeProject project)
    {
        var folder = new VSCodeFolder("Second project", Path.Combine(project.Folder.Path, "second"));
        var endpoint = project.Endpoint with { SessionId = Guid.NewGuid().ToString() };
        var other = new VSCodeProject(endpoint, project.Context with
        { SessionId = endpoint.SessionId, Folders = [folder], ActivePath = folder.Path, LastFocused = 0 }, folder);
        IReadOnlyList<VSCodeProject> projects = [project, other];
        using var section = new VSCodeSection(_ => Task.FromResult(projects),
            (_, _, _) => Task.FromResult(new VSCodeRunResult(true, "")), _ => { });
        await section.RefreshAsync();
        if (section.SelectedProject != project || !await section.RunCommandAsync("synthetic command"))
            throw new InvalidOperationException("The automatic project must accept the first synthetic command.");
        other = other with { Context = other.Context with { LastFocused = 20 } };
        projects = [project, other];
        await section.RefreshAsync();
        if (section.SelectedProject != other)
            throw new InvalidOperationException("Sending a command must preserve automatic following of the active VS Code project.");
        section.SelectProject(project.Id);
        await section.RefreshAsync();
        if (section.SelectedProject != project)
            throw new InvalidOperationException("An explicit project selection must remain pinned when another window takes focus.");
    }

    private async Task CheckVSCodeFolderSelectionAsync(VSCodeProject project)
    {
        var previous = _destinationSections[ComposerDestination.VSCode];
        var folder = new VSCodeFolder("Closed project", Path.Combine(_services.Paths.LocalDirectory, "closed café & project"));
        var opened = project with { Folder = folder, Context = project.Context with { Folders = [folder], ActivePath = folder.Path } };
        string? picked = folder.Path;
        var resolutions = 0;
        var deliveries = 0;
        var remembered = new List<string>();
        TaskCompletionSource<VSCodeProject?>? pending = null;
        using var section = new VSCodeSection(_ => Task.FromResult<IReadOnlyList<VSCodeProject>>([project]),
            (target, text, _) =>
            {
                if (target != opened || text != "npm run dev") throw new InvalidOperationException("Folder delivery was retargeted.");
                deliveries++;
                return Task.FromResult(new VSCodeRunResult(true, ""));
            }, _ => { },
            chooseFolder: () => Task.FromResult<string?>(picked),
            resolveProject: (target, _) =>
            {
                if (target.Path != folder.Path) throw new InvalidOperationException("Opening the wrong directory.");
                resolutions++;
                return pending?.Task ?? Task.FromResult<VSCodeProject?>(project);
            }, rememberFolder: remembered.Add);
        section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        _destinationSections[ComposerDestination.VSCode] = section;
        try
        {
            PresentDestinationSection();
            await section.RefreshAsync();
            if (section.ProjectSelector.Items[section.ProjectSelector.Items.Count - 1] is not VSCodeProjectChoice { IsPicker: true, Label: "Choose folder…" })
                throw new InvalidOperationException("The project selector must include Choose folder….");
            section.ProjectSelector.SelectedIndex = section.ProjectSelector.Items.Count - 1;
            await Task.Delay(80);
            if (section.SelectedFolder?.Path != folder.Path || section.PathText != folder.Path || section.SelectedProject is not null ||
                section.HasContent || resolutions != 0 || deliveries != 0 || !remembered.Contains(folder.Path) ||
                Composer.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Center)
                throw new InvalidOperationException("Selecting a closed folder must retain its path and centered editor without opening or executing.");
            await section.RefreshAsync();
            picked = null;
            await section.BrowseForFolderAsync();
            if (section.SelectedFolder?.Path != folder.Path) throw new InvalidOperationException("Canceling the folder picker must preserve the selection.");
            Composer.Text = "npm run dev";
            pending = new();
            var running = LaunchDestinationAsync(ComposerDestination.VSCode, Composer.Text);
            await LaunchDestinationAsync(ComposerDestination.VSCode, Composer.Text);
            if (!section.IsBusy || section.ProjectSelector.IsEnabled || SendButton.IsEnabled || resolutions != 1 || deliveries != 0 ||
                section.PathText != folder.Path || section.StatusText != "Opening project…")
                throw new InvalidOperationException("Opening a folder must wait, retain the exact directory and reject duplicate commands.");
            pending.SetResult(opened);
            await running;
            if (deliveries != 1 || section.SelectedProject != opened || section.IsBusy)
                throw new InvalidOperationException("A connected folder must execute exactly once without a confirmation dialog.");
            pending = null;
            Composer.Text = "npm run dev";
            await LaunchDestinationAsync(ComposerDestination.VSCode, Composer.Text);
            if (deliveries != 1 || Composer.Text != "npm run dev" || !section.StatusText.StartsWith("Could not connect"))
                throw new InvalidOperationException("A resolver for another project must never receive the selected folder's command.");
        }
        finally
        {
            _destinationSections[ComposerDestination.VSCode] = previous;
            PresentDestinationSection();
            UpdateWritingLayout();
        }
    }

    private async Task CheckVSCodeProjectHistoryAsync(VSCodeProject project)
    {
        var otherFolder = new VSCodeFolder(project.Folder.Name, Path.Combine(_services.Paths.LocalDirectory, "other history project"));
        var other = project with { Folder = otherFolder };
        var closedFolder = Path.Combine(_services.Paths.LocalDirectory, "closed history project");
        IReadOnlyList<VSCodeProject> projects = [project, other];
        var executions = new Dictionary<string, VSCodeExecution>();
        TaskCompletionSource<VSCodeExecution?>? pending = null;
        using var section = new VSCodeSection(_ => Task.FromResult(projects), (target, command, _) =>
        {
            var id = Guid.NewGuid().ToString();
            var isOther = target.Folder.Path == otherFolder.Path;
            executions[id] = new(id, command, target.Folder.Path, "HABITAT history check", isOther ? "failed" : "running",
                100, isOther ? 5100 : null, isOther ? 7 : null, 5000,
                isOther ? "Error in project B\nhttp://localhost:4002/" : "Project A ready\nhttp://localhost:4001/", false);
            return Task.FromResult(new VSCodeRunResult(true, "", id));
        }, exception => throw exception,
            readExecution: (_, id, _) => pending?.Task ?? Task.FromResult<VSCodeExecution?>(executions[id]),
            chooseFolder: () => Task.FromResult<string?>(closedFolder));
        var previous = _destinationSections[ComposerDestination.VSCode];
        section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        _destinationSections[ComposerDestination.VSCode] = section;
        try
        {
            PresentDestinationSection();
            await section.RefreshAsync();
            section.SelectProject(project.Id);
            await section.RunCommandAsync("echo status");
            await section.RefreshExecutionsAsync();
            var first = section.Runs.Single();
            first.ToggleOutput();
            var results = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.StackPanel)
                ((Microsoft.UI.Xaml.Controls.ScrollViewer)section.View).Content).Children[3];
            if (results.Children.Count != 1 || !ReferenceEquals(results.Children[0], first.View) || !first.IsAnimating)
                throw new InvalidOperationException("Project A must initially display its own running command.");

            pending = new();
            var polling = section.RefreshExecutionsAsync();
            section.SelectProject(other.Id);
            await Task.Delay(80);
            if (results.Children.Count != 0 || first.View.Parent is not null || section.HasContent || section.PathText != otherFolder.Path ||
                Composer.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Center)
                throw new InvalidOperationException("Switching to an unused project must detach all previous output and restore the centered editor.");
            executions[first.Snapshot.Id] = executions[first.Snapshot.Id] with { Output = "Late project A output\nhttp://localhost:4001/" };
            pending.SetResult(executions[first.Snapshot.Id]);
            await polling;
            pending = null;
            if (results.Children.Count != 0 || section.HasContent || !first.OutputText.Contains("Late project A"))
                throw new InvalidOperationException("A late response for a hidden project must update its history without appearing in the selected project.");

            await section.RunCommandAsync("echo status");
            await section.RefreshExecutionsAsync();
            var second = section.Runs[0];
            await Task.Delay(80);
            if (section.Runs.Count != 2 || results.Children.Count != 1 || !ReferenceEquals(results.Children[0], second.View) ||
                first.View.Parent is not null || !section.HasContent || !second.OutputText.Contains("Error in project B") ||
                !second.SummaryText.Contains("Failed") || Composer.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Left)
                throw new InvalidOperationException("Project B must show only its own response even when command text and folder names match.");
            executions[first.Snapshot.Id] = executions[first.Snapshot.Id] with
                { State = "completed", ExitCode = 0, EndedAt = 8100, ElapsedMs = 8000, Output = "Project A completed\nhttp://localhost:4001/" };
            await section.RefreshExecutionsAsync();
            if (results.Children.Count != 1 || !ReferenceEquals(results.Children[0], second.View) || first.IsAnimating ||
                !first.SummaryText.StartsWith("Completed"))
                throw new InvalidOperationException("A background project's completion must remain monitored without replacing the selected project's result.");

            var endpoint = project.Endpoint with { SessionId = Guid.NewGuid().ToString() };
            var reconnected = project with { Endpoint = endpoint, Context = project.Context with { SessionId = endpoint.SessionId, ContextId = "reconnected" },
                Folder = project.Folder with { Path = project.Folder.Path.ToUpperInvariant() + Path.DirectorySeparatorChar } };
            projects = [reconnected, other];
            await section.RefreshAsync();
            section.SelectProject(reconnected.Id);
            await Task.Delay(80);
            if (results.Children.Count != 1 || !ReferenceEquals(results.Children[0], first.View) || second.View.Parent is not null ||
                !first.IsExpanded || !first.OutputText.Contains("Project A completed") || !section.HasContent)
                throw new InvalidOperationException("Returning to the same folder in a new VS Code session must restore only its own updated history and expanded output.");
            await section.BrowseForFolderAsync();
            if (results.Children.Count != 0 || section.HasContent || section.PathText != closedFolder)
                throw new InvalidOperationException("Choosing a closed folder must also hide all other projects' history.");
            section.SelectProject(other.Id);
            if (results.Children.Count != 1 || !ReferenceEquals(results.Children[0], second.View) || !section.HasContent)
                throw new InvalidOperationException("Returning to project B must restore its own command history.");
        }
        finally
        {
            _destinationSections[ComposerDestination.VSCode] = previous;
            PresentDestinationSection();
            UpdateWritingLayout();
        }
    }

    private async Task CheckVSCodeExecutionUiAsync(VSCodeProject project)
    {
        var executions = new Dictionary<string, VSCodeExecution>();
        var disconnected = false;
        TaskCompletionSource<VSCodeExecution?>? pending = null;
        using var section = new VSCodeSection(_ => Task.FromResult<IReadOnlyList<VSCodeProject>>([project]),
            (_, command, _) =>
            {
                var id = Guid.NewGuid().ToString();
                executions[id] = new(id, command, project.Folder.Path, "HABITAT synthetic", "running", 100, null, null, 4000,
                    "Ready café ☕\nhttp://localhost:5173/\n", false);
                return Task.FromResult(new VSCodeRunResult(true, "", id));
            }, _ => { }, readExecution: (_, id, _) =>
                pending?.Task ?? Task.FromResult(disconnected ? null : executions[id]));
        var previousSection = _destinationSections[ComposerDestination.VSCode];
        _destinationSections[ComposerDestination.VSCode] = section;
        section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
        SelectDestination(ComposerDestination.VSCode);
        await section.RefreshAsync();
        await section.RunCommandAsync("npm run dev");
        await section.RefreshExecutionsAsync();
        await Task.Delay(80);
        var server = section.Runs.Single();
        if (!server.IsAnimating || !server.SummaryText.StartsWith("Running") || !server.PreviewText.Contains("café ☕") ||
            server.IsExpanded || !server.NeedsPolling || section.IsBusy || server.View.ActualHeight <= 0 ||
            !ReferenceEquals(_destinationContent.Content, section.View))
            throw new InvalidOperationException("Live VS Code execution must show compact progress and output while permitting another command.");
        server.ToggleOutput();
        await Task.Delay(80);
        if (server.OutputText != executions[server.Snapshot.Id].Output)
            throw new InvalidOperationException("Show output must retain the captured command output.");
        await section.RunCommandAsync("npm run site:publish");
        var publish = section.Runs[0];
        executions[publish.Snapshot.Id] = executions[publish.Snapshot.Id] with
            { State = "failed", ExitCode = 7, EndedAt = 5100, ElapsedMs = 5000, Output = "Error: synthetic failure" };
        await section.RefreshExecutionsAsync();
        if (publish.IsAnimating || !publish.SummaryText.Contains("Failed") || !publish.SummaryText.Contains("Exit code 7") ||
            publish.PreviewText != "Error: synthetic failure" || !server.IsAnimating || server.PreviewText.Contains("failure"))
            throw new InvalidOperationException("A VS Code failure must show its error independently of a running server.");
        disconnected = true;
        await section.RefreshExecutionsAsync();
        if (server.IsAnimating || !server.SummaryText.StartsWith("Status unavailable") || !server.OutputText.Contains("Ready"))
            throw new InvalidOperationException("Lost VS Code monitoring must stop progress and retain the last output without claiming success.");
        disconnected = false;
        executions[server.Snapshot.Id] = executions[server.Snapshot.Id] with { State = "completed", ExitCode = 0, EndedAt = 8100, ElapsedMs = 8000 };
        await section.RefreshExecutionsAsync();
        if (server.IsAnimating || !server.SummaryText.StartsWith("Completed") || !server.SummaryText.Contains("00:00:08"))
            throw new InvalidOperationException("Confirmed VS Code completion must show its elapsed duration and stop progress.");
        await section.RunCommandAsync("synthetic pending");
        var late = section.Runs[0];
        pending = new();
        var polling = section.RefreshExecutionsAsync();
        section.Dispose();
        pending.SetResult(executions[late.Snapshot.Id] with { Output = "must not appear" });
        await polling;
        if (late.OutputText == "must not appear") throw new InvalidOperationException("Disposed VS Code views must ignore late monitoring responses.");
        _destinationSections[ComposerDestination.VSCode] = previousSection;
        PresentDestinationSection();
        UpdateWritingLayout();
    }
}
#endif
