namespace Habitat.Services;

internal sealed class SidebarCatalog
{
    public const string Weather = "weather";
    public const string Gmail = "gmail";
    public const string Tasks = "tasks";
    public const string Markets = "markets";
    public static IReadOnlyList<string> Available { get; } = Array.AsReadOnly(new[] { Gmail, Tasks, Markets });
    public static string[] Defaults => [Gmail, Tasks];
    public IReadOnlyList<string> Enabled { get; }
    public IEnumerable<string> Ordered => new[] { Weather }.Concat(Enabled);

    public SidebarCatalog(IEnumerable<string>? enabled = null)
    {
        Enabled = (enabled ?? Defaults).Where(id => Available.Contains(id))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string Label(string id) => UiText.Get(id switch
    {
        Weather => "SidebarWeather", Gmail => "SidebarGmail", Tasks => "Tasks", Markets => "Markets",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    });
}
