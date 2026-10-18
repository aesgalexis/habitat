using Habitat.Services;
using Habitat.Features.VSCode;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Destinations;

internal sealed class VSCodeSection : IDestinationSection
{
    private readonly StackPanel _content = new() { Spacing = 12 };
    private readonly ScrollViewer _view;
    private readonly ComboBox _projects = new()
    {
        Header = "Project", DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 0, MaxWidth = 220
    };
    private readonly TextBlock _path = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 12, Opacity = 0.8 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, Visibility = Visibility.Collapsed };
    private readonly StackPanel _results = new() { Spacing = 12 };
    private readonly List<VSCodeRunView> _runs = [];
    private readonly HashSet<string> _submittedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _executionTimer;
    private readonly Func<VSCodeEndpoint, string, CancellationToken, Task<VSCodeExecution?>> _readExecution;
    private bool _polling;
    private readonly Func<CancellationToken, Task<IReadOnlyList<VSCodeProject>>> _discover;
    private readonly Func<VSCodeProject, string, CancellationToken, Task<VSCodeRunResult>> _run;
    private readonly Action<Exception> _report;
    private readonly Func<VSCodeProject, Task<bool>> _open;
    private readonly Func<Task<bool>> _openDefault;
    private readonly Func<Uri, Task<bool>> _openLink;
    private readonly Func<Task<string?>> _chooseFolder;
    private readonly Func<VSCodeFolder, CancellationToken, Task<VSCodeProject?>> _resolveProject;
    private readonly VSCodeProjectSelection _selection;
    private readonly AsyncOperationScope _operations = new();
    private readonly DispatcherQueueTimer _timer;
    private bool _refreshing, _binding, _disposed;
    private string? _displayedFolder;
    public ComposerDestination Destination => ComposerDestination.VSCode;
    public FrameworkElement View => _view;
    public bool IsBusy { get; private set; }
    public bool HasContent => VSCodeFolders.Normalize(SelectedFolder?.Path) is { } folder && _submittedFolders.Contains(folder);
    public event Action? StateChanged;
    internal VSCodeProject? SelectedProject => _selection.Selected?.Project;
    internal VSCodeFolder? SelectedFolder => _selection.Selected?.Folder;
    internal string StatusText => _status.Text;
    internal string PathText => _path.Text;
    internal ComboBox ProjectSelector => _projects;
    internal IReadOnlyList<VSCodeRunView> Runs => _runs;

    public VSCodeSection(Func<CancellationToken, Task<IReadOnlyList<VSCodeProject>>> discover,
        Func<VSCodeProject, string, CancellationToken, Task<VSCodeRunResult>> run,
        Action<Exception> report,
        Func<VSCodeProject, Task<bool>>? open = null, Func<Task<bool>>? openDefault = null,
        Func<VSCodeEndpoint, string, CancellationToken, Task<VSCodeExecution?>>? readExecution = null,
        Func<Task<string?>>? chooseFolder = null,
        Func<VSCodeFolder, CancellationToken, Task<VSCodeProject?>>? resolveProject = null,
        IEnumerable<string>? recentFolders = null, Action<string>? rememberFolder = null,
        Func<Uri, Task<bool>>? openLink = null)
    {
        _discover = discover; _run = run; _report = report;
        _open = open ?? (_ => Task.FromResult(false));
        _openDefault = openDefault ?? (() => Task.FromResult(false));
        _openLink = openLink ?? (_ => Task.FromResult(false));
        _readExecution = readExecution ?? ((_, _, _) => Task.FromResult<VSCodeExecution?>(null));
        _chooseFolder = chooseFolder ?? (() => Task.FromResult<string?>(null));
        _selection = new VSCodeProjectSelection(recentFolders, rememberFolder);
        _resolveProject = resolveProject ?? ((folder, _) => Task.FromResult(
            _selection.Available.FirstOrDefault(project => VSCodeFolders.Same(project.Folder.Path, folder.Path))));
        _view = new ScrollViewer { Content = _content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _content.Children.Add(_projects);
        _content.Children.Add(_path);
        _content.Children.Add(_status);
        _content.Children.Add(_results);
        AutomationProperties.SetName(_projects, "VS Code project");
        AutomationProperties.SetName(_path, "Working directory");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _projects.SelectionChanged += async (_, _) =>
        {
            if (_binding || _disposed) return;
            if (_projects.SelectedItem is not VSCodeProjectChoice choice) return;
            if (choice.IsPicker)
            {
                _binding = true;
                try { _projects.SelectedItem = _selection.Selected; }
                finally { _binding = false; }
                await BrowseForFolderAsync();
                return;
            }
            _selection.Select(choice);
            ShowProject();
            ShowStatus(SelectedProject is { Context.Trusted: false } ? "Trust this project in VS Code." : "");
        };
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += async (_, _) => { if (!IsBusy) await RefreshAsync(); };
        _executionTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _executionTimer.Interval = TimeSpan.FromSeconds(1);
        _executionTimer.Tick += async (_, _) => await RefreshExecutionsAsync();
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        if (active && !_timer.IsRunning) { _timer.Start(); _ = RefreshAsync(); }
        else if (!active) _timer.Stop();
    }

    internal Task RefreshAsync() => _operations.RunAsync(RefreshCoreAsync);

    private async Task RefreshCoreAsync()
    {
        if (_disposed || IsBusy || _refreshing || _projects.IsDropDownOpen) return;
        _refreshing = true;
        try
        {
            var projects = await _discover(_operations.Token);
            if (_disposed || IsBusy || _projects.IsDropDownOpen) return;
            var previous = _selection.Selected;
            _selection.Refresh(projects);
            BindProjects();
            if (SelectedFolder is null)
                ShowStatus("Choose a project.");
            else if (SelectedProject is { Context.Trusted: false })
                ShowStatus("Trust this project in VS Code.");
            else if (previous?.Project?.Id != SelectedProject?.Id ||
                previous?.Project?.Context.ContextId != SelectedProject?.Context.ContextId || previous?.Folder?.Path != SelectedFolder.Path)
                ShowStatus("");
        }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _report(exception);
                _selection.Refresh([]);
                BindProjects();
                ShowStatus("Could not load VS Code projects.");
            }
        }
        finally { _refreshing = false; }
    }

    private void BindProjects()
    {
        _binding = true;
        try { _projects.ItemsSource = _selection.Choices; _projects.SelectedItem = _selection.Selected; }
        finally { _binding = false; }
        ShowProject();
    }

    internal Task BrowseForFolderAsync() => _operations.RunAsync(BrowseForFolderCoreAsync);

    private async Task BrowseForFolderCoreAsync()
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        _projects.IsEnabled = false;
        StateChanged?.Invoke();
        try
        {
            var path = await _chooseFolder();
            if (_disposed || path is null) return;
            var folder = VSCodeFolders.Normalize(path);
            if (folder is null) { ShowStatus("Choose a local folder."); return; }
            _selection.SelectFolder(folder);
            BindProjects();
            ShowStatus(SelectedProject is { Context.Trusted: false } ? "Trust this project in VS Code." : "");
        }
        catch (Exception exception)
        {
            _report(exception);
            if (!_disposed) ShowStatus("Could not select a folder. Try again.");
        }
        finally
        {
            IsBusy = false;
            if (!_disposed) { _projects.IsEnabled = true; StateChanged?.Invoke(); }
        }
    }

    private void ShowProject()
    {
        _path.Text = SelectedFolder?.Path ?? "";
        var folder = VSCodeFolders.Normalize(SelectedFolder?.Path);
        if (string.Equals(_displayedFolder, folder, StringComparison.OrdinalIgnoreCase)) return;
        _displayedFolder = folder;
        PresentResults();
        _view.ChangeView(null, 0, null, true);
        StateChanged?.Invoke();
    }

    private void PresentResults()
    {
        _results.Children.Clear();
        foreach (var run in _runs.Where(run => VSCodeFolders.Same(run.Snapshot.Path, SelectedFolder?.Path)))
            _results.Children.Add(run.View);
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void SelectProject(string id)
    {
        _projects.SelectedItem = _selection.Choices.FirstOrDefault(choice => choice.Project?.Id == id);
    }

    public async Task SubmitAsync(string text) => await RunCommandAsync(text);

    internal Task<bool> RunCommandAsync(string text, Action? dispatching = null, bool openExternally = false) =>
        _operations.RunAsync(() => RunCommandCoreAsync(text, dispatching, openExternally), false);

    private async Task<bool> RunCommandCoreAsync(string text, Action? dispatching, bool openExternally)
    {
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return false;
        // Capture the folder before changing the layout or opening VS Code.
        var folder = SelectedFolder;
        if (VSCodeFolders.Normalize(folder?.Path) is { } path) _submittedFolders.Add(path);
        IsBusy = true;
        _projects.IsEnabled = false;
        StateChanged?.Invoke();
        try
        {
            if (folder is null) { ShowStatus("Choose a project."); return false; }
            if (SelectedProject is { Context.Trusted: false }) { ShowStatus("Trust this project in VS Code."); return false; }
            var selected = await ResolveProjectAsync(folder);
            if (selected is null || _disposed) return false;
            if (!selected.Context.Trusted) { ShowStatus("Trust this project in VS Code."); return false; }
            dispatching?.Invoke();
            ShowStatus("");
            var result = await _run(selected, text, _operations.Token);
            if (!_disposed) ShowStatus(result.Accepted ? "" : result.Message);
            if (result.Accepted && !_disposed)
            {
                var row = new VSCodeRunView(selected, text, result.ExecutionId, _openLink);
                _runs.Insert(0, row);
                // Prefer retiring completed results, then enforce the same
                // hard bound as the extension's capture history.
                while (_runs.Count > 20 && _runs.LastOrDefault(run => !run.NeedsPolling) is { } old)
                { _runs.Remove(old); }
                while (_runs.Count > 32)
                { _runs.RemoveAt(_runs.Count - 1); }
                PresentResults();
                _executionTimer.Start();
                _ = RefreshExecutionsAsync();
            }
            if (result.Accepted && openExternally && !_disposed)
            {
                // Delivery is final even if opening the window fails. Never
                // label an accepted command as failed or execute it again.
                try
                {
                    if (!await _open(selected)) ShowStatus("Command sent. Could not open VS Code. Update HABITAT Terminal and check VS Code before running again.");
                }
                catch (Exception exception)
                {
                    _report(exception);
                    ShowStatus("Command sent. Could not open VS Code. Check VS Code before running again.");
                }
            }
            return result.Accepted;
        }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { return false; }
        catch (Exception exception)
        {
            _report(exception);
            if (!_disposed) ShowStatus("Check VS Code before sending again.");
            return false;
        }
        finally
        {
            IsBusy = false;
            if (!_disposed) { _projects.IsEnabled = true; StateChanged?.Invoke(); }
        }
    }

    internal Task OpenProjectAsync() => _operations.RunAsync(OpenProjectCoreAsync);

    private async Task OpenProjectCoreAsync()
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        _projects.IsEnabled = false;
        StateChanged?.Invoke();
        try
        {
            var folder = SelectedFolder;
            var selected = folder is null ? null : await ResolveProjectAsync(folder);
            if (_disposed || (folder is not null && selected is null)) return;
            var opened = selected is null ? await _openDefault() : await _open(selected);
            if (!_disposed) ShowStatus(opened ? "" : "Could not open VS Code. Update HABITAT Terminal and try again.");
        }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { }
        catch (Exception exception) { _report(exception); if (!_disposed) ShowStatus("Could not open VS Code. Try again."); }
        finally { IsBusy = false; if (!_disposed) { _projects.IsEnabled = true; StateChanged?.Invoke(); } }
    }

    private async Task<VSCodeProject?> ResolveProjectAsync(VSCodeFolder folder)
    {
        ShowStatus(SelectedProject is null ? "Opening project…" : "Connecting…");
        var project = await _resolveProject(folder, _operations.Token);
        if (_disposed) return null;
        if (project is null || project.Context.Remote || !VSCodeFolders.Same(project.Folder.Path, folder.Path))
        {
            ShowStatus("Could not connect to this project. Check VS Code and try again.");
            return null;
        }
        _selection.Resolved(project);
        BindProjects();
        ShowStatus("");
        return project;
    }

    internal Task RefreshExecutionsAsync() => _operations.RunAsync(RefreshExecutionsCoreAsync);

    private async Task RefreshExecutionsCoreAsync()
    {
        if (_disposed || _polling) return;
        _polling = true;
        try
        {
            var pending = _runs.Where(run => run.NeedsPolling).ToArray();
            var snapshots = await Task.WhenAll(pending.Select(async run =>
            {
                try { return await _readExecution(run.Endpoint, run.Snapshot.Id, _operations.Token); }
                catch (Exception exception) { if (!_disposed) _report(exception); return null; }
            }));
            if (_disposed) return;
            for (var index = 0; index < pending.Length; index++)
            {
                var row = pending[index];
                var snapshot = snapshots[index];
                if (snapshot is not null && snapshot.Id == row.Snapshot.Id && snapshot.Command == row.Snapshot.Command &&
                    string.Equals(snapshot.Path, row.Snapshot.Path, StringComparison.OrdinalIgnoreCase)) row.Update(snapshot);
                else row.Disconnected();
            }
            if (!_runs.Any(run => run.NeedsPolling)) _executionTimer.Stop();
        }
        finally { _polling = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _executionTimer.Stop(); StateChanged = null;
        _ = _operations.StopAsync();
    }

    public Task StopAsync()
    {
        Dispose();
        return _operations.StopAsync();
    }
}
