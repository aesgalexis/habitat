using Habitat.Features.Codex;
using Habitat.Services;

internal static class CodexScheduleControllerChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-schedule-controller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var queue = new CodexScheduleQueue(Path.Combine(root, "outbox.json"));
        queue.Add(null, "Original scheduled text 雪", clock.Now.AddMinutes(1), clock.Now);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<CodexScheduledAttempt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        var errors = 0;
        var controller = new CodexScheduleController(queue, (_, _) => { sends++; started.TrySetResult(); return finish.Task; },
            () => true, _ => errors++, time: clock);
        await controller.TickAsync(clock.Now, true);
        check(sends == 0, "The schedule controller never delivers before the stored due time.");
        clock.Now = clock.Now.AddMinutes(1);
        var running = controller.TickAsync(clock.Now, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.TickAsync(clock.Now.AddSeconds(3), true);
        check(sends == 1 && queue.Messages.Single().State == CodexScheduleState.Sending, "Overlapping timer ticks cannot duplicate an outbox delivery.");
        var stop = controller.StopAsync();
        check(!stop.IsCompleted && ReferenceEquals(stop, controller.StopAsync()), "Controller shutdown is idempotent and drains a provider that ignores cancellation.");
        finish.SetResult(new(CodexDeliveryResult.Sent));
        await running; await stop;
        await controller.TickAsync(clock.Now.AddMinutes(1), true);
        check(sends == 1 && errors == 0 && queue.Messages.Single().State == CodexScheduleState.Sent,
            "A confirmed late delivery stays durably sent while a stopped controller rejects new work.");

        var deferred = new CodexScheduleQueue(Path.Combine(root, "deferred.json"));
        deferred.Add(null, "Wait for the overlay", clock.Now.AddMinutes(1), clock.Now);
        clock.Now = clock.Now.AddMinutes(1);
        var waiting = new CodexScheduleController(deferred, (_, _) => throw new Exception("Blocked delivery was invoked."),
            () => false, _ => errors++, time: clock);
        await waiting.TickAsync(clock.Now, true);
        check(deferred.Messages.Single() is { State: CodexScheduleState.Pending, RetryAt: { } retry } && retry == clock.Now.AddSeconds(30),
            "Availability is checked again before submission and busy UI defers rather than consumes a message.");
        await waiting.StopAsync();

        var brokenPath = Path.Combine(root, "broken.json");
        File.WriteAllText(brokenPath, "{invalid");
        var broken = new CodexScheduleController(new(brokenPath), (_, _) => throw new Exception("No message exists."), () => true, _ => errors++, time: clock);
        await broken.TickAsync(clock.Now, true);
        await broken.TickAsync(clock.Now.AddMinutes(1), true);
        check(broken.HasFailed && errors == 1, "Storage failure suspends polling with one observable diagnostic instead of a repeated failure loop.");
        await broken.StopAsync();
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
