using System.Globalization;
using System.Text;
using Habitat;
using Habitat.Services;

internal static class LocalizationChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var english = UiText.Catalog("en");
        var spanish = UiText.Catalog("es");
        check(english.Count > 400 && english.Keys.Order().SequenceEqual(spanish.Keys.Order()), "Both interface catalogs cover the same messages.");
        foreach (var (key, value) in english)
        {
            check(!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(spanish[key]), $"Nonempty translations: {key}");
            check(CompositeFormat.Parse(value).MinimumArgumentCount == CompositeFormat.Parse(spanish[key]).MinimumArgumentCount,
                $"Formatting arguments agree in both languages: {key}");
        }
        var profile = Path.Combine(root, "language-preferences.json");
        File.WriteAllText(profile, """{"Theme":"night","Opacity":23,"WeatherCity":"Mi ciudad"}""");
        var legacy = Preferences.Load(profile);
        check(legacy.InterfaceLanguage == "en" && legacy.WeatherCity == "Mi ciudad" && legacy.Opacity == 23, "Legacy preferences retain their values and default to English.");
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var ids = ComposerDestinations.Ordered.ToArray();
            UiText.SetLanguage("es");
            check(UiText.Get("Tasks") == "Tareas" && ComposerDestinations.Placeholder(ComposerDestination.Gmail) == "Redacta un nuevo correo…", "Spanish applies to labels and destination prompts.");
            check(UiText.Format("BackgroundOpacityMessage", 12.5) == "Opacidad del fondo · 13%" && UiText.Culture.Name == "es-ES", "Visible formatting uses the interface culture.");
            check(ComposerDestinations.Ordered.SequenceEqual(ids) && ComposerDestination.Translate == "translate", "Changing labels preserves destination identifiers.");
            check(CultureInfo.CurrentCulture == originalCulture, "Interface language does not change protocol parsing culture.");
            check((legacy with { InterfaceLanguage = "es" }).Save(profile) && Preferences.Load(profile).InterfaceLanguage == "es", "The chosen interface language survives restart.");
            check((legacy with { InterfaceLanguage = "unsupported" }).Save(profile) && Preferences.Load(profile).InterfaceLanguage == "en", "Unsupported stored languages safely fall back to English.");
            UiText.SetLanguage("unsupported");
            check(UiText.Get("Tasks") == "Tasks" && UiText.Language == "en", "Unsupported active languages use the English catalog.");
            check(UiText.Resolve("Tasks", null) == "Tasks" && UiText.Get("MissingTestKey") == "MissingTestKey", "Fallbacks remain readable when a locale or resource is missing.");
        }
        finally { UiText.SetLanguage("en"); }
    }
}
