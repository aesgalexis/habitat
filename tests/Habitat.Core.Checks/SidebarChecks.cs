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
        check(fresh.FacebookCardSize == "small", "New profiles use small Facebook contact cards.");
        check(new SidebarCatalog([]).Ordered.SequenceEqual(new[] { "weather" }), "Weather cannot be removed.");
        var normalized = new SidebarCatalog(["markets", "weather", "tasks", "markets", "unknown", null!]);
        check(normalized.Ordered.SequenceEqual(new[] { "weather", "markets", "tasks" }), "Sidebar normalization keeps valid order and fixes Weather first.");
        File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":35}");
        var legacy = Preferences.Load(path);
        check(legacy.FacebookCardSize == "small", "Profiles without a contact size adopt the small default.");
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
        var largeCards = customized with { FacebookCardSize = "large" };
        check(largeCards.Save(path) && Preferences.Load(path).FacebookCardSize == "large" &&
            Preferences.Load(path).SidebarWidgets!.SequenceEqual(customized.SidebarWidgets!), "Large contact cards persist without changing sidebar preferences.");
        check((largeCards with { FacebookCardSize = "small" }).Save(path) && Preferences.Load(path).FacebookCardSize == "small", "Returning to small contact cards persists.");
        foreach (var invalid in new[] { "unknown", "", null! })
        {
            File.WriteAllText(path, JsonSerializer.Serialize(largeCards with { FacebookCardSize = invalid }));
            check(Preferences.Load(path).FacebookCardSize == "small", "Unsupported contact sizes fall back to small cards.");
        }
        foreach (var language in new[] { "en", "es" })
        {
            UiText.SetLanguage(language);
            check(SidebarCatalog.Label(SidebarCatalog.Weather) == "Weather", "Weather stays in English for legacy language preferences.");
            check(SidebarCatalog.Available.All(id => !string.IsNullOrWhiteSpace(SidebarCatalog.Label(id))), "Every optional widget has a label.");
        }
        UiText.SetLanguage("en");
    }
}
