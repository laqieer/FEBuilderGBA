// SPDX-License-Identifier: GPL-3.0-or-later
// Buildfile recipe exporter (#1935).
//
// Emits a deterministic, git-friendly source recipe describing the COMPLETE binary
// delta from a clean ROM to a modded ROM. The governing invariant is losslessness:
// every target byte is owned exactly once by either
//   - the clean baseline (unchanged bytes up to clean.Length),
//   - a declared extension fill (bytes past clean.Length that equal the chosen fill),
//   - or exactly one sparse payload range (bytes that differ from clean-or-fill).
//
// `buildfile.json` + `data/` are the SOLE authoritative recipe (consumed by #1936).
// `main.event` (derived Event Assembler installer), the patch inventory, and the
// optional `source/` projection are advisory surfaces that never weaken the raw
// recipe and are never composed into authoritative reconstruction here.
//
// The planner (pure, deterministic) is separated from filesystem publication so it
// can be unit-tested without touching disk. Publication stages the whole tree in a
// uniquely named sibling under the destination's exact parent, then publishes it
// with a single directory rename; no source ROM path or full ROM is ever written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA
{
    /// <summary>Status of the optional source-aware rebuild projection.</summary>
    internal enum BuildfileProjectionStatus
    {
        Skipped = 0,
        Success = 1,
        Refused = 2,
        Error = 3,
    }

    /// <summary>Outcome of the built-in advisory source projection.</summary>
    internal sealed class BuildfileProjectionOutcome
    {
        public BuildfileProjectionStatus Status { get; set; } = BuildfileProjectionStatus.Skipped;
        public string Reason { get; set; } = "";

        public static BuildfileProjectionOutcome Skip(string reason)
            => new BuildfileProjectionOutcome { Status = BuildfileProjectionStatus.Skipped, Reason = reason ?? "" };
        public static BuildfileProjectionOutcome Ok()
            => new BuildfileProjectionOutcome { Status = BuildfileProjectionStatus.Success, Reason = "" };
        public static BuildfileProjectionOutcome Refuse(string reason)
            => new BuildfileProjectionOutcome { Status = BuildfileProjectionStatus.Refused, Reason = reason ?? "" };
        public static BuildfileProjectionOutcome Fail(string reason)
            => new BuildfileProjectionOutcome { Status = BuildfileProjectionStatus.Error, Reason = reason ?? "" };
    }

    /// <summary>
    /// Test-only source projection seam. Production exports use the built-in synchronous
    /// <see cref="RebuildProducerCore"/> projection. A test runner must finish all work before
    /// returning; detached workers are outside this internal fault-injection contract.
    /// </summary>
    internal delegate BuildfileProjectionOutcome BuildfileProjectionRunner(string scratchDir);

    /// <summary>
    /// Canonical path helpers shared by the exporter and its CLI front-end.
    ///
    /// Normalization: every path is run through <see cref="Path.GetFullPath(string)"/> then
    /// <see cref="Path.TrimEndingDirectorySeparator(string)"/> (roots preserved) BEFORE any
    /// parent/name/existence/staging decision, so a trailing separator (<c>project/</c>) never
    /// changes behavior. Windows device-namespace spellings (<c>\\?\</c>, <c>\\.\</c>, and
    /// <c>\??\</c>) are rejected fail-closed instead of allowing an alternate spelling to bypass
    /// identity checks.
    ///
    /// Input identity (ROM inputs): we do NOT blanket-reject symlinks/junctions. Instead we
    /// resolve each EXISTING input to its PHYSICAL canonical path via
    /// <see cref="ResolvePhysicalPath"/> — a realpath that walks every component from the root
    /// and follows symlink/junction targets, including ANCESTOR links (so
    /// <c>C:\link\mod.gba</c> collapses to <c>C:\real\mod.gba</c> when <c>C:\link</c> is a
    /// junction to <c>C:\real</c>). <see cref="SamePhysicalFile"/> then compares the two
    /// resolved paths, so aliases of the SAME file are rejected while two DISTINCT files that
    /// merely share a benign/system symlinked ancestor (e.g. macOS <c>/var → /private/var</c>)
    /// are accepted. Resolution never broad-catches: permission/IO/loop faults surface as an
    /// explicit <see cref="IOException"/>.
    ///
    /// Output staging: only the IMMEDIATE output parent is checked for a reparse point via
    /// <see cref="IsReparsePoint"/> — the atomic-publish guarantee needs only that the stage
    /// sibling and the destination share the same immediate parent, so we do NOT walk the
    /// output chain to root (which would reject legitimate roots and system symlinks).
    ///
    /// Comparison is OS-appropriate: case-insensitive on Windows/macOS (conservative — per-volume
    /// case sensitivity is not probed), case-sensitive elsewhere. Windows additionally compares
    /// the volume serial + 128-bit file ID, catching hard links, local UNC aliases, and mounted
    /// drive aliases that cannot be collapsed lexically. Final opened-handle identity checks also
    /// reject hard-link aliases on Unix via device/inode comparison.
    /// </summary>
    public static class BuildfilePathSafety
    {
        /// <summary>Full-path normalize + trailing-separator trim (roots preserved).</summary>
        public static string NormalizeFullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is empty.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows()
                && (fullPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
                    || fullPath.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
                    || fullPath.StartsWith(@"\??\", StringComparison.Ordinal)))
            {
                throw new IOException(
                    @"Windows device-namespace paths (\\?\, \\.\, and \??\) are not supported; use a standard drive or UNC path.");
            }
            return Path.TrimEndingDirectorySeparator(fullPath);
        }

        internal static string ToWindowsExtendedPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return fullPath;
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + fullPath.Substring(2);
            return @"\\?\" + fullPath;
        }

        static StringComparison PathComparison =>
            (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>OS-appropriate equality of two normalized full paths.</summary>
        public static bool PathsEqual(string a, string b)
            => string.Equals(NormalizeFullPath(a), NormalizeFullPath(b), PathComparison);

        /// <summary>
        /// True when <paramref name="candidatePath"/> is the same physical directory as, or has
        /// an ancestor with the same filesystem identity as, <paramref name="rootPath"/>.
        /// Physical entry identity catches alternate drive/UNC/mount spellings that normalized
        /// path-prefix comparison cannot.
        /// </summary>
        public static bool IsSameOrDescendantPath(string candidatePath, string rootPath)
            => IsSameOrDescendantPath(
                candidatePath,
                rootPath,
                SameResolvedPhysicalFile);

        internal static bool IsSameOrDescendantPath(
            string candidatePath,
            string rootPath,
            Func<string, string, bool> sameIdentity)
        {
            if (sameIdentity == null) throw new ArgumentNullException(nameof(sameIdentity));
            string candidate = NormalizeFullPath(candidatePath);
            string root = NormalizeFullPath(rootPath);
            while (true)
            {
                if (string.Equals(candidate, root, PathComparison)
                    || sameIdentity(candidate, root))
                    return true;

                string parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent))
                    return false;
                parent = NormalizeFullPath(parent);
                if (string.Equals(parent, candidate, PathComparison))
                    return false;
                candidate = parent;
            }
        }

        /// <summary>
        /// True when the RAW path value contains a path segment that is exactly <c>..</c>
        /// (parent-directory traversal). Splits on the platform's directory separators
        /// (<c>/</c> and <c>\</c> on Windows; only <c>/</c> on Unix, where <c>\</c> is a legal
        /// filename character), so a filename that merely CONTAINS dots (e.g. <c>my..rom.gba</c>,
        /// <c>..config</c>) is NOT matched. On Windows an initial drive designator is removed
        /// before scanning, so drive-relative traversal such as <c>C:..\rom.gba</c> cannot hide
        /// the first <c>..</c> inside a <c>C:..</c> segment. Used to fail-closed on ROM inputs BEFORE any
        /// normalization/existence/load, because <see cref="Path.GetFullPath(string)"/> collapses
        /// <c>..</c> LEXICALLY before symlinks are resolved, which can diverge from the physical
        /// filesystem when a <c>..</c> segment follows a symlinked component.
        /// </summary>
        public static bool ContainsParentTraversal(string rawPath)
        {
            if (string.IsNullOrEmpty(rawPath)) return false;
            string pathToScan = rawPath;
            if (OperatingSystem.IsWindows()
                && rawPath.Length >= 2
                && char.IsLetter(rawPath[0])
                && rawPath[1] == ':')
            {
                pathToScan = rawPath.Substring(2);
            }
            foreach (string seg in pathToScan.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }))
                if (seg == "..") return true;
            return false;
        }

        /// <summary>
        /// Resolve <paramref name="path"/> to a PHYSICAL canonical path (realpath): walk every
        /// component from the volume/filesystem root and follow each existing symlink/junction
        /// to its target — INCLUDING ancestor links — so aliases collapse to one physical path.
        /// Non-existent trailing components (the destination may not exist yet) are appended
        /// lexically (they cannot be links). Windows drive/UNC roots and Unix roots are handled;
        /// relative link targets are resolved against the link's own (already-physical) directory;
        /// symlink loops are bounded and raise an <see cref="IOException"/>; a permission/
        /// attribute/target-read fault also raises an explicit <see cref="IOException"/>. Never
        /// broad-catches or silently accepts uncertainty.
        ///
        /// Raw parent-traversal (<c>..</c>) segments are rejected at this public boundary before
        /// normalization (see <see cref="ContainsParentTraversal"/>). This method normalizes via
        /// <see cref="Path.GetFullPath(string)"/>, which would otherwise resolve <c>..</c>
        /// LEXICALLY before any symlink is followed; a <c>..</c> after a symlinked component can
        /// diverge from the physical filesystem. A traversal segment is therefore always a
        /// fail-closed <see cref="IOException"/>, never silently mis-resolved. Traversal contained
        /// inside a filesystem-owned link target is different: on Unix its components are
        /// processed during the physical walk after preceding target links have been resolved;
        /// Windows applies Win32's lexical link-target normalization before that walk.
        /// </summary>
        public static string ResolvePhysicalPath(string path)
            => ResolvePhysicalPath(path, File.GetAttributes);

        internal static string ResolvePhysicalPath(
            string path,
            Func<string, FileAttributes> getAttributes)
        {
            if (getAttributes == null) throw new ArgumentNullException(nameof(getAttributes));
            if (ContainsParentTraversal(path))
                throw new IOException("Path must not contain parent-directory (..) segments: " + path);
            string full = NormalizeFullPath(path);
            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
                throw new ArgumentException("Cannot resolve a rootless path: " + path, nameof(path));

            var pending = SplitComponents(full.Substring(root.Length));
            string resolvedBase = root; // physical so far
            int hops = 0;
            const int MaxHops = 128;

            int i = 0;
            while (i < pending.Count)
            {
                string comp = pending[i];
                // The caller's path has already been normalized and was raw-checked for '..'.
                // These segments can therefore only come from a link target. Process them here,
                // against the PHYSICAL base reached so far, rather than collapsing them before
                // target links are resolved.
                if (comp == ".")
                {
                    i++;
                    continue;
                }
                if (comp == "..")
                {
                    resolvedBase = PhysicalParentOrRoot(resolvedBase);
                    i++;
                    continue;
                }

                string candidate = Path.Combine(resolvedBase, comp);
                FileAttributes attr;
                bool isMissing = false;
                try { attr = getAttributes(candidate); }
                catch (FileNotFoundException) { attr = default; isMissing = true; }
                catch (DirectoryNotFoundException) { attr = default; isMissing = true; }
                catch (Exception ex) when (IsAttributeInspectionException(ex))
                { throw new IOException("Cannot inspect path component: " + candidate + " (" + ex.Message + ")", ex); }

                if (isMissing)
                {
                    // From here down nothing exists, so append the remaining ordinary components
                    // lexically. A later '..' could escape back into an existing tree (where links
                    // may exist), but the missing component makes physical semantics uncertain;
                    // fail closed rather than claim a potentially wrong canonical path.
                    resolvedBase = candidate;
                    for (int j = i + 1; j < pending.Count; j++)
                    {
                        if (pending[j] == "..")
                            throw new IOException(
                                "Cannot physically resolve parent traversal after missing path component: " +
                                candidate);
                        if (pending[j] != ".")
                            resolvedBase = Path.Combine(resolvedBase, pending[j]);
                    }
                    return Path.TrimEndingDirectorySeparator(resolvedBase);
                }

                bool isDir = (attr & FileAttributes.Directory) != 0;

                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    if (++hops > MaxHops)
                        throw new IOException("Too many symbolic-link hops while resolving: " + path);

                    string target = ReadLinkTarget(candidate, isDir);
                    List<string> newPending;
                    string targetBase;
                    if (OperatingSystem.IsWindows())
                    {
                        if (Path.IsPathRooted(target) && !Path.IsPathFullyQualified(target))
                            throw new IOException(
                                "Partially rooted link targets are not supported: " + candidate);
                        string lexicalTarget = Path.IsPathFullyQualified(target)
                            ? NormalizeFullPath(target)
                            : NormalizeFullPath(Path.Combine(resolvedBase, target));
                        string targetRoot = Path.GetPathRoot(lexicalTarget);
                        if (string.IsNullOrEmpty(targetRoot))
                            throw new IOException("Resolved link target is not rooted: " + target);
                        targetBase = targetRoot;
                        newPending = SplitComponents(lexicalTarget.Substring(targetRoot.Length));
                    }
                    else if (Path.IsPathFullyQualified(target))
                    {
                        string targetRoot = Path.GetPathRoot(target);
                        if (string.IsNullOrEmpty(targetRoot))
                            throw new IOException("Resolved link target is not rooted: " + target);
                        // Normalize only the root. Normalizing the WHOLE target here would collapse
                        // pivot/../file before pivot can itself be physically resolved.
                        targetBase = NormalizeFullPath(targetRoot);
                        newPending = SplitComponents(target.Substring(targetRoot.Length));
                    }
                    else
                    {
                        if (Path.IsPathRooted(target))
                            throw new IOException(
                                "Partially rooted link targets are not supported: " + candidate);
                        // Relative targets are relative to the link's already-physical parent.
                        targetBase = resolvedBase;
                        newPending = SplitComponents(target);
                    }

                    // Re-seed from the target's physical base, then re-append the not-yet-processed
                    // tail so links and traversal inside the target are resolved in filesystem
                    // order.
                    for (int j = i + 1; j < pending.Count; j++) newPending.Add(pending[j]);
                    resolvedBase = targetBase;
                    pending = newPending;
                    i = 0;
                    continue;
                }

                resolvedBase = candidate;
                i++;
            }
            return Path.TrimEndingDirectorySeparator(resolvedBase);
        }

        /// <summary>
        /// True when <paramref name="a"/> and <paramref name="b"/> resolve to the SAME physical
        /// file/dir (realpath + OS-appropriate comparison). Catches ancestor- and final-link
        /// aliases of the same file. On Windows, existing files are also compared by stable
        /// filesystem identity, which catches hard links and drive/UNC aliases.
        /// </summary>
        public static bool SamePhysicalFile(string a, string b)
        {
            return SameResolvedPhysicalFile(
                ResolvePhysicalPath(a),
                ResolvePhysicalPath(b));
        }

        /// <summary>
        /// Compares two paths already returned by <see cref="ResolvePhysicalPath"/> without
        /// walking their components again.
        /// </summary>
        public static bool SameResolvedPhysicalFile(string resolvedA, string resolvedB)
        {
            resolvedA = NormalizeFullPath(resolvedA);
            resolvedB = NormalizeFullPath(resolvedB);
            if (string.Equals(resolvedA, resolvedB, PathComparison))
                return true;
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()
                || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid()
                || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            {
                return ProjectionFileSystemSafety.SameExistingFileSystemEntry(
                    resolvedA,
                    resolvedB);
            }
            return false;
        }

        internal static bool SameWindowsFileIdentity(
            string pathA,
            string pathB,
            bool try128BitIdentity = true)
        {
            using SafeFileHandle handleA = OpenWindowsIdentityHandle(pathA);
            using SafeFileHandle handleB = OpenWindowsIdentityHandle(pathB);
            return SameWindowsFileIdentity(
                handleA,
                handleB,
                pathA,
                pathB,
                try128BitIdentity);
        }

        internal static FileSystemEntryIdentity ReadWindowsFileSystemEntryIdentity(
            string path,
            bool try128BitIdentity = true)
        {
            using SafeFileHandle handle = OpenWindowsIdentityHandle(path);
            if (try128BitIdentity)
            {
                bool hasIdentity128 = TryReadWindowsFileIdentity128(
                    handle,
                    out WindowsFileIdentity128 identity128,
                    out int identity128Error);
                if (hasIdentity128)
                {
                    return new FileSystemEntryIdentity(
                        FileSystemEntryIdentityKind.Windows128,
                        identity128.VolumeSerialNumber,
                        identity128.FileIdLow,
                        identity128.FileIdHigh);
                }
                if (!IsWindowsFileIdInfoUnavailable(identity128Error))
                {
                    throw new IOException(
                        "Cannot inspect Windows FileIdInfo for " + path
                        + " (Win32 error " + identity128Error + ").");
                }
            }

            WindowsFileIdentity64 identity64 =
                ReadWindowsFileIdentity64(handle, path);
            ulong fileIndex = ((ulong)identity64.FileIndexHigh << 32)
                | identity64.FileIndexLow;
            return new FileSystemEntryIdentity(
                FileSystemEntryIdentityKind.Windows64,
                identity64.VolumeSerialNumber,
                fileIndex,
                0);
        }

        internal static bool SameWindowsFileIdentity(
            SafeFileHandle handleA,
            SafeFileHandle handleB,
            bool try128BitIdentity = true)
        {
            if (handleA == null) throw new ArgumentNullException(nameof(handleA));
            if (handleB == null) throw new ArgumentNullException(nameof(handleB));
            return SameWindowsFileIdentity(
                handleA,
                handleB,
                "first opened file",
                "second opened file",
                try128BitIdentity);
        }

        static bool SameWindowsFileIdentity(
            SafeFileHandle handleA,
            SafeFileHandle handleB,
            string labelA,
            string labelB,
            bool try128BitIdentity)
        {
            if (try128BitIdentity)
            {
                bool hasIdentity128A = TryReadWindowsFileIdentity128(
                    handleA,
                    out WindowsFileIdentity128 identity128A,
                    out int identity128ErrorA);
                bool hasIdentity128B = TryReadWindowsFileIdentity128(
                    handleB,
                    out WindowsFileIdentity128 identity128B,
                    out int identity128ErrorB);
                if (hasIdentity128A && hasIdentity128B)
                {
                    return identity128A.VolumeSerialNumber == identity128B.VolumeSerialNumber
                        && identity128A.FileIdLow == identity128B.FileIdLow
                        && identity128A.FileIdHigh == identity128B.FileIdHigh;
                }
                if (!hasIdentity128A
                    && !IsWindowsFileIdInfoUnavailable(identity128ErrorA))
                {
                    throw new IOException(
                        "Cannot inspect Windows FileIdInfo for " + labelA
                        + " (Win32 error " + identity128ErrorA + ").");
                }
                if (!hasIdentity128B
                    && !IsWindowsFileIdInfoUnavailable(identity128ErrorB))
                {
                    throw new IOException(
                        "Cannot inspect Windows FileIdInfo for " + labelB
                        + " (Win32 error " + identity128ErrorB + ").");
                }
                if (hasIdentity128A != hasIdentity128B)
                {
                    // FileIdInfo availability is filesystem-scoped. Handles on opposite sides
                    // of that capability boundary cannot identify the same file, and comparing
                    // the ReFS side through its legacy non-unique index would weaken the result.
                    return false;
                }
            }

            WindowsFileIdentity64 identity64A = ReadWindowsFileIdentity64(handleA, labelA);
            WindowsFileIdentity64 identity64B = ReadWindowsFileIdentity64(handleB, labelB);
            return identity64A.VolumeSerialNumber == identity64B.VolumeSerialNumber
                && identity64A.FileIndexLow == identity64B.FileIndexLow
                && identity64A.FileIndexHigh == identity64B.FileIndexHigh;
        }

        static SafeFileHandle OpenWindowsIdentityHandle(string path)
        {
            const uint FileFlagBackupSemantics = 0x02000000;
            SafeFileHandle handle = CreateFileForIdentity(
                ToWindowsExtendedPath(path),
                desiredAccess: 0,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new IOException(
                    "Cannot inspect Windows file identity: " + path + " (Win32 error " + error + ").");
            }
            return handle;
        }

        static bool TryReadWindowsFileIdentity128(
            SafeFileHandle handle,
            out WindowsFileIdentity128 identity,
            out int error)
        {
            if (GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileIdInfo,
                out identity,
                (uint)Marshal.SizeOf<WindowsFileIdentity128>()))
            {
                error = 0;
                return true;
            }

            error = Marshal.GetLastPInvokeError();
            return false;
        }

        static WindowsFileIdentity64 ReadWindowsFileIdentity64(
            SafeFileHandle handle,
            string label)
        {
            if (!GetFileInformationByHandle(handle, out WindowsFileIdentity64 identity))
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    "Cannot inspect Windows file identity for " + label
                    + " (Win32 error " + error + ").");
            }
            return identity;
        }

        static bool IsWindowsFileIdInfoUnavailable(int error)
        {
            const int ErrorInvalidFunction = 1;
            const int ErrorNotSupported = 50;
            const int ErrorInvalidParameter = 87;
            const int ErrorCallNotImplemented = 120;
            return error == ErrorInvalidFunction
                || error == ErrorNotSupported
                || error == ErrorInvalidParameter
                || error == ErrorCallNotImplemented;
        }

        enum FileInfoByHandleClass
        {
            FileIdInfo = 18,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowsFileIdentity128
        {
            public ulong VolumeSerialNumber;
            public ulong FileIdLow;
            public ulong FileIdHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowsFileIdentity64
        {
            public uint FileAttributes;
            public uint CreationTimeLow;
            public uint CreationTimeHigh;
            public uint LastAccessTimeLow;
            public uint LastAccessTimeHigh;
            public uint LastWriteTimeLow;
            public uint LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateFileW")]
        static extern SafeFileHandle CreateFileForIdentity(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            FileInfoByHandleClass fileInformationClass,
            out WindowsFileIdentity128 fileInformation,
            uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out WindowsFileIdentity64 fileInformation);

        static List<string> SplitComponents(string rest)
            => new List<string>(rest.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries));

        static string PhysicalParentOrRoot(string path)
        {
            string root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                throw new IOException("Cannot find physical path root: " + path);
            string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            return string.IsNullOrEmpty(parent) ? root : parent;
        }

        // Read a reparse point's raw target. The caller applies platform filesystem semantics:
        // Unix preserves component order through the physical walk; Windows uses Win32's lexical
        // target normalization. Explicit failure — never silently empty.
        static string ReadLinkTarget(string linkPath, bool isDir)
        {
            string raw;
            try
            {
                FileSystemInfo fsi = isDir ? new DirectoryInfo(linkPath) : new FileInfo(linkPath);
                raw = fsi.LinkTarget;
                if (string.IsNullOrEmpty(raw))
                    raw = fsi.ResolveLinkTarget(returnFinalTarget: false)?.FullName;
            }
            catch (Exception ex)
            { throw new IOException("Cannot read link target: " + linkPath + " (" + ex.Message + ")", ex); }

            if (string.IsNullOrEmpty(raw))
                throw new IOException("Unresolvable reparse point (no readable target): " + linkPath);

            return raw;
        }

        /// <summary>
        /// True when <paramref name="path"/> exists AND is a reparse point (symlink/junction).
        /// A permission fault while inspecting an existing path is surfaced as an explicit
        /// <see cref="IOException"/> rather than being treated as "not a reparse point".
        /// </summary>
        public static bool IsReparsePoint(string path)
            => IsReparsePoint(path, File.GetAttributes);

        internal static bool IsReparsePoint(
            string path,
            Func<string, FileAttributes> getAttributes)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (getAttributes == null) throw new ArgumentNullException(nameof(getAttributes));
            try
            {
                return (getAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (Exception ex) when (IsAttributeInspectionException(ex))
            {
                throw new IOException("Could not inspect path for reparse point: " + path + " (" + ex.Message + ")", ex);
            }
        }

        static bool IsAttributeInspectionException(Exception ex)
            => ex is IOException
            || ex is UnauthorizedAccessException
            || ex.GetType() == typeof(ArgumentException)
            || ex is NotSupportedException
            || ex is System.Security.SecurityException;
    }

    /// <summary>Inputs for a buildfile export.</summary>
    public sealed class BuildfileExportOptions
    {
        /// <summary>Absolute path of the destination project directory (must not exist).</summary>
        public string OutputDirectory { get; set; } = "";

        /// <summary>Patch-library base directory for this version (config/patch2/{version}); optional.</summary>
        public string PatchBaseDirectory { get; set; }

        /// <summary>Language used when enumerating patch metadata (default "en").</summary>
        public string Language { get; set; } = "en";

        /// <summary>Include the built-in advisory source projection.</summary>
        public bool IncludeSourceProjection { get; set; }

        /// <summary>Internal synchronous projection override for deterministic tests.</summary>
        internal BuildfileProjectionRunner ProjectionRunner { get; set; }

        /// <summary>Internal built-in producer override for deterministic validation tests.</summary>
        internal Action<ROM, ROM, uint, string> BuiltInProjectionProducerForTest { get; set; }

        /// <summary>Internal failure-injection seam used by staged-publication tests.</summary>
        internal Action<string> BeforePayloadWriteForTest { get; set; }

        /// <summary>Internal hook for creating a destination race immediately before publish.</summary>
        internal Action<string> BeforePublishForTest { get; set; }

        /// <summary>
        /// Internal advisory-classifier override for deterministic fault-injection tests
        /// (default null = the real <see cref="DecompDiffMigrationCore"/> classifier is used;
        /// production behavior is unchanged). Never affects which authoritative payload
        /// ranges are emitted — only their advisory category/confidence/suggestion.
        /// </summary>
        internal RangeClassifierOverride ClassifierOverrideForTest { get; set; }

        /// <summary>
        /// Internal patch-directory-listing override for deterministic enumeration-failure
        /// tests (default null = the real recursive <c>PATCH_*.txt</c> scan is used; production
        /// behavior is unchanged). Lets tests simulate an existing-but-inaccessible patch
        /// library directory without relying on flaky real permission changes.
        /// </summary>
        internal Func<string, string[]> PatchDirectoryListerForTest { get; set; }

        /// <summary>Internal deterministic name source for stage-collision tests.</summary>
        internal Func<Guid> GuidFactoryForTest { get; set; }

        /// <summary>Internal hook for simulating a fresh-source swap before its final validation.</summary>
        internal Action<string> AfterProjectionMoveForTest { get; set; }

        /// <summary>Internal hook for simulating a final-entry swap immediately before open.</summary>
        internal Action<string> BeforeProjectionFileOpenForTest { get; set; }

        /// <summary>Internal projection snapshot entry-limit override for bounded tests.</summary>
        internal int? ProjectionSnapshotMaxEntriesForTest { get; set; }

        /// <summary>Internal projection snapshot byte-limit override for bounded tests.</summary>
        internal long? ProjectionSnapshotMaxBytesForTest { get; set; }

        /// <summary>Internal projection text-file byte-limit override for bounded tests.</summary>
        internal long? ProjectionTextFileMaxBytesForTest { get; set; }

        /// <summary>Internal failure injection for unsafe materialized-source cleanup.</summary>
        internal Func<string, bool> UnsafeMovedProjectionCleanupForTest { get; set; }

        /// <summary>Maximum accepted target size (32 MiB).</summary>
        public const int MaxRomSize = 32 * 1024 * 1024;

        /// <summary>
        /// Maximum number of distinct payload ranges a single export will materialize
        /// (resource-safety bound; see <see cref="RomDiffCore.CompareWithFillBounded"/>). A
        /// pathological alternating-byte diff across a 32 MiB ROM could otherwise produce on
        /// the order of 16 million one-byte ranges/files.
        /// </summary>
        public const int MaxPayloadRanges = 16384;

        /// <summary>Maximum directories plus files accepted from an advisory projection.</summary>
        public const int MaxProjectionSnapshotEntries = 32768;

        /// <summary>Maximum total bytes captured from an advisory projection (256 MiB).</summary>
        public const long MaxProjectionSnapshotBytes = 256L * 1024 * 1024;

        /// <summary>Maximum bytes accepted from one projection text file (16 MiB).</summary>
        public const long MaxProjectionTextFileBytes = 16L * 1024 * 1024;
    }

    /// <summary>Result of an export attempt.</summary>
    public sealed class BuildfileExportResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public string PublishedPath { get; set; } = "";
        public BuildfileManifest Manifest { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public static BuildfileExportResult Fail(string error)
            => new BuildfileExportResult { Success = false, Error = error ?? "" };
    }

    // -------------------------------------------------------------- manifest POCOs

    /// <summary>Deterministic schema-v1 buildfile manifest (JSON authority).</summary>
    public sealed class BuildfileManifest
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("tool")] public string Tool { get; set; } = "FEBuilderGBA --export-buildfile";
        [JsonPropertyName("game")] public string Game { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("entryEvent")] public string EntryEvent { get; set; } = "main.event";
        [JsonPropertyName("dataDirectory")] public string DataDirectory { get; set; } = "data";

        [JsonPropertyName("clean")] public BuildfileRomIdentity Clean { get; set; } = new BuildfileRomIdentity();
        [JsonPropertyName("target")] public BuildfileRomIdentity Target { get; set; } = new BuildfileRomIdentity();

        /// <summary>Extension policy when the target is longer than clean; null when equal length.</summary>
        [JsonPropertyName("extension")] public BuildfileExtension Extension { get; set; }

        [JsonPropertyName("totalRanges")] public int TotalRanges { get; set; }
        [JsonPropertyName("totalChangedBytes")] public uint TotalChangedBytes { get; set; }

        [JsonPropertyName("ranges")] public List<BuildfileRange> Ranges { get; set; } = new List<BuildfileRange>();
        [JsonPropertyName("patches")] public BuildfilePatchInventory Patches { get; set; } = new BuildfilePatchInventory();
        [JsonPropertyName("projection")] public BuildfileProjectionInfo Projection { get; set; } = new BuildfileProjectionInfo();
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Size + hash identity of a ROM (never its filesystem path).</summary>
    public sealed class BuildfileRomIdentity
    {
        [JsonPropertyName("size")] public uint Size { get; set; }
        [JsonPropertyName("crc32")] public string Crc32 { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        /// <summary>Only meaningful for the clean ROM: true when it is the known canonical original.</summary>
        [JsonPropertyName("isCanonicalOriginal")] public bool IsCanonicalOriginal { get; set; }
    }

    /// <summary>Virtual extension-fill policy for a target longer than clean.</summary>
    public sealed class BuildfileExtension
    {
        [JsonPropertyName("start")] public uint Start { get; set; }
        [JsonPropertyName("length")] public uint Length { get; set; }
        [JsonPropertyName("fillByte")] public string FillByte { get; set; } = "";
    }

    /// <summary>One ordered, non-overlapping payload range.</summary>
    public sealed class BuildfileRange
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("offset")] public uint Offset { get; set; }
        [JsonPropertyName("gbaAddress")] public string GbaAddress { get; set; } = "";
        [JsonPropertyName("length")] public uint Length { get; set; }
        [JsonPropertyName("changedBytes")] public uint ChangedBytes { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("confidence")] public string Confidence { get; set; } = "";
        [JsonPropertyName("suggestion")] public string Suggestion { get; set; } = "";
        [JsonPropertyName("payload")] public string Payload { get; set; } = "";
        [JsonPropertyName("payloadSha256")] public string PayloadSha256 { get; set; } = "";
    }

    /// <summary>Advisory installed-patch inventory (never authoritative).</summary>
    public sealed class BuildfilePatchInventory
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "unavailable";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("baseRelative")] public string BaseRelative { get; set; } = "";
        [JsonPropertyName("installed")] public List<BuildfilePatchRecord> Installed { get; set; } = new List<BuildfilePatchRecord>();
    }

    /// <summary>One advisory patch record.</summary>
    public sealed class BuildfilePatchRecord
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("confidence")] public string Confidence { get; set; } = "";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("params")] public List<BuildfilePatchParam> Params { get; set; } = new List<BuildfilePatchParam>();
    }

    /// <summary>Raw patch parameter declaration.</summary>
    public sealed class BuildfilePatchParam
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
    }

    /// <summary>Status of the optional source projection recorded in the manifest.</summary>
    public sealed class BuildfileProjectionInfo
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "skipped";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("directory")] public string Directory { get; set; }
    }

    // -------------------------------------------------------------- planner result

    /// <summary>The pure planning output: the manifest plus the raw payload spans to write.</summary>
    public sealed class BuildfileExportPlan
    {
        public BuildfileManifest Manifest { get; set; } = new BuildfileManifest();
        /// <summary>Payloads to write: relative "data/..." path -> (offset,length) into the target.</summary>
        public List<BuildfilePayloadSpan> Payloads { get; } = new List<BuildfilePayloadSpan>();
        public byte FillByte { get; set; }
        public bool HasExtension { get; set; }
    }

    /// <summary>A payload span referencing target bytes by offset/length.</summary>
    public sealed class BuildfilePayloadSpan
    {
        public string RelativePath { get; set; } = "";
        public uint Offset { get; set; }
        public uint Length { get; set; }
    }

    /// <summary>
    /// Buildfile recipe exporter. The planner is pure/deterministic; publication is
    /// staged then atomically renamed. Neither ROM is mutated.
    /// </summary>
    public static class BuildfileExportCore
    {
        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // ---------------------------------------------------------------- planning

        /// <summary>
        /// Build a deterministic export plan for the given clean/target ROMs. Throws
        /// <see cref="ArgumentException"/> on any identity/size validation failure so the
        /// caller can refuse before touching the filesystem. Pure: reads only the two
        /// ROMs (+ optional patch metadata); writes nothing.
        /// </summary>
        public static BuildfileExportPlan Plan(ROM cleanRom, ROM targetRom, BuildfileExportOptions options)
        {
            if (cleanRom == null) throw new ArgumentNullException(nameof(cleanRom));
            if (targetRom == null) throw new ArgumentNullException(nameof(targetRom));
            if (options == null) throw new ArgumentNullException(nameof(options));

            byte[] clean = cleanRom.Data ?? throw new ArgumentException("Clean ROM has no data.", nameof(cleanRom));
            byte[] target = targetRom.Data ?? throw new ArgumentException("Target ROM has no data.", nameof(targetRom));

            string cleanVersion = cleanRom.RomInfo?.VersionToFilename;
            string targetVersion = targetRom.RomInfo?.VersionToFilename;
            if (string.IsNullOrEmpty(cleanVersion) || string.IsNullOrEmpty(targetVersion))
                throw new ArgumentException("Could not detect a supported Fire Emblem GBA version for one or both ROMs.");
            if (!string.Equals(cleanVersion, targetVersion, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Clean and modded ROMs are different versions (clean={cleanVersion}, modded={targetVersion}).");

            if (target.Length < clean.Length)
                throw new ArgumentException(
                    $"Modded ROM ({target.Length} bytes) is shorter than the clean ROM ({clean.Length} bytes); refusing.");
            if (target.Length > BuildfileExportOptions.MaxRomSize)
                throw new ArgumentException(
                    $"Modded ROM ({target.Length} bytes) exceeds the {BuildfileExportOptions.MaxRomSize}-byte (32 MiB) limit.");

            var plan = new BuildfileExportPlan();
            var m = plan.Manifest;
            m.SchemaVersion = 1;
            m.Game = "Fire Emblem GBA";
            m.Version = targetVersion;

            uint cleanCrc = new U.CRC32().Calc(clean);
            uint targetCrc = new U.CRC32().Calc(target);
            uint canonicalCrc = cleanRom.RomInfo != null ? cleanRom.RomInfo.orignal_crc32 : 0;
            bool cleanIsCanonical = canonicalCrc != 0 && cleanCrc == canonicalCrc;

            m.Clean = new BuildfileRomIdentity
            {
                Size = (uint)clean.Length,
                Crc32 = Hex32(cleanCrc),
                Sha256 = Sha256Hex(clean),
                IsCanonicalOriginal = cleanIsCanonical,
            };
            m.Target = new BuildfileRomIdentity
            {
                Size = (uint)target.Length,
                Crc32 = Hex32(targetCrc),
                Sha256 = Sha256Hex(target),
            };

            if (!cleanIsCanonical)
            {
                m.Warnings.Add(
                    "Clean ROM is not the known canonical original for " + targetVersion +
                    " (crc32=" + Hex32(cleanCrc) + "); reproducibility is bound to its sha256 " +
                    m.Clean.Sha256 + ".");
            }

            // Deterministic extension fill: most frequent byte in [clean.Length, target.Length),
            // lowest byte value on a frequency tie.
            byte fill = 0;
            bool hasExtension = target.Length > clean.Length;
            if (hasExtension)
            {
                fill = MostFrequentByte(target, clean.Length, target.Length);
                plan.HasExtension = true;
                plan.FillByte = fill;
                m.Extension = new BuildfileExtension
                {
                    Start = (uint)clean.Length,
                    Length = (uint)(target.Length - clean.Length),
                    FillByte = Hex8(fill),
                };
            }

            // AUTHORITATIVE diff: payload ranges come DIRECTLY from the fill-aware byte
            // comparator (maxGap 0 — every range byte differs from clean-or-fill and is
            // owned exactly once), using the BOUNDED overload so a pathological
            // alternating-byte diff is rejected immediately with an explicit error instead
            // of materializing an unbounded number of ranges/files before we ever touch the
            // filesystem (Copilot review finding: unbounded 16M-range worst case).
            //
            // Classification is looked up per-range AFTERWARDS via the never-throwing
            // ClassifyRangeSafe seam and is advisory-only: a classifier fault (or an injected
            // bad classifier under test, via options.ClassifierOverrideForTest) degrades that
            // ONE range to a stable unknown/low-confidence/manual-review record and can NEVER
            // omit, reorder, or resize the authoritative range itself (Copilot review finding:
            // AnalyzeWithFill's single try/catch around the whole loop could silently return a
            // PARTIAL report on a mid-loop classifier fault — going straight to the diff result
            // here removes that failure mode entirely).
            RomDiffCore.DiffResult diff = RomDiffCore.CompareWithFillBounded(
                clean, target, fill, BuildfileExportOptions.MaxPayloadRanges);

            uint totalChanged = 0;
            int index = 0;
            foreach (RomDiffCore.DiffRange dr in diff.Ranges)
            {
                MigrationRange mr = DecompDiffMigrationCore.ClassifyRangeSafe(
                    cleanRom, clean, dr.Offset, dr.Length, dr.Length,
                    map: null, resolver: null, options.ClassifierOverrideForTest);

                string rel = "data/" + PayloadName(index, dr.Offset, dr.Length);
                byte[] payload = Slice(target, dr.Offset, dr.Length);

                m.Ranges.Add(new BuildfileRange
                {
                    Index = index,
                    Offset = dr.Offset,
                    GbaAddress = Hex32(0x08000000u + dr.Offset),
                    Length = dr.Length,
                    ChangedBytes = dr.Length,
                    Category = CategoryWord(mr.Category),
                    Confidence = ConfidenceWord(mr.Confidence),
                    Suggestion = mr.Suggestion ?? "",
                    Payload = rel,
                    PayloadSha256 = Sha256Hex(payload),
                });
                plan.Payloads.Add(new BuildfilePayloadSpan { RelativePath = rel, Offset = dr.Offset, Length = dr.Length });

                totalChanged += dr.Length;
                index++;
            }
            m.TotalRanges = m.Ranges.Count;
            m.TotalChangedBytes = totalChanged;

            // Losslessness gate: reconstruct target from clean + fill + payloads and prove
            // byte-for-byte equality before we ever publish (defensive, never expected to fail).
            VerifyReconstruction(clean, target, fill, plan.Payloads);

            // Advisory patch inventory is internally guarded (TryEnumeratePatches distinguishes
            // empty from failure; per-record helpers surface degradation in the record reason).
            // Programmer defects deliberately propagate so real exporter bugs are not hidden.
            m.Patches = BuildPatchInventory(cleanRom, targetRom, options);

            return plan;
        }

        // ------------------------------------------------------------- publication

        const int TemporaryDirectoryReservationAttempts = 64;

        static string ReserveUniqueSiblingDirectory(
            string parent,
            string prefix,
            Func<Guid> guidFactory)
        {
            Func<Guid> nextGuid = guidFactory ?? Guid.NewGuid;
            for (int attempt = 0; attempt < TemporaryDirectoryReservationAttempts; attempt++)
            {
                string candidate = Path.Combine(parent, prefix + nextGuid().ToString("N"));
                if (TryCreateDirectoryExclusive(candidate))
                    return candidate;
            }

            throw new IOException(
                "Could not reserve a unique temporary directory after "
                + TemporaryDirectoryReservationAttempts + " name collisions.");
        }

        internal static string MakeTemporaryDirectoryPrefix(string outputName, string kind)
        {
            string outputHash = Sha256Hex(Encoding.UTF8.GetBytes(outputName ?? "")).Substring(0, 16);
            return ".febuild-" + outputHash + "." + kind + "-";
        }

        internal static bool TryCreateDirectoryExclusive(string path)
            => TryCreateDirectoryExclusive(path, OperatingSystem.IsBrowser());

        internal static bool TryCreateDirectoryExclusive(string path, bool isBrowser)
        {
            if (isBrowser)
            {
                throw new PlatformNotSupportedException(
                    "Buildfile export requires native atomic directory reservation; "
                    + "Browser storage is unsupported.");
            }

            if (OperatingSystem.IsWindows())
            {
                // Raw CreateDirectoryW does not receive .NET's automatic long-path handling.
                // Use an internal extended-length spelling so deep standard drive/UNC outputs
                // retain the same >MAX_PATH support as managed Directory.CreateDirectory.
                if (CreateDirectoryWindows(ToWindowsExtendedPath(path), IntPtr.Zero))
                    return true;

                int error = Marshal.GetLastWin32Error();
                if (error == 80 || error == 183) // ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS
                    return false;
                throw new IOException(
                    "Could not reserve temporary directory (Win32 error " + error + "): " + path);
            }

            if (CreateDirectoryUnix(path, 0x1C0) == 0) // 0700: exporter-private stage/scratch
                return true;

            int errno = Marshal.GetLastWin32Error();
            if (errno == 17) // EEXIST on Linux/macOS/Android/iOS
                return false;
            throw new IOException(
                "Could not reserve temporary directory (errno " + errno + "): " + path);
        }

        internal static string ToWindowsExtendedPath(string path)
            => BuildfilePathSafety.ToWindowsExtendedPath(path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateDirectoryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateDirectoryWindows(string path, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "MoveFileExW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool MoveFileNoReplaceWindows(
            string existingPath,
            string newPath,
            uint flags);

        [DllImport("libc", SetLastError = true, EntryPoint = "mkdir")]
        static extern int CreateDirectoryUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            uint mode);

        [DllImport("libc", SetLastError = true, EntryPoint = "renameat2")]
        static extern int MoveDirectoryNoReplaceLinux(
            int oldDirectoryFd,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
            int newDirectoryFd,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath,
            uint flags);

        [DllImport("libc", SetLastError = true, EntryPoint = "renamex_np")]
        static extern int MoveDirectoryNoReplaceDarwin(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath,
            uint flags);

        internal static void PublishDirectoryNoReplace(string source, string destination)
            => PublishDirectoryNoReplace(
                source,
                destination,
                OperatingSystem.IsBrowser());

        internal static void PublishDirectoryNoReplace(
            string source,
            string destination,
            bool isBrowser)
        {
            if (isBrowser)
            {
                throw new PlatformNotSupportedException(
                    "Buildfile export requires native atomic no-replace publication; "
                    + "Browser storage is unsupported.");
            }

            if (OperatingSystem.IsWindows())
            {
                if (MoveFileNoReplaceWindows(
                    ToWindowsExtendedPath(source),
                    ToWindowsExtendedPath(destination),
                    0))
                    return;
                ThrowPublishError(Marshal.GetLastWin32Error());
            }

            int result;
            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
                {
                    const int AtCurrentWorkingDirectory = -100;
                    const uint RenameNoReplace = 1;
                    result = MoveDirectoryNoReplaceLinux(
                        AtCurrentWorkingDirectory, source,
                        AtCurrentWorkingDirectory, destination,
                        RenameNoReplace);
                }
                else if (OperatingSystem.IsMacOS()
                    || OperatingSystem.IsIOS()
                    || OperatingSystem.IsMacCatalyst())
                {
                    const uint RenameExclusive = 0x00000004;
                    result = MoveDirectoryNoReplaceDarwin(source, destination, RenameExclusive);
                }
                else
                {
                    throw new PlatformNotSupportedException(
                        "Atomic no-replace directory publication is unavailable on this platform.");
                }
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new PlatformNotSupportedException(
                    "Atomic no-replace directory publication is unavailable on this platform.", ex);
            }

            if (result == 0)
                return;
            ThrowPublishError(Marshal.GetLastWin32Error());
        }

        static void ThrowPublishError(int error)
        {
            if (error == 17 || error == 80 || error == 183)
                throw new IOException("Destination already exists; refusing to replace.");
            throw new IOException("Atomic directory publication failed (native error " + error + ").");
        }

        /// <summary>
        /// Plan and publish a buildfile project. Stages the whole tree in a uniquely
        /// named sibling under the destination's exact parent, re-reads/hashes each
        /// payload, writes buildfile.json last, and publishes with one directory rename.
        /// On any failure the stage is removed and no destination is created.
        /// </summary>
        public static BuildfileExportResult Export(ROM cleanRom, ROM targetRom, BuildfileExportOptions options)
        {
            if (options == null) return BuildfileExportResult.Fail("Options are required.");
            if (string.IsNullOrEmpty(options.OutputDirectory))
                return BuildfileExportResult.Fail("Output directory is required.");

            string outDir;
            string parent;
            try
            {
                outDir = BuildfilePathSafety.NormalizeFullPath(options.OutputDirectory);
                parent = Path.GetDirectoryName(outDir);
            }
            catch (Exception ex)
            {
                return BuildfileExportResult.Fail("Invalid output directory: " + ex.Message);
            }

            if (string.IsNullOrEmpty(parent))
                return BuildfileExportResult.Fail("Output directory has no parent (cannot export to a filesystem root): " + outDir);
            if (Directory.Exists(outDir) || File.Exists(outDir))
                return BuildfileExportResult.Fail("Output path already exists: " + outDir);
            if (!Directory.Exists(parent))
                return BuildfileExportResult.Fail("Output parent directory does not exist: " + parent);
            try
            {
                if (BuildfilePathSafety.IsReparsePoint(parent))
                    return BuildfileExportResult.Fail(
                        "Output parent directory is a symlink/junction (reparse point); refusing to guarantee an atomic same-parent publish: " + parent);
            }
            catch (Exception ex)
            {
                return BuildfileExportResult.Fail(ex.Message);
            }

            BuildfileExportPlan plan;
            try
            {
                plan = Plan(cleanRom, targetRom, options);
            }
            catch (ArgumentException ex)
            {
                return BuildfileExportResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return BuildfileExportResult.Fail("Planning failed: " + ex.Message);
            }

            byte[] target = targetRom.Data;
            string name = Path.GetFileName(outDir);
            string stage = null;
            string projectionScratch = null;
            ProjectionTreeSnapshot projectionSnapshot = null;

            try
            {
                if (options.IncludeSourceProjection
                    || options.ProjectionRunner != null)
                {
                    // No publish stage exists while projection code runs. The synchronous
                    // built-in projector receives only its private scratch; after return, the
                    // tree is captured through held handles and deleted before a stage is born.
                    projectionScratch = ReserveUniqueSiblingDirectory(
                        parent,
                        MakeTemporaryDirectoryPrefix(name, "psrc"),
                        options.GuidFactoryForTest);
                    projectionSnapshot = RunProjection(
                        plan.Manifest,
                        cleanRom,
                        targetRom,
                        projectionScratch,
                        options);
                }

                // Reserve the publish stage only after projection has quiesced and its path has
                // been removed. A generated-name collision is never reused or deleted.
                stage = ReserveUniqueSiblingDirectory(
                    parent,
                    MakeTemporaryDirectoryPrefix(name, "stage"),
                    options.GuidFactoryForTest);
                Directory.CreateDirectory(Path.Combine(stage, "data"));

                // 1) Raw payloads.
                foreach (BuildfilePayloadSpan span in plan.Payloads)
                {
                    string full = Path.Combine(stage, RelToNative(span.RelativePath));
                    byte[] bytes = Slice(target, span.Offset, span.Length);
                    options.BeforePayloadWriteForTest?.Invoke(full);
                    File.WriteAllBytes(full, bytes);
                }

                // 2) Derived EA installer (does not depend on projection status).
                WriteTextLf(Path.Combine(stage, "main.event"), GenerateMainEvent(plan));

                // 3) README — written after projection so it reflects the final manifest
                // status/warnings (including a projection refusal/error warning, when present).
                WriteTextLf(Path.Combine(stage, "README.md"), GenerateReadme(plan.Manifest));

                // 4) Re-read + verify every payload hash before we publish.
                foreach (BuildfileRange r in plan.Manifest.Ranges)
                {
                    string full = Path.Combine(stage, RelToNative(r.Payload));
                    byte[] bytes = File.ReadAllBytes(full);
                    if ((uint)bytes.Length != r.Length || Sha256Hex(bytes) != r.PayloadSha256)
                        throw new IOException("Payload verification failed for " + r.Payload);
                }

                // 5) buildfile.json LAST (the authority file marks a complete project).
                WriteTextLf(Path.Combine(stage, "buildfile.json"), SerializeManifest(plan.Manifest));

                // A detached test runner must not be able to leave or recreate its private
                // scratch after capture. Production projection is synchronous and built in.
                if (!string.IsNullOrEmpty(projectionScratch)
                    && !DeleteAndVerifyGone(
                        projectionScratch,
                        out string finalScratchCleanupError))
                {
                    throw new IOException(
                        "Projection scratch was recreated or could not be removed: "
                        + finalScratchCleanupError);
                }

                // Preserve the destination-race seam before source materialization. No callback
                // or unrelated work runs after a successful fresh source is validated.
                options.BeforePublishForTest?.Invoke(outDir);

                // 6) Materialize any successful projection from the immutable snapshot into a
                // fresh exporter-owned source tree at the publication boundary.
                if (projectionSnapshot != null)
                {
                    if (!TryMaterializeProjectionSnapshot(
                        projectionSnapshot,
                        stage,
                        options,
                        out string materializeError))
                    {
                        RemoveUnsafeMaterializedProjection(stage, options);
                        MarkProjectionMaterializationError(
                            plan.Manifest,
                            materializeError);
                        RewriteProjectionMetadata(stage, plan.Manifest);
                    }
                }

                // 7) Publish with one atomic no-replace directory rename. The destination must
                // remain absent even if another process creates it after preflight.
                PublishDirectoryNoReplace(stage, outDir);
            }
            catch (Exception ex)
            {
                var cleanupErrors = new List<string>();
                if (!string.IsNullOrEmpty(projectionScratch)
                    && !DeleteAndVerifyGone(projectionScratch, out string projectionCleanupError))
                    cleanupErrors.Add("projection scratch '" + projectionScratch + "': " + projectionCleanupError);
                if (!string.IsNullOrEmpty(stage)
                    && !DeleteAndVerifyGone(stage, out string stageCleanupError))
                    cleanupErrors.Add("stage '" + stage + "': " + stageCleanupError);

                string error = "Export failed: " + ex.Message;
                if (cleanupErrors.Count > 0)
                    error += " Cleanup incomplete: " + string.Join("; ", cleanupErrors);
                return BuildfileExportResult.Fail(error);
            }

            var result = new BuildfileExportResult
            {
                Success = true,
                PublishedPath = outDir,
                Manifest = plan.Manifest,
            };
            result.Warnings.AddRange(plan.Manifest.Warnings);
            return result;
        }

        /// <summary>Serialize a manifest to deterministic LF-terminated JSON text.</summary>
        public static string SerializeManifest(BuildfileManifest manifest)
        {
            string json = JsonSerializer.Serialize(manifest, JsonOptions);
            return NormalizeLf(json) + "\n";
        }

        // --------------------------------------------------------------- main.event

        /// <summary>
        /// Generate the derived Event Assembler installer from the plan. Uses EA's
        /// documented <c>ORG Offset</c> and <c>FILL Amount Value</c> forms; payload
        /// writes follow the fill so sparse overrides win. The JSON remains the authority.
        /// </summary>
        public static string GenerateMainEvent(BuildfileExportPlan plan)
        {
            var sb = new StringBuilder();
            BuildfileManifest m = plan.Manifest;
            sb.Append("// Generated by FEBuilderGBA --export-buildfile (#1935). DO NOT EDIT BY HAND.\n");
            sb.Append("// Authoritative recipe: buildfile.json + data/. This file is a derived\n");
            sb.Append("// Event Assembler interoperability surface only.\n");
            sb.Append("PUSH\n");

            if (m.Extension != null)
            {
                sb.Append("ORG 0x" + m.Extension.Start.ToString("X") + "\n");
                sb.Append("FILL 0x" + m.Extension.Length.ToString("X")
                    + " " + m.Extension.FillByte + "\n");
            }

            foreach (BuildfileRange r in m.Ranges)
            {
                sb.Append("ORG 0x" + r.Offset.ToString("X") + "\n");
                sb.Append("#incbin \"" + r.Payload + "\" // HINT=BIN\n");
            }

            sb.Append("POP\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------- README

        static string GenerateReadme(BuildfileManifest m)
        {
            var sb = new StringBuilder();
            sb.Append("# FEBuilderGBA buildfile recipe\n\n");
            sb.Append("Deterministic source recipe for the binary delta from a clean ROM to a modded ROM.\n\n");
            sb.Append("- Game/version: " + m.Version + "\n");
            sb.Append("- Clean size: " + m.Clean.Size + " bytes (sha256 " + m.Clean.Sha256 + ")\n");
            sb.Append("- Target size: " + m.Target.Size + " bytes (sha256 " + m.Target.Sha256 + ")\n");
            sb.Append("- Ranges: " + m.TotalRanges + " (" + m.TotalChangedBytes + " changed bytes)\n");
            if (m.Extension != null)
                sb.Append("- Extension: " + m.Extension.Length + " bytes from 0x" +
                    m.Extension.Start.ToString("X") + " filled with " + m.Extension.FillByte + "\n");
            sb.Append("\n## Layout\n\n");
            sb.Append("- `buildfile.json` + `data/` are the ONLY build authority (consumed by #1936).\n");
            sb.Append("- `main.event` is a derived Event Assembler entry point.\n");
            sb.Append("- `source/` (when present) is a non-composable, best-effort source projection.\n");
            sb.Append("- The patch inventory in `buildfile.json` is advisory only.\n");
            sb.Append("- No source ROM path or full ROM is stored in this project.\n\n");
            sb.Append("## Authority model\n\n");
            sb.Append("Applying/verifying this recipe is issue #1936; emulator/playtest validation is #1932.\n");
            if (m.Warnings.Count > 0)
            {
                sb.Append("\n## Warnings\n\n");
                foreach (string w in m.Warnings)
                    sb.Append("- " + w + "\n");
            }
            return sb.ToString();
        }

        // -------------------------------------------------------------- projection

        static ProjectionTreeSnapshot RunProjection(
            BuildfileManifest m,
            ROM cleanRom,
            ROM targetRom,
            string scratch,
            BuildfileExportOptions options)
        {
            long maxBytes = options.ProjectionSnapshotMaxBytesForTest
                ?? BuildfileExportOptions.MaxProjectionSnapshotBytes;
            int maxEntries = options.ProjectionSnapshotMaxEntriesForTest
                ?? BuildfileExportOptions.MaxProjectionSnapshotEntries;
            long maxTextFileBytes = options.ProjectionTextFileMaxBytesForTest
                ?? BuildfileExportOptions.MaxProjectionTextFileBytes;
            BuildfileProjectionRunner runner = options.ProjectionRunner;
            if (runner == null && options.IncludeSourceProjection)
            {
                runner = path => RunBuiltInProjection(
                    cleanRom,
                    targetRom,
                    path,
                    maxBytes,
                    maxEntries,
                    maxTextFileBytes,
                    options.BuiltInProjectionProducerForTest);
            }
            if (runner == null)
            {
                m.Projection.Status = "skipped";
                m.Projection.Reason = "source projection not requested";
                return null;
            }

            BuildfileProjectionOutcome outcome;
            try
            {
                outcome = runner(scratch)
                    ?? BuildfileProjectionOutcome.Fail("projection returned no outcome");
            }
            catch (Exception ex)
            {
                // Projection is advisory. The built-in path reports its own outcome; this broad
                // boundary also keeps an internal fault-injection runner from corrupting the
                // authoritative export.
                outcome = BuildfileProjectionOutcome.Fail(ex.Message);
            }
            // Sanitize the exporter-owned scratch path out of the outcome's reason IMMEDIATELY —
            // before it is ever stored in the manifest or embedded in a thrown message. This
            // covers success/refused/error/exception outcomes uniformly, since a projector
            // outcome or exception message could otherwise echo the absolute scratch path back
            // (Copilot review finding: projection scratch reason paths).
            outcome.Reason = SanitizeScratchPath(outcome.Reason, scratch);

            ProjectionTreeSnapshot snapshot = null;
            if (outcome.Status == BuildfileProjectionStatus.Success)
            {
                try
                {
                    snapshot = ProjectionTreeSnapshotReader.Capture(
                        scratch,
                        maxEntries,
                        maxBytes,
                        maxTextFileBytes,
                        options.BeforeProjectionFileOpenForTest);
                    SanitizeProjectionSnapshot(
                        snapshot,
                        scratch,
                        maxBytes,
                        maxTextFileBytes);
                }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    snapshot = null;
                    outcome = BuildfileProjectionOutcome.Fail(
                        SanitizeScratchPath(
                            "capture failed: " + ex.Message,
                            scratch));
                }
            }

            // The runner-owned scratch is never mutated or published. Delete it for every outcome
            // and verify absence before any exporter-owned source tree can be materialized.
            string primaryReason = outcome.Reason ?? "";
            if (!DeleteAndVerifyGone(scratch, out string cleanupError))
            {
                throw new IOException(SanitizeScratchPath(
                    "Source projection " + StatusWord(outcome.Status) + " (" + primaryReason +
                    ") and its scratch could not be removed (" + scratch + "): " + cleanupError +
                    "; refusing to publish.", scratch));
            }

            if (outcome.Status == BuildfileProjectionStatus.Success
                && snapshot != null)
            {
                m.Projection.Status = "success";
                m.Projection.Reason = primaryReason;
                m.Projection.Directory = "source";
                return snapshot;
            }

            switch (outcome.Status)
            {
                case BuildfileProjectionStatus.Refused: m.Projection.Status = "refused"; break;
                case BuildfileProjectionStatus.Skipped: m.Projection.Status = "skipped"; break;
                default: m.Projection.Status = "error"; break;
            }
            m.Projection.Reason = primaryReason;
            if (m.Projection.Status != "skipped")
                m.Warnings.Add("Source projection " + m.Projection.Status + ": " + primaryReason);
            return null;
        }

        static BuildfileProjectionOutcome RunBuiltInProjection(
            ROM cleanRom,
            ROM targetRom,
            string scratch,
            long maxProjectionBytes,
            int maxProjectionEntries,
            long maxProjectionTextFileBytes,
            Action<ROM, ROM, uint, string> producerForTest)
        {
            try
            {
                uint rebuildAddress = U.toOffset(targetRom.RomInfo.extends_address);
                string manifestPath = Path.Combine(scratch, "rom.rebuild");
                if (producerForTest == null)
                {
                    RebuildProducerCore.MakeWithProducer(
                        targetRom,
                        cleanRom,
                        rebuildAddress,
                        manifestPath,
                        isUseOtherGraphics: true,
                        isUseOAMSP: false);
                }
                else
                {
                    producerForTest(targetRom, cleanRom, rebuildAddress, manifestPath);
                }
                RebuildMakeCore.ValidateProjectionOutput(
                    manifestPath,
                    File.GetAttributes,
                    Math.Min(maxProjectionBytes, maxProjectionTextFileBytes),
                    maxProjectionEntries);
                return BuildfileProjectionOutcome.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BuildfileProjectionOutcome.Refuse(ex.Message);
            }
            catch (Exception ex)
            {
                return BuildfileProjectionOutcome.Fail(ex.Message);
            }
        }

        static void SanitizeProjectionSnapshot(
            ProjectionTreeSnapshot snapshot,
            string scratch,
            long maxBytes,
            long maxTextFileBytes)
        {
            long totalBytes = 0;
            foreach (ProjectionTreeSnapshotFile file in snapshot.Files)
            {
                if (ProjectionTextEncoding.IsTextFile(file.RelativePath))
                {
                    if (file.Data.LongLength > maxTextFileBytes)
                    {
                        throw new IOException(
                            "Projection text file exceeds the "
                            + maxTextFileBytes + "-byte text-file limit.");
                    }
                    try
                    {
                        string text = ProjectionTextEncoding.DecodeStrictUtf8(file.Data);
                        string sanitized = NormalizeLf(SanitizeScratchPath(text, scratch));
                        file.Data = new UTF8Encoding(false).GetBytes(sanitized);
                    }
                    catch (DecoderFallbackException ex)
                    {
                        throw new IOException(
                            "Projection text is not valid UTF-8: "
                            + file.RelativePath,
                            ex);
                    }
                }

                if (file.Data.LongLength > maxBytes - totalBytes)
                {
                    throw new IOException(
                        "Sanitized projection snapshot exceeds the "
                        + maxBytes + "-byte limit.");
                }
                totalBytes += file.Data.LongLength;
            }
        }

        static bool TryMaterializeProjectionSnapshot(
            ProjectionTreeSnapshot snapshot,
            string stage,
            BuildfileExportOptions options,
            out string error)
        {
            string source = Path.Combine(stage, "source");
            error = "";
            try
            {
                if (!TryWriteProjectionSnapshot(snapshot, source, out error))
                    return false;

                // The historical hook name is retained for test compatibility; the source is no
                // longer moved from runner scratch and is entirely exporter-owned.
                options.AfterProjectionMoveForTest?.Invoke(source);
                if (!TryEnumeratePlainProjectionTree(
                    source,
                    stage,
                    out _,
                    out error))
                {
                    return false;
                }

                // The callback is an internal fault-injection seam. Discard every inode it could
                // have touched, then recreate the final source only from the immutable snapshot.
                // A regular-file replacement or hard link can therefore never survive into the
                // published tree even though it passes the plain-file type check above.
                if (options.AfterProjectionMoveForTest != null)
                {
                    if (!DeleteAndVerifyGone(source, out string discardError))
                    {
                        error = SanitizeScratchPath(
                            "Could not discard validated projection candidate: "
                            + discardError,
                            stage);
                        return false;
                    }

                    if (!TryWriteProjectionSnapshot(snapshot, source, out error))
                        return false;
                }
                return true;
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = SanitizeScratchPath(
                    "Could not materialize projection snapshot: " + ex.Message,
                    stage);
                return false;
            }
        }

        static bool TryWriteProjectionSnapshot(
            ProjectionTreeSnapshot snapshot,
            string source,
            out string error)
        {
            error = "";
            if (!TryCreateDirectoryExclusive(source))
            {
                error = "Exporter-owned source directory already exists.";
                return false;
            }

            foreach (string relativeDirectory in snapshot.Directories)
            {
                Directory.CreateDirectory(
                    ProjectionSnapshotPath(source, relativeDirectory));
            }
            foreach (ProjectionTreeSnapshotFile file in snapshot.Files)
            {
                string path = ProjectionSnapshotPath(source, file.RelativePath);
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                using var output = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                output.Write(file.Data, 0, file.Data.Length);
            }
            return true;
        }

        static string ProjectionSnapshotPath(string source, string relativePath)
        {
            string nativeRelative = relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(source, nativeRelative));
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))
                + Path.DirectorySeparatorChar;
            StringComparison comparison = (OperatingSystem.IsWindows()
                || OperatingSystem.IsMacOS())
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!full.StartsWith(root, comparison))
                throw new IOException("Projection snapshot path escapes source.");
            return full;
        }

        static void RemoveUnsafeMaterializedProjection(
            string stage,
            BuildfileExportOptions options)
        {
            string source = Path.Combine(stage, "source");
            bool removed;
            string cleanupError;
            if (options.UnsafeMovedProjectionCleanupForTest != null)
            {
                removed = options.UnsafeMovedProjectionCleanupForTest(source);
                cleanupError = removed ? "" : "injected cleanup failure";
            }
            else
            {
                removed = DeleteAndVerifyGone(source, out cleanupError);
            }
            if (!removed)
            {
                throw new InvalidOperationException(
                    "Unsafe materialized projection could not be removed: "
                    + cleanupError);
            }
        }

        static void MarkProjectionMaterializationError(
            BuildfileManifest manifest,
            string reason)
        {
            manifest.Projection.Status = "error";
            manifest.Projection.Reason = reason ?? "";
            manifest.Projection.Directory = "";
            manifest.Warnings.Add(
                "Source projection error: " + manifest.Projection.Reason);
        }

        static void RewriteProjectionMetadata(
            string stage,
            BuildfileManifest manifest)
        {
            WriteTextLf(
                Path.Combine(stage, "README.md"),
                GenerateReadme(manifest));
            WriteTextLf(
                Path.Combine(stage, "buildfile.json"),
                SerializeManifest(manifest));
        }

        static string StatusWord(BuildfileProjectionStatus s)
        {
            switch (s)
            {
                case BuildfileProjectionStatus.Refused: return "refused";
                case BuildfileProjectionStatus.Skipped: return "skipped";
                default: return "error";
            }
        }

        // Delete a directory tree and confirm it is gone. Returns false (with a reason) when the
        // directory still exists afterwards, so the caller can refuse to publish.
        static bool DeleteAndVerifyGone(string dir, out string error)
            => DeleteAndVerifyGone(dir, Directory.Delete, File.GetAttributes, out error);

        internal static bool DeleteAndVerifyGone(
            string dir,
            Action<string, bool> deleteDirectory,
            Func<string, FileAttributes> getAttributes,
            out string error)
        {
            if (deleteDirectory == null) throw new ArgumentNullException(nameof(deleteDirectory));
            if (getAttributes == null) throw new ArgumentNullException(nameof(getAttributes));
            error = "";
            bool recursive;
            try
            {
                // If an external projection runner replaced the reserved root with a symlink or
                // junction, delete only that link. Never recursively traverse an external target.
                FileAttributes attributes = getAttributes(dir);
                recursive = (attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (FileNotFoundException)
            {
                return VerifyPathAbsent(dir, getAttributes, out error);
            }
            catch (DirectoryNotFoundException)
            {
                return VerifyPathAbsent(dir, getAttributes, out error);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = "could not inspect path before delete: " + ex.Message;
                return false;
            }

            try
            {
                deleteDirectory(dir, recursive);
            }
            catch (FileNotFoundException)
            {
                return VerifyPathAbsent(dir, getAttributes, out error);
            }
            catch (DirectoryNotFoundException)
            {
                return VerifyPathAbsent(dir, getAttributes, out error);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                // Only expected filesystem/access faults are cleanup detail; a programmer defect
                // during cleanup propagates rather than being recorded as pass/fail data.
                error = ex.Message;
                return false;
            }
            return VerifyPathAbsent(dir, getAttributes, out error);
        }

        static bool VerifyPathAbsent(
            string path,
            Func<string, FileAttributes> getAttributes,
            out string error)
        {
            error = "";
            try
            {
                getAttributes(path);
                error = "path still present after delete";
                return false;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = "could not verify path absence: " + ex.Message;
                return false;
            }
        }

        internal static bool TryEnumeratePlainProjectionTree(
            string rootDirectory,
            string sanitizeRoot,
            out List<string> files,
            out string error)
        {
            files = new List<string>();
            error = "";
            try
            {
                // Walk explicitly so every entry is inspected before descent. A linked file or
                // directory would make the published source tree depend on an external target.
                var pending = new Stack<string>();
                pending.Push(rootDirectory);
                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    if (!ValidatePlainProjectionDirectory(current, sanitizeRoot, out error))
                        return false;
                    foreach (string entry in Directory.GetFileSystemEntries(current))
                    {
                        FileAttributes attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            error = SanitizeScratchPath(
                                "projection contains a symlink/junction: " + entry,
                                sanitizeRoot);
                            return false;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (!ProjectionFileSystemSafety.TryValidateDirectory(
                                entry, out string directoryTypeError))
                            {
                                error = SanitizeScratchPath(
                                    "projection contains a non-directory entry: " + entry
                                    + " (" + directoryTypeError + ")",
                                    sanitizeRoot);
                                return false;
                            }
                            pending.Push(entry);
                        }
                        else
                        {
                            if (!ValidatePlainProjectionFile(entry, sanitizeRoot, out error))
                                return false;
                            files.Add(entry);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = SanitizeScratchPath("enumerate failed: " + ex.Message, sanitizeRoot);
                return false;
            }
        }

        static bool ValidatePlainProjectionDirectory(
            string directory,
            string scratchDir,
            out string error)
        {
            error = "";
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(directory);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = SanitizeScratchPath(
                    "cannot inspect projection directory: " + ex.Message,
                    scratchDir);
                return false;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                error = SanitizeScratchPath(
                    "projection path is not a directory: " + directory,
                    scratchDir);
                return false;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                error = SanitizeScratchPath(
                    "projection contains a symlink/junction directory: " + directory,
                    scratchDir);
                return false;
            }
            if (!ProjectionFileSystemSafety.TryValidateDirectory(
                directory, out string directoryTypeError))
            {
                error = SanitizeScratchPath(
                    "projection path is not a plain directory: " + directory
                    + " (" + directoryTypeError + ")",
                    scratchDir);
                return false;
            }
            return true;
        }

        static bool ValidatePlainProjectionFile(
            string file,
            string scratchDir,
            out string error)
        {
            error = "";
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(file);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = SanitizeScratchPath(
                    "cannot inspect projection file: " + ex.Message,
                    scratchDir);
                return false;
            }

            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                error = SanitizeScratchPath(
                    "projection contains a non-regular file: " + file,
                    scratchDir);
                return false;
            }
            if (!ProjectionFileSystemSafety.TryValidateRegularFile(
                file, out string fileTypeError))
            {
                error = SanitizeScratchPath(
                    "projection contains a non-regular file: " + file
                    + " (" + fileTypeError + ")",
                    scratchDir);
                return false;
            }
            return true;
        }

        // Strip the exporter-owned scratch absolute path (native, forward-slash, and
        // JSON/C-escaped spellings) from arbitrary text, replacing it with "source". Shared by
        // BOTH projected file content (SanitizeAndNormalizeTree) and every projection outcome
        // reason / thrown message (RunProjection) so no scratch/environment location can leak
        // into the manifest, warnings, or README through ANY path (success reason, refusal
        // reason, runner-exception message, or publish-sanitize error). We only strip the
        // scratch path we control — never claims to strip arbitrary absolute paths a projector
        // might otherwise emit. Never throws: an unresolvable scratchDir returns text unchanged.
        static string SanitizeScratchPath(string text, string scratchDir)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(scratchDir)) return text ?? "";
            string scratchAbs;
            try { scratchAbs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchDir)); }
            catch (Exception ex) when (IsExpectedFileSystemException(ex)) { return text; }
            string scratchFwd = scratchAbs.Replace('\\', '/');
            // JSON/C-style escaped Windows spelling: backslashes are doubled (C:\\temp\\...).
            string scratchEsc = scratchAbs.Replace("\\", "\\\\");
            StringComparison comparison = (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return text.Replace(scratchEsc, "source", comparison)
                .Replace(scratchAbs, "source", comparison)
                .Replace(scratchFwd, "source", comparison);
        }

        // -------------------------------------------------------- patch inventory

        static BuildfilePatchInventory BuildPatchInventory(ROM cleanRom, ROM targetRom, BuildfileExportOptions options)
        {
            var inv = new BuildfilePatchInventory();
            string baseDir = options.PatchBaseDirectory;
            string version = targetRom.RomInfo?.VersionToFilename ?? "";
            inv.BaseRelative = string.IsNullOrEmpty(version) ? "config/patch2" : "config/patch2/" + version;

            if (string.IsNullOrEmpty(baseDir))
            {
                inv.Status = "unavailable";
                inv.Reason = "patch library not found; raw recipe is complete without it";
                return inv;
            }

            List<PatchMetadataCore.PatchInfo> patches;
            if (!PatchMetadataCore.TryEnumeratePatches(baseDir, targetRom, options.Language ?? "en",
                    File.ReadAllLines, options.PatchDirectoryListerForTest, out patches, out _))
            {
                // Enumeration FAILED (distinct from an empty directory) → unavailable, not
                // "available with zero entries". The manifest reason is a STABLE, path-free
                // string: the underlying error may contain the absolute patch base directory
                // (from the underlying OS exception message) and must never be serialized into
                // buildfile.json (Copilot review finding: enumError absolute path).
                inv.Status = "unavailable";
                inv.Reason = "patch enumeration failed; check patch library directory permissions";
                return inv;
            }
            if (patches.Count == 0)
            {
                inv.Status = "unavailable";
                inv.Reason = "patch library is empty or not initialized; raw recipe is complete without it";
                return inv;
            }

            inv.Status = "available";
            foreach (PatchMetadataCore.PatchInfo p in patches)
            {
                if (p == null) continue;
                // Only installed / unknown patches are advisory-relevant; unknown is never
                // promoted to installed.
                string status;
                string confidence;
                string reason;
                switch (p.Status)
                {
                    case PatchMetadataCore.PatchStatus.Installed:
                        status = "installed"; confidence = "high"; reason = "install markers matched";
                        break;
                    case PatchMetadataCore.PatchStatus.Unknown:
                        status = "unknown"; confidence = "low"; reason = "install markers could not be resolved";
                        break;
                    default:
                        continue; // not installed → not part of the ROM
                }

                var rec = new BuildfilePatchRecord
                {
                    Name = !string.IsNullOrEmpty(p.Name) ? p.Name : (p.DirectoryName ?? ""),
                    Status = status,
                    Confidence = confidence,
                    Reason = reason,
                };
                rec.Path = RelativePatchPath(baseDir, p.PatchFilePath, rec);
                AppendRawParams(rec, p.PatchFilePath);
                inv.Installed.Add(rec);
            }

            // Deterministic ordering: sort by definition-relative path (ordinal).
            inv.Installed = inv.Installed
                .OrderBy(r => r.Path, StringComparer.Ordinal)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ToList();
            return inv;
        }

        // Append the raw parameter declarations to a record. Only documented filesystem/access
        // exceptions are caught; a failure is surfaced in the record reason (never a silent empty
        // list). Programmer defects propagate.
        static void AppendRawParams(BuildfilePatchRecord rec, string patchFilePath)
        {
            if (string.IsNullOrEmpty(patchFilePath)) return;
            List<PatchMetadataCore.PatchParam> parsed;
            try
            {
                parsed = PatchMetadataCore.ParsePatchParams(patchFilePath);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                // Stable, path-free reason: `ex.Message` may embed the absolute patch file path
                // and must never be serialized into buildfile.json (Copilot review finding:
                // per-record raw exception path).
                rec.Reason += "; raw parameters unavailable";
                return;
            }
            if (parsed == null) return;
            foreach (PatchMetadataCore.PatchParam pp in parsed)
            {
                if (pp == null) continue;
                rec.Params.Add(new BuildfilePatchParam { Key = pp.RawKey ?? "", Value = pp.Value ?? "" });
            }
        }

        // Compute the patch's definition-relative forward-slash path. A path-format fault is an
        // expected degradation surfaced in the record reason (falls back to the file name).
        static string RelativePatchPath(string baseDir, string patchFilePath, BuildfilePatchRecord rec)
        {
            if (string.IsNullOrEmpty(patchFilePath)) return "";
            try
            {
                return Path.GetRelativePath(baseDir, patchFilePath).Replace('\\', '/');
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                // Stable, path-free reason: `ex.Message` (and `patchFilePath`) may embed an
                // absolute path and must never be serialized into buildfile.json (Copilot review
                // finding: per-record raw exception path).
                rec.Reason += "; relative path unavailable";
                return Path.GetFileName(patchFilePath);
            }
        }

        /// <summary>
        /// True for documented filesystem/access/path/format exceptions the exporter's advisory
        /// paths may legitimately encounter. Excludes programmer defects (argument-null,
        /// null-reference, index-out-of-range, invalid-operation) so they are never swallowed.
        /// </summary>
        static bool IsExpectedFileSystemException(Exception ex)
            => ex is IOException
            || ex is UnauthorizedAccessException
            || ex is System.Security.SecurityException
            || ex is NotSupportedException
            || ex.GetType() == typeof(ArgumentException);

        // --------------------------------------------------------------- utilities

        static void VerifyReconstruction(byte[] clean, byte[] target, byte fill, List<BuildfilePayloadSpan> payloads)
        {
            var recon = new byte[target.Length];
            Array.Copy(clean, 0, recon, 0, Math.Min(clean.Length, target.Length));
            for (int i = clean.Length; i < target.Length; i++)
                recon[i] = fill;
            foreach (BuildfilePayloadSpan span in payloads)
            {
                for (uint i = 0; i < span.Length; i++)
                    recon[span.Offset + i] = target[span.Offset + i];
            }
            for (int i = 0; i < target.Length; i++)
            {
                if (recon[i] != target[i])
                    throw new InvalidOperationException(
                        "Internal losslessness check failed at offset 0x" + i.ToString("X") + ".");
            }
        }

        static byte MostFrequentByte(byte[] data, int start, int end)
        {
            var counts = new long[256];
            for (int i = start; i < end; i++)
                counts[data[i]]++;
            int best = 0;
            long bestCount = -1;
            for (int b = 0; b < 256; b++)
            {
                if (counts[b] > bestCount)
                {
                    bestCount = counts[b];
                    best = b; // lowest byte wins ties because we scan ascending with strict >
                }
            }
            return (byte)best;
        }

        static byte[] Slice(byte[] data, uint offset, uint length)
        {
            var buf = new byte[length];
            Array.Copy(data, offset, buf, 0, length);
            return buf;
        }

        // Canonical schema-v1 spellings are shared with the #1936 consumer via
        // BuildfileFormat so the emit side and the validate side can never diverge.
        static string PayloadName(int index, uint offset, uint length)
            => BuildfileFormat.PayloadName(index, offset, length);

        static string RelToNative(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

        static string Hex32(uint v) => BuildfileFormat.Hex32(v);
        static string Hex8(byte v) => BuildfileFormat.Hex8(v);

        static string Sha256Hex(byte[] data) => BuildfileFormat.Sha256Hex(data);

        static string NormalizeLf(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

        static void WriteTextLf(string path, string text) => File.WriteAllText(path, NormalizeLf(text));

        static string CategoryWord(MigrationCategory c)
        {
            switch (c)
            {
                case MigrationCategory.StructTable: return "struct-table";
                case MigrationCategory.GraphicsPalette: return "graphics-palette";
                case MigrationCategory.Compressed: return "compressed";
                case MigrationCategory.Map: return "map";
                case MigrationCategory.Text: return "text";
                case MigrationCategory.Music: return "music";
                default: return "unknown";
            }
        }

        static string ConfidenceWord(MigrationConfidence c)
        {
            switch (c)
            {
                case MigrationConfidence.High: return "high";
                case MigrationConfidence.Medium: return "medium";
                default: return "low";
            }
        }
    }
}
