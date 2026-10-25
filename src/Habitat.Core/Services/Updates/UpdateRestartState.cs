using System.Text.Json;

namespace Habitat.Services;

internal sealed record UpdateRestartState(int Schema, string Destination, ComposerDrafts.Snapshot Drafts)
{
    internal void Save(string path) => AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(this));

    internal static UpdateRestartState? Consume(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Oversized update restart state.");
        var state = JsonSerializer.Deserialize<UpdateRestartState>(File.ReadAllBytes(path));
        if (state is not { Schema: 1, Destination.Length: > 0 and <= 160, Drafts.Shared: not null, Drafts.Local: not null } ||
            state.Drafts.Local.Count > 1000 || state.Drafts.Local.Any(item => item.Key.Length is 0 or > 160 || item.Value is null))
            throw new InvalidDataException("Invalid update restart state.");
        File.Delete(path);
        return state;
    }
}
