using System.Runtime.InteropServices;

namespace Habitat.Hotkey;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ProcessEntry
    {
        public uint Size, Usage, Id; public nuint Heap; public uint Module, Threads, Parent;
        public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll")] internal static extern nint CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [StructLayout(LayoutKind.Sequential)] internal struct Message
    { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard
    { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint window, uint first, uint last);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] internal static extern bool CloseClipboard();
    [DllImport("user32.dll")] internal static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] internal static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] internal static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll")] internal static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("ole32.dll")] internal static extern nint OleDuplicateData(nint data, ushort format, uint flags);
    [DllImport("kernel32.dll")] internal static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] internal static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] internal static extern nint GlobalFree(nint memory);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] internal static extern nint CopyEnhMetaFile(nint handle, string? file);
    [DllImport("gdi32.dll")] internal static extern bool DeleteEnhMetaFile(nint handle);
    [DllImport("gdi32.dll")] internal static extern bool DeleteMetaFile(nint handle);
}
