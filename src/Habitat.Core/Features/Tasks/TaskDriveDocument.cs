using System.Text.Json;
using Habitat.Models;
using Habitat.Services;

namespace Habitat.Features.Tasks;

internal enum TaskDriveChoice { Local, Drive }
internal sealed record TaskDriveConflict(TaskItem Local, TaskItem Drive, string[] Fields, TaskDriveChoice? Selected = null);
internal sealed record TaskDriveMerge(TaskItem[] Items, TaskDriveConflict[] Conflicts)
{
    internal bool NeedsReview => Conflicts.Any(conflict => conflict.Selected is null);
}

internal static class TaskDriveDocument
{
    private sealed record Document(int Schema, TaskItem[] Tasks);
    internal const int MaxBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { RespectRequiredConstructorParameters = true };
    internal static byte[] Encode(IEnumerable<TaskItem> items)
    {
        var tasks = items.OrderBy(task => task.CreatedAt).ThenBy(task => task.Id)
            .Select(task => task with { CreatedAt = task.CreatedAt.ToUniversalTime() }).ToArray();
        Validate(tasks);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, tasks), Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The tasks document is too large to sync.");
        return bytes;
    }
    internal static TaskItem[] Decode(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The tasks document is too large to sync.");
        var document = JsonSerializer.Deserialize<Document>(bytes, Json);
        if (document is not { Schema: 1, Tasks: not null }) throw new InvalidDataException("The Google Drive tasks format is unsupported. Your local tasks were kept.");
        Validate(document.Tasks);
        return document.Tasks;
    }
    private static void Validate(TaskItem[] tasks)
    {
        if (tasks.Length > 50000) throw new InvalidDataException("Too many tasks to sync.");
        TaskStore.Validate(tasks);
    }
    internal static TaskDriveMerge Merge(IReadOnlyList<TaskItem> baseline, IReadOnlyList<TaskItem> local,
        IReadOnlyList<TaskItem> remote, IReadOnlyDictionary<Guid, TaskDriveChoice>? choices = null)
    {
        TaskStore.Validate(baseline); TaskStore.Validate(local); TaskStore.Validate(remote);
        var before = baseline.ToDictionary(task => task.Id);
        var here = local.ToDictionary(task => task.Id);
        var there = remote.ToDictionary(task => task.Id);
        var merged = new List<TaskItem>();
        var conflicts = new List<TaskDriveConflict>();
        foreach (var id in before.Keys.Union(here.Keys).Union(there.Keys))
        {
            var old = before.GetValueOrDefault(id);
            var left = here.GetValueOrDefault(id);
            var right = there.GetValueOrDefault(id);
            // TaskStore deliberately retains every known ID. Missing records
            // never mean deletion, including a truncated remote snapshot.
            if (left is null || right is null)
            { merged.Add(left ?? right ?? old!); continue; }
            if (left.CreatedAt != right.CreatedAt || old is not null && old.CreatedAt != left.CreatedAt)
                throw new InvalidDataException("A task identifier has incompatible creation data in Google Drive. Your local tasks were kept.");
            var fields = new List<string>();
            TaskDriveChoice? selected = choices is not null && choices.TryGetValue(id, out var choice) ? choice : null;
            T Field<T>(string name, T localValue, T driveValue, T previous)
            {
                var same = EqualityComparer<T>.Default;
                if (same.Equals(localValue, driveValue)) return localValue;
                if (old is not null && same.Equals(driveValue, previous)) return localValue;
                if (old is not null && same.Equals(localValue, previous)) return driveValue;
                fields.Add(name);
                return selected == TaskDriveChoice.Drive ? driveValue : localValue;
            }
            var text = Field("Text", left.Text, right.Text, old?.Text ?? "");
            var completed = Field("Completed", left.IsCompleted, right.IsCompleted, old?.IsCompleted ?? false);
            merged.Add(left with { Text = text, IsCompleted = completed });
            if (fields.Count > 0) conflicts.Add(new(left, right, fields.ToArray(), selected));
        }
        return new(merged.OrderBy(task => task.CreatedAt).ThenBy(task => task.Id).ToArray(), conflicts.ToArray());
    }
}

internal sealed class TaskSyncLocalChangeException() : Exception("Tasks changed during synchronization. The latest local changes were kept and will be checked again.");
