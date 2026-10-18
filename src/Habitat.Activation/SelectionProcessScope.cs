namespace Habitat.Activation;

public static class SelectionProcessScope
{
    // WebView2 may own the clipboard in a browser/renderer child of the native host.
    // Do not accept an unrelated browser simply because its executable is the same.
    public static bool Contains(uint source, uint owner, IReadOnlyDictionary<uint, uint> parents)
    {
        if (source == 0 || owner == 0) return false;
        var seen = new HashSet<uint>();
        for (var depth = 0; depth < 32 && seen.Add(owner); depth++)
        {
            if (owner == source) return true;
            if (!parents.TryGetValue(owner, out owner) || owner == 0) break;
        }
        return false;
    }
}
