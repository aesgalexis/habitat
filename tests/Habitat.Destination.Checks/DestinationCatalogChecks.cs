using System.Diagnostics;
using System.Text.Json;
using Habitat;
using Habitat.Services;
using Habitat.Features.Composer;

internal static class DestinationCatalogChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-destinations", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prefsPath = Path.Combine(root, "preferences.json");
        var legacy = new[] { ComposerDestination.OpenAI, ComposerDestination.Google, ComposerDestination.YouTube,
            ComposerDestination.Notepad, ComposerDestination.Translate, ComposerDestination.Gmail, ComposerDestination.Terminal,
            ComposerDestination.GoogleMaps, ComposerDestination.Codex, ComposerDestination.VSCode, ComposerDestination.Run };
        for (var index = 0; index < legacy.Length; index++)
        {
            File.WriteAllText(prefsPath, $$"""{"Theme":"night","Opacity":37,"Destination":{{index}},"TextOnly":false}""");
            var migrated = Preferences.Load(prefsPath);
            check(migrated.Destination == legacy[index] && migrated.Theme == "night" && !migrated.TextOnly &&
                new DestinationCatalog(migrated.EnabledDestinations).Enabled.SequenceEqual(ComposerDestinations.Ordered),
                $"Legacy destination {index} migrates without changing its selection or options");
        }
        var executable = Path.Combine(root, "Test application 雪.exe");
        File.WriteAllText(executable, "Synthetic file. Never executed.");
        var app = new WindowsAppDestination(WindowsAppDestination.NewId(), "Mi aplicación ☕", executable);
        var catalog = new DestinationCatalog([app.Id, ComposerDestination.Notepad, app.Id, "missing"], [app, app]);
        check(catalog.Enabled.SequenceEqual(new[] { app.Id, ComposerDestination.Notepad }) && catalog.WindowsApps.Count == 1,
            "Custom applications have stable IDs and duplicate or unknown entries cannot enter navigation");
        check(catalog.Cycle(app.Id, -1) == ComposerDestination.Notepad && catalog.Cycle(ComposerDestination.Notepad, 3) == app.Id &&
            catalog.Select(ComposerDestination.Terminal) == app.Id && catalog.Label(app.Id) == "Mi aplicación ☕",
            "Navigation follows the personal order and preserves user supplied names");
        var empty = new DestinationCatalog([], [app]);
        check(empty.Enabled.Count == 0 && empty.Select(app.Id) == "" && empty.Cycle("", int.MinValue) == "" &&
            new DestinationCatalog([app.Id], [app]).Cycle(app.Id, int.MaxValue) == app.Id,
            "Empty and single destination profiles remain usable without restoring hidden destinations");
        var settings = new Preferences("hybrid", 22)
        {
            Destination = app.Id, EnabledDestinations = catalog.Enabled.ToArray(), WindowsApps = [app], DefaultDestination = app.Id
        };
        check(settings.Save(prefsPath), "Custom destination preferences save atomically");
        var saved = Preferences.Load(prefsPath);
        check(saved.Destination == app.Id && saved.DefaultDestination == app.Id && saved.WindowsApps.Single() == app &&
            saved.EnabledDestinations!.SequenceEqual(catalog.Enabled) &&
            File.ReadAllText(prefsPath).Contains("\"Destination\": \"windows:"),
            "Stable IDs, order, application settings and startup choice survive a restart");
        (settings with { EnabledDestinations = [] }).Save(prefsPath);
        saved = Preferences.Load(prefsPath);
        check(new DestinationCatalog(saved.EnabledDestinations, saved.WindowsApps).Enabled.Count == 0 && saved.WindowsApps.Single() == app,
            "Removing an application preserves its configuration and does not re-enable it on restart");
        (settings with { WindowsApps = [app with { Target = "https://example.com" }, app with { Id = "windows:bad" }] }).Save(prefsPath);
        saved = Preferences.Load(prefsPath);
        check(new DestinationCatalog(saved.EnabledDestinations, saved.WindowsApps).Enabled.SequenceEqual(new[] { ComposerDestination.Notepad }),
            "Invalid saved application targets cannot become executable destinations");

        ProcessStartInfo? captured = null;
        var count = 0;
        var launcher = new WindowsAppLauncher(info => { captured = info; count++; return true; });
        const string text = "  café ☕ 雪\r\n\"quoted\" & echo unwanted | test  ";
        check(launcher.Open(app, text) && captured!.FileName == executable && captured.ArgumentList.Count == 0 &&
            captured.Arguments == "" && captured.UseShellExecute && captured.Verb == "",
            "Open-only applications receive no draft, shell command or elevation");
        check(launcher.Open(app with { PassText = true }, text) && captured!.ArgumentList.SequenceEqual(new[] { text }) &&
            captured.Arguments == "" && captured.WorkingDirectory == root,
            "Text reaches an executable as one literal argument including quotes, newlines and shell punctuation");
        var packaged = app with { Packaged = true, Target = "Microsoft.Test_abc123!App" };
        check(launcher.Open(packaged, text) && Path.GetFileName(captured!.FileName) == "explorer.exe" &&
            captured.ArgumentList.SequenceEqual(new[] { "shell:AppsFolder\\Microsoft.Test_abc123!App" }),
            "Store applications launch through their explicit Windows application ID without receiving the draft");
        var before = count;
        foreach (var invalid in new[] { app with { Target = "cmd.exe /c anything" }, app with { Target = "https://example.com" },
            app with { Target = Path.Combine(root, "script.cmd") }, app with { Target = Path.Combine(root, "missing.exe") },
            packaged with { Target = "Microsoft.Test_abc!App & calc" }, packaged with { PassText = true } })
            check(!launcher.Open(invalid, text), "Invalid or missing application is rejected before Windows launch");
        check(count == before, "Rejected application targets have no launch side effects");

        var drafts = new ComposerDrafts();
        var coordinator = new ComposerCoordinator(drafts, () => { });
        var enabled = false;
        coordinator.IsEnabled = id => enabled && id == app.Id;
        var runs = 0;
        coordinator.Register(app.Id, false, _ => { runs++; return Task.CompletedTask; }, _ => false, allowEmpty: true);
        await coordinator.SubmitAsync(app.Id, text);
        check(runs == 0 && !coordinator.CanSend(app.Id, text), "Removed destinations are blocked at dispatch as well as hidden in the selector");
        enabled = true;
        await coordinator.SubmitAsync(app.Id, "");
        check(runs == 1, "Application destinations can open with an empty editor");
        drafts.Edit(app.Id, text);
        var submission = drafts.Capture(app.Id);
        drafts.Accept(submission);
        drafts.Edit(app.Id, "newer draft");
        drafts.Clear(submission);
        check(drafts.Text(app.Id) == "newer draft" && drafts.Text(ComposerDestination.OpenAI) == "",
            "Custom destination submissions retain draft isolation and never erase a newer revision");
    }
}
