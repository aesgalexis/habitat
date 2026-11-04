using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Habitat.Features.Contacts;

namespace Habitat.Services;

[SupportedOSPlatform("windows")]
internal sealed class PhoneLinkDesktopAutomation : IPhoneLinkDesktop
{
    internal const string PackageFamily = "Microsoft.YourPhone_8wekyb3d8bbwe";
    internal sealed record Node(string Id, int Type, string AutomationId, string ClassName, string Name, string Value,
        bool Enabled, bool Offscreen, bool CanInvoke, bool CanSetValue, double[] Bounds, string Role = "control");
    internal sealed record Snapshot(nint Window, Node[] Nodes, bool Blocked);

    public async Task<IReadOnlyList<PhoneLinkComposer>> ReadComposersAsync(CancellationToken token) =>
        (await ReadAsync(token)).Select(Composer).Where(c => c.EditorId.Length > 0).ToArray();

    private static PhoneLinkComposer Composer(Snapshot snapshot)
    {
        var editors = snapshot.Nodes.Where(n => n.AutomationId == "InputTextBox" && n.Type == 50004 && n.CanSetValue).ToArray();
        var sends = snapshot.Nodes.Where(n => n.AutomationId == "SendMessageButton" && n.Type == 50000 && n.CanInvoke).ToArray();
        var connection = snapshot.Nodes.Where(n => n.AutomationId == "ConnectivityStatusTextBlock").ToArray();
        return new(snapshot.Window, editors.Length == 1 ? editors[0].Id : "", sends.Length == 1 ? sends[0].Id : "",
            snapshot.Nodes.Where(n => n.Role == "recipient").Select(n => ContactBook.NormalizePhone(n.Name))
                .Where(p => p.Length > 0).Distinct().ToArray(), editors.Length == 1 ? editors[0].Value : "",
            connection.Length == 1 && connection[0].Name.Trim() is "Connected" or "Conectado",
            editors.Length == 1 && editors[0].Enabled, sends.Length == 1 && sends[0].Enabled, snapshot.Blocked,
            MessageType(editors.Select(n => n.Name).Concat(sends.Select(n => n.Name))));
    }

    internal static PhoneLinkMessageType MessageType(IEnumerable<string> labels)
    {
        // Only explicit composer/control labels are evidence; never infer from
        // an iPhone label, recipient name, phone number or message history.
        var types = labels.Select(label => label.Trim().ToLowerInvariant() switch
        {
            "imessage" or "send imessage" or "send an imessage" or "enviar imessage" or "enviar un imessage" => PhoneLinkMessageType.IMessage,
            "sms" or "send sms" or "enviar sms" => PhoneLinkMessageType.Sms,
            _ => PhoneLinkMessageType.Unknown
        }).Where(t => t != PhoneLinkMessageType.Unknown).Distinct().ToArray();
        return types.Length == 1 ? types[0] : PhoneLinkMessageType.Unknown;
    }

