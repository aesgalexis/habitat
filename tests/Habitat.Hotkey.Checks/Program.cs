using Habitat.Activation;
using System.Diagnostics;

if (args is ["--wait-fixture", var readyFile])
{
    File.WriteAllText(readyFile, "ready");
    await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}

await CompanionShutdownChecks.RunAsync();

if (args.Contains("--native-clipboard"))
{
    Console.WriteLine($"Native clipboard checks passed: {NativeClipboardChecks.Run()}.");
    return;
}
if (args.Contains("--native-registration"))
{
    NativeClipboardChecks.CheckHotkeyRelease();
    Console.WriteLine("Native Ctrl+H registration and release passed. The clipboard was not opened.");
    return;
}
if (args.Contains("--resident-cycle"))
{
    var name = ActivationChannel.Name("Hotkey");
    var resident = await ActivationChannel.SendAsync(name, new(Guid.NewGuid(), "status"))
        ?? throw new Exception("No HABITAT resident is running");
    var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    if (!resident.Accepted || resident.Executable is null ||
        !resident.Executable.StartsWith(Path.Combine(project, "artifacts", "releases") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(resident.Executable) != "HABITAT.Hotkey.exe") throw new Exception("Resident is not a verified project release");
    try
    {
        await ActivationChannel.SendAsync(name, new(Guid.NewGuid(), "stop"));
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await ActivationChannel.SendAsync(name, new(Guid.NewGuid(), "status"), 50) is null) break;
            await Task.Delay(100);
        }
        // RegisterHotKey with a null HWND belongs to the calling thread.
        await Task.Run(NativeClipboardChecks.CheckHotkeyRelease);
        Console.WriteLine("PASS: stopping the resident releases Ctrl+H for other applications");
    }
    finally
    {
        using var restarted = Process.Start(new ProcessStartInfo(resident.Executable)
        {
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
        });
    }
    ActivationReply? ready = null;
    for (var attempt = 0; attempt < 50; attempt++)
    {
        ready = await ActivationChannel.SendAsync(name, new(Guid.NewGuid(), "status"), 100);
        if (ready?.Accepted == true) break;
        await Task.Delay(100);
    }
    if (ready?.Accepted != true) throw new Exception("The resident did not regain Ctrl+H");
    Console.WriteLine("PASS: the original resident is running again with Ctrl+H registered; no clipboard was opened");
    return;
}

var checks = 0;
void Check(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); checks++; }
checks += NativeClipboardChecks.RunMemoryCopies();
using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--wait-fixture" } })!)
{
    try
    {
        Check(Habitat.Hotkey.WindowsSelectionDesktop.IsSourceProcess((uint)Environment.ProcessId, (uint)child.Id) &&
            !Habitat.Hotkey.WindowsSelectionDesktop.IsSourceProcess((uint)child.Id, (uint)Environment.ProcessId),
            "The native process snapshot accepts a real child without accepting its parent in reverse");
    }
    finally { if (!child.HasExited) child.Kill(); child.WaitForExit(); }
}
Dictionary<uint, uint> processTree = new() { [20] = 10, [30] = 20, [40] = 2, [50] = 60, [60] = 50 };
Check(SelectionProcessScope.Contains(10, 10, processTree), "Native editors retain exact clipboard ownership");
Check(SelectionProcessScope.Contains(10, 20, processTree) && SelectionProcessScope.Contains(10, 30, processTree),
    "WebView2 browser and renderer children belong to their native source host");
Check(!SelectionProcessScope.Contains(10, 40, processTree), "Another browser process cannot supply clipboard content");
Check(!SelectionProcessScope.Contains(10, 50, processTree) && !SelectionProcessScope.Contains(10, 0, processTree),
    "Cycles, unknown owners and missing process ancestry fail closed");
Check(SelectionResolver.Resolve(true, false, [[], ["selected in Codex document"]]) == SelectionResolver.Hash("selected in Codex document"),
    "A Chromium document supplies the selection when the focused child has no TextPattern");
Check(SelectionResolver.Resolve(true, false, [[""], ["selected response"]]) == SelectionResolver.Hash("selected response"),
    "An empty editor caret does not hide the selected response in its document");
Check(SelectionResolver.Resolve(true, false, [["selected\r\ntext"], ["selected\ntext"]]) == SelectionResolver.Hash("selected\rtext"),
    "Agreeing editor and document selections share a fingerprint across Windows line endings");
Check(SelectionResolver.Resolve(true, false, [["first"], ["other"]]) is null, "Conflicting document selections are rejected");
Check(SelectionResolver.Resolve(false, false, [["foreign selection"]]) is null, "A focus outside the source HWND is rejected");
Check(SelectionResolver.Resolve(true, true, [["secret"]]) is null, "Password fields never use a document fallback");
Check(SelectionResolver.Resolve(true, false, [[], [""]]) is null, "Empty document selection never triggers line copying");
Check(SelectionResolver.Resolve(true, false, [["one", "two"]]) is null, "Multiple selected ranges are not guessed");
Check(SelectionResolver.Resolve(true, false, [[new string('x', ActivationChannel.MaxTextLength + 1)]]) is null,
    "Document fallback retains the size limit");

