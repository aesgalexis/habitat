using Habitat;
using Habitat.Services;
using System.Text.Json;

internal static class SidebarChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "sidebar-preferences.json");
        var fresh = Preferences.Load(path);
        check(fresh.SidebarWidgets!.SequenceEqual(new[] { "gmail", "tasks" }), "New profiles omit Markets.");
        check(new SidebarCatalog([]).Ordered.SequenceEqual(new[] { "weather" }), "Weather cannot be removed.");
        var normalized = new SidebarCatalog(["markets", "weather", "tasks", "markets", "unknown", null!]);
        check(normalized.Ordered.SequenceEqual(new[] { "weather", "markets", "tasks" }), "Sidebar normalization keeps valid order and fixes Weather first.");
        File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":35}");
        var legacy = Preferences.Load(path);
        check(legacy.SidebarWidgets!.SequenceEqual(SidebarCatalog.Available), "Older profiles retain all existing widgets.");
        check(legacy.Save(path) && Preferences.Load(path).SidebarWidgets!.SequenceEqual(SidebarCatalog.Available), "Migration survives a save and reload.");
        var customized = legacy with { SidebarWidgets = ["markets", "tasks"], Markets = new() { Symbols = ["AAPL"], PinnedSymbol = "AAPL" } };
        check(customized.Save(path) && Preferences.Load(path).SidebarWidgets!.SequenceEqual(new[] { "markets", "tasks" }), "Custom sidebar order persists.");
        check((customized with { SidebarWidgets = [] }).Save(path), "Removing every optional widget saves.");
        var hidden = Preferences.Load(path);
        check(hidden.SidebarWidgets!.Length == 0 && hidden.Markets.PinnedSymbol == "AAPL" && hidden.Markets.Symbols.SequenceEqual(new[] { "AAPL" }), "Empty sidebar selection persists without deleting Markets settings.");
        File.WriteAllText(path, JsonSerializer.Serialize(customized with { SidebarWidgets = ["tasks", "bad", "weather", "tasks"] }));
        check(Preferences.Load(path).SidebarWidgets!.SequenceEqual(new[] { "tasks" }), "Stored invalid and duplicate IDs are normalized.");
        File.WriteAllText(path, JsonSerializer.Serialize(customized with { SidebarWidgets = null }));
        check(Preferences.Load(path).SidebarWidgets!.SequenceEqual(SidebarCatalog.Defaults), "Explicit null falls back to safe new defaults.");
        check(new Preferences("hybrid", 15).Save(path) && Preferences.Load(path).SidebarWidgets!.SequenceEqual(SidebarCatalog.Defaults), "Fresh preferences round-trip without enabling Markets.");
        foreach (var language in new[] { "en", "es" })
        {
            UiText.SetLanguage(language);
            check(SidebarCatalog.Label(SidebarCatalog.Weather) == (language == "es" ? "Tiempo" : "Weather"), "Weather label follows the interface language.");
            check(SidebarCatalog.Available.All(id => !string.IsNullOrWhiteSpace(SidebarCatalog.Label(id))), "Every optional widget has a label.");
        }
        UiText.SetLanguage("en");
    }
}
