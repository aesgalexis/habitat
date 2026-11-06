using System.Diagnostics;
using System.Text.Json;
using Habitat.Features.Codex;
using Habitat.Services;

// Explicit integration diagnostic. The default only reads the production
// picker. Enqueuing is opt-in, requires the normal profile and a closed HABITAT,
// and leaves actual delivery to the installed application's scheduler.
internal static class CodexScheduleProbe
{
    internal static async Task RunAsync(string[] args)
    {
        string Required(string prefix) => args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..]
            ?? throw new ArgumentException("Missing diagnostic argument: " + prefix);
        var id = Required("--codex-thread-id=");
        if (!CodexReadClient.ValidId(id)) throw new ArgumentException("A valid selected chat ID is required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var reader = new CodexReadClient(Required("--codex-reader="));
        var paths = AppDataPaths.Production;
        await using var service = new CodexChatService(new NoDesktop(), reader,
            (_, _) => throw new InvalidOperationException("The probe cannot create or submit to a chat."),
            new CodexChatOrigins(paths.CodexChatsPath));
        var candidate = (await service.ListScheduleTargetsAsync(timeout.Token)).SingleOrDefault(item => item.Target?.Id == id)
            ?? throw new InvalidOperationException("The requested chat is not available in the production schedule picker.");
        var target = candidate.Target!;
        var history = await reader.ReadAsync(target, timeout.Token);
        CodexScheduledMessage? scheduled = null;
        if (args.Contains("--enqueue-local-probe"))
        {
            if (!OperatingSystem.IsWindows() ||
                !string.Equals(Path.GetDirectoryName(paths.LocalDirectory), AppDataPaths.DesktopLocalDirectory(), StringComparison.OrdinalIgnoreCase) ||
                paths.LocalDirectory.Contains("sandbox.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Enqueuing a real probe requires the normal Windows profile.");
            if (Process.GetProcessesByName("HABITAT").Length != 0)
                throw new InvalidOperationException("Close HABITAT before adding the probe to its durable outbox.");
            var queue = new CodexScheduleQueue(paths.CodexSchedulesPath);
            const string prefix = "HABITAT_SCHEDULE_TEST ";
            // Re-running this diagnostic after a lost result must not enqueue
            // the same test again, including a sent or uncertain test.
            scheduled = queue.Messages.LastOrDefault(item => item.Target?.Id == id &&
                item.Text.StartsWith(prefix, StringComparison.Ordinal) && item.SendAt > DateTimeOffset.UtcNow.AddHours(-1) &&
                item.State != CodexScheduleState.Cancelled);
            if (scheduled is null)
            {
                var marker = Guid.NewGuid().ToString("N")[..8];
                var text = prefix + marker + ". Prueba de envío programado a este mismo chat. Confirma la recepción sin ejecutar herramientas ni modificar archivos.";
                var now = DateTimeOffset.UtcNow;
                scheduled = queue.Add(target, text, now.AddSeconds(25), now);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            FoundInPicker = true, target.Id, target.Title, history.IsRunning, candidate.UsageLimited,
            ScheduleId = scheduled?.Id, SendAtLocal = scheduled?.SendAt.ToLocalTime(),
            State = scheduled?.State.ToString(), scheduled?.TargetSelected
        }));
    }

    private sealed class NoDesktop : ICodexChatDesktop
    {
        public Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token) => throw new InvalidOperationException("Read-only picker probe.");
        public Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token) =>
            throw new InvalidOperationException("The probe never invokes desktop submission.");
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => throw new InvalidOperationException("Read-only picker probe.");
        public void ReturnFocus(nint ownerWindow) { }
    }
}
