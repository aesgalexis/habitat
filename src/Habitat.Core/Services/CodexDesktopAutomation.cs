using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Habitat.Services;

// Windows accessibility verifies the editor and acknowledges submission.
// Navigation uses Codex's documented deep link. A unique marker identifies
// the new editor before ValuePattern inserts literal text without Markdown parsing.
[SupportedOSPlatform("windows")]
internal sealed partial class CodexDesktopAutomation(string packageFamily, Func<Uri, Task<bool>> openNewChat,
    string? executablePath = null, Action<string>? trace = null) : ICodexDesktop, Habitat.Features.Codex.ICodexChatDesktop
{
    private nint[]? _windows;
    public Task<bool> OpenNewChatAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return openNewChat(uri);
    }

    public Task<IReadOnlyList<CodexComposer>> ReadComposersAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<CodexComposer>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = GetForegroundWindow();
        var windows = IsCodexWindow(window) ? [window] :
            (_windows ??= FindWindows()).Where(IsCodexWindow).ToArray();
        if (windows.Length == 0) { _windows = null; trace?.Invoke("Waiting for the installed Codex window."); return []; }
        using var automation = new Automation();
        return windows.SelectMany(handle => ReadWindow(automation, handle, trace).Composers).ToArray();
    }, cancellationToken);

    public Task<bool> SetTextAsync(CodexComposer composer, string text, CancellationToken cancellationToken) =>
        SetTextCoreAsync(composer, text, cancellationToken);

    private Task<bool> SetTextCoreAsync(CodexComposer composer, string text, CancellationToken cancellationToken,
        bool allowEmpty = false, IReadOnlyList<string>? selection = null) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCodexWindow(composer.Window)) return false;
        using var automation = new Automation();
        var state = ReadWindow(automation, composer.Window, trace);
        if (state.Blocked) return false;
        var current = state.Composers.SingleOrDefault(item =>
            item.EditorId == composer.EditorId && item.SendId == composer.SendId && item.IsNewChat == composer.IsNewChat &&
            (item.CanSend || allowEmpty) &&
            CodexDelivery.SameText(item.Text, composer.Text));
        if (current is null) return false;
        var root = automation.Root(composer.Window);
        if (selection is not null && !SelectedTitles(automation, root).SequenceEqual(selection)) return false;
        var editor = automation.Elements(root, 30003, 50004).SingleOrDefault(element => Id(element, composer.Window) == composer.EditorId);
        if (editor is null || editor.GetCurrentPropertyValue(30043) is not true ||
            editor.GetCurrentPropertyValue(30046) is not false ||
            !CodexDelivery.SameText(automation.Text(editor), composer.Text)) return false;
        var value = (IValue)automation.Keep(editor.GetCurrentPattern(10002));
        cancellationToken.ThrowIfCancellationRequested();
        value.SetValue(CodexDelivery.Normalize(text));
        trace?.Invoke("Codex literal instruction set; waiting for exact text verification before Enter.");
        return true;
    }, cancellationToken);

    public Task<bool> PressEnterAsync(CodexComposer composer, CancellationToken cancellationToken) =>
        PressEnterCoreAsync(composer, cancellationToken);

    private Task<bool> PressEnterCoreAsync(CodexComposer composer, CancellationToken cancellationToken,
        IReadOnlyList<string>? selection = null) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCodexWindow(composer.Window)) return false;
        if (IsIconic(composer.Window)) ShowWindowAsync(composer.Window, 9); // SW_RESTORE
        if (GetForegroundWindow() != composer.Window && !SetForegroundWindow(composer.Window))
        {
            trace?.Invoke("Codex could not take foreground focus; Enter was not sent.");
            return false;
        }
        using var automation = new Automation();
        var state = ReadWindow(automation, composer.Window, trace);
        if (state.Blocked) return false;
        var current = state.Composers.SingleOrDefault(item =>
            item.EditorId == composer.EditorId && item.SendId == composer.SendId && item.IsNewChat == composer.IsNewChat && item.CanSend &&
            CodexDelivery.SameText(item.Text, composer.Text));
        if (current is null) return false;
        var root = automation.Root(composer.Window);
        if (selection is not null && !SelectedTitles(automation, root).SequenceEqual(selection)) return false;
        foreach (var editor in automation.Elements(root, 30003, 50004))
        {
            if (Id(editor, composer.Window) != composer.EditorId) continue;
            editor.SetFocus();
            var focused = automation.FocusedElement();
            // Keyboard input is allowed only for this verified editor, in the
            // foreground Codex window, with no user-held modifier keys.
            if (Id(focused, composer.Window) != composer.EditorId || GetForegroundWindow() != composer.Window ||
                !CodexDelivery.SameText(automation.Text(editor), composer.Text) ||
                new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            Input[] enter = [new() { Type = 1, Data = new() { Keyboard = new() { Key = 0x0D } } },
                new() { Type = 1, Data = new() { Keyboard = new() { Key = 0x0D, Flags = 2 } } }];
            var count = SendInput((uint)enter.Length, enter, Marshal.SizeOf<Input>());
            trace?.Invoke($"Codex Enter: {count}/{enter.Length} input events delivered.");
            if (count == 0) return false;
            if (count != enter.Length) throw new InvalidOperationException("Codex Enter delivery was incomplete.");
            _submittedWindow = composer.Window;
            return true; // Acknowledgement is checked separately; never press again.
        }
        return false;
    }, cancellationToken);

    public Task<bool> WasSubmittedAsync(CodexComposer composer, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCodexWindow(composer.Window)) return false;
        using var automation = new Automation();
        var root = automation.Root(composer.Window);
        var conversation = Conversation(automation, root);
        if (conversation is null) { trace?.Invoke("Codex acknowledgement: waiting for Conversation."); return false; }
        // The exact instruction was checked immediately before Enter. An
        // established Conversation plus a consumed composer acknowledges
        // submission independently of Markdown
        // rendering, split paragraphs or a model response still in progress.
        // The route can replace the editor, so its runtime ID may change.
        var editors = automation.Elements(root, 30003, 50004).Where(element =>
            element.GetCurrentPropertyValue(30022) is not true && element.GetCurrentPropertyValue(30012) is string name &&
            name.Contains("ProseMirror", StringComparison.Ordinal)).ToArray();
        if (editors.Length != 1) return false;
        var text = automation.Text(editors[0]);
        // Chromium includes the empty editor's placeholder in Value.Value.
        // Its accessible name is that same localized placeholder. Only use
        // this acknowledgement after the transition to a new Conversation;
        // it must never relax the exact instruction check before Enter.
        return IsEmptyEditor(editors[0], text);
    }, cancellationToken);

    private static bool IsEmptyEditor(IElement editor, string text) => string.IsNullOrWhiteSpace(text) ||
        editor.GetCurrentPropertyValue(30005) is string label && label.Length > 0 && text.TrimEnd('\r', '\n') == label;

    private sealed record WindowState(IReadOnlyList<CodexComposer> Composers, bool Blocked);

    private static WindowState ReadWindow(Automation automation, nint window, Action<string>? trace = null)
    {
        var root = automation.Root(window);
        // The home title is localized and is not consistently exposed as H1.
        // A draft on the new-chat page has no Conversation landmark yet.
        var isNewChat = Conversation(automation, root) is null;
        var blocked = root.GetCurrentPropertyValue(30174) is true ||
            automation.Elements(root, 30174, true).Any(element => element.GetCurrentPropertyValue(30022) is not true) || // IsDialog
            automation.Elements(root, 30077, true).Any(element => element.GetCurrentPropertyValue(30022) is not true); // Window.IsModal
        var buttons = automation.Elements(root, 30003, 50000).Where(element =>
            element.GetCurrentPropertyValue(30022) is not true &&
            element.GetCurrentPropertyValue(30005) is string name && name is "Send" or "Enviar").ToArray();
        var send = buttons.Length == 1 ? buttons[0] : null;
        var composers = automation.Elements(root, 30003, 50004).Where(element => element.GetCurrentPropertyValue(30022) is not true &&
            element.GetCurrentPropertyValue(30012) is string name && name.Contains("ProseMirror", StringComparison.Ordinal))
            .Select(element => new CodexComposer(
                window, Id(element, window), send is null ? "" : Id(send, window), automation.Text(element),
                isNewChat && !blocked, send?.GetCurrentPropertyValue(30010) is true)).ToArray();
        trace?.Invoke($"Codex controls: home={isNewChat}, blocked={blocked}, sendButtons={buttons.Length}, editors={composers.Length}, ready={composers.Count(item => item.CanSend)}.");
        return new(composers, blocked);
    }

    private static IElement? Conversation(Automation automation, IElement root) =>
        automation.Elements(root, 30005, "Conversation").FirstOrDefault(element =>
            element.GetCurrentPropertyValue(30101) is "region" || element.GetCurrentPropertyValue(30003) is 50026 or 50033);

    private static string Id(IElement element, nint window) => window + ":" + string.Join(",", element.GetRuntimeId());

    private bool IsCodexWindow(nint window)
    {
        if (window == 0 || !IsWindowVisible(window)) return false;
        GetWindowThreadProcessId(window, out var id);
        var process = OpenProcess(0x1000, false, id); // QUERY_LIMITED_INFORMATION
        if (process == 0) return false;
        try
        {
            uint count = 0;
            var familyResult = GetPackageFamilyName(process, ref count, null);
            if (familyResult == 122)
            {
                var family = new StringBuilder((int)count);
                if (GetPackageFamilyName(process, ref count, family) == 0 &&
                    family.ToString().Equals(packageFamily, StringComparison.OrdinalIgnoreCase)) return true;
            }
            // Full-trust packaged desktop apps may lack a process package
            // identity. Match the exact executable obtained from PackageManager.
            if (executablePath is not null)
            {
                uint length = 32768;
                var image = new StringBuilder((int)length);
                if (QueryFullProcessImageName(process, 0, image, ref length) &&
                    image.ToString().Equals(executablePath, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        finally { CloseHandle(process); }
    }

    private nint[] FindWindows()
    {
        var windows = new List<nint>();
        EnumWindows((window, _) => { if (IsCodexWindow(window)) windows.Add(window); return true; }, 0);
        trace?.Invoke($"Installed Codex windows found: {windows.Count}.");
        return windows.ToArray();
    }

    private sealed class Automation : IDisposable
    {
        private readonly List<object> _objects = new();
        private readonly IAutomation _client;
        internal Automation() => _client = (IAutomation)Keep(Activator.CreateInstance(
            Type.GetTypeFromCLSID(new("FF48DBA4-60EF-4201-AA87-54103EEF594E"), true)!)!);
        internal T Keep<T>(T value) where T : class { _objects.Add(value); return value; }
        internal IElement Root(nint window) => Keep(_client.ElementFromHandle(window));
        internal IElement FocusedElement() => Keep(_client.GetFocusedElement());
        internal IElement? Find(IElement root, int property, object value)
        {
            var condition = Keep(_client.CreatePropertyCondition(property, value));
            var element = root.FindFirst(4, condition);
            return element is null ? null : Keep(element);
        }
        internal IReadOnlyList<IElement> Elements(IElement root, int property, object value)
        {
            var condition = Keep(_client.CreatePropertyCondition(property, value));
            var array = Keep(root.FindAll(4, condition));
            var elements = new List<IElement>();
            for (var index = 0; index < array.Length; index++) elements.Add(Keep(array.GetElement(index)));
            return elements;
        }
        internal string Text(IElement element)
        {
            if (element.GetCurrentPropertyValue(30043) is true && // IsValuePatternAvailable
                element.GetCurrentPropertyValue(30045) is string value) return value; // Value.Value
            if (element.GetCurrentPropertyValue(30040) is not true) return ""; // IsTextPatternAvailable
            var pattern = (IText)Keep(element.GetCurrentPattern(10014));
            return Keep(pattern.DocumentRange).GetText(-1);
        }
        public void Dispose()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(_objects[index])) Marshal.ReleaseComObject(_objects[index]);
        }
    }

    // Minimal Windows SDK UI Automation declarations, matching NotepadTabs.
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomation
    {
        void _VtblGap1_3();
        IElement ElementFromHandle(nint window);
        void _VtblGap2_1();
        IElement GetFocusedElement();
        void _VtblGap3_14();
        [return: MarshalAs(UnmanagedType.Interface)]
        object CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
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
    [ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IText
    {
        void _VtblGap1_4();
        ITextRange DocumentRange { get; }
    }
    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITextRange
    {
        void _VtblGap1_9();
        [return: MarshalAs(UnmanagedType.BStr)] string GetText(int maxLength);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { internal int X, Y; internal uint MouseData, Flags, Time; internal nuint ExtraInfo; }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref uint length);
}
