using Habitat;
using Habitat.Services;
using Habitat.Features.Translation;

if (args.Length > 0 && args[0] == "app-server" && args.Contains("--stdio"))
{
    await CodexChatChecks.ReaderServerAsync();
    return;
}

if (args.Contains("--windows-apps-smoke"))
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var applications = await WindowsApps.DiscoverAsync();
    if (applications.Count == 0 || applications.Any(app => !app.IsValid))
        throw new InvalidOperationException("Windows did not return valid installed applications.");
    Console.WriteLine($"PASS: {applications.Count} installed Windows applications discovered without launching any application.");
    return;
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS: {name}");
}

if (args.Contains("--search-browser-smoke"))
{
    await SearchChecks.BrowserSmokeAsync(Check);
    return;
}

if (args.Contains("--youtube-browser-smoke"))
{
    await YouTubeSearchChecks.BrowserSmokeAsync(Check);
    return;
}

if (args.Contains("--vscode-smoke"))
{
    await VSCodeChecks.SmokeAsync(Check);
    return;
}

if (args.Contains("--codex-composer-inspect"))
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var family = args.First(arg => arg.StartsWith("--codex-family="))["--codex-family=".Length..];
    var executable = args.First(arg => arg.StartsWith("--codex-executable="))["--codex-executable=".Length..];
    var desktop = new CodexDesktopAutomation(family, _ => Task.FromResult(false), executable);
    var composers = await desktop.ReadComposersAsync(default);
    var visible = await desktop.ReadVisibleAsync(default);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ComposerCount = composers.Count,
        HasVisibleChat = visible is not null, HasDraft = visible is { IsEmpty: false } }));
    // No navigation, interaction, thread resolution or history reads.
    return;
}

if (args.Contains("--codex-chat-inspect"))
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var family = args.First(arg => arg.StartsWith("--codex-family="))["--codex-family=".Length..];
    var executable = args.First(arg => arg.StartsWith("--codex-executable="))["--codex-executable=".Length..];
    var desktop = new CodexDesktopAutomation(family, _ => Task.FromResult(false), executable, Console.WriteLine);
    var visible = await desktop.ReadVisibleAsync(default);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(visible is null ? null : new {
        visible.Composer.IsNewChat, visible.IsEmpty, visible.CanAccept, visible.Composer.CanSend,
        SendIdLength = visible.Composer.SendId.Length, TextLength = visible.Composer.Text.Length,
        SelectionCount = visible.SelectedTitles.Count }));
    if (visible is not null)
    {
        await using var reader = new CodexReadClient(Path.Combine(Path.GetDirectoryName(executable)!, "resources", "codex.exe"));
        var target = await reader.ResolveAsync(visible.SelectedTitles, default);
        var history = target is null ? null : await reader.ReadAsync(target, default);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { target?.Id, Turns = history?.Turns.Count, LastStatus = history?.Turns.LastOrDefault()?.Status }));
    }
    return;
}

if (args.Contains("--codex-smoke"))
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var family = args.FirstOrDefault(arg => arg.StartsWith("--codex-family="))?["--codex-family=".Length..]
        ?? throw new ArgumentException("The Codex package family is required for this explicit integration check.");
    var executable = args.FirstOrDefault(arg => arg.StartsWith("--codex-executable="))?["--codex-executable=".Length..];
    var marker = "HABITAT_ENTER_" + Guid.NewGuid().ToString("N")[..8];
    var instruction = args.Contains("--codex-multiline")
        ? $"  Integration check {marker}: café ☕ 雪 & #?\r\nSecond line.\nReply only {marker}_OK. Do not use tools, edit files, or change settings.  "
        : $"Integration check {marker}. Reply only {marker}_OK. Do not use tools, edit files, or change settings.";
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var desktop = new CodexDesktopAutomation(family, uri =>
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            { UseShellExecute = true });
        return Task.FromResult(true);
    }, executable, Console.WriteLine);
    var result = await new CodexDelivery(desktop, trace: Console.WriteLine).SendAsync(instruction);
    Console.WriteLine($"Codex integration result: {result}; elapsed={stopwatch.ElapsedMilliseconds} ms; marker={marker}");
    Check(result == CodexDeliveryResult.Sent, "A new real Codex chat receives the synthetic instruction and starts after one Enter");
    return;
}

