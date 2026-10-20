namespace Habitat.Services;

// Persist stable IDs rather than positions in the catalog.
internal static class ComposerDestination
{
    public const string OpenAI = "chatgpt", Google = "google-search", YouTube = "youtube",
        Notepad = "notepad", Translate = "translate", Gmail = "gmail", Terminal = "powershell",
        GoogleMaps = "google-maps", Codex = "codex", VSCode = "vscode", Run = "run";
}

internal static class ComposerDestinations
{
    private static readonly string[] Order =
    [
        ComposerDestination.OpenAI, ComposerDestination.Google, ComposerDestination.YouTube,
        ComposerDestination.Notepad, ComposerDestination.Translate, ComposerDestination.Gmail,
        ComposerDestination.GoogleMaps, ComposerDestination.Codex, ComposerDestination.VSCode, ComposerDestination.Terminal,
        ComposerDestination.Run
    ];
    public static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly(Order);

    public static string Normalize(string? destination) =>
        destination is not null && (Order.Contains(destination) || WindowsAppDestination.IsId(destination))
            ? destination : ComposerDestination.OpenAI;

    public static string FromLegacy(int value) => value switch
    {
        0 => ComposerDestination.OpenAI, 1 => ComposerDestination.Google, 2 => ComposerDestination.YouTube,
        3 => ComposerDestination.Notepad, 4 => ComposerDestination.Translate, 5 => ComposerDestination.Gmail,
        6 => ComposerDestination.Terminal, 7 => ComposerDestination.GoogleMaps, 8 => ComposerDestination.Codex,
        9 => ComposerDestination.VSCode, 10 => ComposerDestination.Run, _ => ComposerDestination.OpenAI
    };

    public static string Cycle(string destination, int steps)
    {
        var count = Order.Length;
        var index = Math.Max(0, Array.IndexOf(Order, Normalize(destination)));
        return Order[(index + steps % count + count) % count];
    }

    public static string CycleFromWheel(string destination, int steps) => Cycle(destination, -steps);

    public static string Placeholder(string destination) => Normalize(destination) switch
    {
        ComposerDestination.Google => "Search Google…",
        ComposerDestination.YouTube => "Search YouTube…",
        ComposerDestination.Notepad => "Write a new note…",
        ComposerDestination.Translate => "Enter text to translate…",
        ComposerDestination.Gmail => "Draft a new email…",
        ComposerDestination.Terminal => "Enter a PowerShell command…",
        ComposerDestination.GoogleMaps => "Search places or get directions…",
        ComposerDestination.Codex => "Write an instruction for Codex…",
        ComposerDestination.VSCode => "Enter a command…",
        ComposerDestination.Run => "Run a program or a command…",
        _ => "Message OpenAI…"
    };

    public static string Label(string destination) => destination switch
    {
        ComposerDestination.GoogleMaps => "Google Maps",
        ComposerDestination.VSCode => "VS Code",
        ComposerDestination.OpenAI => "OpenAI", ComposerDestination.Google => "Google",
        ComposerDestination.YouTube => "YouTube", ComposerDestination.Notepad => "Notepad",
        ComposerDestination.Translate => "Translate", ComposerDestination.Gmail => "Gmail",
        ComposerDestination.Terminal => "Terminal", ComposerDestination.Codex => "Codex",
        ComposerDestination.Run => "Run", _ => destination
    };

    public static string ExternalActionLabel(string destination, bool hasText) => (destination, hasText) switch
    {
        (ComposerDestination.Gmail, _) => "Open in browser",
        (ComposerDestination.OpenAI, true) => "Send and open ChatGPT",
        (ComposerDestination.OpenAI, false) => "Open ChatGPT",
        (ComposerDestination.Google or ComposerDestination.YouTube or ComposerDestination.GoogleMaps, true) =>
            $"Search and open {Label(destination)}",
        (ComposerDestination.Translate, true) => "Translate and open Google Translate",
        (ComposerDestination.Translate, false) => "Open Google Translate",
        (ComposerDestination.Codex, true) => "Send and open Codex",
        (ComposerDestination.VSCode, true) => "Run command and open VS Code",
        (ComposerDestination.Terminal, true) => "Run command and open PowerShell…",
        (ComposerDestination.Terminal, false) => "Open PowerShell",
        (ComposerDestination.Run, true) => "Run and open",
        (ComposerDestination.Run, false) => "Open Run",
        _ => $"Open {Label(destination)}"
    };

    public static Uri? ExternalUri(string destination, string text, string? gmailAccount = null,
        string targetLanguage = "en")
    {
        var empty = string.IsNullOrWhiteSpace(text);
        return destination switch
        {
            ComposerDestination.Google when empty => new("https://www.google.com/"),
            ComposerDestination.YouTube when empty => new("https://www.youtube.com/"),
            ComposerDestination.GoogleMaps when empty => new("https://www.google.com/maps/"),
            ComposerDestination.Translate => new("https://translate.google.com/?sl=auto&tl=" +
                Uri.EscapeDataString(targetLanguage) + "&text=" + Uri.EscapeDataString(empty ? "" : text) + "&op=translate"),
            ComposerDestination.Codex when empty => new("codex://threads/new"),
            ComposerDestination.VSCode when empty => new("vscode://"),
            _ => SearchUri(destination, empty ? "" : text, gmailAccount)
        };
    }

    public static Uri? SearchUri(string destination, string text, string? gmailAccount = null) => destination switch
    {
        ComposerDestination.Google => new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(text)),
        ComposerDestination.YouTube => new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text)),
        ComposerDestination.Gmail => Habitat.Models.GmailDraft.DraftsUri(gmailAccount),
        ComposerDestination.GoogleMaps => GoogleMapsDestination.SearchUri(text),
        _ => null
    };
}
