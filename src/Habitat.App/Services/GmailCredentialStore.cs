using Habitat.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Habitat.Services;

internal interface IGmailCredentialStore
{
    GmailAccount Load();
    void Save(GmailAccount account);
}

internal sealed class GmailCredentialStore(string? path = null) : IGmailCredentialStore
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HABITAT", "gmail-account.bin");

    public GmailAccount Load()
    {
        if (!File.Exists(_path)) return new GmailAccount(null, null, null);
        var encrypted = File.ReadAllBytes(_path);
        if (encrypted.Length > 131072) throw new InvalidDataException("Invalid Gmail account storage.");
        var json = Protect(encrypted, decrypt: true);
        try { return JsonSerializer.Deserialize<GmailAccount>(json) ?? new GmailAccount(null, null, null); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    public void Save(GmailAccount account)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(account);
        byte[] encrypted;
        try { encrypted = Protect(json, decrypt: false); }
        finally { CryptographicOperations.ZeroMemory(json); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllBytes(temporaryPath, encrypted);
        File.Move(temporaryPath, _path, overwrite: true);
    }

    // DPAPI binds the encrypted file to the current Windows user. No account
    // passwords, refresh tokens or client credentials are stored in the repo.
    private static byte[] Protect(byte[] data, bool decrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Gmail account storage requires Windows.");
        var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var success = decrypt
                ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not protect the Gmail account with Windows.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[data.Length], 0, input.Data, data.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                if (decrypt) Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