foreach (var format in new[] { "empty", "text", "image", "html", "files", "custom" })
{
    var desktop = new FakeDesktop { Format = format };
    Check(new SelectionCapture(desktop).Capture() == "selected\r\ntexto", $"Capture from {format}");
    Check(desktop.Restored == format && desktop.Disposed && desktop.Copies == 1, $"Restore {format} and release backup");
}
foreach (var state in new[] { "habitat", "no-selection", "locked", "keys-held", "focus-changed", "backup-changed" })
{
    var desktop = new FakeDesktop { State = state };
    Check(new SelectionCapture(desktop).Capture() is null && desktop.Copies == 0, $"No side effects: {state}");
}
foreach (var state in new[] { "unchanged", "unrelated-owner", "non-text", "oversize", "copy-failed" })
{
    var desktop = new FakeDesktop { State = state };
    Check(new SelectionCapture(desktop).Capture() is null, $"Do not insert stale or invalid text: {state}");
    Check(state is "non-text" or "oversize" ? desktop.Restored is not null : desktop.Restored is null, $"Clipboard ownership: {state}");
}
var race = new FakeDesktop { State = "restore-race" };
Check(new SelectionCapture(race).Capture() is not null && race.Restored is null && race.Reports == 1, "Concurrent user copy wins");
var failure = new FakeDesktop { State = "read-failed" };
try { new SelectionCapture(failure).Capture(); throw new Exception("Expected read failure"); }
catch (IOException) { Check(failure.Restored is not null && failure.Disposed, "Restore after read exception"); }

foreach (var state in new[] { "closed", "open", "minimized", "starting", "uncertain" })
{
    var sends = 0; var launches = 0;
    Guid id = Guid.Empty;
    var delivery = new ActivationDelivery(request =>
    {
        Check(request.Text == "literal; do not run" && (id == Guid.Empty || id == request.Id), "Literal payload and stable request ID");
        id = request.Id; sends++;
        return Task.FromResult<ActivationReply?>(state == "uncertain" ? new(false, "uncertain") :
            state is "closed" or "starting" && sends < (state == "starting" ? 4 : 2) ? null : new(true));
    }, () => launches++, () => Task.CompletedTask);
    var result = await delivery.DeliverAsync("literal; do not run");
    Check(result.Accepted == (state != "uncertain") && launches == (state is "closed" or "starting" ? 1 : 0), $"Activation {state}");
    if (state == "uncertain") Check(sends == 1, "No retry after uncertain acknowledgement");
}

var pipeName = "HABITAT.Checks." + Guid.NewGuid().ToString("N");
var received = 0;
await using (var channel = new ActivationChannel(pipeName, request =>
{
    received++;
    return Task.FromResult(new ActivationReply(request.Kind == "activate" && request.Text == "日本語\nñ"));
}))
{
    var request = new ActivationRequest(Guid.NewGuid(), Text: "日本語\nñ");
    Check((await ActivationChannel.SendAsync(pipeName, request, 1000))?.Accepted == true, "Actual current-user pipe transports Unicode");
    Check((await ActivationChannel.SendAsync(pipeName, request, 1000))?.Accepted == true && received == 1, "Duplicate activation is acknowledged without insertion");
    Check((await ActivationChannel.SendAsync(pipeName, new(Guid.NewGuid(), Text: new('x', ActivationChannel.MaxTextLength + 1)), 1000))?.Accepted == false,
        "Oversized text is rejected before UI dispatch");
}
Check(await ActivationChannel.SendAsync(pipeName, new(Guid.NewGuid()), 30) is null, "Disposed listener releases pipe");
Console.WriteLine($"Hotkey checks passed: {checks}. Only synthetic clipboard data was used. Native integration is available with --native-clipboard on a host that permits private window stations.");

sealed class FakeDesktop : ISelectionDesktop
{
    public string State = "normal", Format = "text";
    public int Copies, Reports;
    public bool Disposed;
    public string? Restored;
    public nint Foreground => State == "focus-changed" && Disposed == false && _backedUp ? 2 : 1;
    private bool _backedUp;
    public bool IsHabitat(nint window) => State == "habitat";
    public bool WaitForKeyRelease(nint source) => State != "keys-held";
    public bool HasSelection(nint source) => State != "no-selection";
    public IClipboardBackup? Backup() { _backedUp = true; return State == "locked" ? null : new Saved(this); }
    public uint Sequence => State == "backup-changed" ? 3u : Copies == 0 || State is "unchanged" or "copy-failed" ? 1u : 2u;
    public bool Copy(nint source) { Copies++; return State != "copy-failed"; }
    public bool ReadCopy(nint source, uint before, out uint sequence, out string? text)
    {
        sequence = 0; text = null;
        if (Sequence == before || State == "unrelated-owner") return false;
        sequence = 2;
        if (State == "read-failed") { State = "normal"; throw new IOException("synthetic"); }
        text = State == "non-text" ? null : State == "oversize" ? new('x', ActivationChannel.MaxTextLength + 1) : "selected\r\ntexto";
        return true;
    }
    public bool Restore(IClipboardBackup backup, uint sequence)
    { if (State == "restore-race") return false; Restored = Format; return true; }
    public void Pause() { }
    public void Report(string message) => Reports++;
    private sealed class Saved(FakeDesktop desktop) : IClipboardBackup
    { public uint Sequence => 1; public void Dispose() => desktop.Disposed = true; }
}
