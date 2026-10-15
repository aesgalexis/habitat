using Habitat;
using Habitat.Services;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS: {name}");
}

var destinations = Enum.GetValues<ComposerDestination>();
Check(destinations.Select((destination, index) => ComposerDestinations.Cycle(destination, 1) ==
    destinations[(index + 1) % destinations.Length]).All(result => result),
    "Wheel cycles forward through all destinations");
Check(ComposerDestinations.Cycle(ComposerDestination.OpenAI, -1) == destinations[^1] &&
    ComposerDestinations.Cycle(ComposerDestination.Google, -destinations.Length - 1) == ComposerDestination.OpenAI,
    "Reverse and multi-notch wheel cycling wrap correctly");
Check(ComposerDestinations.CycleFromWheel(ComposerDestination.OpenAI, 1) == destinations[^1] &&
    ComposerDestinations.CycleFromWheel(ComposerDestination.OpenAI, -1) == ComposerDestination.Google,
    "Ctrl + wheel up goes to the previous destination; wheel down goes to the next");
const string query = "café & cats+dogs #1? 雪 ☕\nnext / https://example.com";
foreach (var destination in new[] { ComposerDestination.Google, ComposerDestination.YouTube })
{
    var uri = ComposerDestinations.SearchUri(destination, query)!;
    var prefix = destination == ComposerDestination.Google
        ? "https://www.google.com/search?q=" : "https://www.youtube.com/results?search_query=";
    Check(uri.AbsoluteUri == prefix + Uri.EscapeDataString(query) &&
        Uri.UnescapeDataString(uri.Query[(uri.Query.IndexOf('=') + 1)..]) == query && uri.Fragment == "",
        $"{destination} correctly encodes Unicode, spaces, newlines and URL delimiters");
}
Check(ComposerDestinations.SearchUri(ComposerDestination.OpenAI, query) is null,
    "OpenAI never creates a browser search");
Check(ComposerDestinations.SearchUri(ComposerDestination.Notepad, query) is null,
    "Notepad never creates a browser search");
var translate = ComposerDestinations.SearchUri(ComposerDestination.Translate, query)!;
Check(translate.Host == "translate.google.com" && translate.Query.StartsWith("?sl=auto&tl=") &&
    translate.Query.Contains("&text=" + Uri.EscapeDataString(query) + "&op=translate") && translate.Fragment == "",
    "Google Translate receives the encoded text with automatic source language detection");
Check(ComposerDestinations.SearchUri(ComposerDestination.Gmail, query)!.AbsoluteUri ==
    "https://mail.google.com/mail/?view=cm&fs=1&body=" + Uri.EscapeDataString(query),
    "Gmail opens a new draft with the exact encoded body and no recipient or send action");
Check(ComposerDestinations.SearchUri(ComposerDestination.Gmail, query, "person+test@example.com")!.Query.EndsWith(
    "&authuser=person%2Btest%40example.com"), "Gmail uses the connected account when available");
Check(ComposerDestinations.SearchUri(ComposerDestination.GoogleMaps, query)!.AbsoluteUri ==
    "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(query),
    "Google Maps opens a correctly encoded place or address search");
Check(ComposerDestinations.Label(ComposerDestination.GoogleMaps) == "Google Maps",
    "Google Maps uses its readable English label");
var path = Path.Combine(AppContext.BaseDirectory, "isolated-preferences", "preferences.json");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":35,\"TextOnly\":false}");
var legacy = Preferences.Load(path);
Check(legacy.Destination == ComposerDestination.OpenAI && !legacy.TextOnly,
    "Existing preferences default to OpenAI and preserve chat options");
foreach (var destination in Enum.GetValues<ComposerDestination>())
{
    (legacy with { Destination = destination }).Save(path);
    Check(Preferences.Load(path).Destination == destination && Preferences.Load(path).Theme == "night",
        $"{destination} survives a preferences reload");
}
File.WriteAllText(path, "{\"Theme\":\"day\",\"Opacity\":15,\"Destination\":99}");
Check(Preferences.Load(path).Destination == ComposerDestination.OpenAI,
    "An unknown saved destination safely falls back to OpenAI");

var noteStart = NotepadLauncher.StartInfo();
Check(noteStart.FileName == "notepad.exe" && noteStart.UseShellExecute && noteStart.ArgumentList.Count == 0 && noteStart.Arguments == "",
    "Notepad launches without a file path or save operation");
const string note = "  café ☕ 雪\r\nLine two\nquotes \" & / --flags\n  ";
var sequence = new List<string>();
var notepad = new NotepadLauncher(() => { sequence.Add("new document"); return Task.FromResult<nint>(123); },
    (editor, text) => { Check(editor == 123 && text == note, "Notepad receives the exact content in the new unsaved editor"); sequence.Add("insert"); return true; });
Check(await notepad.OpenAsync(note) && sequence.SequenceEqual(new[] { "new document", "insert" }),
    "Notepad creates a new document before inserting content");
var inserts = 0;
Check(!await new NotepadLauncher(() => Task.FromResult<nint>(0), (_, _) => { inserts++; return true; }).OpenAsync(note) && inserts == 0,
    "A missing blank editor prevents insertion into an existing document");
Check(!await new NotepadLauncher(() => Task.FromResult<nint>(123), (_, _) => false).OpenAsync(note),
    "Unconfirmed Notepad insertion reports failure so the composer keeps its draft");
Check(NotepadLauncher.NormalizeLineEndings("one\r\ntwo\rthree\nfour") == "one\ntwo\nthree\nfour",
    "Notepad verification handles Windows and Rich Edit line endings");
Check(!await notepad.OpenAsync("one\0two"), "Text that would be truncated by a native string is rejected without opening a document");

System.Diagnostics.ProcessStartInfo? powerShellInfo = null;
const string command = "Write-Output 'café & 雪';\r\nWrite-Output \"$HOME\"";
Check(new PowerShellLauncher(AppContext.BaseDirectory, info => { powerShellInfo = info; return true; }).Run(command),
    "PowerShell launch reports acceptance");
Check(powerShellInfo!.ArgumentList.SequenceEqual(new[] { "-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand",
    Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)) }) &&
    powerShellInfo.WorkingDirectory == AppContext.BaseDirectory && powerShellInfo.UseShellExecute &&
    powerShellInfo.Verb != "runas",
    "PowerShell preserves the command through UTF-16 encoding, keeps output visible and requests no elevation");
Check(!new PowerShellLauncher(AppContext.BaseDirectory, _ => false).Run(command),
    "A rejected PowerShell launch is reported");
