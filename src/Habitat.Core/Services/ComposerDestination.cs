namespace Habitat.Services;

internal enum ComposerDestination { OpenAI, Google, YouTube, Notepad, Translate, Gmail, Terminal, GoogleMaps, Codex, VSCode, Run }

internal static class ComposerDestinations
{
    private static readonly ComposerDestination[] Order =
    [
        ComposerDestination.OpenAI, ComposerDestination.Google, ComposerDestination.YouTube,
        ComposerDestination.Notepad, ComposerDestination.Translate, ComposerDestination.Gmail,
        ComposerDestination.GoogleMaps, ComposerDestination.Codex, ComposerDestination.VSCode, ComposerDestination.Terminal,
        ComposerDestination.Run
    ];
    public static IReadOnlyList<ComposerDestination> Ordered { get; } = Array.AsReadOnly(Order);

    public static ComposerDestination Normalize(ComposerDestination destination) =>
        Enum.IsDefined(destination) ? destination : ComposerDestination.OpenAI;

    public static ComposerDestination Cycle(ComposerDestination destination, int steps)
    {
        var count = Order.Length;
        var index = Array.IndexOf(Order, Normalize(destination));
        return Order[(index + steps % count + count) % count];
    }

    public static ComposerDestination CycleFromWheel(ComposerDestination destination, int steps) => Cycle(destination, -steps);

    public static string Placeholder(ComposerDestination destination) => Normalize(destination) switch
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

    public static string Label(ComposerDestination destination) => destination switch
    {
        ComposerDestination.GoogleMaps => "Google Maps",
        ComposerDestination.VSCode => "VS Code",
        _ => destination.ToString()
    };

    public static string ExternalActionLabel(ComposerDestination destination, bool hasText) => (destination, hasText) switch
    {
        (ComposerDestination.Gmail, _) => "Draft email and open Gmail",
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

    public static Uri? ExternalUri(ComposerDestination destination, string text, string? gmailAccount = null,
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

    public static Uri? SearchUri(ComposerDestination destination, string text, string? gmailAccount = null) => destination switch
    {
        ComposerDestination.Google => new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(text)),
        ComposerDestination.YouTube => new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text)),
        ComposerDestination.Gmail => new Uri("https://mail.google.com/mail/?view=cm&fs=1&body=" + Uri.EscapeDataString(text) +
            (string.IsNullOrWhiteSpace(gmailAccount) ? "" : "&authuser=" + Uri.EscapeDataString(gmailAccount))),
        ComposerDestination.GoogleMaps => GoogleMapsDestination.SearchUri(text),
        _ => null
    };
}
