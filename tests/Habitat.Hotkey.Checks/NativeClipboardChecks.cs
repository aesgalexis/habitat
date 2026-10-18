using System.Runtime.InteropServices;
using System.Text;
using Habitat.Hotkey;

internal static class NativeClipboardChecks
{
    public static void CheckHotkeyRelease()
    {
        if (!Native.RegisterHotKey(0, 0x6841, 0x4002, 0x48))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Register Ctrl+H");
        try
        {
            if (!Native.UnregisterHotKey(0, 0x6841) || !Native.RegisterHotKey(0, 0x6842, 0x4002, 0x48))
                throw new Exception("Ctrl+H was not released for another registration");
        }
        finally { Native.UnregisterHotKey(0, 0x6841); Native.UnregisterHotKey(0, 0x6842); }
    }
    public static int RunMemoryCopies()
    {
        // Exercise the actual native duplication path without opening ANY clipboard.
        Dictionary<uint, byte[]> formats = new()
        {
            [13] = Encoding.Unicode.GetBytes("original ñ\0"),
            [0xC001] = Encoding.UTF8.GetBytes("<html><b>original</b></html>\0"),
            [15] = FileDrop(), [8] = Dib(), [0xC002] = [0, 1, 2, 0, 255, 128]
        };
        foreach (var pair in formats)
        {
            var original = GlobalAlloc(2, (nuint)pair.Value.Length);
            var pointer = Native.GlobalLock(original);
            Marshal.Copy(pair.Value, 0, pointer, pair.Value.Length);
            Native.GlobalUnlock(original);
            nint copy = 0;
            try
            {
                copy = WindowsSelectionDesktop.DuplicateFormat(pair.Key, original);
                if (copy == 0 || copy == original) throw new Exception("Native data was not independently cloned");
                pointer = Native.GlobalLock(original);
                Marshal.WriteByte(pointer, 0, 99); // Mutating the original cannot alter the backup.
                Native.GlobalUnlock(original);
                pointer = Native.GlobalLock(copy);
                var bytes = new byte[pair.Value.Length];
                try { Marshal.Copy(pointer, bytes, 0, bytes.Length); }
                finally { Native.GlobalUnlock(copy); }
                if (!bytes.SequenceEqual(pair.Value)) throw new Exception($"Native format {pair.Key} was not preserved");
            }
            finally { if (copy != 0) Native.GlobalFree(copy); Native.GlobalFree(original); }
        }
        var pixels = new byte[] { 1, 2, 3, 255 };
        var bitmap = CreateBitmap(1, 1, 1, 32, pixels);
        var bitmapCopy = WindowsSelectionDesktop.DuplicateFormat(2, bitmap);
        try
        {
            var restored = new byte[4];
            if (bitmap == 0 || bitmapCopy == 0 || bitmap == bitmapCopy || GetBitmapBits(bitmapCopy, 4, restored) != 4 ||
                !restored.SequenceEqual(pixels)) throw new Exception("Native GDI bitmap backup failed");
        }
        finally { if (bitmapCopy != 0) Native.DeleteObject(bitmapCopy); if (bitmap != 0) Native.DeleteObject(bitmap); }
        return formats.Count + 1;
    }
    public static int Run()
    {
        // A new window station has its OWN clipboard. Never open the interactive clipboard.
        var station = CreateWindowStation("HabitatChecks" + Guid.NewGuid().ToString("N"), 0, 0xF037F, 0);
        if (station == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Create private clipboard station");
        if (!SetProcessWindowStation(station)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Select private clipboard station");
        var desktop = CreateDesktop("checks", 0, 0, 0, 0x01FF, 0);
        if (desktop == 0 || !SetThreadDesktop(desktop)) throw new InvalidOperationException("Private clipboard desktop unavailable.");
        var window = Native.CreateWindowEx(0, "STATIC", "clipboard checks", 0, 0, 0, 0, 0, -3, 0, 0, 0);
        if (window == 0) throw new InvalidOperationException("Private clipboard owner unavailable.");
        try
        {
            var adapter = new WindowsSelectionDesktop(window, _ => { });
            var checks = 0;
            var html = RegisterClipboardFormat("HTML Format");
            var custom = RegisterClipboardFormat("HABITAT synthetic binary");
            // Unicode, HTML, file drop, DIB image and custom binary formats are preserved byte for byte.
            Dictionary<uint, byte[]> formats = new()
            {
                [13] = Encoding.Unicode.GetBytes("original ñ\0"),
                [html] = Encoding.UTF8.GetBytes("Version:0.9\r\n<html><b>original</b></html>\0"),
                [15] = FileDrop(), [8] = Dib(), [custom] = [0, 1, 2, 0, 255, 128]
            };
            foreach (var original in new[] { new Dictionary<uint, byte[]>(), formats })
            {
                Write(window, original);
                using var backup = adapter.Backup() ?? throw new Exception("Could not back up private clipboard");
                Write(window, new() { [13] = Encoding.Unicode.GetBytes("selected\0") });
                if (!adapter.Restore(backup, adapter.Sequence)) throw new Exception("Restore failed");
                if (!Native.OpenClipboard(window)) throw new Exception("Open failed");
                try
                {
                    foreach (var pair in original)
                    {
                        var data = Native.GetClipboardData(pair.Key);
                        var pointer = Native.GlobalLock(data);
                        var bytes = new byte[pair.Value.Length];
                        try { Marshal.Copy(pointer, bytes, 0, bytes.Length); }
                        finally { Native.GlobalUnlock(data); }
                        if (!bytes.SequenceEqual(pair.Value)) throw new Exception($"Format {pair.Key} changed");
                        checks++;
                    }
                    if (original.Count == 0 && Native.EnumClipboardFormats(0) != 0) throw new Exception("Empty clipboard was not restored");
                    checks++;
                }
                finally { Native.CloseClipboard(); }
            }
            Write(window, formats);
            using (var backup = adapter.Backup()!)
            {
                var previous = adapter.Sequence;
                Write(window, new() { [13] = Encoding.Unicode.GetBytes("new user copy\0") });
                if (adapter.Restore(backup, previous)) throw new Exception("A newer clipboard was overwritten");
                checks++;
            }
            if (!Native.RegisterHotKey(window, 33, 0x4002, 0x48)) throw new Exception("Private hotkey registration failed");
            Native.UnregisterHotKey(window, 33);
            if (!Native.RegisterHotKey(window, 34, 0x4002, 0x48)) throw new Exception("Disabling did not release Ctrl+H");
            Native.UnregisterHotKey(window, 34);
            return checks + 2;
        }
        finally { Native.DestroyWindow(window); }
    }
    private static void Write(nint owner, Dictionary<uint, byte[]> formats)
    {
        if (!Native.OpenClipboard(owner)) throw new Exception("Open failed");
        try
        {
            if (!Native.EmptyClipboard()) throw new Exception("Empty failed");
            foreach (var pair in formats)
            {
                var memory = GlobalAlloc(2, (nuint)pair.Value.Length);
                var pointer = Native.GlobalLock(memory);
                Marshal.Copy(pair.Value, 0, pointer, pair.Value.Length);
                Native.GlobalUnlock(memory);
                if (Native.SetClipboardData(pair.Key, memory) == 0) { Native.GlobalFree(memory); throw new Exception("Set failed"); }
            }
        }
        finally { Native.CloseClipboard(); }
    }
    private static byte[] FileDrop()
    {
        var path = Encoding.Unicode.GetBytes("C:\\synthetic\\example.txt\0\0");
        var bytes = new byte[20 + path.Length];
        BitConverter.GetBytes(20).CopyTo(bytes, 0); BitConverter.GetBytes(1).CopyTo(bytes, 16); path.CopyTo(bytes, 20);
        return bytes;
    }
    private static byte[] Dib()
    {
        var bytes = new byte[44];
        BitConverter.GetBytes(40).CopyTo(bytes, 0); BitConverter.GetBytes(1).CopyTo(bytes, 4); BitConverter.GetBytes(1).CopyTo(bytes, 8);
        bytes[12] = 1; bytes[14] = 32; bytes[40] = 123; bytes[43] = 255;
        return bytes;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowStation(string name, uint flags, uint access, nint security);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessWindowStation(nint station);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);
    [DllImport("user32.dll")] private static extern bool SetThreadDesktop(nint desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);
    [DllImport("gdi32.dll")] private static extern int GetBitmapBits(nint bitmap, int length, byte[] bits);
}
