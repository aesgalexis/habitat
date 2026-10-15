namespace Habitat.Services;

internal enum ComposerDestination { OpenAI, Google, YouTube, Notepad, Translate, Gmail, Terminal, GoogleMaps, Codex }

internal static class ComposerDestinations
{
    public static ComposerDestination Normalize(ComposerDestination destination) =>
        Enum.IsDefined(destination) ? destination : ComposerDestination.OpenAI;

    public static ComposerDestination Cycle(ComposerDestination destination, int steps)
    {
        var count = Enum.GetValues<ComposerDestination>().Length;
        return (ComposerDestination)(((int)Normalize(destination) + steps % count + count) % count);
    }

    public static ComposerDestination CycleFromWheel(ComposerDestination destination, int steps) => Cycle(destination, -steps);

    public static string Placeholder(ComposerDestination destination) => Normalize(destination) switch
    {
        ComposerDestination.Google => "Search Google…",
        ComposerDestination.YouTube => "Search YouTube…",
        ComposerDestination.Notepad => "Write a new note…",
        ComposerDestination.Translate => "Translate with Google…",
        ComposerDestination.Gmail => "Draft a new email…",
        ComposerDestination.Terminal => "Enter a PowerShell command…",
        ComposerDestination.GoogleMaps => "Search places or addresses…",
        ComposerDestination.Codex => "Write an instruction for Codex…",
        _ => "Message OpenAI…"
    };

    public static string Label(ComposerDestination destination) => destination == ComposerDestination.GoogleMaps
        ? "Google Maps" : destination.ToString();

    public static Uri? SearchUri(ComposerDestination destination, string text, string? gmailAccount = null) => destination switch
    {
        ComposerDestination.Google => new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(text)),
        ComposerDestination.YouTube => new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text)),
        ComposerDestination.Translate => new Uri("https://translate.google.com/?sl=auto&tl=" +
            Uri.EscapeDataString(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) +
            "&text=" + Uri.EscapeDataString(text) + "&op=translate"),
        ComposerDestination.Gmail => new Uri("https://mail.google.com/mail/?view=cm&fs=1&body=" + Uri.EscapeDataString(text) +
            (string.IsNullOrWhiteSpace(gmailAccount) ? "" : "&authuser=" + Uri.EscapeDataString(gmailAccount))),
        ComposerDestination.GoogleMaps => new Uri("https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(text)),
        _ => null
    };
}
