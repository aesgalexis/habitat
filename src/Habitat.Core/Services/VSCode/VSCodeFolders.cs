using System.Diagnostics;

namespace Habitat.Services;

internal sealed record VSCodeProjectChoice(VSCodeFolder? Folder, VSCodeProject? Project)
{
    public bool IsPicker => Folder is null;
    public string Label => Folder is null ? UiText.Get("ChooseFolder") : Project?.Label ?? Folder.Name;
}

internal static class VSCodeFolders
{
    public static string? Normalize(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static bool Same(string? left, string? right) => Normalize(left) is { } path &&
        string.Equals(path, Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static string[] Remember(IEnumerable<string>? folders, string path) => new[] { path }.Concat(folders ?? [])
        .Select(Normalize).Where(value => value is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();

    public static IReadOnlyList<VSCodeProjectChoice> Choices(IReadOnlyList<VSCodeProject> projects, IEnumerable<string> recent)
    {
        var result = projects.Where(project => !project.Context.Remote && Normalize(project.Folder.Path) is not null)
            .Select(project => new VSCodeProjectChoice(project.Folder, project)).ToList();
        foreach (var path in recent.Select(Normalize).Where(path => path is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
        {
            if (result.Any(choice => Same(choice.Folder!.Path, path))) continue;
            var name = Path.GetFileName(path);
            result.Add(new(new(name.Length == 0 ? path : name, path), null));
        }
        result.Add(new(null, null));
        return result;
    }
}

internal sealed class VSCodeFolderLauncher
{
    private readonly string? _executable;
    private readonly Func<ProcessStartInfo, bool> _start;

    public VSCodeFolderLauncher(string? executable = null, Func<ProcessStartInfo, bool>? start = null)
    {
        _executable = executable ?? new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe")
        }.FirstOrDefault(File.Exists);
        _start = start ?? (info => { using var process = Process.Start(info); return process is not null; });
    }

    public bool Open(string path)
    {
        var folder = VSCodeFolders.Normalize(path);
        if (folder is null || !Directory.Exists(folder) || string.IsNullOrEmpty(_executable)) return false;
        var info = new ProcessStartInfo(_executable) { UseShellExecute = true, WorkingDirectory = folder };
        info.ArgumentList.Add("--new-window");
        info.ArgumentList.Add(folder);
        return _start(info);
    }
}
