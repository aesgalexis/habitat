namespace Habitat.Models;

internal sealed record TaskItem(Guid Id, string Text, bool IsCompleted, DateTimeOffset CreatedAt);
