using Habitat.Services;

namespace Habitat.Features.VSCode;

// Selection policy is independent of controls and command delivery. Resolving a
// captured folder updates its connection without turning an automatic choice
// into a manual one. Only an explicit user selection pins a folder.
internal sealed class VSCodeProjectSelection(IEnumerable<string>? recentFolders = null, Action<string>? remember = null)
{
    private string[] _recent = (recentFolders ?? []).ToArray();
    private string? _selectedId, _selectedPath;
    private bool _followActive = true;
    internal IReadOnlyList<VSCodeProject> Available { get; private set; } = [];
    internal IReadOnlyList<VSCodeProjectChoice> Choices { get; private set; } = [];
    internal VSCodeProjectChoice? Selected { get; private set; }

    internal void Refresh(IReadOnlyList<VSCodeProject> projects)
    {
        Available = projects;
        Bind();
    }

    internal void Select(VSCodeProjectChoice choice)
    {
        if (choice.Folder is null) return;
        _followActive = false;
        Selected = choice;
        _selectedId = choice.Project?.Id;
        _selectedPath = choice.Folder.Path;
        Remember(choice.Folder.Path);
    }

    internal void SelectFolder(string path)
    {
        var folder = VSCodeFolders.Normalize(path) ?? throw new ArgumentException("Choose a local folder.", nameof(path));
        Remember(folder);
        _followActive = false;
        _selectedId = null;
        _selectedPath = folder;
        Bind();
    }

    internal void Resolved(VSCodeProject project)
    {
        Available = Available.Where(candidate => candidate.Id != project.Id).Append(project).ToArray();
        Bind();
    }

    private void Bind()
    {
        var active = _followActive ? VSCodeProjects.Active(Available) : null;
        if (active is not null) Remember(active.Folder.Path);
        Choices = VSCodeFolders.Choices(Available, _recent);
        Selected = _followActive
            ? Choices.FirstOrDefault(choice => choice.Project == active && !choice.IsPicker && active is not null)
                ?? (Available.Count == 0 ? Choices.FirstOrDefault(choice => !choice.IsPicker) : null)
            : Choices.FirstOrDefault(choice => choice.Project is not null && choice.Project.Id == _selectedId)
                ?? Choices.FirstOrDefault(choice => choice.Folder is not null && VSCodeFolders.Same(choice.Folder.Path, _selectedPath));
        if (Selected?.Folder is { } folder)
        {
            _selectedId = Selected.Project?.Id;
            _selectedPath = folder.Path;
        }
    }

    private void Remember(string path)
    {
        if (_recent.Any(recent => VSCodeFolders.Same(recent, path))) return;
        _recent = VSCodeFolders.Remember(_recent, path);
        remember?.Invoke(path);
    }
}
