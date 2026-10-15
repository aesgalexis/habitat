$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force
$releaseDirectory = Get-HabitatActiveDirectory (Join-Path $projectRoot 'artifacts')
$executable = Join-Path $releaseDirectory 'HABITAT.exe'
$icon = Join-Path $releaseDirectory 'Assets\HabitatDot.ico'
$launcher = Join-Path $env:WINDIR 'System32\wscript.exe'
$launcherArguments = '"' + (Join-Path $projectRoot 'HABITAT.vbs') + '"'
$appId = "HABITAT.Desktop"

if (-not ("Habitat.ShellLinkIdentity" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Habitat
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                VariantType = 31,
                PointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    public static class ShellLinkIdentity
    {
        static PropertyKey AppIdKey = new PropertyKey(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        public static string SetAndRead(string path, string appId)
        {
            object link = new ShellLink();
            try
            {
                var file = (IPersistFile)link;
                file.Load(path, 2);
                var store = (IPropertyStore)link;
                var value = PropVariant.FromString(appId);
                try
                {
                    Marshal.ThrowExceptionForHR(store.SetValue(ref AppIdKey, ref value));
                    Marshal.ThrowExceptionForHR(store.Commit());
                    file.Save(path, true);
                }
                finally
                {
                    PropVariantClear(ref value);
                }

                PropVariant saved;
                Marshal.ThrowExceptionForHR(store.GetValue(ref AppIdKey, out saved));
                try
                {
                    return saved.PointerValue == IntPtr.Zero
                        ? ""
                        : Marshal.PtrToStringUni(saved.PointerValue);
                }
                finally
                {
                    PropVariantClear(ref saved);
                }
            }
            finally
            {
                if (Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
            }
        }

        public static void Notify(string path)
        {
            SHChangeNotify(0x00002000, 0x0005, path, IntPtr.Zero);
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant value);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(uint eventId, uint flags,
            [MarshalAs(UnmanagedType.LPWStr)] string item1, IntPtr item2);
    }
}
'@
}

$shell = New-Object -ComObject WScript.Shell
$locations = @(
    (Join-Path ([Environment]::GetFolderPath("ApplicationData")) "Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
    [Environment]::GetFolderPath("Desktop")
)

$updated = @()
$shortcutBackupRoot = Join-Path $projectRoot ('artifacts\shortcut-backups\' + [guid]::NewGuid().ToString('N'))
$projectArtifacts = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')).TrimEnd('\') + '\'

foreach ($location in $locations) {
    if (-not (Test-Path -LiteralPath $location)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $location -Filter "*.lnk") {
        $shortcut = $shell.CreateShortcut($file.FullName)
        $isProjectExecutable = [IO.Path]::GetFileName($shortcut.TargetPath) -eq 'HABITAT.exe' -and
            $shortcut.TargetPath.StartsWith($projectArtifacts, [StringComparison]::OrdinalIgnoreCase)
        $isStableLauncher = $shortcut.TargetPath -eq $launcher -and $shortcut.Arguments -eq $launcherArguments
        if (-not $isProjectExecutable -and -not $isStableLauncher -and $shortcut.TargetPath -ne (Join-Path $projectRoot 'HABITAT.vbs')) { continue }
        New-Item -ItemType Directory -Path $shortcutBackupRoot -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $shortcutBackupRoot ([guid]::NewGuid().ToString('N') + '.lnk'))
        $shortcut.TargetPath = $launcher
        $shortcut.Arguments = $launcherArguments
        $shortcut.WorkingDirectory = $projectRoot
        $shortcut.IconLocation = "$icon,0"
        $shortcut.Save()
        $savedIcon = $shortcut.IconLocation
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        $savedAppId = [Habitat.ShellLinkIdentity]::SetAndRead($file.FullName, $appId)
        [Habitat.ShellLinkIdentity]::Notify($file.FullName)
        $updated += [pscustomobject]@{
            Shortcut = $file.FullName
            Icon = $savedIcon
            AppUserModelId = $savedAppId
        }
    }
}

$updated | Format-List
