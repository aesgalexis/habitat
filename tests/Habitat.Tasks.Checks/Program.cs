using Habitat.Models;
using Habitat.Services;
using System.Text.Json;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS: {name}");
}

static void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
    { Console.WriteLine($"PASS: {name}"); return; }
    throw new InvalidOperationException(name);
}

var directory = Path.Combine(AppContext.BaseDirectory, "habitat-tasks-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "nested", "tasks.json");
    var store = new TaskStore(path);
    Check(store.Load().Count == 0, "First launch starts with an empty list");
    List<TaskItem> tasks = [new(Guid.NewGuid(), "Comprar café ☕", false, DateTimeOffset.UtcNow),
        new(Guid.NewGuid(), "Preparar informe", false, DateTimeOffset.UtcNow)];
    store.Save(tasks);
    Check(new TaskStore(path).Load().SequenceEqual(tasks), "Added tasks survive reopening with Unicode and IDs intact");
    tasks[0] = tasks[0] with { IsCompleted = true };
    store.Save(tasks);
    Check(store.Load()[0].IsCompleted && !store.Load()[1].IsCompleted, "Completing one task preserves the others");
    tasks[0] = tasks[0] with { IsCompleted = false, Text = "Comprar café y pan" };
    store.Save(tasks);
    Check(store.Load().SequenceEqual(tasks), "Reopened and edited tasks persist");
    Reject(() => store.Save([tasks[0]]), "Removing a saved task is blocked");
    Reject(() => store.Save([]), "An empty list cannot erase saved tasks");
    Check(store.Load().SequenceEqual(tasks), "Rejected deletions preserve pending and completed tasks");
    var saved = File.ReadAllText(path);
    Reject(() => store.Save([tasks[0], tasks[0]]), "Duplicate IDs are rejected");
    Reject(() => store.Save([tasks[0] with { Text = " " }]), "Blank tasks are rejected");
    Reject(() => store.Save([tasks[0] with { Text = new string('a', 1001) }]), "Oversized tasks are rejected");
    Reject(() => store.Save([tasks[0] with { CreatedAt = DateTimeOffset.MinValue }]), "Missing creation dates are rejected");
    Check(File.ReadAllText(path) == saved, "Invalid changes preserve the saved file");
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Reject(() => store.Save(tasks), "A failed read before replacement is reported");
    Check(File.ReadAllText(path) == saved && !Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(),
        "Failed writes preserve tasks and remove temporary files");
    File.WriteAllText(path, "{broken");
    Check(store.Load().SequenceEqual(tasks) && store.RecoveredFromBackup, "Corrupt storage restores the latest complete list from backup");
    Check(Directory.EnumerateFiles(path + ".history", "damaged-*.json").Any(file => File.ReadAllText(file) == "{broken"),
        "Recovery preserves the damaged original for diagnosis");
    File.Delete(path);
    Check(new TaskStore(path).Load().SequenceEqual(tasks), "A missing main file restores tasks instead of starting empty");

    var stale = new TaskStore(path);
    stale.Load();
    tasks.Add(new(Guid.NewGuid(), "Newer task", false, DateTimeOffset.UtcNow));
    store.Load();
    store.Save(tasks);
    Reject(() => stale.Save([.. tasks.Take(2)]), "A stale session cannot overwrite a newer list");
    Check(new TaskStore(path).Load().SequenceEqual(tasks), "Newer tasks survive a stale save attempt");

    tasks[0] = tasks[0] with { IsCompleted = true };
    store.Save(tasks);
    var completedState = tasks.ToList();
    tasks[0] = tasks[0] with { IsCompleted = false };
    store.Save(tasks);
    tasks[0] = tasks[0] with { IsCompleted = true };
    store.Save(tasks);
    File.Delete(path);
    Check(new TaskStore(path).Load().SequenceEqual(completedState), "Repeated completion states recover the most recent revision");
    Check(new TaskStore(path).Load().Count(task => task.IsCompleted) == 1, "Completed tasks survive recovery and reopening");
    var olderCompletionState = completedState.Select(task => task with { IsCompleted = false }).ToList();
    File.WriteAllText(path, JsonSerializer.Serialize(olderCompletionState));
    Check(new TaskStore(path).Load().SequenceEqual(completedState), "An older revision cannot silently undo completed tasks");
    File.WriteAllText(path, "[]");
    Check(new TaskStore(path).Load().SequenceEqual(completedState), "A valid but empty file cannot hide previously saved tasks");
    File.WriteAllText(path, JsonSerializer.Serialize(completedState.Take(1)));
    Check(new TaskStore(path).Load().SequenceEqual(completedState), "A truncated list restores missing IDs and completion states");

    var beforeFailedSave = File.ReadAllText(path);
    store.Load();
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => store.Save([.. completedState, new(Guid.NewGuid(), "Draft", false, DateTimeOffset.UtcNow)]),
            "A locked replacement fails without deleting the original");
    Check(File.ReadAllText(path) == beforeFailedSave && !Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(),
        "Failed replacement keeps the original file and cleans temporary files");
    Check(new TaskStore(path).Load().SequenceEqual(completedState), "An uncommitted backup never replaces a healthy main file");
    File.Delete(path);
    Check(new TaskStore(path).Load().SequenceEqual(completedState), "Recovery excludes a failed, uncommitted save");

    var noLoadPath = Path.Combine(directory, "unloaded", "tasks.json");
    Reject(() => new TaskStore(noLoadPath).Save(tasks), "Saving without a successful load is blocked");
    Check(!File.Exists(noLoadPath), "An unloaded store cannot create or overwrite user data");
    foreach (var invalid in new[] { "{broken", "[null]", "null", "[{\"Id\":\"" + Guid.NewGuid() + "\",\"Text\":\"Incomplete record\"}]" })
    {
        var badPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(badPath, invalid);
        var badStore = new TaskStore(badPath);
        Reject(() => badStore.Load(), "Invalid storage without a backup is reported");
        Reject(() => badStore.Save(tasks), "A failed load cannot be followed by an overwrite");
        Check(File.ReadAllText(badPath) == invalid, "Invalid original data is preserved");
    }

    var brokenHistoryPath = Path.Combine(directory, "broken-history.json");
    File.WriteAllText(brokenHistoryPath, JsonSerializer.Serialize(tasks));
    File.WriteAllText(brokenHistoryPath + ".history", "unavailable");
    Reject(() => new TaskStore(brokenHistoryPath).Load(), "An unavailable backup directory blocks unsafe editing");
    Check(JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(brokenHistoryPath))!.SequenceEqual(tasks),
        "Backup failure preserves the main list");

    var missingPath = Path.Combine(directory, "missing-history.json");
    Directory.CreateDirectory(missingPath + ".history");
    File.WriteAllText(Path.Combine(missingPath + ".history", "revision-invalid.json"), "{broken");
    Reject(() => new TaskStore(missingPath).Load(), "Missing main file with unusable backups cannot become an empty list");

    Check(TaskStore.IsTemporaryProfile(@"C:\Users\someone\AppData\Local\Packages\sandbox.{123}\AC") &&
        !TaskStore.IsTemporaryProfile(@"C:\Users\someone\AppData\Local") &&
        !TaskStore.IsTemporaryProfile(@"C:\Users\someone\AppData\Local\Packages\OpenAI.Codex\LocalCache\Local"),
        "Temporary sandbox profiles are distinguished from persistent Windows profiles");
    var defaultStore = new TaskStore();
    if (defaultStore.TemporaryProfileBlocked)
        Reject(() => defaultStore.Load(), "A temporary launch cannot read or create a separate user task list");

    var racePath = Path.Combine(directory, "race.json");
    var raceFirst = new TaskStore(racePath);
    var raceSecond = new TaskStore(racePath);
    raceFirst.Load();
    raceSecond.Load();
    var raceAdded = new TaskItem(Guid.NewGuid(), "Concurrent task", false, DateTimeOffset.UtcNow);
    var outcomes = await Task.WhenAll(new[] { raceFirst, raceSecond }.Select(candidate => Task.Run(() =>
    {
        try { candidate.Save([raceAdded]); return true; }
        catch (IOException) { return false; }
    })));
    Check(outcomes.Count(success => success) == 1 && new TaskStore(racePath).Load().SequenceEqual([raceAdded]),
        "Concurrent sessions commit once without replacing each other's tasks");

    foreach (var width in new[] { 320d, 440, 600, 800, 1200, 1920 })
    {
        foreach (var active in new[] { false, true })
        foreach (var open in new[] { false, true })
        {
            var layout = TopWidgetLayout.Calculate(width, active, open);
            Check(layout.ContentInset <= width && layout.WidgetWidth <= layout.ContentInset &&
                (!open || layout.TaskLeft + layout.TaskWidth <= layout.ContentInset),
                $"Sidebar and task drawer reserve space at width {width}, conversation={active}, tasks={open}");
            if (active) Check(!layout.Expanded && layout.WidgetWidth == 40, "Conversation keeps an icon-only rail");
        }
    }
    Check(TopWidgetLayout.Calculate(800, false, false).Expanded, "Welcome screen has an expanded sidebar");
    Check(!TopWidgetLayout.Calculate(440, false, false).Expanded, "Narrow windows retain an icon rail");
    Check(TopWidgetLayout.Calculate(800, true, false).ContentInset < TopWidgetLayout.Calculate(800, false, false).ContentInset,
        "Conversation releases sidebar width to the chat");
    Check(TopWidgetLayout.Calculate(800, true, false, true).Expanded,
        "Manual expansion is available during a conversation");
    Check(!TopWidgetLayout.Calculate(800, false, false, false).Expanded,
        "Manual collapse is available on the welcome screen");
}
finally
{
    var resolved = Path.GetFullPath(directory);
    var tempRoot = Path.GetFullPath(AppContext.BaseDirectory);
    if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(resolved).StartsWith("habitat-tasks-checks-", StringComparison.Ordinal))
        throw new InvalidOperationException("Unexpected test cleanup path.");
    Directory.Delete(resolved, recursive: true);
}