    internal Task<Snapshot[]> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var automation = new Automation();
        return PhoneLinkWindows().Select(window => Read(automation, window, token)).ToArray();
    }, token);

    internal Task<IReadOnlyList<PhoneLinkConversationMessage>?> ReadConversationAsync(string address, CancellationToken token) =>
        Task.Run<IReadOnlyList<PhoneLinkConversationMessage>?>(() =>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var automation = new Automation();
                var matches = PhoneLinkWindows().Select(window => (Window: window, Pane: VerifiedPane(window)))
                    .Where(match => match.Pane is not null).ToArray();
                if (matches.Length != 1) return null;
                var (window, pane) = matches[0];
                var paneId = Id(pane!);
                var lists = automation.Find(pane!, 30003, 50008);
                if (lists.Count != 1) return null;
                var messages = new List<PhoneLinkConversationMessage>();
                foreach (var item in automation.Find(lists[0], 30003, 50007).TakeLast(100))
                {
                    token.ThrowIfCancellationRequested();
                    var outgoing = MessageDirection(item.GetCurrentPropertyValue(30005) as string);
                    if (outgoing is null) continue;
                    var bodies = automation.Find(item, 30011, "MessageBody");
                    if (bodies.Count == 1 && bodies[0].GetCurrentPropertyValue(30005) is string text &&
                        !string.IsNullOrWhiteSpace(text))
                        messages.Add(new(Id(item), text, outgoing.Value));
                }
                var current = VerifiedPane(window);
                return current is not null && Id(current) == paneId ? messages : null;

                IElement? VerifiedPane(nint window)
                {
                    var composer = Composer(Read(automation, window, token, out var pane, includeEditor: false));
                    return !composer.Blocked && composer.Connected && PhoneLinkDelivery.RecipientMatches(composer, address) ? pane : null;
                }
            }
            catch (COMException) { return null; } // The window or selected thread may have changed mid-read.
        }, token);

    internal static bool? MessageDirection(string? name)
    {
        if (name is null) return null;
        if (name.StartsWith("Message from you. ", StringComparison.Ordinal) ||
            name.StartsWith("Mensaje tuyo. ", StringComparison.Ordinal)) return true;
        if (name.StartsWith("Message from ", StringComparison.Ordinal) ||
            name.StartsWith("Mensaje de ", StringComparison.Ordinal)) return false;
        return null;
    }

    internal async Task<object> DescribeAsync(CancellationToken token)
    {
        var snapshots = await ReadAsync(token);
        string SafeName(string name) => name is "Send" or "Enviar" or "Send a message" or "Enviar un mensaje" or
            "Connected" or "Conectado" or "New message" or "Nuevo mensaje" ? name : $"<{name.Length} characters>";
        return snapshots.Select(snapshot => new { Window = (long)snapshot.Window, Count = snapshot.Nodes.Length,
            RecipientCount = Composer(snapshot).Recipients.Length, Connected = Composer(snapshot).Connected,
            Controls = snapshot.Nodes.Select(node => new { node.Id, node.Type, node.AutomationId, node.ClassName,
                Name = SafeName(node.Name), ValueLength = node.Value.Length, node.Enabled, node.Offscreen,
                node.CanInvoke, node.CanSetValue, node.Bounds, node.Role,
                PhoneDigits = node.Role == "recipient" ? new string(node.Name.Where(char.IsAsciiDigit).ToArray()).Length : 0 }) }).ToArray();
    }

    private static Snapshot Read(Automation automation, nint window, CancellationToken token) =>
        Read(automation, window, token, out _);

    private static Snapshot Read(Automation automation, nint window, CancellationToken token, out IElement? pane, bool includeEditor = true)
    {
        var root = automation.Root(window);
        var nodes = new List<Node>();
        foreach (var element in automation.Find(root, 30011, "ConnectivityStatusTextBlock")) Add(element);
        var panes = automation.Find(root, 30011, "ConversationPane");
        pane = panes.Count == 1 ? panes[0] : null;
        if (pane is not null)
        {
            if (includeEditor)
            {
                foreach (var element in automation.Find(pane, 30011, "InputTextBox")) Add(element);
                foreach (var element in automation.Find(pane, 30011, "SendMessageButton")) Add(element);
            }
            WalkHeader(pane, 0);
        }
        var blocked = automation.Find(root, 30174, true).Concat(automation.Find(root, 30077, true))
            .Any(element => element.GetCurrentPropertyValue(30022) is not true);
        return new(window, nodes.ToArray(), blocked);

        void WalkHeader(IElement parent, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 25 || nodes.Count > 200) throw new InvalidOperationException("Phone Link's conversation header could not be verified.");
            foreach (var element in automation.Children(parent))
            {
                var type = element.GetCurrentPropertyValue(30003) is int control ? control : 0;
                var id = element.GetCurrentPropertyValue(30011) as string ?? "";
                // Verify the header separately, before reading the selected conversation.
                if (type is 50007 or 50008 || id is "MessageBody" or "InputTextBox" or "SendMessageButton" ||
                    element.GetCurrentPropertyValue(30022) is true) continue;
                if (type == 50020) Add(element, "recipient");
                else if (type != 50000) WalkHeader(element, depth + 1);
            }
        }
        void Add(IElement element, string role = "control")
        {
            if (element.GetCurrentPropertyValue(30022) is true) return;
            var type = element.GetCurrentPropertyValue(30003) is int control ? control : 0;
            var value = type == 50004 && element.GetCurrentPropertyValue(30043) is true
                ? element.GetCurrentPropertyValue(30045) as string ?? "" : "";
            nodes.Add(new(Id(element), type, element.GetCurrentPropertyValue(30011) as string ?? "",
                element.GetCurrentPropertyValue(30012) as string ?? "", element.GetCurrentPropertyValue(30005) as string ?? "", value,
                element.GetCurrentPropertyValue(30010) is true, false, element.GetCurrentPropertyValue(30031) is true,
                element.GetCurrentPropertyValue(30043) is true, element.GetCurrentPropertyValue(30001) as double[] ?? [], role));
        }
    }

    public Task<bool> SetTextAsync(PhoneLinkComposer expected, string address, string text, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!IsPhoneLinkWindow(expected.Window)) return false;
        using var automation = new Automation();
        var current = Composer(Read(automation, expected.Window, token));
        if (!SameControls(current, expected) || !PhoneLinkDelivery.RecipientMatches(current, address) ||
            !current.Connected || current.Blocked || !current.EditorEnabled || current.Text.Length > 0 ||
            current.MessageType == PhoneLinkMessageType.Sms) return false;
        var editors = automation.Find(automation.Root(expected.Window), 30011, "InputTextBox");
        if (editors.Count != 1 || Id(editors[0]) != expected.EditorId ||
            editors[0].GetCurrentPropertyValue(30045) is not string value || value.Length > 0) return false;
        var pattern = (IValue)automation.Keep(editors[0].GetCurrentPattern(10002));
        token.ThrowIfCancellationRequested();
        pattern.SetValue(text);
        return true;
    }, token);

    public Task<PhoneLinkSendOutcome> InvokeSendAsync(PhoneLinkComposer expected, string address, string text,
        TimeSpan acknowledgementTimeout, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!IsPhoneLinkWindow(expected.Window)) return PhoneLinkSendOutcome.NotInvoked;
        using var automation = new Automation(notifications: true);
        var current = Composer(Read(automation, expected.Window, token, out var pane));
        if (pane is null || !SameControls(current, expected) || !PhoneLinkDelivery.Ready(current, address, text))
            return PhoneLinkSendOutcome.NotInvoked;
        var root = automation.Root(expected.Window);
        var buttons = automation.Find(root, 30011, "SendMessageButton");
        if (buttons.Count != 1 || Id(buttons[0]) != expected.SendId || buttons[0].GetCurrentPropertyValue(30010) is not true ||
            buttons[0].GetCurrentPropertyValue(30031) is not true) return PhoneLinkSendOutcome.NotInvoked;
        var pattern = (IInvoke)automation.Keep(buttons[0].GetCurrentPattern(10000));
        using var notifications = SentNotifications.TrySubscribe(automation, root, pane, text);
        current = Composer(Read(automation, expected.Window, token));
        if (!SameControls(current, expected) || !PhoneLinkDelivery.Ready(current, address, text))
            return PhoneLinkSendOutcome.NotInvoked;
        token.ThrowIfCancellationRequested();
        notifications?.Arm();
        pattern.Invoke(); // The only send action; an exception is an uncertain outcome.
        return PhoneLinkDelivery.WaitForAcknowledgement(expected, address, Observe, notifications is not null,
            acknowledgementTimeout, token);

        (PhoneLinkComposer?, bool) Observe()
        {
            if (!IsPhoneLinkWindow(expected.Window)) return (null, false);
            try
            {
                using var reader = new Automation();
                var composer = Composer(Read(reader, expected.Window, token, out var currentPane));
                return (composer, currentPane is not null && PhoneLinkDelivery.RecipientMatches(composer, address) &&
                    notifications?.Matches(reader, currentPane) == true);
            }
            catch (COMException) { return (null, false); } // The pane can be replaced while Phone Link opens the thread.
        }
    }, token);

    internal static bool IsSentNotification(int kind, string activityId, string message) =>
        kind == 2 && activityId == "OutgoingMessageStatusIndicator" &&
        message is "Message Sent" or "Mensaje enviado";

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class SentNotifications(IAutomationNotifications client, IElement root, HashSet<string> existing, string text)
        : INotificationHandler, IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _sent = new();
        private volatile bool _armed;

        internal static SentNotifications? TrySubscribe(Automation automation, IElement root, IElement pane, string text)
        {
            try
            {
                if (automation.NotificationClient is not { } client) return null;
                // Take only IDs before sending. Existing rows must never acknowledge the new attempt.
                var existing = automation.Find(pane, 30011, "MessageBody").Select(Id).ToHashSet();
                var listener = new SentNotifications(client, root, existing, text);
                client.AddNotificationEventHandler(root, 4, 0, listener);
                return listener;
            }
            catch (COMException) { return null; }
        }

        internal void Arm() => _armed = true;

        internal bool Matches(Automation automation, IElement pane) => !_sent.IsEmpty &&
            automation.Find(pane, 30011, "MessageBody").Any(body => _sent.ContainsKey(Id(body)));

        public void HandleNotificationEvent(IElement sender, int kind, int processing, string displayString, string activityId)
        {
            if (!_armed || !IsSentNotification(kind, activityId, displayString)) return;
            try
            {
                using var reader = new Automation();
                var bodies = reader.Find(sender, 30011, "MessageBody");
                if (bodies.Count != 1) return;
                var body = bodies[0];
                var id = Id(body);
                if (!existing.Contains(id) && body.GetCurrentPropertyValue(30005) is string value &&
                    PhoneLinkDelivery.SameText(value, text))
                    _sent.TryAdd(id, 0);
            }
            catch (COMException) { } // The notified row may have disappeared.
        }

        public void Dispose()
        {
            _armed = false;
            try { client.RemoveNotificationEventHandler(root, this); }
            catch (COMException) { } // The window may have closed.
        }
    }

    private static bool SameControls(PhoneLinkComposer left, PhoneLinkComposer right) => left.Window == right.Window &&
        left.EditorId == right.EditorId && left.SendId == right.SendId;

    internal static void ReturnFocus(nint window)
    {
        if (window == 0 || !IsWindow(window)) return;
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
    }

    private static string Id(IElement element) => string.Join(",", element.GetRuntimeId());
    private static List<nint> PhoneLinkWindows()
    {
        var windows = new List<nint>();
        EnumWindows((window, _) => { if (IsPhoneLinkWindow(window)) windows.Add(window); return true; }, 0);
        return windows;
    }

    internal static bool IsPhoneLinkWindow(nint window)
    {
        if (window == 0 || !IsWindowVisible(window)) return false;
        GetWindowThreadProcessId(window, out var id);
        var process = OpenProcess(0x1000, false, id);
        if (process == 0) return false;
        try
        {
            uint length = 0;
            if (GetPackageFamilyName(process, ref length, null) != 122) return false;
            var family = new StringBuilder((int)length);
            return GetPackageFamilyName(process, ref length, family) == 0 && family.ToString() == PackageFamily;
        }
        finally { CloseHandle(process); }
    }

    private sealed class Automation : IDisposable
    {
        private readonly List<object> _objects = [];
        private readonly IAutomation _client;
        internal Automation(bool notifications = false) => _client = (IAutomation)Keep(Activator.CreateInstance(
            Type.GetTypeFromCLSID(notifications ? new("E22AD333-B25F-460C-83D0-0581107395C9")
                : new("FF48DBA4-60EF-4201-AA87-54103EEF594E"), true)!)!);
        internal IAutomationNotifications? NotificationClient => _client as IAutomationNotifications;
        internal T Keep<T>(T value) where T : class { _objects.Add(value); return value; }
        internal IElement Root(nint window) => Keep(_client.ElementFromHandle(window));
        internal IReadOnlyList<IElement> Children(IElement root)
        {
            var condition = Keep(_client.CreateTrueCondition());
            return Elements(root.FindAll(2, condition));
        }
        internal IReadOnlyList<IElement> Find(IElement root, int property, object value)
        {
            var condition = Keep(_client.CreatePropertyCondition(property, value));
            return Elements(root.FindAll(4, condition));
        }
        private IReadOnlyList<IElement> Elements(IElementArray result)
        {
            var array = Keep(result);
            var elements = new List<IElement>();
            for (var index = 0; index < array.Length; index++) elements.Add(Keep(array.GetElement(index)));
            return elements;
        }
        public void Dispose()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(_objects[index])) Marshal.ReleaseComObject(_objects[index]);
        }
    }

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomation
    {
        void _VtblGap1_3();
        IElement ElementFromHandle(nint window);
        void _VtblGap2_14();
        [return: MarshalAs(UnmanagedType.Interface)] object CreateTrueCondition();
        void _VtblGap3_1();
        [return: MarshalAs(UnmanagedType.Interface)] object CreatePropertyCondition(int property, [MarshalAs(UnmanagedType.Struct)] object value);
    }
    [ComImport, Guid("25F700C8-D816-4057-A9DC-3CBDEE77E256"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomationNotifications
    {
        void _VtblGap1_65(); // IUIAutomation through IUIAutomation4.
        void AddNotificationEventHandler(IElement element, int scope, nint cacheRequest, INotificationHandler handler);
        void RemoveNotificationEventHandler(IElement element, INotificationHandler handler);
    }
    [ComImport, Guid("C7CB2637-E6C2-4D0C-85DE-4948C02175C7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationHandler
    {
        void HandleNotificationEvent(IElement sender, int kind, int processing,
            [MarshalAs(UnmanagedType.BStr)] string displayString, [MarshalAs(UnmanagedType.BStr)] string activityId);
    }
    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElement
    {
        void SetFocus();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] GetRuntimeId();
        IElement? FindFirst(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        IElementArray FindAll(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        void _VtblGap2_3();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
        void _VtblGap3_5();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int patternId);
    }
    [ComImport, Guid("14314595-B4BC-4055-95F2-58F2E42C9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IElementArray { int Length { get; } IElement GetElement(int index); }
    [ComImport, Guid("A94CD8B1-0844-4CD6-9D2D-640537AB39E9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IValue { void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value); }
    [ComImport, Guid("FB377FBE-8EA6-46D5-9C73-6499642D3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInvoke { void Invoke(); }
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder? name);
}
