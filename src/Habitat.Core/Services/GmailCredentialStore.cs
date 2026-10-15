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

internal sealed class GmailCredentialStore(string? path = null, string? vaultTarget = null) : IGmailCredentialStore
{
    private readonly string _path = path ?? DefaultPath;
    private readonly bool _useVault = path is null || vaultTarget is not null;
    private readonly string _vaultTarget = vaultTarget ?? "HABITAT/Gmail/DesktopAccount/v1";
    public string StorageLocation => _useVault ? "Windows Credential Manager: " + _vaultTarget : _path;

    internal static string DefaultPath => Path.Combine(DesktopDataDirectory(), "gmail-account.bin");

    internal static string DesktopDataDirectory() => AppDataPaths.Production.DesktopDirectory;

    public GmailAccount Load()
    {
        if (_useVault && ReadVault() is GmailAccount vaulted) return vaulted;
        byte[] encrypted;
        try { encrypted = File.ReadAllBytes(_path); }
        catch (FileNotFoundException) { return new GmailAccount(null, null, null); }
        catch (DirectoryNotFoundException) { return new GmailAccount(null, null, null); }
        if (encrypted.Length > 131072) throw new InvalidDataException("Invalid Gmail account storage.");
        var json = Protect(encrypted, decrypt: true);
        try
        {
            var account = JsonSerializer.Deserialize<GmailAccount>(json) ?? throw new InvalidDataException("Invalid Gmail account storage.");
            if (_useVault && account.Client is not null) Save(account);
            return account;
        }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    public void Save(GmailAccount account)
    {
        if (_useVault) { WriteVault(account); return; }
        var json = JsonSerializer.SerializeToUtf8Bytes(account);
        byte[] encrypted;
        try { encrypted = Protect(json, decrypt: false); }
        finally { CryptographicOperations.ZeroMemory(json); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private GmailAccount? ReadVault()
    {
        if (!CredRead(_vaultTarget, 1, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null; // ERROR_NOT_FOUND
            throw new Win32Exception(error, "Windows could not read the saved Gmail authorization.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.BlobSize is 0 or > 2560) throw new InvalidDataException("Invalid Gmail credential in Windows.");
            var json = new byte[credential.BlobSize];
            Marshal.Copy(credential.Blob, json, 0, json.Length);
            try { return JsonSerializer.Deserialize<GmailAccount>(json) ?? throw new InvalidDataException("Invalid Gmail credential in Windows."); }
            finally { CryptographicOperations.ZeroMemory(json); }
        }
        finally { CredFree(pointer); }
    }

    private void WriteVault(GmailAccount account)
    {
        // Persist offline authorization. Short-lived access tokens remain in
        // memory and are renewed silently after each application restart.
        var durable = account with { Tokens = account.Tokens is null ? null :
            new GmailTokens("", account.Tokens.RefreshToken, DateTimeOffset.MinValue) };
        var json = JsonSerializer.SerializeToUtf8Bytes(durable);
        var pointer = IntPtr.Zero;
        try
        {
            if (json.Length > 2560) throw new InvalidDataException("The Gmail authorization exceeds Windows credential storage limits.");
            pointer = Marshal.AllocHGlobal(json.Length);
            Marshal.Copy(json, 0, pointer, json.Length);
            var credential = new NativeCredential
            {
                Type = 1, TargetName = _vaultTarget, UserName = "HABITAT",
                BlobSize = (uint)json.Length, Blob = pointer, Persist = 2 // CRED_PERSIST_LOCAL_MACHINE
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not save the Gmail authorization.");
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.Copy(new byte[json.Length], 0, pointer, json.Length);
                Marshal.FreeHGlobal(pointer);
            }
            CryptographicOperations.ZeroMemory(json);
        }
    }

    internal static void DeleteTestVault(string target)
    {
        if (!target.StartsWith("HABITAT/Gmail/Test/", StringComparison.Ordinal)) throw new ArgumentException("Only test credentials may be removed.");
        if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