if (args.Contains("--notepad-smoke"))
{
    var editors = new List<nint>();
    var realNotepad = new NotepadLauncher(insertText: (editor, text) =>
    {
        editors.Add(editor);
        return NotepadLauncher.InsertText(editor, text);
    });
    try
    {
        Check(await realNotepad.OpenAsync("HABITAT Notepad check 1\r\n  café ☕ 雪 Ελληνικά  \nLast line"),
            "Real Notepad creates a new tab and receives exact Unicode text with line breaks");
        Check(await realNotepad.OpenAsync("HABITAT Notepad check 2\nSecond document"),
            "A second send creates another tab without replacing the previous document");
    }
    finally
    {
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "notepad-smoke-editors.txt"),
            editors.Select(editor => editor.ToString()));
    }
    return;
}

var destinations = ComposerDestinations.Ordered;
await DestinationCatalogChecks.RunAsync(Check);
await CodexDeliveryChecks.RunAsync(Check);
await CodexChatChecks.RunAsync(Check);
await CodexScheduleChecks.RunAsync(Check);
CodexRetentionChecks.Run(Check);
await VSCodeChecks.RunAsync(Check);
await ExternalDestinationChecks.RunAsync(Check);
ComposerDraftChecks.Run(Check);
await ComposerCoordinatorChecks.RunAsync(Check);
RunChecks.Run(Check);
Check(destinations.Select((destination, index) => ComposerDestinations.Cycle(destination, 1) ==
    destinations[(index + 1) % destinations.Count]).All(result => result),
    "Wheel cycles forward through all destinations");
Check(ComposerDestinations.Cycle(ComposerDestination.OpenAI, -1) == destinations[^1] &&
    ComposerDestinations.Cycle(ComposerDestination.Google, -destinations.Count - 1) == ComposerDestination.OpenAI,
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
Check(ComposerDestinations.SearchUri(ComposerDestination.Translate, query) is null,
    "Translate never creates a website or browser search");
await TranslationChecks.RunAsync(Check);
await SearchChecks.RunAsync(Check);
await YouTubeSearchChecks.RunAsync(Check);
if (args.Contains("--translation-browser-smoke")) await TranslationChecks.BrowserSmokeAsync(Check);
Check(ComposerDestinations.SearchUri(ComposerDestination.Gmail, query)!.AbsoluteUri ==
    "https://mail.google.com/mail/?authuser=#drafts",
    "Gmail browser links open saved drafts without creating another message");
Check(ComposerDestinations.SearchUri(ComposerDestination.Gmail, query, "person+test@example.com")!.Query.EndsWith(
    "authuser=person%2Btest%40example.com"), "Gmail uses the connected account when available");
Check(ComposerDestinations.SearchUri(ComposerDestination.GoogleMaps, query)!.AbsoluteUri ==
    "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(query),
    "Google Maps opens a correctly encoded place or address search");
Check(ComposerDestinations.Label(ComposerDestination.GoogleMaps) == "Google Maps",
    "Google Maps uses its readable English label");
GoogleMapsChecks.Run(Check);
var path = Path.Combine(AppContext.BaseDirectory, "isolated-preferences", "preferences.json");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":35,\"TextOnly\":false}");
var legacy = Preferences.Load(path);
Check(legacy.Destination == ComposerDestination.OpenAI && !legacy.TextOnly,
    "Existing preferences default to OpenAI and preserve chat options");
foreach (var destination in ComposerDestinations.Ordered.ToArray())
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
