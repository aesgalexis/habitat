using System.Net;
using System.Text;
using System.Text.Json;
using Habitat;
using Habitat.Features.Contacts;
using Habitat.Features.Drive;
using Habitat.Features.Tasks;
using Habitat.Models;
using Habitat.Services;

internal static class DriveTaskChecks
{
    private static TaskItem NewTask(string text) => new(Guid.NewGuid(), text, false, DateTimeOffset.UtcNow);
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var original = NewTask("  café ☕ 雪\nsecond line  ");
        check(TaskDriveDocument.Decode(TaskDriveDocument.Encode([original])).Single() == original,
            "Tasks JSON preserves stable IDs, exact Unicode text, completion and creation time.");
        var compatible = TaskDriveDocument.Merge([original], [original with { Text = "Edited locally" }], [original with { IsCompleted = true }]);
        check(!compatible.NeedsReview && compatible.Items.Single() is { Text: "Edited locally", IsCompleted: true },
            "Changes to separate fields of the same task are combined.");
        var conflict = TaskDriveDocument.Merge([original], [original with { Text = "Local" }], [original with { Text = "Remote", IsCompleted = true }]);
        var chosen = TaskDriveDocument.Merge([original], [original with { Text = "Local" }], [original with { Text = "Remote", IsCompleted = true }],
            new Dictionary<Guid, TaskDriveChoice> { [original.Id] = TaskDriveChoice.Local });
        check(conflict.NeedsReview && conflict.Conflicts.Single().Fields.SequenceEqual(["Text"]) &&
            chosen.Items.Single() is { Text: "Local", IsCompleted: true }, "Conflict choices affect only the conflicting fields of that task.");
        check(TaskDriveDocument.Merge([original], [], []).Items.Single() == original &&
            TaskDriveDocument.Merge([], [original], [NewTask(original.Text)]).Items.Length == 2,
            "Missing tasks never imply deletion and identical text never replaces stable identity.");
        try { TaskDriveDocument.Decode(Encoding.UTF8.GetBytes("{\"Schema\":2,\"Tasks\":[]}")); check(false, "Unsupported task schema accepted."); }
        catch (System.IO.InvalidDataException) { check(true, "Unsupported task schemas fail without replacing the local list."); }
        try { TaskDriveDocument.Encode([original, original]); check(false, "Duplicate task IDs accepted."); }
        catch (System.IO.InvalidDataException) { check(true, "Duplicate task identifiers are rejected."); }
        await CheckPeersAsync(Path.Combine(root, "drive-task-peers"), check);
        await CheckWorkspaceAsync(Path.Combine(root, "drive-workspace"), check);
        await CheckRecoveryAsync(Path.Combine(root, "drive-task-recovery"), check);
        await CheckGuardsAsync(Path.Combine(root, "drive-task-guards"), check);
    }

    private static async Task CheckPeersAsync(string root, Action<bool, string> check)
    {
        using var handler = new DriveMemoryHandler();
        using var http = new HttpClient(handler);
        using var a = new Device(Path.Combine(root, "a"), http);
        using var b = new Device(Path.Combine(root, "b"), http);
        await a.LoadAsync(); await b.LoadAsync();
        var first = NewTask("First ☕");
        await a.Tasks.UpdateAsync(items => [.. items, first]);
        await a.Sync.SyncNowAsync(default);
        check(handler.Files.All(file => !file.Properties.ContainsKey("habitatTasksJson")),
            "Existing contact sync never uploads tasks until Sync tasks is explicitly enabled.");
        a.Sync.SetEnabled(true, true); b.Sync.SetEnabled(true, true);
        await a.Sync.SyncNowAsync(default); await b.Sync.SyncNowAsync(default);
        var folder = handler.Files.Single(file => file.Properties.ContainsKey(DriveWorkspace.Marker));
        check(a.Tasks.Items.Single() == first && b.Tasks.Items.Single() == first &&
            handler.Files.Count(file => file.Parents.Contains(folder.Id)) == 2 &&
            handler.TasksFile.Parents.SequenceEqual([folder.Id]), "Two PCs share one HABITAT folder with the contacts CSV and tasks JSON.");
        await a.Tasks.UpdateAsync(items => items.Select(task => task with { IsCompleted = true }).ToList());
        await a.TaskSync.SyncAsync(default); await b.TaskSync.SyncAsync(default);
        check(b.Tasks.Items.Single().IsCompleted, "Completing a task propagates to the second PC without changing identity.");
        await b.Tasks.UpdateAsync(items => items.Select(task => task with { IsCompleted = false }).ToList());
        await b.TaskSync.SyncAsync(default); await a.TaskSync.SyncAsync(default);
        check(!a.Tasks.Items.Single().IsCompleted, "An intentional reopen propagates back to the first PC.");

        var fromA = NewTask("Added on A");
        var fromB = NewTask("Added on B");
        await a.Tasks.UpdateAsync(items => [.. items, fromA]); await b.Tasks.UpdateAsync(items => [.. items, fromB]);
        handler.RaceNextAppends("habitatTasksNext");
        await Task.WhenAll(a.TaskSync.SyncAsync(default), b.TaskSync.SyncAsync(default));
        await a.TaskSync.SyncAsync(default); await b.TaskSync.SyncAsync(default);
        check(handler.RejectedAppends == 1 && a.Tasks.Items.Count == 3 && b.Tasks.Items.Count == 3 &&
            TaskDriveDocument.Decode(handler.TasksFile.Bytes).Select(task => task.Id).ToHashSet().SetEquals([first.Id, fromA.Id, fromB.Id]),
            "Concurrent task revisions retry the losing writer and retain both PCs' additions.");

        await a.Tasks.UpdateAsync(items => items.Select(task => task.Id == first.Id ? task with { Text = "A first" } :
            task.Id == fromA.Id ? task with { Text = "A second" } : task).ToList());
        await b.Tasks.UpdateAsync(items => items.Select(task => task.Id == first.Id ? task with { Text = "B first" } :
            task.Id == fromA.Id ? task with { Text = "B second" } : task).ToList());
        await a.TaskSync.SyncAsync(default); await b.TaskSync.SyncAsync(default);
        check(b.TaskSync.Conflicts.Count == 2 && b.TaskSync.NeedsAttention && b.Tasks.Items.Single(task => task.Id == first.Id).Text == "B first",
            "Two conflicting tasks preserve local edits and expose separate review choices.");
        var writes = handler.Writes;
        await b.TaskSync.SyncAsync(default, (first.Id, TaskDriveChoice.Drive));
        check(handler.Writes == writes && b.TaskSync.Conflicts.Count(conflict => conflict.Selected is not null) == 1,
            "Resolving one task cannot publish unreviewed choices for another task.");
        await b.Tasks.UpdateAsync(items => items.Select(task => task.Id == fromB.Id ? task with { Text = "A new edit during review" } : task).ToList());
        await b.TaskSync.SyncAsync(default, (fromA.Id, TaskDriveChoice.Local));
        check(handler.Writes == writes && b.TaskSync.Conflicts.All(conflict => conflict.Selected is null),
            "Changing the local snapshot invalidates every old review choice before it can be published.");
        await b.TaskSync.SyncAsync(default, (first.Id, TaskDriveChoice.Drive));
        await b.TaskSync.SyncAsync(default, (fromA.Id, TaskDriveChoice.Local)); await a.TaskSync.SyncAsync(default);
        check(!b.TaskSync.NeedsAttention && a.Tasks.Items.Single(task => task.Id == first.Id).Text == "A first" &&
            a.Tasks.Items.Single(task => task.Id == fromA.Id).Text == "B second", "Per-task conflict choices converge on both PCs.");

        var late = NewTask("Added while Drive is reading");
        handler.BeforeTasksDownload = async () =>
        {
            check(await a.Tasks.UpdateAsync(items => [.. items, late]), "Local task editing stays available during Drive I/O.");
        };
        await a.TaskSync.SyncAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        check(a.Tasks.Items.Any(task => task.Id == late.Id) && a.TaskSync.HasPendingChanges &&
            TaskDriveDocument.Decode(handler.TasksFile.Bytes).All(task => task.Id != late.Id),
            "An edit during the request is retained locally and is not falsely acknowledged as uploaded.");
        await a.TaskSync.SyncAsync(default); await b.TaskSync.SyncAsync(default);
        check(b.Tasks.Items.Any(task => task.Id == late.Id), "The retained late edit uploads on the next sync.");

        var lost = NewTask("Committed before a lost response");
        await a.Tasks.UpdateAsync(items => [.. items, lost]);
        var revisions = handler.Files.Count(file => file.Properties.ContainsKey("habitatTasksNext"));
        handler.FailAfterNextAppend = true;
        await a.TaskSync.SyncAsync(default);
        check(!a.TaskSync.NeedsAttention && a.Tasks.IsLoaded, "A lost remote response leaves local tasks usable and permits retry.");
        await a.TaskSync.SyncAsync(default); await b.TaskSync.SyncAsync(default);
        check(b.Tasks.Items.Count(task => task.Id == lost.Id) == 1 &&
            handler.Files.Count(file => file.Properties.ContainsKey("habitatTasksNext")) == revisions + 1,
            "Retry after a committed revision recovers its existing ID without duplicating tasks or revisions.");

        var validBytes = handler.TasksFile.Bytes;
        handler.Change(handler.TasksFile.Id, file => file with { Bytes = Encoding.UTF8.GetBytes("{broken") });
        writes = handler.Writes;
        await a.TaskSync.SyncAsync(default);
        check(a.TaskSync.NeedsAttention && a.Tasks.IsLoaded && handler.Writes == writes,
            "An externally edited tasks projection is preserved and never disables the local task list.");
        await a.Contacts.ChangeAsync(book => book.SaveContact([new("name", "Contact while tasks need review")]));
        a.Sync.RequestTasksSync();
        await a.Sync.TickAsync(DateTimeOffset.UtcNow.AddMinutes(10), false, default);
        check(ContactDriveCsv.Decode(handler.VisibleBytes).Count == 1,
            "A tasks synchronization error does not block contact synchronization.");
        handler.Change(handler.TasksFile.Id, file => file with { Bytes = validBytes });
        await a.TaskSync.SyncAsync(default);
        handler.Override = request => request.RequestUri!.AbsolutePath.EndsWith("/about") ? new(HttpStatusCode.TooManyRequests) : null;
        await a.TaskSync.SyncAsync(default);
        check(!a.TaskSync.NeedsAttention && a.Tasks.IsLoaded && await a.Tasks.UpdateAsync(items => items.ToList()),
            "Drive throttling leaves task editing enabled and remains retryable.");
        handler.Override = null;

        var contactBytes = handler.VisibleBytes;
        var contactFile = handler.Files.Single(file => file.Properties.ContainsKey("habitatContactsCsv"));
        handler.Change(contactFile.Id, file => file with { Bytes = Encoding.UTF8.GetBytes("external contact edit") });
        var independent = NewTask("Tasks while contacts need review");
        await a.Tasks.UpdateAsync(items => [.. items, independent]);
        await a.Sync.SyncNowAsync(default);
        check(a.ContactSync.NeedsAttention && !a.TaskSync.NeedsAttention &&
            TaskDriveDocument.Decode(handler.TasksFile.Bytes).Any(task => task.Id == independent.Id),
            "Contact sync needing review does not prevent tasks from synchronizing.");
        handler.Change(contactFile.Id, file => file with { Bytes = contactBytes });
        a.Sync.SetEnabled(true, false);
        var paused = NewTask("Saved while tasks sync is paused");
        await a.Tasks.UpdateAsync(items => [.. items, paused]);
        await a.Sync.SyncNowAsync(default);
        check(a.Connection.Connected && !a.ContactSync.NeedsAttention &&
            TaskDriveDocument.Decode(handler.TasksFile.Bytes).All(task => task.Id != paused.Id),
            "Pausing task sync retains local edits and keeps the shared contact connection active.");
        a.Sync.SetEnabled(true, true);
        await a.Sync.SyncNowAsync(default);
        check(TaskDriveDocument.Decode(handler.TasksFile.Bytes).Any(task => task.Id == paused.Id),
            "Resuming tasks uses the retained baseline and uploads edits made while paused.");
        var contactLinkPath = Path.Combine(root, "a", "contacts-drive.json");
        var contactLink = await File.ReadAllBytesAsync(contactLinkPath);
        await File.WriteAllTextAsync(contactLinkPath, "{broken");
        var healthyTasks = NewTask("Tasks with a damaged local contacts link");
        await a.Tasks.UpdateAsync(items => [.. items, healthyTasks]);
        await a.Sync.SyncNowAsync(default);
        check(a.ContactSync.NeedsAttention && !a.TaskSync.NeedsAttention && a.Connection.Connected &&
            TaskDriveDocument.Decode(handler.TasksFile.Bytes).Any(task => task.Id == healthyTasks.Id),
            "An established workspace isolates a damaged local module link while preserving the account guard.");
        await File.WriteAllBytesAsync(contactLinkPath, contactLink);
    }

    private static async Task CheckGuardsAsync(string root, Action<bool, string> check)
    {
        using var handler = new DriveMemoryHandler();
        using var http = new HttpClient(handler);
        var settingsRoot = Path.Combine(root, "settings");
        Directory.CreateDirectory(settingsRoot);
        var settingsPath = Path.Combine(settingsRoot, "drive-sync-settings.json");
        await File.WriteAllTextAsync(settingsPath, "{broken");
        using var settings = new Device(settingsRoot, http);
        await settings.LoadAsync();
        await settings.Sync.SyncNowAsync(default);
        check(!settings.Sync.Settings.Contacts && !settings.Sync.Settings.Tasks && handler.Writes == 0,
            "Invalid module settings fail closed without uploading either dataset.");
        settings.Sync.SetEnabled(true, false);
        check(Directory.GetFiles(settingsRoot, "drive-sync-settings.json.invalid-*").Length == 1 && settings.Sync.Settings.Contacts,
            "Explicitly replacing invalid settings retains the original file for recovery.");

        var identityRoot = Path.Combine(root, "identity");
        Directory.CreateDirectory(identityRoot);
        await File.WriteAllTextAsync(Path.Combine(identityRoot, "tasks-drive.json"),
            "{\"Schema\":1,\"ClientId\":\"drive.apps.googleusercontent.com\",\"Owner\":\"another-owner\"}");
        using var identity = new Device(identityRoot, http);
        await identity.LoadAsync();
        await identity.ContactSync.SyncAsync(default);
        check(identity.ContactSync.NeedsAttention && !identity.Connection.Connected && handler.Writes == 0,
            "A retained tasks account link is validated before creating a folder or sending contacts to a different account.");
    }

    private static async Task CheckWorkspaceAsync(string root, Action<bool, string> check)
    {
        using var handler = new DriveMemoryHandler();
        var unrelated = handler.AddFolder(false, "Personal folder");
        using var http = new HttpClient(handler);
        using var device = new Device(root, http);
        await device.LoadAsync();
        await device.Contacts.ChangeAsync(book => book.SaveContact([new("name", "Legacy contact")]));
        await device.ContactSync.SyncAsync(default);
        var folder = handler.Files.Single(file => file.Properties.ContainsKey(DriveWorkspace.Marker));
        var csv = handler.Files.Single(file => file.Properties.ContainsKey("habitatContactsCsv"));
        handler.Change(csv.Id, file => file with { Parents = ["root"] });
        var moves = handler.Moves;
        handler.FailAfterNextMove = true;
        await device.ContactSync.SyncAsync(default);
        check(!device.ContactSync.NeedsAttention && handler.Moves == moves + 1,
            "A lost move response is retryable after retaining the existing contact file.");
        await device.ContactSync.SyncAsync(default);
        var placed = handler.Files.Single(file => file.Id == csv.Id);
        check(handler.Moves == moves + 1 && placed.Parents.SequenceEqual([folder.Id]) && placed.Bytes.SequenceEqual(csv.Bytes) &&
            placed.Properties["habitatContactsRoot"] == csv.Properties["habitatContactsRoot"] &&
            handler.Files.Single(file => file.Id == unrelated).Parents.SequenceEqual(["root"]),
            "Legacy CSV migration preserves ID, bytes and private-history root and leaves unrelated files alone.");
        handler.Change(folder.Id, file => file with { Name = "My renamed HABITAT", Parents = ["another-parent"] });
        await device.ContactSync.SyncAsync(default);
        check(handler.Files.Count(file => file.Properties.ContainsKey(DriveWorkspace.Marker)) == 1 && !device.ContactSync.NeedsAttention,
            "A renamed or moved linked workspace is reused by ID instead of creating another root folder.");
        _ = handler.AddFolder();
        var writes = handler.Writes;
        await device.ContactSync.SyncAsync(default);
        check(device.ContactSync.NeedsAttention && handler.Writes == writes && device.Connection.Workspace.Choices.Count == 2,
            "Duplicate managed folders require explicit review before any dataset is moved or uploaded.");
        await device.Connection.SelectFolderAsync(folder.Id, default);
        await device.ContactSync.SyncAsync(default);
        check(!device.ContactSync.NeedsAttention, "A verified explicit folder selection resumes the existing contact history.");
        handler.Change(folder.Id, file => file with { Trashed = true });
        writes = handler.Writes;
        await device.ContactSync.SyncAsync(default);
        check(device.ContactSync.NeedsAttention && handler.Writes == writes && device.Contacts.Book.People.Length == 1,
            "A deleted linked folder never becomes an empty replacement dataset.");
        handler.Change(folder.Id, file => file with { Trashed = false });
        await device.ContactSync.SyncAsync(default);
        handler.Owner = "another-account";
        writes = handler.Writes;
        await device.ContactSync.SyncAsync(default);
        check(!device.Connection.Connected && handler.Writes == writes, "A different account is disconnected before writing workspace or contact data.");
    }

    private static async Task CheckRecoveryAsync(string root, Action<bool, string> check)
    {
        using var handler = new DriveMemoryHandler();
        using var http = new HttpClient(handler);
        using var initial = new Device(root, http);
        await initial.LoadAsync();
        var task = NewTask("Recovered task");
        await initial.Tasks.UpdateAsync(items => [.. items, task]);
        await initial.TaskSync.SyncAsync(default);
        File.WriteAllText(Path.Combine(root, "tasks.json"), "[]");
        using var recovered = new Device(root, http);
        await recovered.LoadAsync();
        var writes = handler.Writes;
        await recovered.TaskSync.SyncAsync(default);
        check(recovered.Tasks.RecoveredFromBackup && recovered.TaskSync.NeedsRecoveryReview && handler.Writes == writes,
            "Recovered local tasks require review before publishing a possible rollback to Drive.");
        using var restarted = new Device(root, http);
        await restarted.LoadAsync();
        await restarted.TaskSync.SyncAsync(default);
        check(restarted.TaskSync.NeedsRecoveryReview && handler.Writes == writes,
            "The recovery-review barrier survives restarting with a now-healthy main file.");
        await restarted.TaskSync.ConfirmRecoveryAsync(default);
        check(!restarted.TaskSync.NeedsRecoveryReview && !restarted.TaskSync.NeedsAttention && restarted.Tasks.Items.Single().Id == task.Id,
            "Explicit recovery review resumes synchronization without changing task identity.");
        initial.ContactSync.Dispose();
        check(initial.Connection.Connected, "Disposing one dataset does not dispose the shared Drive authorization.");
    }

    private sealed class Device : IDisposable
    {
        internal TaskSession Tasks { get; }
        internal ContactSession Contacts { get; }
        internal GoogleDriveConnection Connection { get; }
        internal ContactDriveSync ContactSync { get; }
        internal TaskDriveSync TaskSync { get; }
        internal GoogleDriveSync Sync { get; }
        internal Device(string root, HttpClient http)
        {
            Directory.CreateDirectory(root);
            Tasks = new(new TaskStore(Path.Combine(root, "tasks.json")));
            Contacts = new(new ContactStore(Path.Combine(root, "contacts.json")));
            var link = Path.Combine(root, "contacts-drive.json");
            GmailAuthService Auth() => new(new Credentials(), http, _ => throw new Exception("No browser allowed in task synchronization checks."), driveOnly: true);
            Connection = new(Path.Combine(root, "drive-workspace.json"), link, Auth, http,
                legacyTasksPath: Path.Combine(root, "tasks-drive.json"));
            ContactSync = new(Contacts, link, Auth, http, sharedConnection: Connection);
            TaskSync = new(Tasks, Connection, Path.Combine(root, "tasks-drive.json"));
            Sync = new(Connection, ContactSync, TaskSync, Path.Combine(root, "drive-sync-settings.json"));
        }
        internal async Task LoadAsync() { await Tasks.LoadAsync(); await Contacts.LoadAsync(); }
        public void Dispose() { ContactSync.Dispose(); Connection.Dispose(); }
    }
    private sealed class Credentials : IGmailCredentialStore
    {
        private GmailAccount _account = new(new("drive.apps.googleusercontent.com", "test-secret"),
            new("access", "refresh", DateTimeOffset.UtcNow.AddHours(1)), "owner@example.test");
        public GmailAccount Load() => _account;
        public void Save(GmailAccount account) => _account = account;
    }
}
