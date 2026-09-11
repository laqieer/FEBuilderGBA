using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FEBuilderGBA
{
    internal static class PatchDatabaseOperationLeaseCore
    {
        internal const string WorkspaceName = ".patch2-import";
        internal const string LeaseName = "lease.lock";

        internal sealed class ExistingReadSnapshot
        {
            internal ExistingLeaseProbe Scope { get; }
            internal string ContentIdentity { get; }

            ExistingReadSnapshot(ExistingLeaseProbe scope, string identity)
                => (Scope, ContentIdentity) = (scope, identity);

            // The caller owns this scope's existing reader lease or prepared-import lease.
            internal static ExistingReadSnapshot Capture(ExistingLeaseProbe probe, CancellationToken token,
                PatchDatabaseImportCore.InventoryLimits? limits = null)
            {
                var scope = ProbeExisting(probe.BaseDirectory, probe.Version, probe.SelectedDirectory);
                if (!probe.Managed || !scope.Managed || !scope.HasLock)
                    throw new IOException("The managed patch database is no longer available.");
                limits ??= new PatchDatabaseImportCore.InventoryLimits();
                limits.Validate();
                token.ThrowIfCancellationRequested();
                string root = Path.Combine(scope.BaseDirectory, "config", "patch2");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                Append(hash, "FEBuilderGBA.PatchReadSnapshot.v1");
                bool exists;
                try { File.GetAttributes(root); exists = true; }
                catch (FileNotFoundException) { exists = false; }
                catch (DirectoryNotFoundException) { exists = false; }
                Append(hash, exists ? "directory" : "absent");
                if (exists)
                {
                    if (!ProjectionFileSystemSafety.TryValidateDirectory(root, out string error))
                        throw new IOException(error);
                    byte[] buffer = new byte[64 * 1024];
                    foreach (string path in PatchDatabaseImportCore.EnumerateReadSnapshotTree(root, limits.MaxOperationNodes, token))
                    {
                        token.ThrowIfCancellationRequested();
                        var attributes = File.GetAttributes(path);
                        bool directory = (attributes & FileAttributes.Directory) != 0;
                        EnsureSafeAncestry(path, allowFileLeaf: !directory);
                        Append(hash, Path.GetRelativePath(root, path));
                        Append(hash, directory ? "D" : "F");
                        if (directory)
                        {
                            if (!ProjectionFileSystemSafety.TryValidateDirectory(path, out error))
                                throw new IOException(error);
                            continue;
                        }
                        using var file = ProjectionFileSystemSafety.OpenRegularFileForRead(path);
                        var before = ProjectionFileSystemSafety.InspectOpenedRegularFile(file.SafeFileHandle, path, false);
                        AppendLength(hash, before.Length);
                        long read = 0;
                        int count;
                        while ((count = file.Read(buffer)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            read = checked(read + count);
                            if (read > before.Length) throw new IOException("A patch database file changed during verification.");
                            hash.AppendData(buffer.AsSpan(0, count));
                        }
                        if (read != before.Length) throw new IOException("A patch database file changed during verification.");
                        using var current = ProjectionFileSystemSafety.OpenRegularFileForRead(path);
                        if (!ProjectionFileSystemSafety.SameOpenedFile(file, current))
                            throw new IOException("A patch database file was replaced during verification.");
                        ProjectionFileSystemSafety.VerifyOpenedRegularFileUnchanged(file.SafeFileHandle, path, false, before);
                    }
                }
                token.ThrowIfCancellationRequested();
                var after = ProbeExisting(scope.BaseDirectory, scope.Version, scope.SelectedDirectory);
                if (!after.Managed || !after.HasLock) throw new IOException("The managed patch database changed during verification.");
                return new ExistingReadSnapshot(scope, Convert.ToHexString(hash.GetHashAndReset()));
            }

            internal bool Matches(ExistingLeaseProbe scope, CancellationToken token)
            {
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return scope.Managed && scope.Version == Scope.Version &&
                    string.Equals(scope.BaseDirectory, Scope.BaseDirectory, comparison) &&
                    string.Equals(scope.SelectedDirectory, Scope.SelectedDirectory, comparison) &&
                    ContentIdentity == Capture(scope, token).ContentIdentity;
            }

            static void Append(IncrementalHash hash, string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                AppendLength(hash, bytes.LongLength);
                hash.AppendData(bytes);
            }

            static void AppendLength(IncrementalHash hash, long value)
            {
                Span<byte> bytes = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
                hash.AppendData(bytes);
            }
        }

        internal sealed class ExistingLeaseProbe
        {
            internal string BaseDirectory { get; }
            internal string Version { get; }
            internal string SelectedDirectory { get; }
            internal bool Managed { get; }
            internal bool HasLock { get; }

            ExistingLeaseProbe(string root, string version, string selected, bool managed, bool hasLock)
                => (BaseDirectory, Version, SelectedDirectory, Managed, HasLock) = (root, version, selected, managed, hasLock);

            internal static ExistingLeaseProbe Create(string baseDirectory, string version, string selectedDirectory)
            {
                string root = CanonicalBase(baseDirectory);
                string selected = CanonicalBase(selectedDirectory);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!PatchDatabaseImportCore.IsSupportedVersion(version) ||
                    !string.Equals(Path.Combine(root, "config", "patch2", version), selected, comparison))
                    throw new IOException("The selected patch library does not match its admitted application scope.");
                EnsureSafeAncestry(selected);
                string workspace = Path.Combine(root, WorkspaceName);
                string lockPath = Path.Combine(workspace, LeaseName);
                string marker = Path.Combine(selected, PatchDatabaseZipReaderCore.OwnershipFileName);
                EnsureSafeAncestry(workspace);
                EnsureSafeAncestry(lockPath, allowFileLeaf: true);
                EnsureSafeAncestry(marker, allowFileLeaf: true);
                bool workspacePresent = Present(workspace);
                bool lockPresent = Present(lockPath);
                bool markerPresent = Present(marker);
                return new ExistingLeaseProbe(root, version, selected,
                    workspacePresent || lockPresent || markerPresent, lockPresent);
            }
        }

        static bool Present(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal static ExistingLeaseProbe ProbeExisting(string expectedBaseDirectory, string version, string selectedPatchDirectory)
            => ExistingLeaseProbe.Create(expectedBaseDirectory, version, selectedPatchDirectory);

        internal static Lease AcquireExisting(ExistingLeaseProbe managedProbe)
        {
            ArgumentNullException.ThrowIfNull(managedProbe);
            if (!managedProbe.Managed) throw new IOException("An unmanaged library has no existing lease.");
            ProbeExisting(managedProbe.BaseDirectory, managedProbe.Version, managedProbe.SelectedDirectory);
            string path = Path.Combine(managedProbe.BaseDirectory, WorkspaceName, LeaseName);
            FileStream? stream = null;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1);
                ProbeExisting(managedProbe.BaseDirectory, managedProbe.Version, managedProbe.SelectedDirectory);
                if (!Present(path)) throw new IOException("The managed patch database lease disappeared.");
                var lease = new Lease(managedProbe.BaseDirectory, stream);
                stream = null;
                return lease;
            }
            catch (IOException ex) when (IsLeaseContention(ex.HResult))
            {
                throw new BusyException(ex);
            }
            finally { stream?.Dispose(); }
        }

        internal sealed class BusyException : IOException
        {
            internal BusyException(Exception inner)
                : base("A patch database operation is already running for this application directory.", inner) { }
        }

        internal sealed class Lease : IDisposable
        {
            readonly FileStream stream;
            internal string BaseDirectory { get; }
            internal string WorkspaceDirectory => Path.Combine(BaseDirectory, WorkspaceName);

            internal Lease(string baseDirectory, FileStream stream)
            {
                BaseDirectory = baseDirectory;
                this.stream = stream;
            }

            public void Dispose() => stream.Dispose();
        }

        internal static Lease Acquire(string baseDirectory)
        {
            string root = CanonicalBase(baseDirectory);
            EnsureSafeAncestry(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The application directory does not exist.");
            string workspace = Path.Combine(root, WorkspaceName);
            EnsureSafeAncestry(workspace);
            CreatePrivateDirectory(workspace);
            string lockPath = Path.Combine(workspace, LeaseName);
            EnsureSafeAncestry(lockPath, allowFileLeaf: true);
            var options = new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = 1,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                // The inode/path remains in place after disposal. Removing a lock file permits
                // two cooperating processes to lock different files bearing the same name.
                return new Lease(root, new FileStream(lockPath, options));
            }
            catch (IOException ex) when (IsLeaseContention(ex.HResult))
            {
                throw new BusyException(ex);
            }
        }

        internal static bool IsLeaseContention(int hr)
        {
            // FileStream reports raw Unix errno, but HRESULT_FROM_WIN32 on Windows.
            if (OperatingSystem.IsWindows())
                return hr is unchecked((int)0x80070020) or unchecked((int)0x80070021);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
                return hr == 35;
            return (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) && hr == 11;
        }

        internal static Lease? AcquireForPatch2Repository(string repoDirectory)
        {
            string? root = BaseForPatch2Repository(repoDirectory);
            return root == null ? null : Acquire(root);
        }

        internal static string? BaseForPatch2Repository(string repoDirectory)
        {
            if (string.IsNullOrEmpty(repoDirectory)) return null;
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoDirectory));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string? config = Path.GetDirectoryName(path);
            if (!Path.GetFileName(path).Equals("patch2", comparison) || config == null ||
                !Path.GetFileName(config).Equals("config", comparison))
                return null;
            return Path.GetDirectoryName(config);
        }

        internal static string CanonicalBase(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                throw new IOException("Patch database storage requires a local absolute application directory.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        internal static void CreatePrivateDirectory(string path)
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
            else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            EnsureSafeAncestry(path);
        }

        internal static void EnsureSafeAncestry(string path, bool allowFileLeaf = false)
        {
            string full = CanonicalBase(path);
            string volume = Path.GetPathRoot(full)!;
            string current = volume;
            CheckComponent(current, false);
            string relative = full.Substring(volume.Length);
            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                CheckComponent(current, allowFileLeaf && i == parts.Length - 1);
            }
        }

        static void CheckComponent(string path, bool allowFile)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Patch database storage cannot traverse a reparse point or symbolic link.");
            if (!allowFile && (attributes & FileAttributes.Directory) == 0)
                throw new IOException("A patch database directory component is a file.");
            if (allowFile && (attributes & FileAttributes.Directory) != 0)
                throw new IOException("A patch database file component is a directory.");
        }
    }
}
