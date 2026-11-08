using Microsoft.UI.Dispatching;

namespace Habitat.Features.Sidebar;

internal sealed class SidebarDeadline
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<DateTimeOffset?> _next;
    private readonly Action<DateTimeOffset> _run;
    private bool _started;

    internal SidebarDeadline(DispatcherQueue dispatcher, Func<DateTimeOffset?> next, Action<DateTimeOffset> run)
    {
        _next = next;
        _run = run;
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            if (!_started) return;
            _run(DateTimeOffset.UtcNow);
            Reschedule();
        };
    }

    internal void Start() { _started = true; Reschedule(); }
    internal void Stop() { _started = false; _timer.Stop(); }

    internal void Reschedule()
    {
        _timer.Stop();
        if (!_started || _next() is not { } due) return;
        var delay = due - DateTimeOffset.UtcNow;
        _timer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(20);
        _timer.Start();
    }
}
