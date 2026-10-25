namespace Habitat.Services;

internal sealed record UpdateInstallation(string Root, string Executable, UpdatePackageKind Kind)
{
    internal static UpdateInstallation? Find(string baseDirectory)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var executable = Path.Combine(current, "HABITAT.exe");
        if (!File.Exists(executable)) return null;
        if (File.Exists(Path.Combine(current, "unins000.exe")) && File.Exists(Path.Combine(current, "release.json")))
            return new(current, executable, UpdatePackageKind.Installer);
        for (var parent = new DirectoryInfo(current).Parent; parent is not null; parent = parent.Parent)
        {
            var artifacts = Path.Combine(parent.FullName, "artifacts");
            if (!current.StartsWith(artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(parent.FullName, "HABITAT.vbs"))) continue;
            var pointer = Path.Combine(artifacts, "current-release.txt");
            var active = File.Exists(pointer) ? Path.GetFullPath(Path.Combine(artifacts, File.ReadAllText(pointer).Trim())) : Path.Combine(artifacts, "win-x64");
            if (active.Equals(current, StringComparison.OrdinalIgnoreCase)) return new(parent.FullName, executable, UpdatePackageKind.Zip);
        }
        return null;
    }
}
