// Shared by Habitat.Core and Windows PowerShell 5.1 (Add-Type). Keep C# 5 compatible.
#pragma warning disable 0649 // DataContractJsonSerializer assigns metadata fields.
#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Habitat.Storage
{
    public sealed class StorageReport
    {
        public long RecoverableBytes { get; internal set; }
        public long FreedBytes { get; internal set; }
        public int Candidates { get; internal set; }
        public bool Busy { get; internal set; }
        public bool Unavailable { get; internal set; }
        public List<string> Preserved { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> Eligible { get; private set; }
        public StorageReport() { Preserved = new List<string>(); Errors = new List<string>(); Eligible = new List<string>(); }
    }

    [DataContract]
    public sealed class GeneratedFile
    {
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "length")] public long Length;
        [DataMember(Name = "sha256")] public string Sha256;
    }

    [DataContract]
    public sealed class GeneratedReceipt
    {
        [DataMember(Name = "version")] public int Version = 1;
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "kind")] public string Kind;
        [DataMember(Name = "files")] public GeneratedFile[] Files;
        [DataMember(Name = "directories")] public string[] Directories;
    }

    [DataContract]
    internal sealed class BuildManifest
    {
        [DataMember(Name = "version")] public int Version;
        [DataMember(Name = "files")] public GeneratedFile[] Files;
    }

    [DataContract]
    internal sealed class OperationRecord
    {
        [DataMember(Name = "paths")] public string[] Paths;
    }

    [DataContract]
    internal sealed class StorageBinding
    {
        [DataMember(Name = "version")] public int Version;
        [DataMember(Name = "installationRoot")] public string InstallationRoot;
        [DataMember(Name = "artifactsRoot")] public string ArtifactsRoot;
    }

    public static class GeneratedStorage
    {
        public const int RetentionDays = 7;
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex ReleaseName = new Regex(@"^release-[a-f0-9]{32}$", RegexOptions.CultureInvariant);
        private static readonly Regex Id = new Regex(@"^[a-f0-9]{32}$", RegexOptions.CultureInvariant);

        // A normal EXE installation has no build tree. Only an explicit, local
        // registration may associate it with a developer's generated storage.
        public static string ReadRegisteredRoot(string installationRoot, string bindingPath)
        {
            try
            {
                if (!File.Exists(bindingPath)) return null;
                var install = Full(installationRoot);
                using (var bindingPins = new Pins(true, System.IO.Path.GetDirectoryName(Full(bindingPath))))
                {
                    bindingPins.Add(System.IO.Path.GetDirectoryName(Full(bindingPath)));
                    using (var file = new Pinned(Full(bindingPath), false, true))
                    using (var stream = file.Read())
                    {
                        var binding = Read<StorageBinding>(stream);
                        if (binding.Version != 1 || !Names.Equals(Full(binding.InstallationRoot), install)) return null;
                        return ValidateRegisteredRoot(binding.ArtifactsRoot);
                    }
                }
            }
            catch (Exception error)
            { if (!IsFileError(error) && !(error is SerializationException) && !(error is ArgumentException)) throw; return null; }
        }

        public static void RegisterRoot(string installationRoot, string artifactsRoot, string bindingPath)
        {
            var install = Full(installationRoot); var root = ValidateRegisteredRoot(artifactsRoot);
            var path = Full(bindingPath); var parent = System.IO.Path.GetDirectoryName(path);
            using (var installPins = new Pins(true, install))
            using (var bindingPins = new Pins(true, parent))
            {
                installPins.Add(install); bindingPins.Add(parent);
                using (var executable = new Pinned(System.IO.Path.Combine(install, "HABITAT.exe"), false, true)) { }
                if (File.Exists(path))
                {
                    using (var file = new Pinned(path, false, true))
                    using (var stream = file.Read())
                        if (Read<StorageBinding>(stream).Version != 1) throw new IOException("Unknown storage registration is preserved.");
                }
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { Write(stream, new StorageBinding { Version = 1, InstallationRoot = install, ArtifactsRoot = root }); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
        }

        private static string ValidateRegisteredRoot(string artifactsRoot)
        {
            if (!System.IO.Path.IsPathRooted(artifactsRoot)) throw new IOException("An absolute generated storage path is required.");
            var root = Full(artifactsRoot);
            if (!Names.Equals(System.IO.Path.GetFileName(root), "artifacts")) throw new IOException("An artifacts directory is required.");
            using (var pins = new Pins(true, root))
            {
                pins.Add(root);
                using (var launcher = new Pinned(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(root), "HABITAT.vbs"), false, true)) { }
                ReadPointer(root, "current-release.txt", true);
            }
            return root;
        }

        // Each path component is opened without following reparse points and pinned
        // against rename/deletion. Deletion uses these exact handles, never recursion
        // by pathname. This also closes the check/delete junction substitution race.
        private sealed class Pinned : IDisposable
        {
            public readonly string Path;
            public readonly SafeFileHandle Handle;
            public readonly bool Directory;
            public readonly long Length;
            public readonly DateTime LastChange;
            public Pinned(string path, bool delete, bool read, bool cloudAncestor = false, bool sharedWrite = false)
            {
                Path = path;
                var share = read && !sharedWrite ? 1u : 3u;
                // Files selected for deletion must have no other readers: bytes
                // reported as freed cannot still be held by a delete-pending file.
                if (delete && (File.GetAttributes(NativePath(path)) & FileAttributes.Directory) == 0) share = 0;
                Handle = CreateFile(NativePath(path), (read ? 0x80000000u : 0x81u) | (delete ? 0x10000u : 0),
                    share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (Handle.IsInvalid) { Handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                try
                {
                    FileInformation info;
                    if (!GetFileInformationByHandle(Handle, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if ((info.Attributes & 0x400) != 0)
                    {
                        AttributeTag tag;
                        if (!cloudAncestor || !GetFileInformationByHandleEx(Handle, 9, out tag, 8) || !IsCloudMetadataTag(tag.Tag))
                            throw new IOException("Reparse points are preserved.");
                    }
                    Directory = (info.Attributes & 0x10) != 0;
                    if (!Directory && info.Links != 1) throw new IOException("Linked files are preserved.");
                    if (delete && (info.Attributes & 1) != 0) throw new IOException("Read-only files are preserved.");
                    Length = ((long)info.SizeHigh << 32) | info.SizeLow;
                    LastChange = new[] { Time(info.Creation), Time(info.Write) }.Max();
                }
                catch { Handle.Dispose(); throw; }
            }
            public FileStream Read()
            {
                // The stream must not own the pinned handle.
                return new FileStream(new SafeFileHandle(Handle.DangerousGetHandle(), false), FileAccess.Read);
            }
            public void Delete()
            {
                var disposition = new Disposition { Delete = true };
                if (!SetFileInformationByHandle(Handle, 4, ref disposition, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Dispose();
            }
            public void Dispose() { Handle.Dispose(); }
        }

        private sealed class Tree : IDisposable
        {
            public readonly List<Pinned> Entries = new List<Pinned>();
            public readonly Dictionary<string, Pinned> Files = new Dictionary<string, Pinned>(Names);
            public readonly string Root;
            public Tree(string root, bool delete, CancellationToken token)
            {
                Root = root;
                try { Walk(root, delete, token, 0); }
                catch { Dispose(); throw; }
            }
            private void Walk(string path, bool delete, CancellationToken token, int depth)
            {
                token.ThrowIfCancellationRequested();
                if (depth > 64 || Entries.Count > 40000) throw new IOException("Generated directory exceeds inspection limits.");
                var item = new Pinned(path, delete, true);
                Entries.Add(item);
                if (item.Directory)
                    foreach (var child in System.IO.Directory.GetFileSystemEntries(NativePath(path))) Walk(LogicalPath(child), delete, token, depth + 1);
                else Files.Add(Relative(Root, path), item);
            }
            public GeneratedFile[] Inventory(CancellationToken token)
            {
                return Files.Select(pair => new GeneratedFile { Path = pair.Key, Length = pair.Value.Length,
                    Sha256 = Hash(pair.Value, token) }).OrderBy(file => file.Path, Names).ToArray();
            }
            public string[] Directories() { return Entries.Where(item => item.Directory && !Names.Equals(item.Path, Root)).Select(item => Relative(Root, item.Path)).OrderBy(path => path, Names).ToArray(); }
            public void Dispose() { foreach (var item in Entries.AsEnumerable().Reverse()) item.Dispose(); }
        }

        private sealed class Pins : IDisposable
        {
            private readonly bool _cloudAncestors;
            private readonly string _anchor;
            private readonly List<Pinned> _items = new List<Pinned>();
            private readonly HashSet<string> _paths = new HashSet<string>(Names);
            public Pins(bool cloudAncestors, string anchor) { _cloudAncestors = cloudAncestors; _anchor = Full(anchor); }
            public void Add(string path)
            {
                var ancestors = new Stack<string>();
                for (var item = new DirectoryInfo(Full(path)); item != null; item = item.Parent) ancestors.Push(item.FullName);
                while (ancestors.Count > 0)
                {
                    var next = ancestors.Pop();
                    if (!_paths.Contains(next))
                    {
                        var cloud = _cloudAncestors && _anchor.StartsWith(Full(next) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                        var pin = new Pinned(next, false, false, cloud);
                        _items.Add(pin); _paths.Add(next);
                    }
                }
            }
            public void Dispose() { foreach (var item in _items.AsEnumerable().Reverse()) item.Dispose(); }
        }

        public sealed class Operation : IDisposable
        {
            private FileStream _lease;
            private readonly string _path;
            internal Operation(FileStream lease, string path) { _lease = lease; _path = path; }
            public void Dispose()
            {
                if (_lease == null) return;
                _lease.Dispose(); _lease = null;
                try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public static Operation BeginOperation(string artifactsRoot, string[] paths)
        {
            var root = Full(artifactsRoot);
            using (var pins = new Pins(true, root))
            {
                pins.Add(root);
                using (AcquireLock(root))
                {
                    var relative = paths.Select(path => Relative(root, Full(path))).ToArray();
                    foreach (var path in relative) CheckRelative(path);
                    var directory = System.IO.Path.Combine(root, ".storage-operations");
                    System.IO.Directory.CreateDirectory(directory); pins.Add(directory);
                    var record = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                    var lease = new FileStream(record, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                    try { Write(lease, new OperationRecord { Paths = relative }); lease.Flush(true); return new Operation(lease, record); }
                    catch { lease.Dispose(); throw; }
                }
            }
        }

        // Producers call this only after successful completion. An exact inventory
        // outside the output tree proves ownership without adding files to packages.
        public static void Complete(string artifactsRoot, string directory, string kind)
        {
            if (kind != "test" && kind != "package" && kind != "consumed" && kind != "build") throw new ArgumentException("Unknown generated output kind.");
            var root = Full(artifactsRoot); var path = Full(directory);
            var relative = Relative(root, path);
            if (!ReceiptPathAllowed(relative)) throw new IOException("This output cannot be registered for cleanup.");
            if (kind == "consumed" && !relative.StartsWith("staged/", StringComparison.Ordinal)) throw new IOException("Only stages can be consumed.");
            if (kind == "build" && !relative.StartsWith("staged/", StringComparison.Ordinal)) throw new IOException("Only generated stages can record a failed build.");
            using (var pins = new Pins(true, root))
            {
                pins.Add(System.IO.Path.GetDirectoryName(path));
                using (AcquireLock(root))
                using (var tree = new Tree(path, false, CancellationToken.None))
                    SaveReceipt(root, new GeneratedReceipt { Path = relative, Kind = kind, Files = tree.Inventory(CancellationToken.None), Directories = tree.Directories() }, pins);
            }
        }

        public static FileStream AcquireLock(string artifactsRoot)
        {
            var root = Full(artifactsRoot);
            using (var pins = new Pins(true, root))
            {
                pins.Add(root);
                var path = System.IO.Path.Combine(root, "update.lock");
                var handle = CreateFile(NativePath(path), 0xC0000080u, 0, IntPtr.Zero, 4, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Maintenance or update already in progress.", new Win32Exception(Marshal.GetLastWin32Error())); }
                FileInformation info;
                if (!GetFileInformationByHandle(handle, out info) || (info.Attributes & 0x410) != 0 || info.Links != 1)
                { handle.Dispose(); throw new IOException("Unsafe update lock."); }
                return new FileStream(handle, FileAccess.ReadWrite);
            }
        }

        public static StorageReport Run(string artifactsRoot, bool clean, string runningDirectory, CancellationToken token)
        {
            return RunAt(artifactsRoot, clean, runningDirectory, DateTime.UtcNow, token);
        }

        // Explicit clock keeps retention tests deterministic without weakening the policy.
        public static StorageReport RunAt(string artifactsRoot, bool clean, string runningDirectory, DateTime now, CancellationToken token)
        {
            var report = new StorageReport();
            clean = clean && Environment.GetEnvironmentVariable("HABITAT_STORAGE_DRY_RUN") != "1";
            try
            {
                var root = Full(artifactsRoot);
                if (!Names.Equals(System.IO.Path.GetFileName(root), "artifacts")) throw new IOException("An installation artifacts directory is required.");
                using (var pins = new Pins(true, root))
                {
                    pins.Add(root);
                    FileStream updateLock;
                    try { updateLock = AcquireLock(root); }
                    catch (IOException) { report.Busy = true; return report; }
                    using (updateLock)
                    {
                        var protectedPaths = new HashSet<string>(Names);
                        var active = ReadPointer(root, "current-release.txt", true);
                        var previous = ReadPointer(root, "previous-release.txt", false);
                        protectedPaths.Add(active);
                        if (previous != null) protectedPaths.Add(previous);
                        if (!String.IsNullOrEmpty(runningDirectory)) protectedPaths.Add(Full(runningDirectory));
                        foreach (var name in new[] { "HABITAT", "HABITAT.Hotkey" })
                            foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                                using (process)
                                {
                                    try { protectedPaths.Add(Full(System.IO.Path.GetDirectoryName(process.MainModule.FileName))); }
                                    catch (InvalidOperationException) { }
                                }
                        ReadOperations(root, protectedPaths, pins);
                        var receipts = ReadReceipts(root, pins);
                        var candidates = new Dictionary<string, GeneratedReceipt>(Names);
                        foreach (var receipt in receipts) candidates.Add(Child(root, receipt.Path), receipt);
                        AddChildren(root, "releases", candidates, pins, true);
                        var legacy = System.IO.Path.Combine(root, "win-x64");
                        if (System.IO.Directory.Exists(legacy) && !candidates.ContainsKey(legacy)) candidates.Add(legacy, null);
                        AddChildren(root, "staged", candidates, pins, false);
                        var pendingStage = candidates.Where(pair => !protectedPaths.Any(keep => Overlap(keep, pair.Key)) &&
                                Names.Equals(System.IO.Path.GetDirectoryName(pair.Key), System.IO.Path.Combine(root, "staged")) &&
                                (pair.Value == null || pair.Value.Kind != "consumed" && pair.Value.Kind != "cleanup"))
                            .OrderByDescending(pair => System.IO.Directory.GetCreationTimeUtc(pair.Key))
                            .Select(pair => pair.Key).FirstOrDefault(path => Usable(root, path, token));
                        if (pendingStage != null) protectedPaths.Add(pendingStage);
                        // Historical Inno work has a fully manifested payload. Other
                        // historical test/checkouts/backups have no ownership proof.
                        var packaging = System.IO.Path.Combine(root, "packaging");
                        if (System.IO.Directory.Exists(packaging))
                        {
                            pins.Add(packaging);
                            foreach (var work in System.IO.Directory.GetDirectories(NativePath(packaging)).Select(LogicalPath))
                            {
                                if (!Id.IsMatch(System.IO.Path.GetFileName(work)) || candidates.ContainsKey(work)) continue;
                                try
                                {
                                    pins.Add(work);
                                    var payload = System.IO.Path.Combine(work, "payload");
                                    if (System.IO.Directory.Exists(payload)) candidates[payload] = null;
                                }
                                catch (Exception error) { if (!IsFileError(error)) throw; report.Preserved.Add(Relative(root, work) + ": linked or unreadable"); }
                            }
                        }
                        // If an old pointer is missing/damaged, retain the newest
                        // complete alternative as well. Never rely on age as usability.
                        if (Names.Equals(previous, active) || !Usable(root, previous, token))
                        {
                            var fallback = candidates.Keys.Where(path => IsVersion(root, path) && !Names.Equals(path, active))
                                .OrderByDescending(path => System.IO.Directory.GetCreationTimeUtc(path))
                                .FirstOrDefault(path => Usable(root, path, token));
                            if (fallback != null) protectedPaths.Add(fallback);
                        }
                        foreach (var candidate in candidates.OrderBy(pair => pair.Key, Names))
                        {
                            token.ThrowIfCancellationRequested();
                            var path = candidate.Key; var relative = Relative(root, path);
                            if (protectedPaths.Any(keep => Overlap(keep, path)))
                            { report.Preserved.Add(relative + ": active, recovery or operation"); continue; }
                            if (!System.IO.Directory.Exists(path)) continue;
                            try
                            {
                                pins.Add(System.IO.Path.GetDirectoryName(path));
                                using (var tree = new Tree(path, clean, token))
                                {
                                    if (tree.Entries.Any(item => item.LastChange > now.AddDays(-RetentionDays) &&
                                        (candidate.Value == null || candidate.Value.Kind != "cleanup" || !item.Directory)))
                                    { report.Preserved.Add(relative + ": seven-day safety margin"); continue; }
                                    if (candidate.Value == null) PreflightBuild(tree);
                                    var inventory = tree.Inventory(token);
                                    if (candidate.Value != null)
                                    {
                                        VerifyInventory(candidate.Value.Files, inventory, candidate.Value.Kind == "cleanup");
                                        VerifyDirectories(tree, candidate.Value.Files, candidate.Value.Directories, candidate.Value.Kind == "cleanup");
                                    }
                                    else VerifyBuild(tree, inventory);
                                    var bytes = inventory.Sum(file => file.Length);
                                    report.RecoverableBytes += bytes; report.Candidates++; report.Eligible.Add(relative);
                                    if (!clean) continue;
                                    // Persist the validated inventory first. If interrupted,
                                    // only unchanged remaining files can be retried.
                                    SaveReceipt(root, new GeneratedReceipt { Path = relative, Kind = "cleanup", Files = inventory, Directories = tree.Directories() }, pins);
                                    foreach (var item in tree.Entries.AsEnumerable().Reverse())
                                    {
                                        token.ThrowIfCancellationRequested();
                                        try { item.Delete(); if (!item.Directory) report.FreedBytes += item.Length; }
                                        catch (Exception error) { if (!IsFileError(error)) throw; report.Errors.Add(relative + ": " + error.GetType().Name); }
                                    }
                                }
                                if (!System.IO.Directory.Exists(path)) File.Delete(ReceiptFile(root, relative));
                            }
                            catch (Exception error)
                            { if (!IsFileError(error) && !(error is SerializationException) && !(error is ArgumentException)) throw;
                                report.Preserved.Add(relative + ": unverified, changed, linked or locked"); report.Errors.Add(relative + ": " + error.GetType().Name); }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            { if (!IsFileError(error) && !(error is SerializationException) && !(error is ArgumentException)) throw;
                report.Unavailable = true; report.Errors.Add(error.GetType().Name); }
            return report;
        }

        private static void AddChildren(string root, string name, Dictionary<string, GeneratedReceipt> candidates, Pins pins, bool releases)
        {
            var parent = System.IO.Path.Combine(root, name);
            if (!System.IO.Directory.Exists(parent)) return;
            pins.Add(parent);
            foreach (var path in System.IO.Directory.GetDirectories(NativePath(parent)).Select(LogicalPath))
                if (!releases || ReleaseName.IsMatch(System.IO.Path.GetFileName(path)))
                    if (!candidates.ContainsKey(path)) candidates.Add(path, null);
        }

        private static string ReadPointer(string root, string name, bool active)
        {
            var path = System.IO.Path.Combine(root, name);
            if (!File.Exists(path)) return active ? System.IO.Path.Combine(root, "win-x64") : null;
            string value;
            using (var item = new Pinned(path, false, true))
            using (var stream = item.Read())
            using (var reader = new StreamReader(stream))
            { if (item.Length > 256) throw new IOException("Invalid release pointer."); value = reader.ReadToEnd().Trim().Replace('\\', '/'); }
            if (value != "win-x64" && !Regex.IsMatch(value, @"^releases/release-[a-f0-9]{32}$")) throw new IOException("Invalid release pointer.");
            return Child(root, value);
        }

        private static void ReadOperations(string root, HashSet<string> paths, Pins pins)
        {
            var directory = System.IO.Path.Combine(root, ".storage-operations");
            if (!System.IO.Directory.Exists(directory)) return;
            pins.Add(directory);
            foreach (var file in System.IO.Directory.GetFiles(NativePath(directory), "*.json").Select(LogicalPath))
            {
                using (var probe = CreateFile(NativePath(file), 0xC0000080u, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
                {
                    if (!probe.IsInvalid)
                    {
                        FileInformation info;
                        if (!GetFileInformationByHandle(probe, out info) || (info.Attributes & 0x410) != 0) throw new IOException("Unsafe operation record.");
                        continue; // No holder: completed or interrupted operation.
                    }
                    if (Marshal.GetLastWin32Error() != 32) throw new IOException("Unreadable operation record.");
                    using (var pin = new Pinned(file, false, true, false, true))
                    using (var stream = pin.Read())
                    {
                        var record = Read<OperationRecord>(stream);
                        if (record.Paths == null || record.Paths.Length == 0) throw new IOException("Unrecognized operation record.");
                        foreach (var path in record.Paths) paths.Add(Child(root, path));
                    }
                }
            }
        }

        private static List<GeneratedReceipt> ReadReceipts(string root, Pins pins)
        {
            var result = new List<GeneratedReceipt>(); var directory = System.IO.Path.Combine(root, ".storage-receipts");
            if (!System.IO.Directory.Exists(directory)) return result;
            pins.Add(directory);
            foreach (var file in System.IO.Directory.GetFiles(NativePath(directory), "*.json").Select(LogicalPath))
            {
                using (var pin = new Pinned(file, false, true))
                using (var stream = pin.Read())
                {
                    var receipt = Read<GeneratedReceipt>(stream);
                    if (receipt.Version != 1 || !ReceiptPathAllowed(receipt.Path) ||
                        (receipt.Kind != "test" && receipt.Kind != "package" && receipt.Kind != "cleanup" && receipt.Kind != "consumed" && receipt.Kind != "build") ||
                        !Names.Equals(file, ReceiptFile(root, receipt.Path))) throw new IOException("Invalid generated output receipt.");
                    result.Add(receipt);
                }
            }
            return result;
        }

        private static bool ReceiptPathAllowed(string path)
        {
            CheckRelative(path);
            return Regex.IsMatch(path, @"^(win-x64|staged/[^/]+|releases/release-[a-f0-9]{32}|packaging/[a-f0-9]{32}(/payload)?|packages/bundle-[a-f0-9]{32}|verification/run-[a-f0-9]{32}|ui-checks/run-[a-f0-9]{32}|storage-checks/run-[a-f0-9]{32})$");
        }

        private static bool Usable(string root, string path, CancellationToken token)
        {
            if (path == null || !System.IO.Directory.Exists(path)) return false;
            try
            {
                using (var pins = new Pins(true, root))
                {
                    pins.Add(System.IO.Path.GetDirectoryName(path));
                    using (var tree = new Tree(path, false, token)) { PreflightBuild(tree); VerifyBuild(tree, tree.Inventory(token)); }
                }
                return true;
            }
            catch (Exception error) { if (!IsFileError(error) && !(error is SerializationException) && !(error is ArgumentException)) throw; return false; }
        }

        private static void VerifyBuild(Tree tree, GeneratedFile[] inventory)
        {
            Pinned pin;
            if (!tree.Files.TryGetValue("build-manifest.json", out pin)) throw new IOException("No complete build manifest.");
            BuildManifest manifest;
            using (var stream = pin.Read()) manifest = Read<BuildManifest>(stream);
            if (manifest.Version != 1) throw new IOException("Unknown build manifest.");
            VerifyInventory(manifest.Files, inventory.Where(file => !Names.Equals(file.Path, "build-manifest.json")).ToArray(), false);
            foreach (var name in new[] { "HABITAT.exe", "HABITAT.dll", "Habitat.Core.dll" })
                if (!tree.Files.ContainsKey(name)) throw new IOException("Build is incomplete.");
            VerifyDirectories(tree, inventory, null, false);
        }

        private static void PreflightBuild(Tree tree)
        {
            Pinned pin;
            if (!tree.Files.TryGetValue("build-manifest.json", out pin)) throw new IOException("No build manifest.");
            BuildManifest manifest;
            using (var stream = pin.Read()) manifest = Read<BuildManifest>(stream);
            if (manifest.Version != 1 || manifest.Files == null || manifest.Files.Length + 1 != tree.Files.Count)
                throw new IOException("Build contents have changed.");
            var names = new HashSet<string>(Names);
            foreach (var file in manifest.Files)
            {
                CheckRelative(file.Path);
                Pinned actual;
                if (!names.Add(file.Path) || !tree.Files.TryGetValue(file.Path, out actual) || actual.Length != file.Length)
                    throw new IOException("Build contents have changed.");
            }
        }

        private static void VerifyDirectories(Tree tree, GeneratedFile[] files, string[] expected, bool remaining)
        {
            var known = new HashSet<string>(Names);
            if (expected != null) foreach (var path in expected) { CheckRelative(path); if (!known.Add(path)) throw new IOException("Duplicate directory."); }
            else foreach (var file in files)
            {
                var slash = file.Path.LastIndexOf('/');
                while (slash > 0) { var parent = file.Path.Substring(0, slash); known.Add(parent); slash = parent.LastIndexOf('/'); }
            }
            var actual = tree.Directories();
            if ((!remaining && actual.Length != known.Count) || actual.Any(path => !known.Contains(path))) throw new IOException("Unclassified directory in generated output.");
        }

        private static void VerifyInventory(GeneratedFile[] expected, GeneratedFile[] actual, bool remaining)
        {
            if (expected == null || expected.Length == 0 || expected.Length > 40000) throw new IOException("Invalid inventory.");
            var files = new Dictionary<string, GeneratedFile>(Names);
            foreach (var file in expected)
            {
                CheckRelative(file.Path);
                if (file.Length < 0 || file.Sha256 == null || !Regex.IsMatch(file.Sha256, "^[a-fA-F0-9]{64}$") || files.ContainsKey(file.Path))
                    throw new IOException("Invalid inventory entry.");
                files.Add(file.Path, file);
            }
            if (!remaining && files.Count != actual.Length) throw new IOException("Output has changed.");
            foreach (var file in actual)
            {
                GeneratedFile match;
                if (!files.TryGetValue(file.Path, out match) || match.Length != file.Length || !Names.Equals(match.Sha256, file.Sha256))
                    throw new IOException("Output has changed.");
            }
        }

        private static void SaveReceipt(string root, GeneratedReceipt receipt, Pins pins)
        {
            if (!ReceiptPathAllowed(receipt.Path)) throw new IOException("Invalid receipt path.");
            var directory = System.IO.Path.Combine(root, ".storage-receipts");
            System.IO.Directory.CreateDirectory(directory); pins.Add(directory);
            var file = ReceiptFile(root, receipt.Path); var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            if (File.Exists(file)) using (var pin = new Pinned(file, false, false)) { }
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { Write(stream, receipt); stream.Flush(true); }
            if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
        }

        private static string ReceiptFile(string root, string relative)
        {
            using (var hash = NewHash())
                return System.IO.Path.Combine(root, ".storage-receipts", BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(relative.ToLowerInvariant()))).Replace("-", "").ToLowerInvariant() + ".json");
        }
        private static string Hash(Pinned file, CancellationToken token)
        {
            using (var stream = file.Read())
            using (var hash = NewHash())
            {
                stream.Position = 0;
                var buffer = new byte[128 * 1024]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
                hash.TransformFinalBlock(buffer, 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }
        private static T Read<T>(Stream stream)
        {
            stream.Position = 0;
            if (stream.Length > 16 * 1024 * 1024) throw new IOException("Metadata exceeds inspection limit.");
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static void Write<T>(Stream stream, T value) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); }
        private static string Full(string path) { return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar); }
        private static string NativePath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }
        private static string LogicalPath(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }
        private static string Child(string root, string relative) { CheckRelative(relative); return Full(System.IO.Path.Combine(root, relative)); }
        private static void CheckRelative(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.IndexOf('\\') >= 0 || System.IO.Path.IsPathRooted(path) || path.IndexOf(':') >= 0 ||
                path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".." || part != part.TrimEnd(' ', '.') || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0))
                throw new IOException("Unsafe relative generated path.");
        }
        private static string Relative(string root, string path)
        {
            if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path is outside generated storage.");
            var value = path.Substring(root.Length + 1).Replace('\\', '/'); CheckRelative(value); return value;
        }
        private static bool Overlap(string a, string b) { return Names.Equals(a, b) || a.StartsWith(b + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        private static bool IsVersion(string root, string path) { return Names.Equals(path, System.IO.Path.Combine(root, "win-x64")) || Names.Equals(System.IO.Path.GetDirectoryName(path), System.IO.Path.Combine(root, "releases")); }
        private static bool IsFileError(Exception error) { return error is IOException || error is UnauthorizedAccessException || error is Win32Exception; }
        private static SHA256 NewHash()
        {
#if NET10_0_OR_GREATER
            return SHA256.Create();
#else
            return new SHA256Cng();
#endif
        }
        public static bool IsCloudMetadataTag(uint tag) { return (tag & 0xFFFF0FFFu) == 0x9000001Au; }
        private static DateTime Time(System.Runtime.InteropServices.ComTypes.FILETIME time) { return DateTime.FromFileTimeUtc(((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime); }

        [StructLayout(LayoutKind.Sequential)] private struct FileInformation
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
        [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public uint Attributes, Tag; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out AttributeTag info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref Disposition info, uint size);
    }
}
