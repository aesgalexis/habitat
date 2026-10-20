namespace Habitat.Services;

internal sealed class DestinationCatalog
{
    public IReadOnlyList<string> Enabled { get; }
    public IReadOnlyList<WindowsAppDestination> WindowsApps { get; }
    public IEnumerable<string> Available => ComposerDestinations.Ordered.Concat(WindowsApps.Select(app => app.Id));

    public DestinationCatalog(IEnumerable<string>? enabled = null, IEnumerable<WindowsAppDestination>? windowsApps = null)
    {
        WindowsApps = (windowsApps ?? []).Where(app => app is not null && app.IsValid)
            .DistinctBy(app => app.Id).ToArray();
        var available = Available.ToHashSet(StringComparer.Ordinal);
        // Null is the legacy profile. An explicit empty list means no destinations.
        Enabled = (enabled ?? ComposerDestinations.Ordered).Where(id => id is not null && available.Contains(id))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public WindowsAppDestination? WindowsApp(string id) => WindowsApps.FirstOrDefault(app => app.Id == id);
    public string Label(string id) => WindowsApp(id)?.Name ?? ComposerDestinations.Label(id);
    public string Placeholder(string id) => WindowsApp(id) is { } app
        ? app.PassText ? $"Enter text for {app.Name}…" : $"Open {app.Name}…"
        : id.Length == 0 ? "Choose your destinations…" : ComposerDestinations.Placeholder(id);
    public string ActionLabel(string id) => WindowsApp(id) is { } app ? $"Open {app.Name}" : "Add destination";
    public string Select(string? preferred) => preferred is not null && Enabled.Contains(preferred)
        ? preferred : Enabled.FirstOrDefault() ?? "";
    public string Cycle(string current, int steps)
    {
        if (Enabled.Count == 0) return "";
        var index = Math.Max(0, Enabled.ToList().IndexOf(current));
        return Enabled[(index + steps % Enabled.Count + Enabled.Count) % Enabled.Count];
    }
}
