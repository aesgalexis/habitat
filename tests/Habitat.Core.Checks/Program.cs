using Habitat;
using Habitat.Models;
using Habitat.Services;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var root = Path.Combine(AppContext.BaseDirectory, "isolated-core", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
BrowserChecks.Run(root, Check);
var paths = AppDataPaths.Isolated(root);
Check(paths.IsIsolated && paths.LocalDirectory == root && paths.DesktopDirectory == root && paths.TasksPath.StartsWith(root), "Isolated paths never resolve desktop data.");

var conversation = new ConversationSession();
conversation.Add("SYSTEM", "status", MessageTone.Muted);
Check(!conversation.HasActiveConversation, "Status alone does not collapse the sidebar.");
conversation.Add("YOU", "hello", MessageTone.User);
Check(conversation.HasActiveConversation && conversation.Messages.Count == 2, "Conversation state is independent of controls.");
conversation.Clear();
Check(!conversation.HasActiveConversation && conversation.Messages.Count == 0, "Clearing a conversation restores its state.");
var bounded = new ConversationSession(maxMessages: 3, maxCharacters: 80);
for (var index = 0; index < 10; index++) bounded.Add("YOU", "message " + index, MessageTone.User);
Check(bounded.HasTrimmedMessages && bounded.Messages.Select(message => message.Body).SequenceEqual(["message 7", "message 8", "message 9"]),
    "Conversation retention bounds old messages while preserving the latest ones in order.");
bounded.Add("OpenAI", new string('x', 100), MessageTone.Assistant);
Check(bounded.Messages.Count == 1 && bounded.Messages[0].Body.Length == 100 && bounded.HasActiveConversation,
    "The character budget retires older messages without truncating the newest reply.");
bounded.Clear();
Check(!bounded.HasTrimmedMessages && !bounded.HasActiveConversation && bounded.Messages.Count == 0,
    "Clearing resets retention bookkeeping and the conversation state.");

var scope = new AsyncOperationScope();
var started = new TaskCompletionSource();
var finished = false;
var running = scope.RunAsync(async () =>
{
    started.SetResult();
    try { await Task.Delay(Timeout.InfiniteTimeSpan, scope.Token); }
    finally { finished = true; }
});
await started.Task;
var stop = scope.StopAsync();
await stop;
try { await running; } catch (OperationCanceledException) { }
Check(finished && scope.IsStopping && scope.Token.IsCancellationRequested, "Shutdown cancels and drains work before completion.");
Check(ReferenceEquals(stop, scope.StopAsync()), "Shutdown is idempotent.");
var newWork = false;
await scope.RunAsync(() => { newWork = true; return Task.CompletedTask; });
Check(!newWork, "Shutdown rejects new operations.");
Check(await scope.RunAsync(() => Task.FromResult(42), -1) == -1, "Stopped operations return their explicit fallback without running.");
var drainingScope = new AsyncOperationScope();
var cleanUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var draining = drainingScope.RunAsync(async () =>
{
    try { await Task.Delay(Timeout.InfiniteTimeSpan, drainingScope.Token); }
    finally { await cleanUp.Task; }
    return true;
}, false);
var drainStop = drainingScope.StopAsync();
Check(!drainStop.IsCompleted, "Stop waits for asynchronous cleanup after cancellation.");
cleanUp.SetResult();
await drainStop;
try { await draining; } catch (OperationCanceledException) { }

var failingScope = new AsyncOperationScope();
using var callback = failingScope.Token.Register(() => throw new InvalidOperationException("Synthetic cancellation failure"));
var drained = false;
var pending = failingScope.RunAsync(async () =>
{
    try { await Task.Delay(Timeout.InfiniteTimeSpan, failingScope.Token); }
    finally { drained = true; }
});
try { await failingScope.StopAsync(); } catch (AggregateException) { }
try { await pending; } catch (OperationCanceledException) { }
Check(drained, "Even a failing cancellation callback cannot skip draining operations.");

using var noNetwork = new HttpClient(new ErrorHandler());
using var syntheticWeather = new WeatherService(noNetwork);
var locationCompletion = new TaskCompletionSource<DeviceLocationResult>();
var weatherClock = new ManualTime();
var weatherSession = new WeatherSession(syntheticWeather, (_, _) => locationCompletion.Task, weatherClock);
var weatherRequest = weatherSession.RefreshAsync("Saved city", true, true, () => false, CancellationToken.None);
Check(weatherSession.IsRefreshing, "Weather work exposes its active state independently of controls.");
locationCompletion.SetResult(new(null, "Unavailable"));
Check(await weatherRequest is null && !weatherSession.IsRefreshing, "A stale weather request stops before contacting a forecast service.");
try { await weatherSession.RefreshAsync("Saved city", false, false, () => true, CancellationToken.None); } catch (HttpRequestException) { }
Check(weatherSession.NextRefresh == weatherClock.GetUtcNow().AddMinutes(5) && !weatherSession.IsRefreshing,
    "Weather failures retain the existing five-minute retry and release the operation.");

var preferences = new Preferences("night", 35)
    { Destination = ComposerDestination.Translate, TextOnly = false, VSCodeFolders = [Path.Combine(root, "sample project")] };
Check(preferences.Save(paths.PreferencesPath), "Preferences save succeeds.");
Check(Preferences.Load(Path.Combine(root, "missing-preferences.json")).GlobalHotkeyEnabled,
    "Existing installations enable the global shortcut by default.");
Check((preferences with { GlobalHotkeyEnabled = false }).Save(paths.PreferencesPath) &&
    !Preferences.Load(paths.PreferencesPath).GlobalHotkeyEnabled, "Disabling Ctrl+H survives a preferences reload.");
Check(preferences.Save(paths.PreferencesPath), "Restore synthetic preferences for persistence checks.");
var original = File.ReadAllText(paths.PreferencesPath);
var reported = false;
using (var locked = new FileStream(paths.PreferencesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    Check(!(preferences with { Opacity = 50 }).Save(paths.PreferencesPath, _ => reported = true), "A locked replacement reports failure.");
Check(reported && File.ReadAllText(paths.PreferencesPath) == original && !Directory.EnumerateFiles(root, "*.tmp").Any(), "Failed preference writes preserve the original and clean up.");
var storedPreferences = Preferences.Load(paths.PreferencesPath);
Check((storedPreferences with { VSCodeFolders = preferences.VSCodeFolders }) == preferences &&
    storedPreferences.VSCodeFolders.SequenceEqual(preferences.VSCodeFolders),
    "All existing preference values and remembered project folders survive atomic storage.");
var replacement = Path.Combine(root, "prepared.json");
File.WriteAllText(replacement, "replacement");
var replaceAttempts = 0;
AtomicFile.Replace(replacement, paths.PreferencesPath, (source, target) =>
{
    if (++replaceAttempts < 3) throw new IOException("Synthetic Windows scanner lock", unchecked((int)0x80070497));
    File.Replace(source, target, null);
});
Check(replaceAttempts == 3 && File.ReadAllText(paths.PreferencesPath) == "replacement",
    "A transient Windows replacement lock is retried without truncating the original.");

var store = new TaskStore(paths.TasksPath);
var session = new TaskSession(store);
await session.LoadAsync();
var updates = Enumerable.Range(0, 12).Select(i => session.UpdateAsync(items =>
    [.. items, new(Guid.NewGuid(), "Synthetic " + i, false, DateTimeOffset.UtcNow)])).ToArray();
Check((await Task.WhenAll(updates)).All(result => result) && session.Items.Count == 12,
    "Queued task mutations use the latest committed state. " + session.Error);
var independent = new TaskStore(paths.TasksPath);
var independentItems = independent.Load();
independent.Save([.. independentItems, new(Guid.NewGuid(), "External", false, DateTimeOffset.UtcNow)]);
Check(!await session.UpdateAsync(items => [.. items]) && !session.IsLoaded && session.Error is TaskStoreConflictException,
    "Asynchronous task sessions preserve conflict detection.");
Check(await session.LoadAsync() && session.Items.Count == 13, "Reload restores the newer task list.");
var extraHistoryTask = new TaskItem(Guid.NewGuid(), "Recovered external history", false, DateTimeOffset.UtcNow);
var addedRevision = JsonSerializer.SerializeToUtf8Bytes<List<TaskItem>>([.. session.Items, extraHistoryTask]);
File.WriteAllBytes(Path.Combine(paths.TasksPath + ".history",
    "revision-99999999999999999999-" + Convert.ToHexString(SHA256.HashData(addedRevision)) + ".json"), addedRevision);
Check(await session.UpdateAsync(items => [.. items]), "An existing session can preserve a newly imported history revision.");
Check(new TaskStore(paths.TasksPath).Load().Any(item => item.Id == extraHistoryTask.Id),
    "The index cannot conceal tasks found in a revision imported between load and save.");

var clock = new ManualTime();
var delivery = new BridgeSession(clock);
Check(!delivery.Accept(new("response", null, "orphan", null)), "An event without an active request is rejected.");
Check(delivery.TryQueue("hello") && !delivery.TryQueue("duplicate"), "Only one delivery can be queued.");
var id = delivery.Message!.Id;
Check(!delivery.Accept(new("started", "wrong", null, null)), "Wrong IDs cannot change delivery state.");
delivery.Accept(new("started", id, null, null));
delivery.Accept(new("received", id, null, null));
Check(delivery.Phase == DeliveryPhase.Responding && delivery.PendingDelivery is null, "A late receipt cannot regress response state or redeliver.");
clock.Advance(BridgeProtocol.RequestTimeout);
Check(!delivery.TryExpire(BridgeProtocol.RequestTimeout), "The existing timeout boundary is preserved.");
clock.Advance(TimeSpan.FromTicks(1));
Check(delivery.TryExpire(BridgeProtocol.RequestTimeout) && !delivery.Accept(new("response", id, "late", null)), "Expired IDs cannot complete another request.");
delivery.TryQueue("next");
var nextId = delivery.Message!.Id;
delivery.Accept(new("response", nextId, "answer", null));
Check(delivery.Phase == DeliveryPhase.Idle && !delivery.Accept(new("response", nextId, "duplicate", null)), "Completion is accepted once.");
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using (var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge-v1.json"))))
{
    Check(fixture.RootElement.GetProperty("protocolVersion").GetInt32() == BridgeProtocol.Version, "Fixture and desktop protocol versions agree.");
    var message = fixture.RootElement.GetProperty("message").Deserialize<OutgoingMessage>(jsonOptions)!;
    Check(message.Text == "Synthetic café\n雪" && fixture.RootElement.GetProperty("events").EnumerateArray()
        .Select(element => element.Deserialize<BridgeEvent>(jsonOptions)!).All(data => data.Id == message.Id), "Wire fixtures retain Unicode, IDs and event field names.");
}

// Representative history with 200 complete revisions, never user tasks.
var largePath = Path.Combine(root, "history-benchmark.json");
var history = largePath + ".history";
Directory.CreateDirectory(history);
var largeItems = Enumerable.Range(0, 1000).Select(i => new TaskItem(Guid.NewGuid(), "Synthetic task " + i, false, DateTimeOffset.UtcNow)).ToList();
byte[] latest = [];
for (var revision = 0; revision < 200; revision++)
{
    largeItems[0] = largeItems[0] with { Text = "Revision " + revision };
    latest = JsonSerializer.SerializeToUtf8Bytes(largeItems);
    File.WriteAllBytes(Path.Combine(history, $"revision-{revision:D8}-{Convert.ToHexString(SHA256.HashData(latest))}.json"), latest);
}
File.WriteAllBytes(largePath, latest);
var watch = Stopwatch.StartNew();
Check(new TaskStore(largePath).Load().Count == 1000, "Cold history scan preserves all tasks.");
var cold = watch.Elapsed.TotalMilliseconds;
watch.Restart();
Check(new TaskStore(largePath).Load().Count == 1000, "Indexed history preserves all tasks.");
var warm = watch.Elapsed.TotalMilliseconds;
File.WriteAllText(Path.Combine(history, "index.json"), "{damaged");
File.Delete(largePath);
Check(new TaskStore(largePath).Load().SequenceEqual(largeItems), "A damaged index falls back to the original verified revisions for recovery.");
Check(Directory.EnumerateFiles(history, "revision-*.json").Count() >= 200, "Optimization never deletes historical revisions.");
Console.WriteLine($"History benchmark: 1,000 tasks / 200 revisions; full scan {cold:F1} ms, indexed {warm:F1} ms.");
Console.WriteLine($"Core checks passed: {checks}. Only isolated synthetic data was used.");

sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

sealed class ErrorHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic network failure"));
}
