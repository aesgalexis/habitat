using System.Security.Cryptography;
using System.Text;

namespace Habitat.Activation;

public static class SelectionResolver
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        text.Replace("\r\n", "\n").Replace('\r', '\n'))));

    // A Chromium document may own the selection while a different child has
    // keyboard focus. Agreeing providers are one selection; ambiguity is rejected.
    public static string? Resolve(bool focusInsideWindow, bool password, IEnumerable<string[]> candidates)
    {
        if (!focusInsideWindow || password) return null;
        string? selected = null;
        var count = 0;
        foreach (var ranges in candidates)
        {
            if (++count > 64 || ranges.Length > 1) return null;
            if (ranges.Length == 0 || ranges[0].Length == 0) continue;
            if (ranges[0].Length > ActivationChannel.MaxTextLength) return null;
            var hash = Hash(ranges[0]);
            if (selected is not null && selected != hash) return null;
            selected = hash;
        }
        return selected;
    }
}
