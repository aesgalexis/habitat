using System.Diagnostics;
using System.Runtime.InteropServices;
using Habitat.Activation;

namespace Habitat.Hotkey;

internal sealed class WindowsSelectionDesktop(nint owner, Action<string> report) : ISelectionDesktop
{
    private string? _selectedHash;
    public nint Foreground => Native.GetForegroundWindow();
    public uint Sequence => Native.GetClipboardSequenceNumber();
    public bool IsHabitat(nint window)
    {
        Native.GetWindowThreadProcessId(window, out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName.Equals("HABITAT", StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; } // Unknown/protected foreground: do not inject keys.
    }
    public bool WaitForKeyRelease(nint source)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (Foreground != source) return false;
            if (new[] { 0x11, 0x10, 0x12, 0x5B, 0x5C, 0x48 }.All(key => (Native.GetAsyncKeyState(key) & 0x8000) == 0)) return true;
            Thread.Sleep(20);
        }
        return false;
    }
    public bool HasSelection(nint source)
    {
        // Isolate unresponsive accessibility providers from the resident message loop.
        using var probe = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            ArgumentList = { "--selection", source.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        });
        if (probe is null) return false;
        if (!probe.WaitForExit(2000)) { probe.Kill(); return false; }
        _selectedHash = probe.StandardOutput.ReadToEnd();
        return probe.ExitCode == 0 && _selectedHash.Length == 64 && Foreground == source;
    }
    private bool Open()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Native.OpenClipboard(owner)) return true;
            Thread.Sleep(10);
        }
        return false;
    }
    public IClipboardBackup? Backup()
    {
        if (!Open()) return null;
        var backup = new BackupData();
        try
        {
            ulong total = 0;
            uint format = 0;
            while (true)
            {
                Marshal.SetLastPInvokeError(0);
                format = Native.EnumClipboardFormats(format);
                if (format == 0)
                {
                    if (Marshal.GetLastPInvokeError() != 0) return null;
                    break;
                }
                // Owner-display/private GDI formats cannot be safely cloned generically.
                if (format is 0x80 or >= 0x200 and <= 0x3FF || backup.Items.Count >= 256) return null;
                var original = Native.GetClipboardData(format); // Materialize delayed formats before copying.
                if (original == 0) return null;
                total += (ulong)Native.GlobalSize(original);
                if (total > 64 * 1024 * 1024) return null;
                var copy = DuplicateFormat(format, original);
                if (copy == 0) return null;
                backup.Items.Add(new(format, copy));
            }
            backup.Sequence = Sequence;
            backup.Complete = true;
            return backup;
        }
        finally
        {
            Native.CloseClipboard();
            if (!backup.Complete) backup.Dispose();
        }
    }
    public bool Copy(nint source)
    {
        if (Foreground != source || new[] { 0x11, 0x10, 0x12, 0x5B, 0x5C, 0x48 }
            .Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0)) return false;
        static Native.Input Key(ushort key, bool up = false) => new() { Type = 1,
            Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0 } } };
        Native.Input[] input = [Key(0x11), Key(0x43), Key(0x43, true), Key(0x11, true)];
        var sent = Native.SendInput(4, input, Marshal.SizeOf<Native.Input>());
        if (sent is > 0 and < 4)
        {
            Native.Input[] release = [Key(0x43, true), Key(0x11, true)];
            Native.SendInput(2, release, Marshal.SizeOf<Native.Input>());
        }
        return sent == 4;
    }
    public bool ReadCopy(nint source, uint before, out uint sequence, out string? text)
    {
        sequence = 0;
        text = null;
        if (Sequence == before) return false;
        Native.GetWindowThreadProcessId(source, out var sourceProcess);
        Native.GetWindowThreadProcessId(Native.GetClipboardOwner(), out var clipboardProcess);
        if (!IsSourceProcess(sourceProcess, clipboardProcess)) return false;
        sequence = Sequence;
        if (!Open()) return true;
        try
        {
            if (Sequence != sequence) { sequence = 0; return false; }
            var data = Native.GetClipboardData(13); // Unicode only; never reuse old text.
            var size = (ulong)Native.GlobalSize(data);
            if (data == 0 || size < 2 || size > (ActivationChannel.MaxTextLength + 1UL) * 2) return true;
            var pointer = Native.GlobalLock(data);
            if (pointer == 0) return true;
            try
            {
                var value = Marshal.PtrToStringUni(pointer, (int)(size / 2));
                var candidate = value?.Split('\0', 2)[0];
                if (candidate is not null && SelectionResolver.Hash(candidate) == _selectedHash) text = candidate;
            }
            finally { Native.GlobalUnlock(data); }
            // Delayed rendering may advance the sequence while GetClipboardData runs.
            sequence = Sequence;
            return true;
        }
        finally { Native.CloseClipboard(); }
    }
    public bool Restore(IClipboardBackup saved, uint expectedSequence)
    {
        var backup = (BackupData)saved;
        if (!Open()) return false;
        try
        {
            if (Sequence != expectedSequence) return false;
            if (!Native.EmptyClipboard()) return false;
            var restored = true;
            foreach (var item in backup.Items)
            {
                if (Native.SetClipboardData(item.Format, item.Handle) == 0) restored = false;
                else item.Handle = 0; // Ownership transferred to Windows.
            }
            return restored;
        }
        finally { Native.CloseClipboard(); }
    }
    public void Pause() => Thread.Sleep(20);
    public void Report(string message) => report(message);
    internal static nint DuplicateFormat(uint format, nint original) => format == 14
        ? Native.CopyEnhMetaFile(original, null) : Native.OleDuplicateData(original, (ushort)format, 2);

    internal static bool IsSourceProcess(uint source, uint candidate)
    {
        if (source == 0 || candidate == 0) return false;
        if (source == candidate) return true;
        var snapshot = Native.CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == 0 || snapshot == -1) return false;
        try
        {
            var parents = new Dictionary<uint, uint>();
            var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>() };
            if (!Native.Process32First(snapshot, ref entry)) return false;
            do { parents[entry.Id] = entry.Parent; }
            while (parents.Count < 4096 && Native.Process32Next(snapshot, ref entry));
            return SelectionProcessScope.Contains(source, candidate, parents);
        }
        finally { Native.CloseHandle(snapshot); }
    }

    private sealed class Item(uint format, nint handle)
    { public uint Format = format; public nint Handle = handle; }
    private sealed class BackupData : IClipboardBackup
    {
        public List<Item> Items { get; } = [];
        public bool Complete;
        public uint Sequence { get; set; }
        public void Dispose()
        {
            foreach (var item in Items.Where(item => item.Handle != 0))
            {
                if (item.Format is 2 or 9) Native.DeleteObject(item.Handle);
                else if (item.Format == 14) Native.DeleteEnhMetaFile(item.Handle);
                else
                {
                    if (item.Format == 3)
                    {
                        var pointer = Native.GlobalLock(item.Handle);
                        if (pointer != 0)
                        {
                            Native.DeleteMetaFile(Marshal.ReadIntPtr(pointer, 12 + (IntPtr.Size == 8 ? 4 : 0)));
                            Native.GlobalUnlock(item.Handle);
                        }
                    }
                    Native.GlobalFree(item.Handle);
                }
                item.Handle = 0;
            }
        }
    }
}
