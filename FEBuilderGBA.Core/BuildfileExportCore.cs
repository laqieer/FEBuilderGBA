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
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace FEBuilderGBA
{
    /// <summary>Status of the optional source-aware rebuild projection.</summary>
    public enum BuildfileProjectionStatus
    {
        Skipped = 0,
        Success = 1,
        Refused = 2,
        Error = 3,
    }

    /// <summary>Outcome returned by a caller-supplied projection runner.</summary>
    public sealed class BuildfileProjectionOutcome
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
    /// Optional source projection runner. It must produce its artifacts INTO
    /// <paramref name="scratchDir"/> (an empty, private directory the exporter owns
    /// and will move to <c>source/</c> only on <see cref="BuildfileProjectionStatus.Success"/>).
    /// It must never throw; any fault should be reported as a Refused/Error outcome.
    /// </summary>
    public delegate BuildfileProjectionOutcome BuildfileProjectionRunner(string scratchDir);

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
    /// drive aliases that cannot be collapsed lexically. Hard-link identity remains out of scope
    /// on other platforms.
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

        static StringComparison PathComparison =>
            (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>OS-appropriate equality of two normalized full paths.</summary>
        public static bool PathsEqual(string a, string b)
            => string.Equals(NormalizeFullPath(a), NormalizeFullPath(b), PathComparison);

        /// <summary>
        /// True when the RAW path value contains a path segment that is exactly <c>..</c>
        /// (parent-directory traversal). Splits on the platform's directory separators
        /// (<c>/</c> and <c>\</c> on Windows; only <c>/</c> on Unix, where <c>\</c> is a legal
        /// filename character), so a filename that merely CONTAINS dots (e.g. <c>my..rom.gba</c>,
        /// <c>..config</c>) is NOT matched. Used to fail-closed on ROM inputs BEFORE any
        /// normalization/existence/load, because <see cref="Path.GetFullPath(string)"/> collapses
        /// <c>..</c> LEXICALLY before symlinks are resolved, which can diverge from the physical
        /// filesystem when a <c>..</c> segment follows a symlinked component.
        /// </summary>
        public static bool ContainsParentTraversal(string rawPath)
        {
            if (string.IsNullOrEmpty(rawPath)) return false;
            foreach (string seg in rawPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }))
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
        /// fail-closed <see cref="IOException"/>, never silently mis-resolved.
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
                // GetFullPath already resolved '.'/'..' lexically; a residual traversal segment
                // would mean an unexpected parse — fail closed rather than mis-resolve.
                if (comp == "." || comp == "..")
                    throw new IOException("Unexpected traversal segment '" + comp + "' after normalization of: " + full);

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
                    // From here down nothing exists → append the rest lexically (no links possible).
                    resolvedBase = candidate;
                    for (int j = i + 1; j < pending.Count; j++)
                        resolvedBase = Path.Combine(resolvedBase, pending[j]);
                    return Path.TrimEndingDirectorySeparator(resolvedBase);
                }

                bool isDir = (attr & FileAttributes.Directory) != 0;

                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    if (++hops > MaxHops)
                        throw new IOException("Too many symbolic-link hops while resolving: " + path);

                    string target = ReadLinkTarget(candidate, isDir, resolvedBase);
                    string targetRoot = Path.GetPathRoot(target);
                    if (string.IsNullOrEmpty(targetRoot))
                        throw new IOException("Resolved link target is not rooted: " + target);

                    // Re-seed from the target's root, then re-append the not-yet-processed tail,
                    // so ancestor links inside the target are themselves resolved.
                    var newPending = SplitComponents(target.Substring(targetRoot.Length));
                    for (int j = i + 1; j < pending.Count; j++) newPending.Add(pending[j]);
                    resolvedBase = targetRoot;
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
            if (!OperatingSystem.IsWindows())
                return false;

            return SameWindowsFileIdentity(resolvedA, resolvedB);
        }

        internal static bool SameWindowsFileIdentity(
            string pathA,
            string pathB,
            bool try128BitIdentity = true)
        {
            using SafeFileHandle handleA = OpenWindowsIdentityHandle(pathA);
            using SafeFileHandle handleB = OpenWindowsIdentityHandle(pathB);

            if (try128BitIdentity
                && TryReadWindowsFileIdentity128(handleA, out WindowsFileIdentity128 identity128A)
                && TryReadWindowsFileIdentity128(handleB, out WindowsFileIdentity128 identity128B))
            {
                return identity128A.VolumeSerialNumber == identity128B.VolumeSerialNumber
                    && identity128A.FileIdLow == identity128B.FileIdLow
                    && identity128A.FileIdHigh == identity128B.FileIdHigh;
            }

            WindowsFileIdentity64 identity64A = ReadWindowsFileIdentity64(handleA, pathA);
            WindowsFileIdentity64 identity64B = ReadWindowsFileIdentity64(handleB, pathB);
            return identity64A.VolumeSerialNumber == identity64B.VolumeSerialNumber
                && identity64A.FileIndexLow == identity64B.FileIndexLow
                && identity64A.FileIndexHigh == identity64B.FileIndexHigh;
        }

        static SafeFileHandle OpenWindowsIdentityHandle(string path)
        {
            const uint FileFlagBackupSemantics = 0x02000000;
            SafeFileHandle handle = CreateFileForIdentity(
                path,
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
            out WindowsFileIdentity128 identity)
        {
            return GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileIdInfo,
                out identity,
                (uint)Marshal.SizeOf<WindowsFileIdentity128>());
        }

        static WindowsFileIdentity64 ReadWindowsFileIdentity64(
            SafeFileHandle handle,
            string path)
        {
            if (!GetFileInformationByHandle(handle, out WindowsFileIdentity64 identity))
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    "Cannot inspect Windows file identity: " + path + " (Win32 error " + error + ").");
            }
            return identity;
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

        // Read a reparse point's target and resolve it to an absolute path (relative targets are
        // resolved against the link's own directory). Explicit failure — never silently empty.
        static string ReadLinkTarget(string linkPath, bool isDir, string resolvedBase)
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

            string linkDir = Path.GetDirectoryName(linkPath) ?? resolvedBase;
            return Path.IsPathRooted(raw)
                ? NormalizeFullPath(raw)
                : NormalizeFullPath(Path.Combine(linkDir, raw));
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
            || ex is ArgumentException
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

        /// <summary>Optional source-aware projection runner; null = projection skipped.</summary>
        public BuildfileProjectionRunner ProjectionRunner { get; set; }

        /// <summary>Internal failure-injection seam used by staged-publication tests.</summary>
        internal Action<string> BeforePayloadWriteForTest { get; set; }

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

        /// <summary>Maximum accepted target size (32 MiB).</summary>
        public const int MaxRomSize = 32 * 1024 * 1024;

        /// <summary>
        /// Maximum number of distinct payload ranges a single export will materialize
        /// (resource-safety bound; see <see cref="RomDiffCore.CompareWithFillBounded"/>). A
        /// pathological alternating-byte diff across a 32 MiB ROM could otherwise produce on
        /// the order of 16 million one-byte ranges/files.
        /// </summary>
        public const int MaxPayloadRanges = 16384;
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
                    GbaAddress = Hex32(U.toPointer(dr.Offset)),
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

        internal static bool TryCreateDirectoryExclusive(string path)
        {
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

            // Browser storage is single-process; native libc is unavailable there.
            if (OperatingSystem.IsBrowser())
            {
                if (Directory.Exists(path) || File.Exists(path))
                    return false;
                Directory.CreateDirectory(path);
                return true;
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
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return fullPath;
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + fullPath.Substring(2);
            return @"\\?\" + fullPath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateDirectoryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateDirectoryWindows(string path, IntPtr securityAttributes);

        [DllImport("libc", SetLastError = true, EntryPoint = "mkdir")]
        static extern int CreateDirectoryUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            uint mode);

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

            try
            {
                // Reserve each private tree with an atomic create-new operation. A generated-name
                // collision is never reused, overwritten, or deleted; retry with a fresh name.
                stage = ReserveUniqueSiblingDirectory(
                    parent, "." + name + ".stage-", options.GuidFactoryForTest);
                if (options.ProjectionRunner != null)
                {
                    // The optional projection scratch is a UNIQUE SIBLING OUTSIDE the publish
                    // stage (same parent -> same volume). It moves into stage/source only after
                    // complete projection success.
                    projectionScratch = ReserveUniqueSiblingDirectory(
                        parent, "." + name + ".psrc-", options.GuidFactoryForTest);
                }
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

                // 3) Optional advisory source projection (external scratch → stage/source on
                // success). MUST run BEFORE the README is generated: the README surfaces the
                // projection status/warning, and generating it first would freeze a stale
                // "skipped" status even when the projection later succeeds/fails/refuses
                // (Copilot review finding: README-before-projection-warning).
                RunProjection(plan.Manifest, stage, projectionScratch, options);

                // 3.5) README — written AFTER projection so it reflects the FINAL manifest
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

                // 6) Publish with a single directory rename.
                Directory.Move(stage, outDir);
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
                sb.Append("FILL 0x" + m.Extension.Length.ToString("X") + " " + m.Extension.FillByte + "\n");
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

        static void RunProjection(BuildfileManifest m, string stage, string scratch, BuildfileExportOptions options)
        {
            if (options.ProjectionRunner == null)
            {
                m.Projection.Status = "skipped";
                m.Projection.Reason = "source projection not requested";
                return;
            }

            BuildfileProjectionOutcome outcome;
            try
            {
                outcome = options.ProjectionRunner(scratch) ?? BuildfileProjectionOutcome.Fail("projection returned no outcome");
            }
            catch (Exception ex)
            {
                // Plugin boundary: the projection runner is arbitrary caller-supplied code, so ANY
                // fault it raises is treated as an advisory projection failure (never corrupts the
                // authoritative export). This is deliberately broad and scoped to the delegate call.
                outcome = BuildfileProjectionOutcome.Fail(ex.Message);
            }
            // Sanitize the exporter-owned scratch path out of the outcome's reason IMMEDIATELY —
            // before it is ever stored in the manifest or embedded in a thrown message. This
            // covers success/refused/error/exception outcomes uniformly, since a caller-supplied
            // runner or its exception message could otherwise echo the absolute scratch path back
            // (Copilot review finding: projection scratch reason paths).
            outcome.Reason = SanitizeScratchPath(outcome.Reason, scratch);

            if (outcome.Status == BuildfileProjectionStatus.Success)
            {
                try
                {
                    if (!SanitizeAndNormalizeTree(scratch, out string sanitizeError))
                        throw new IOException(sanitizeError);
                    // Move the external scratch INTO the stage as source/ only now that it is
                    // complete and sanitized. This is the sole way source/ ever gets published.
                    Directory.Move(scratch, Path.Combine(stage, "source"));
                    m.Projection.Status = "success";
                    m.Projection.Reason = outcome.Reason ?? "";
                    m.Projection.Directory = "source";
                    return;
                }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    outcome = BuildfileProjectionOutcome.Fail(SanitizeScratchPath("publish failed: " + ex.Message, scratch));
                }
            }

            // Refusal / error (or a non-null runner reporting skipped): delete the EXTERNAL
            // scratch and VERIFY it is gone. A cleanup failure must not let the export publish a
            // partial scratch — surface it and abort by throwing, preserving the original
            // projection reason as primary context.
            string primaryReason = outcome.Reason ?? "";
            if (!DeleteAndVerifyGone(scratch, out string cleanupError))
            {
                throw new IOException(SanitizeScratchPath(
                    "Source projection " + StatusWord(outcome.Status) + " (" + primaryReason +
                    ") and its scratch could not be removed (" + scratch + "): " + cleanupError +
                    "; refusing to publish.", scratch));
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
            try
            {
                deleteDirectory(dir, true);
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

        // Normalize an advisory projection tree before publication: LF line endings and removal
        // of the (exporter-owned) scratch absolute path so no environment/scratch location leaks
        // into source/. We only sanitize the scratch path we control — we do NOT claim to strip
        // arbitrary absolute paths a projector might emit. Fail-closed on any read/write fault so
        // a partial/leaky source/ is never published.
        static bool SanitizeAndNormalizeTree(string scratchDir, out string error)
        {
            error = "";
            string[] textExt = { ".rebuild", ".event", ".txt", ".s", ".asm", ".json", ".md", ".inc", ".c", ".h" };

            var files = new List<string>();
            try
            {
                // Walk explicitly so every entry is inspected before descent. A linked file or
                // directory would make the published source tree depend on an external target.
                var pending = new Stack<string>();
                pending.Push(scratchDir);
                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    foreach (string entry in Directory.GetFileSystemEntries(current))
                    {
                        FileAttributes attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            error = SanitizeScratchPath(
                                "projection contains a symlink/junction: " + entry,
                                scratchDir);
                            return false;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                            pending.Push(entry);
                        else
                            files.Add(entry);
                    }
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = SanitizeScratchPath("enumerate failed: " + ex.Message, scratchDir);
                return false;
            }

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(textExt, ext) < 0) continue;

                string text;
                try { text = File.ReadAllText(file); }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    // `file` is itself an absolute path UNDER scratchDir — sanitize it too so an
                    // enumeration/read failure can never leak the scratch location (Copilot
                    // review finding: per-record/per-file raw exception path).
                    error = SanitizeScratchPath("read failed for " + file + ": " + ex.Message, scratchDir);
                    return false;
                }

                // Strip only the exporter-owned scratch path (escaped, native, then forward
                // spellings). The unique guid in the path makes this boundary-safe: it cannot be
                // a prefix of an unrelated fixed-width token. We do NOT claim to strip arbitrary
                // absolute paths a projector might otherwise embed.
                string sanitized = NormalizeLf(SanitizeScratchPath(text, scratchDir));

                try { File.WriteAllText(file, sanitized); }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    error = SanitizeScratchPath("write failed for " + file + ": " + ex.Message, scratchDir);
                    return false;
                }
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

        static string PayloadName(int index, uint offset, uint length)
            => index.ToString("D4") + "_" + offset.ToString("X6") + "_" + length + ".bin";

        static string RelToNative(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

        static string Hex32(uint v) => "0x" + v.ToString("X8");
        static string Hex8(byte v) => "0x" + v.ToString("X2");

        static string Sha256Hex(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

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
