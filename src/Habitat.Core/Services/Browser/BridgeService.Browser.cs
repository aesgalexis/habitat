using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Habitat.Services;

public sealed partial class BridgeService
{
    private string? _browserFamily;
    private CancellationTokenSource _browserLifetime = new();

    internal void UseBrowser(string family)
    {
        CancellationTokenSource previous;
        lock (_stateLock)
        {
            if (_browserFamily == family) return;
            _browserFamily = family;
            previous = _browserLifetime;
            _browserLifetime = new();
            _lastPoll = DateTimeOffset.MinValue;
            _connectionOwner = null;
            _supportsExternalOpen = false;
            _connectionIssue = "Open the selected browser and connect the HABITAT extension. Check Preferences > Browser.";
        }
        previous.Cancel();
        previous.Dispose();
    }

    private CancellationToken BrowserToken { get { lock (_stateLock) return _browserLifetime.Token; } }

    private bool IsBrowserAllowed(HttpRequest request, JsonDocument? auth = null)
    {
        var browser = auth is not null && auth.RootElement.TryGetProperty("browser", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : request.Headers["X-Habitat-Browser"].ToString();
        return AllowsBrowser(_browserFamily, browser, request.Headers.UserAgent.ToString());
    }

    internal static bool AllowsBrowser(string? selected, string? reported, string userAgent)
    {
        // A null policy is reserved for isolated bridge checks and smoke tools.
        if (selected is null) return true;
        var detected = reported is "chrome" or "edge" or "brave" or "vivaldi" or "opera" or "firefox" ? reported :
            userAgent.Contains("Edg/", StringComparison.Ordinal) ? "edge" :
            userAgent.Contains("OPR/", StringComparison.Ordinal) ? "opera" :
            userAgent.Contains("Vivaldi/", StringComparison.Ordinal) ? "vivaldi" :
            userAgent.Contains("Firefox/", StringComparison.Ordinal) ? "firefox" :
            userAgent.Contains("Chrome/", StringComparison.Ordinal) ? "chrome" : "unknown";
        return selected != "unavailable" && selected == detected;
    }
}
