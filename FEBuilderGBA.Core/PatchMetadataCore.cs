using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FEBuilderGBA
{
    /// <summary>
    /// Core patch metadata parsing and detection — no UI dependencies.
    /// Reads PATCH_*.txt files from config/patch2/{version}/ directories,
    /// extracts metadata, and checks installation status via PATCHED_IF conditions.
    /// </summary>
    public static class PatchMetadataCore
    {
        public enum PatchStatus
        {
            Unknown,
            Installed,
            NotInstalled
        }

        /// <summary>Metadata extracted from a single patch directory.</summary>
        public class PatchInfo
        {
            public string Name { get; set; } = "";
            public string DirectoryName { get; set; } = "";
            public string DirectoryPath { get; set; } = "";
            public string Description { get; set; } = "";
            public string Author { get; set; } = "";
            public string Tags { get; set; } = "";
            public string Type { get; set; } = "";
            public PatchStatus Status { get; set; } = PatchStatus.Unknown;
            public string PatchFilePath { get; set; } = "";
            /// <summary>Number of IF: dependency conditions in the patch file.</summary>
            public int DependencyCount { get; set; }
            /// <summary>Number of unsatisfied IF: dependencies. 0 = all met.</summary>
            public int UnsatisfiedDependencyCount { get; set; }
            /// <summary>Unsatisfied dependency details (only populated when > 0).</summary>
            public List<PatchDependency> UnsatisfiedDependencies { get; set; } = new();
        }

        /// <summary>
        /// Desktop empty-state message for the Patch Manager when <c>config/patch2/{version}</c>
        /// has not been downloaded yet (#1811). Since #1766 the patch library is delivered over
        /// git (repo <c>laqieer/FEBuilderGBA-patch2</c>) rather than bundled, so a fresh install
        /// ships five empty <c>FE6/FE7J/FE7U/FE8J/FE8U</c> stub dirs. Unlike the Android-only
        /// <see cref="AndroidResourceNoticeCore.PatchLibraryUnavailableMessage"/>, this points the
        /// user at the in-app Initialize / Check-for-Updates flow. Bilingual (JA+EN) to match the
        /// startup patch2 prompt.
        /// </summary>
        public const string NotInitializedMessage =
            "パッチデータがまだダウンロードされていません。\r\n" +
            "「更新の確認」からパッチデータベースをダウンロードしてください。\r\n\r\n" +
            "The patch database has not been downloaded yet.\r\n" +
            "Use Check for Updates / Initialize Repository to fetch it.";

        /// <summary>
        /// True when the patch library for a version has no installable patches — the directory is
        /// missing, or exists but a successful scan finds no <c>PATCH_*.txt</c> (the fresh-install
        /// state, #1811). Never throws. An <em>existing</em> directory that fails to enumerate
        /// (permission / path-too-long) returns <c>false</c>: that is a real error, not the
        /// not-initialized state, so callers won't mislead the user with the download notice.
        /// </summary>
        /// <param name="patchBaseDir">The <c>config/patch2/{version}</c> directory.</param>
        public static bool IsPatchLibraryEmpty(string patchBaseDir)
        {
            if (string.IsNullOrEmpty(patchBaseDir))
                return true;
            try
            {
                return Directory.GetFiles(patchBaseDir, "PATCH_*.txt", SearchOption.AllDirectories).Length == 0;
            }
            catch (DirectoryNotFoundException)
            {
                // Genuinely missing directory -> the fresh-install / not-initialized state.
                return true;
            }
            catch
            {
                // An existing directory that fails to enumerate (permission / path-too-long) is a real
                // error, not the fresh-install state — return false so callers do NOT mislead the user
                // with the not-downloaded-yet notice and mask the actual failure. (Directory.Exists is
                // deliberately NOT used to gate this: it also returns false on an existence-probe error,
                // which would misclassify an inaccessible dir as "not initialized".)
                return false;
            }
        }

        /// <summary>
        /// Enumerate all patch directories for a given ROM version and parse metadata.
        /// </summary>
        /// <param name="patchBaseDir">The config/patch2/{version} directory.</param>
        /// <param name="rom">Current ROM for installation detection.</param>
        /// <param name="lang">Language suffix ("en", "zh", or "" for Japanese).</param>
        /// <returns>List of parsed patches, sorted by directory name.</returns>
        public static List<PatchInfo> EnumeratePatches(string patchBaseDir, ROM rom, string lang)
        {
            // Preserve the historical API/behavior: on a filesystem/access failure, LOG and
            // return an empty list (callers that need to distinguish "empty" from "failed" use
            // TryEnumeratePatches instead).
            if (!TryEnumeratePatches(patchBaseDir, rom, lang, out List<PatchInfo> patches, out string error))
            {
                // Log.Error is params string[] (joined with spaces) — concatenate, do NOT use {0}.
                Log.Error("PatchMetadataCore.EnumeratePatches failed for '" + patchBaseDir + "': " + error);
                return new List<PatchInfo>();
            }
            return patches;
        }

        /// <summary>
        /// Enumerate + parse patch metadata, distinguishing a genuinely EMPTY directory
        /// (returns <c>true</c> with an empty list) from an enumeration/parse FAILURE (returns
        /// <c>false</c> with an explicit <paramref name="error"/>). Only documented filesystem/
        /// access/path exceptions are caught — programmer defects (NullReference, argument-null,
        /// index-out-of-range, invalid-operation, …) propagate so real bugs are not hidden.
        /// </summary>
        public static bool TryEnumeratePatches(string patchBaseDir, ROM rom, string lang,
            out List<PatchInfo> patches, out string error)
            => TryEnumeratePatches(patchBaseDir, rom, lang, File.ReadAllLines, out patches, out error);

        /// <summary>Internal read seam for deterministic enumeration-failure coverage.</summary>
        internal static bool TryEnumeratePatches(string patchBaseDir, ROM rom, string lang,
            Func<string, string[]> readAllLines, out List<PatchInfo> patches, out string error)
            => TryEnumeratePatches(patchBaseDir, rom, lang, readAllLines, null, out patches, out error);

        /// <summary>
        /// Internal read + directory-listing seam. <paramref name="listPatchFiles"/> defaults to
        /// a recursive <c>PATCH_*.txt</c> scan when <c>null</c>; injecting it lets tests simulate
        /// a directory-enumeration ACCESS failure deterministically (no flaky real permission
        /// changes needed).
        /// </summary>
        internal static bool TryEnumeratePatches(string patchBaseDir, ROM rom, string lang,
            Func<string, string[]> readAllLines, Func<string, string[]> listPatchFiles,
            out List<PatchInfo> patches, out string error)
        {
            patches = new List<PatchInfo>();
            error = "";
            // A null/empty patchBaseDir never touches the filesystem — legacy callers rely on
            // this resolving to "successful empty" (preserved on purpose; see remarks below).
            if (string.IsNullOrEmpty(patchBaseDir))
                return true;

            Func<string, string[]> list = listPatchFiles
                ?? (dir => Directory.GetFiles(dir, "PATCH_*.txt", SearchOption.AllDirectories));

            string[] patchFiles;
            try
            {
                // Guard the ACTUAL enumeration — NOT a separate Directory.Exists probe. An
                // existing-but-inaccessible directory (permission/IO/path-too-long) must be
                // reported as a real failure, never silently downgraded to "empty" (Copilot
                // review finding: Directory.Exists inaccessible=>empty). A genuinely MISSING
                // directory still resolves to "successful empty" via DirectoryNotFoundException,
                // matching the historical contract and mirroring IsPatchLibraryEmpty's pattern.
                patchFiles = list(patchBaseDir);
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                error = ex.Message;
                return false;
            }

            try
            {
                // Enumerate EVERY PATCH_*.txt recursively, matching WinForms PatchForm.ScanPatchs
                // (SearchOption.AllDirectories); each patch is named by its NAME param (fallback =
                // filename minus the PATCH_ prefix).
                foreach (string file in patchFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string defaultName = fileName.StartsWith("PATCH_", StringComparison.OrdinalIgnoreCase)
                        ? fileName.Substring("PATCH_".Length)
                        : fileName;

                    var info = ParsePatchFileStrict(file, defaultName, rom, lang, readAllLines);
                    // Group by the patch's real containing folder (e.g. "SYSTEM") so the CLI
                    // --patch-name folder filter keeps working with recursion.
                    string containingDir = Path.GetFileName(Path.GetDirectoryName(file) ?? "") ?? "";
                    if (!string.IsNullOrEmpty(containingDir))
                        info.DirectoryName = containingDir;
                    patches.Add(info);
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                patches = new List<PatchInfo>();
                error = ex.Message;
                return false;
            }
            return true;
        }

        /// <summary>
        /// True for documented filesystem/access/path/format exceptions that advisory patch
        /// enumeration may legitimately encounter. Excludes programmer defects (argument-null,
        /// null-reference, index-out-of-range, invalid-operation) so they are never swallowed.
        /// </summary>
        internal static bool IsExpectedFileSystemException(Exception ex)
            => ex is IOException
            || ex is UnauthorizedAccessException
            || ex is System.Security.SecurityException
            || ex is NotSupportedException
            || ex.GetType() == typeof(ArgumentException);

        /// <summary>
        /// Parse a PATCH_*.txt metadata file.
        /// </summary>
        public static PatchInfo ParsePatchFile(string patchFilePath, string dirName, ROM rom, string lang)
        {
            try
            {
                return ParsePatchFileStrict(patchFilePath, dirName, rom, lang, File.ReadAllLines);
            }
            catch (Exception ex)
            {
                Log.ErrorF("PatchMetadataCore: Failed to parse {0}: {1}", patchFilePath, ex.Message);
                return CreatePatchInfo(patchFilePath, dirName);
            }
        }

        static PatchInfo ParsePatchFileStrict(string patchFilePath, string dirName, ROM rom, string lang,
            Func<string, string[]> readAllLines)
        {
            var info = CreatePatchInfo(patchFilePath, dirName);
            string[] lines = readAllLines(patchFilePath);
            string? patchedIf = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("//")) continue;

                // Localized NAME
                if (!string.IsNullOrEmpty(lang) && line.StartsWith($"NAME.{lang}=", StringComparison.OrdinalIgnoreCase))
                    info.Name = line.Substring($"NAME.{lang}=".Length).Trim();
                else if (line.StartsWith("NAME=", StringComparison.OrdinalIgnoreCase) && info.Name == dirName)
                    info.Name = line.Substring(5).Trim();

                // Localized INFO
                if (!string.IsNullOrEmpty(lang) && line.StartsWith($"INFO.{lang}=", StringComparison.OrdinalIgnoreCase))
                    info.Description = CleanDescription(line.Substring($"INFO.{lang}=".Length));
                else if (line.StartsWith("INFO=", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(info.Description))
                    info.Description = CleanDescription(line.Substring(5));

                if (line.StartsWith("AUTHOR=", StringComparison.OrdinalIgnoreCase))
                    info.Author = line.Substring(7).Trim();

                if (line.StartsWith("TAG=", StringComparison.OrdinalIgnoreCase))
                    info.Tags = line.Substring(4).Trim();

                if (line.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase))
                    info.Type = line.Substring(5).Trim();

                if (line.StartsWith("PATCHED_IF:", StringComparison.OrdinalIgnoreCase))
                    patchedIf = line.Substring(11);
            }

            if (!string.IsNullOrEmpty(patchedIf))
                info.Status = CheckPatchInstalled(patchedIf, rom, Path.GetDirectoryName(patchFilePath) ?? "");

            var allDeps = ParsePatchDependencies(lines, lang);
            info.DependencyCount = allDeps.Count;
            if (allDeps.Count > 0)
            {
                var missing = new List<PatchDependency>();
                foreach (var dep in allDeps)
                {
                    dep.IsSatisfied = EvaluateIfCondition(dep.Condition, rom);
                    if (!dep.IsSatisfied)
                        missing.Add(dep);
                }
                info.UnsatisfiedDependencyCount = missing.Count;
                info.UnsatisfiedDependencies = missing;
            }
            return info;
        }

        static PatchInfo CreatePatchInfo(string patchFilePath, string dirName)
            => new PatchInfo
            {
                Name = dirName,
                DirectoryName = dirName,
                DirectoryPath = Path.GetDirectoryName(patchFilePath) ?? "",
                PatchFilePath = patchFilePath,
            };

        /// <summary>
        /// Check if a patch is installed by evaluating a PATCHED_IF condition string.
        /// Fixed <c>0xADDR</c> / bare-hex addresses are hex-parsed directly. Any
        /// <c>$</c>-prefixed address macro — the full family handled by
        /// <see cref="PatchMacroAddressResolverCore.Resolve"/>: <c>$GREP</c>/<c>$XGREP</c>/
        /// <c>$FGREP</c> (with <c>END</c>/<c>ENDA</c>/<c>+skip</c>), <c>$GREP_ENABLE_POINTER</c>,
        /// <c>$P32</c>/<c>$P32+4</c>, <c>$TEXTID</c>/<c>$TEXTID_P</c>, and the
        /// <c>$&lt;hexaddr&gt;</c> pointer-indirection form (e.g. <c>$0x0812345</c> reads the
        /// 32-bit GBA pointer stored at that offset — there is no literal <c>$deref</c> keyword)
        /// — is resolved through that shared, tested resolver, mirroring WinForms install
        /// detection (#1919). <paramref name="basedir"/> is the patch's own directory, needed
        /// to resolve <c>$FGREP</c> (external .bin) patterns.
        /// Returns <c>Unknown</c> only for a malformed condition (no <c>=</c>, no expected
        /// bytes, or a fixed address that isn't valid hex). A macro that doesn't resolve
        /// (<see cref="U.NOT_FOUND"/>, incl. a missing <c>$FGREP</c> file), an out-of-bounds
        /// address, or a byte mismatch is reported as <c>NotInstalled</c>.
        /// </summary>
        public static PatchStatus CheckPatchInstalled(string condition, ROM rom)
            => CheckPatchInstalled(condition, rom, "");

        public static PatchStatus CheckPatchInstalled(string condition, ROM rom, string basedir)
        {
            try
            {
                int eqIdx = condition.IndexOf('=');
                if (eqIdx < 0) return PatchStatus.Unknown;

                string addrStr = condition.Substring(0, eqIdx).Trim();
                string dataStr = condition.Substring(eqIdx + 1).Trim();

                byte[] expected = ParseByteArray(dataStr);
                if (expected.Length == 0) return PatchStatus.Unknown;

                // Fixed addresses keep the original hex parse: patch metadata contains
                // BARE hex like "2C2F0" that the resolver's atoi0x reads as DECIMAL, so
                // only $-prefixed macros ($GREP/$XGREP/$FGREP/$P32/$TEXTID/$<addr> deref)
                // go through the shared resolver (#1919).
                uint addr;
                if (addrStr.StartsWith("$", StringComparison.Ordinal))
                {
                    addr = PatchMacroAddressResolverCore.Resolve(rom, addrStr, basedir, 0x100);
                    // NOT_FOUND (0xFFFFFFFF) — the macro/pattern didn't resolve (e.g. a GREP
                    // pattern absent from the ROM) → the patch is simply not installed.
                    if (addr == U.NOT_FOUND) return PatchStatus.NotInstalled;
                }
                else
                {
                    string hex = addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? addrStr.Substring(2) : addrStr;
                    if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                        return PatchStatus.Unknown;
                }

                // (long) widens the add so a huge addr can't wrap past the bounds check.
                if ((long)addr + expected.Length > rom.Data.Length) return PatchStatus.NotInstalled;

                byte[] actual = rom.getBinaryData(addr, expected.Length);
                return U.memcmp(expected, actual) == 0
                    ? PatchStatus.Installed
                    : PatchStatus.NotInstalled;
            }
            catch
            {
                return PatchStatus.Unknown;
            }
        }

        /// <summary>
        /// Parse a space-separated hex byte string like "0xAB 0xCD 0xEF" into a byte array.
        /// </summary>
        public static byte[] ParseByteArray(string dataStr)
        {
            var result = new List<byte>();
            string[] parts = dataStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string hex = part.Trim();
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex.Substring(2);
                if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    result.Add(b);
                else
                    break; // Stop at first non-byte token
            }
            return result.ToArray();
        }

        /// <summary>Clean description text from patch metadata.</summary>
        public static string CleanDescription(string desc)
        {
            return desc.Replace("\\r\\n", "\n").Replace("\\n", "\n").Trim();
        }

        /// <summary>
        /// Get the language suffix for localized patch metadata.
        /// </summary>
        public static string GetLanguageSuffix()
        {
            string lang = CoreState.Language ?? "";
            if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
            if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "";
            return "en";
        }

        /// <summary>A single dependency condition from a patch file (IF: line).</summary>
        public class PatchDependency
        {
            /// <summary>The raw condition string (e.g. "0x02BA4=0x00 0xB5 0xC2 0x0F").</summary>
            public string Condition { get; set; } = "";
            /// <summary>Human-readable comment from IF_COMMENT (localized), or empty.</summary>
            public string Comment { get; set; } = "";
            /// <summary>Whether this dependency is satisfied in the current ROM.</summary>
            public bool IsSatisfied { get; set; }
        }

        /// <summary>
        /// Extract IF: dependency conditions from a PATCH_*.txt file.
        /// These are preconditions that must be met (other patches installed) before this patch can be applied.
        /// </summary>
        /// <param name="patchFilePath">Path to the PATCH_*.txt file.</param>
        /// <param name="lang">Language suffix for localized IF_COMMENT.</param>
        /// <returns>List of dependency conditions.</returns>
        public static List<PatchDependency> GetPatchDependencies(string patchFilePath, string lang = "")
        {
            if (!File.Exists(patchFilePath)) return new List<PatchDependency>();

            try
            {
                return ParsePatchDependencies(File.ReadAllLines(patchFilePath), lang);
            }
            catch (Exception ex)
            {
                Log.ErrorF("PatchMetadataCore.GetPatchDependencies: {0}: {1}", patchFilePath, ex.Message);
                return new List<PatchDependency>();
            }
        }

        static List<PatchDependency> ParsePatchDependencies(string[] lines, string lang)
        {
            var result = new List<PatchDependency>();
            string ifComment = "";
            string ifCommentLocalized = "";

            // First pass: collect IF_COMMENT values
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("//")) continue;

                int sep = line.IndexOf('=');
                if (sep < 0) continue;

                string key = line.Substring(0, sep).Trim();
                string value = line.Substring(sep + 1).Trim();

                if (!string.IsNullOrEmpty(lang) &&
                    key.Equals($"IF_COMMENT.{lang}", StringComparison.OrdinalIgnoreCase))
                    ifCommentLocalized = value;
                else if (key.Equals("IF_COMMENT", StringComparison.OrdinalIgnoreCase) &&
                         string.IsNullOrEmpty(ifCommentLocalized))
                    ifComment = value;
            }

            string resolvedComment = !string.IsNullOrEmpty(ifCommentLocalized) ? ifCommentLocalized : ifComment;

            // Second pass: collect IF: conditions
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("//")) continue;
                if (!line.StartsWith("IF:", StringComparison.OrdinalIgnoreCase)) continue;

                string condition = line.Substring(3).Trim();
                int commentIdx = condition.IndexOf("//");
                string inlineComment = "";
                if (commentIdx >= 0)
                {
                    inlineComment = condition.Substring(commentIdx + 2).Trim();
                    condition = condition.Substring(0, commentIdx).Trim();
                }

                string depComment = !string.IsNullOrEmpty(resolvedComment) ? resolvedComment
                    : !string.IsNullOrEmpty(inlineComment) ? inlineComment : "";

                result.Add(new PatchDependency
                {
                    Condition = condition,
                    Comment = depComment,
                });
            }
            return result;
        }

        /// <summary>
        /// Check all IF: dependencies for a patch and return those that are NOT satisfied.
        /// </summary>
        /// <param name="rom">The ROM to check against.</param>
        /// <param name="patchFilePath">Path to the PATCH_*.txt file.</param>
        /// <param name="lang">Language suffix for localized IF_COMMENT.</param>
        /// <returns>List of unsatisfied dependencies. Empty means all dependencies are met.</returns>
        public static List<PatchDependency> CheckDependencies(ROM rom, string patchFilePath, string lang = "")
        {
            var deps = GetPatchDependencies(patchFilePath, lang);
            var missing = new List<PatchDependency>();

            foreach (var dep in deps)
            {
                dep.IsSatisfied = EvaluateIfCondition(dep.Condition, rom);
                if (!dep.IsSatisfied)
                    missing.Add(dep);
            }

            return missing;
        }

        /// <summary>
        /// Evaluate a single IF: condition against a ROM.
        /// Supports fixed-address checks (0xADDR=0xBB 0xBB ...).
        /// Returns true if the condition is satisfied, false otherwise.
        /// $GREP/$FGREP conditions are treated as satisfied (we can't check them simply).
        /// </summary>
        public static bool EvaluateIfCondition(string condition, ROM rom)
        {
            if (rom == null) return false;

            try
            {
                // $GREP/$FGREP conditions require searching the entire ROM — skip (assume met)
                if (condition.Contains("$GREP", StringComparison.OrdinalIgnoreCase) ||
                    condition.Contains("$FGREP", StringComparison.OrdinalIgnoreCase))
                    return true;

                int eqIdx = condition.IndexOf('=');
                if (eqIdx < 0) return true; // Malformed, assume OK

                string addrStr = condition.Substring(0, eqIdx).Trim();
                string dataStr = condition.Substring(eqIdx + 1).Trim();

                if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addrStr = addrStr.Substring(2);
                if (!uint.TryParse(addrStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                    return true; // Can't parse, assume OK

                byte[] expected = ParseByteArray(dataStr);
                if (expected.Length == 0) return true;

                if (addr + expected.Length > rom.Data.Length)
                    return false;

                byte[] actual = rom.getBinaryData(addr, expected.Length);
                return U.memcmp(expected, actual) == 0;
            }
            catch
            {
                return true; // On error, don't block
            }
        }

        /// <summary>Result of a patch apply/uninstall operation.</summary>
        public class PatchApplyResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int BytesWritten { get; set; }

            public static PatchApplyResult Ok(string msg, int bytesWritten = 0)
                => new PatchApplyResult { Success = true, Message = msg, BytesWritten = bytesWritten };
            public static PatchApplyResult Fail(string msg)
                => new PatchApplyResult { Success = false, Message = msg };
        }

        /// <summary>
        /// A parsed key=value entry from a PATCH_*.txt file, where the key
        /// can have colon-separated parts (e.g. "BIN:0x2900").
        /// </summary>
        public class PatchParam
        {
            public string RawKey { get; set; } = "";
            public string Value { get; set; } = "";
            /// <summary>Key split by ':'.</summary>
            public string[] KeyParts { get; set; } = Array.Empty<string>();
            /// <summary>The keyword (first part before ':').</summary>
            public string Keyword => KeyParts.Length > 0 ? KeyParts[0] : "";
        }

        /// <summary>
        /// Parse a PATCH_*.txt file into a list of key=value params (for installation).
        /// </summary>
        public static List<PatchParam> ParsePatchParams(string patchFilePath)
        {
            var result = new List<PatchParam>();
            if (!File.Exists(patchFilePath)) return result;

            string[] lines = File.ReadAllLines(patchFilePath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("//")) continue;

                int sep = line.IndexOf('=');
                if (sep < 0) continue;

                string key = line.Substring(0, sep).Trim();
                string value = line.Substring(sep + 1).Trim();

                result.Add(new PatchParam
                {
                    RawKey = key,
                    Value = value,
                    KeyParts = key.Split(':'),
                });
            }
            return result;
        }

        /// <summary>
        /// Apply a BIN-type patch to a ROM. Handles BIN: lines with fixed addresses
        /// and BIN:$FREEAREA with JUMP: hookup. Returns success/failure result.
        /// EA-type patches are not supported (require external assembler).
        /// </summary>
        /// <param name="rom">The ROM to modify.</param>
        /// <param name="patchFilePath">Full path to the PATCH_*.txt file.</param>
        /// <param name="undoData">Undo data for rollback support (optional, pass null to skip undo tracking).</param>
        public static PatchApplyResult ApplyPatch(ROM rom, string patchFilePath, Undo.UndoData? undoData = null)
        {
            if (rom == null) return PatchApplyResult.Fail("No ROM loaded.");
            if (!File.Exists(patchFilePath)) return PatchApplyResult.Fail("Patch file not found: " + patchFilePath);

            string patchDir = Path.GetDirectoryName(patchFilePath) ?? "";
            var allParams = ParsePatchParams(patchFilePath);

            // Check TYPE
            string type = allParams.FirstOrDefault(p => p.Keyword == "TYPE")?.Value ?? "";
            if (type == "EA")
                return PatchApplyResult.Fail("EA-type patches require an external assembler and are not yet supported in the Avalonia port.");

            if (type != "BIN")
                return PatchApplyResult.Fail($"Unsupported patch type: '{type}'. Only BIN patches are currently supported.");

            // Collect BIN/JUMP entries in order
            var binKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIN", "BINP", "BINAP", "BINF" };
            var actionParams = allParams.Where(p =>
                binKeywords.Contains(p.Keyword) || p.Keyword == "JUMP").ToList();

            if (actionParams.Count == 0)
                return PatchApplyResult.Fail("No BIN or JUMP entries found in patch file.");

            // Track where binary files are placed (for JUMP resolution)
            var binBlocks = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            int totalBytesWritten = 0;

            // Collect regions that will be overwritten (for backup)
            var regionsToBackup = new List<(uint address, int length)>();

            try
            {
                // Process BIN entries first, then JUMP entries
                var binEntries = actionParams.Where(p => binKeywords.Contains(p.Keyword)).ToList();
                var jumpEntries = actionParams.Where(p => p.Keyword == "JUMP").ToList();

                // Pre-scan to collect regions for backup
                CollectBinRegions(rom, patchDir, binEntries, regionsToBackup);
                CollectJumpRegions(rom, patchDir, jumpEntries, binEntries, regionsToBackup);

                // Save backup of original bytes before writing
                if (regionsToBackup.Count > 0)
                    SaveBackup(rom, patchFilePath, regionsToBackup);

                foreach (var param in binEntries)
                {
                    var result = ApplyBinEntry(rom, patchDir, param, binBlocks, undoData);
                    if (!result.Success) return result;
                    totalBytesWritten += result.BytesWritten;
                }

                foreach (var param in jumpEntries)
                {
                    var result = ApplyJumpEntry(rom, patchDir, param, binBlocks, undoData);
                    if (!result.Success) return result;
                    totalBytesWritten += result.BytesWritten;
                }
            }
            catch (Exception ex)
            {
                return PatchApplyResult.Fail("Patch install error: " + ex.Message);
            }

            return PatchApplyResult.Ok(
                $"Patch installed successfully. {totalBytesWritten} bytes written.",
                totalBytesWritten);
        }

        /// <summary>
        /// Get the backup file path for a given patch file.
        /// Format: {patchDir}/.backup_{patchFileName}.txt
        /// </summary>
        public static string GetBackupFilePath(string patchFilePath)
        {
            string dir = Path.GetDirectoryName(patchFilePath) ?? "";
            string patchName = Path.GetFileNameWithoutExtension(patchFilePath);
            return Path.Combine(dir, $".backup_{patchName}.txt");
        }

        /// <summary>
        /// Check whether a backup file exists for the given patch, enabling uninstall.
        /// </summary>
        public static bool HasBackup(string patchFilePath)
        {
            return File.Exists(GetBackupFilePath(patchFilePath));
        }

        /// <summary>
        /// Save a backup of original ROM bytes before they are overwritten.
        /// Each record is one line: "0xADDRESS:LENGTH:HH HH HH ..."
        /// </summary>
        public static void SaveBackup(ROM rom, string patchFilePath, List<(uint address, int length)> regions)
        {
            string backupPath = GetBackupFilePath(patchFilePath);
            var lines = new List<string>();
            foreach (var (address, length) in regions)
            {
                if (address + length > rom.Data.Length)
                    continue;
                byte[] original = rom.getBinaryData(address, length);
                string hexBytes = string.Join(" ", original.Select(b => b.ToString("X2")));
                lines.Add($"0x{address:X}:{length}:{hexBytes}");
            }
            File.WriteAllLines(backupPath, lines);
        }

        /// <summary>
        /// Parse a backup file into a list of (address, originalBytes) records.
        /// Returns null if the file doesn't exist or is malformed.
        /// </summary>
        public static List<(uint address, byte[] data)>? ParseBackupFile(string backupPath)
        {
            if (!File.Exists(backupPath))
                return null;

            var records = new List<(uint address, byte[] data)>();
            string[] lines = File.ReadAllLines(backupPath);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Format: 0xADDRESS:LENGTH:HH HH HH ...
                string[] parts = line.Split(':', 3);
                if (parts.Length < 3) return null;

                string addrStr = parts[0].Trim();
                if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addrStr = addrStr.Substring(2);
                if (!uint.TryParse(addrStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                    return null;

                if (!int.TryParse(parts[1].Trim(), out int length) || length < 0)
                    return null;

                byte[] data = ParseBackupHexBytes(parts[2].Trim());
                if (data.Length != length) return null;

                records.Add((addr, data));
            }
            return records;
        }

        /// <summary>Parse space-separated hex bytes (no 0x prefix) from backup file.</summary>
        static byte[] ParseBackupHexBytes(string hexStr)
        {
            if (string.IsNullOrWhiteSpace(hexStr))
                return Array.Empty<byte>();

            string[] tokens = hexStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!byte.TryParse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
                    return Array.Empty<byte>();
            }
            return result;
        }

        /// <summary>
        /// Uninstall a patch by restoring original ROM bytes from a backup file.
        /// The backup file is created during patch installation. The backup file is
        /// PRESERVED across uninstall so that undoing the uninstall (which reapplies the
        /// patched ROM bytes) leaves the patch uninstallable again; a re-install overwrites it.
        /// </summary>
        public static PatchApplyResult UninstallPatch(ROM rom, string patchFilePath)
            => UninstallPatch(rom, patchFilePath, null);

        /// <summary>
        /// Uninstall a patch by restoring original ROM bytes from a backup file.
        /// The backup file is created during patch installation.
        /// When <paramref name="undoData"/> is supplied, every restored ROM region is
        /// recorded into it (via the recording <c>write_range</c> overload) BEFORE the
        /// write so the caller can <c>Push</c> on success or <c>Rollback</c> to byte-
        /// identity on failure — even a partial restore (the records written before a
        /// later record fails validation) is captured. The backup file is PRESERVED
        /// across uninstall so that undoing the uninstall (which reapplies the patched
        /// ROM bytes) leaves the patch uninstallable again; a re-install overwrites it.
        /// </summary>
        public static PatchApplyResult UninstallPatch(ROM rom, string patchFilePath, Undo.UndoData? undoData)
        {
            if (rom == null) return PatchApplyResult.Fail("No ROM loaded.");
            if (!File.Exists(patchFilePath)) return PatchApplyResult.Fail("Patch file not found.");

            string backupPath = GetBackupFilePath(patchFilePath);
            if (!File.Exists(backupPath))
            {
                return PatchApplyResult.Fail(
                    "No backup file found for this patch. Uninstall is only possible for patches " +
                    "installed with a backup (SaveBackup) (which saves a backup automatically). " +
                    "Restore from a ROM backup instead.");
            }

            var records = ParseBackupFile(backupPath);
            if (records == null || records.Count == 0)
            {
                return PatchApplyResult.Fail("Backup file is empty or malformed.");
            }

            try
            {
                int totalBytes = 0;
                foreach (var (address, data) in records)
                {
                    if (address + data.Length > rom.Data.Length)
                    {
                        return PatchApplyResult.Fail(
                            $"Backup record at 0x{address:X} ({data.Length} bytes) exceeds ROM size. " +
                            "The ROM may have been modified since the patch was installed.");
                    }
                    if (undoData != null)
                        rom.write_range(address, data, undoData);
                    else
                        rom.write_range(address, data);
                    totalBytes += data.Length;
                }

                // Backup is intentionally PRESERVED across uninstall: undoing the uninstall reapplies the patched ROM bytes, and keeping the backup lets the user uninstall again. It is harmless while not installed (the GUI gates uninstall on Status==Installed, though UninstallPatch itself does not guard on status and is safe to call idempotently) and a re-install overwrites it via SaveBackup.

                return PatchApplyResult.Ok(
                    $"Patch uninstalled successfully. {totalBytes} bytes restored.",
                    totalBytes);
            }
            catch (Exception ex)
            {
                return PatchApplyResult.Fail("Patch uninstall error: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Clean-ROM-diff uninstall (#1462)
        //
        // Ports the WinForms PatchForm.UninstallPatchInner engine so a BIN
        // patch installed in a PRIOR session (no per-patch backup file) — the
        // common case for a freshly loaded ROM that already contains patches —
        // can still be uninstalled by diffing against a user-supplied patch-free
        // ("clean") ROM. Mirrors PatchForm.UnInstallPatch ->
        // PatchFormUninstallDialogForm -> UninstallPatchInner.
        //
        // Scope: fixed-address BIN entries + JUMP injection points (the same
        // regions the install path tracks for SaveBackup).
        //
        // CORRECTION-ONLY RESTORE (review #1462): the engine restores ONLY the
        // bytes that actually DIFFER between the current (patched) ROM and the
        // clean ROM inside each traced region. This is the key safety property —
        // an over-estimated region/JUMP length never clobbers an adjacent unrelated
        // patch, because bytes that already match the clean ROM are written as
        // no-ops; and it faithfully removes exactly this patch's edits.
        //
        // HONEST PARTIAL REPORTING: entries we cannot trace from the patch text
        // alone — $FREEAREA payloads, $GREP/$FGREP/$XGREP/pointer-deref address
        // forms, and EA patches (TYPE=EA) — are surfaced as an untraceable count
        // so the result is reported as a PARTIAL/incomplete uninstall, never an
        // over-claimed success. WinForms' GREP/mask trace (TraceBINPatchedMapping)
        // and CalcAutoLength/StripROM remain WF-only (documented non-goals).
        // ------------------------------------------------------------------

        /// <summary>
        /// A traced patch region: where the patch wrote, how many bytes, and (for fixed-address
        /// BIN entries) the patch's OWN recorded bytes — the content of the patch's <c>.bin</c>
        /// sidecar, i.e. WinForms <c>BinMapping.bin</c>. <see cref="PatchBytes"/> is <c>null</c>
        /// for JUMP regions whose injected code we cannot synthesise from the patch text alone.
        /// </summary>
        public readonly struct PatchRegion
        {
            public readonly uint Address;
            public readonly int Length;
            public readonly byte[]? PatchBytes; // null when the patch's own bytes are unknown (JUMP)
            public PatchRegion(uint address, int length, byte[]? patchBytes)
            {
                Address = address; Length = length; PatchBytes = patchBytes;
            }
        }

        /// <summary>
        /// Collect the fixed-address regions a BIN patch touches WITH each region's own patch
        /// bytes (the <c>.bin</c> sidecar content = WinForms <c>BinMapping.bin</c>), and report
        /// how many entries could NOT be traced from the patch text alone (EA patches, $FREEAREA
        /// payloads, $GREP/macro/pointer address forms). Returns an empty list (never null) for
        /// non-BIN/EA patches or on parse failure.
        /// </summary>
        public static List<PatchRegion> CollectPatchRegionsWithBytes(
            ROM rom, string patchFilePath, out int untraceableCount)
        {
            untraceableCount = 0;
            var regions = new List<PatchRegion>();
            if (rom == null || !File.Exists(patchFilePath)) return regions;

            var allParams = ParsePatchParams(patchFilePath);
            string type = allParams.FirstOrDefault(p => p.Keyword == "TYPE")?.Value ?? "";
            // BIN patches have a portable fixed-address region map. An EMPTY/missing TYPE is
            // treated as BIN — matching the Avalonia Patch Manager's CanInstall/CanUninstall
            // convention (string.IsNullOrEmpty(Type)) so legacy BIN patches that omit TYPE= are
            // still uninstallable. EA tracing is WF-only.
            bool isBin = string.IsNullOrEmpty(type) || type.Equals("BIN", StringComparison.OrdinalIgnoreCase);
            if (!isBin)
            {
                // Count every action-bearing line as untraceable so EA is reported as not supported.
                untraceableCount = allParams.Count(p =>
                    p.Keyword == "ORG" || p.Keyword == "ASM" || p.Keyword == "PROCS" || p.Keyword == "JUMP" ||
                    p.Keyword == "BIN" || p.Keyword == "BINP" || p.Keyword == "BINAP" || p.Keyword == "BINF");
                if (untraceableCount == 0) untraceableCount = 1; // unknown type: never claim a clean trace
                return regions;
            }

            string patchDir = Path.GetDirectoryName(patchFilePath) ?? "";
            var binKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BIN", "BINP", "BINAP", "BINF" };
            var binEntries = allParams.Where(p => binKeywords.Contains(p.Keyword)).ToList();
            var jumpEntries = allParams.Where(p => p.Keyword == "JUMP").ToList();

            // Fixed-address BIN entries: record the address AND the patch's own bytes (the .bin
            // file content) so the patch-absence check can compare candidate-vs-patch-bytes
            // exactly like WinForms SearchContainThisPatchBy (memcmp against t.bin). $FREEAREA /
            // $GREP / non-literal address forms cannot be traced from text -> untraceable.
            foreach (var param in binEntries)
            {
                string addrPart = param.KeyParts.Length > 1 ? param.KeyParts[1] : "";
                string filePath = Path.Combine(patchDir, param.Value);

                if (addrPart.StartsWith("$", StringComparison.OrdinalIgnoreCase)) { untraceableCount++; continue; }
                uint addr = ParseHexAddress(addrPart);
                if (addr == U.NOT_FOUND) { untraceableCount++; continue; }
                if (!File.Exists(filePath)) { untraceableCount++; continue; }

                byte[] binData;
                try { binData = File.ReadAllBytes(filePath); }
                catch { untraceableCount++; continue; }
                if (binData.Length == 0) continue;
                if (addr + binData.Length > rom.Data.Length) continue;

                regions.Add(new PatchRegion(addr, binData.Length, binData));
            }

            // JUMP entries: the injected code is synthesised at install from register/offset,
            // which we cannot reproduce from text alone. Keep the region (with its estimated
            // length) for the restore-coverage size gate, but PatchBytes=null so the
            // patch-absence check skips it (we can't assert "still contains the patch" for it).
            foreach (var param in jumpEntries)
            {
                if (param.KeyParts.Length < 2) continue;
                uint injectionAddr = ParseHexAddress(param.KeyParts[1]);
                if (injectionAddr == U.NOT_FOUND) { untraceableCount++; continue; }

                int jumpSize = 8; // $NONE/$B/$BL=4, register-based=8 (conservative)
                if (param.KeyParts.Length > 2)
                {
                    string regStr = param.KeyParts[2];
                    if (regStr == "$NONE" || regStr == "$BL" || regStr == "$B") jumpSize = 4;
                }
                if (injectionAddr + jumpSize <= rom.Data.Length)
                    regions.Add(new PatchRegion(injectionAddr, jumpSize, null));
            }

            return regions;
        }

        /// <summary>
        /// Backward-compatible (address, length) view of <see cref="CollectPatchRegionsWithBytes"/>.
        /// </summary>
        public static List<(uint address, int length)> CollectPatchRegions(
            ROM rom, string patchFilePath, out int untraceableCount)
        {
            var rich = CollectPatchRegionsWithBytes(rom, patchFilePath, out untraceableCount);
            var regions = new List<(uint address, int length)>(rich.Count);
            foreach (var r in rich) regions.Add((r.Address, r.Length));
            return regions;
        }

        /// <summary>Overload without the untraceable-count out parameter.</summary>
        public static List<(uint address, int length)> CollectPatchRegions(ROM rom, string patchFilePath)
            => CollectPatchRegions(rom, patchFilePath, out _);

        /// <summary>
        /// Faithful port of WinForms <c>PatchFormUninstallDialogForm.SearchContainThisPatchBy</c>:
        /// returns <c>true</c> if <paramref name="candidateRomBytes"/> still holds the PATCH'S OWN
        /// bytes (<see cref="PatchRegion.PatchBytes"/> == WinForms <c>t.bin</c>) at ANY traced
        /// region — i.e. the candidate STILL CONTAINS THE PATCH and must be rejected as not-clean.
        /// Regions whose patch bytes are unknown (JUMP, PatchBytes==null) are skipped — we cannot
        /// assert containment for them. Returns <c>false</c> when no region's own bytes are present
        /// (a genuine pre-patch ROM, vanilla OR otherwise-modified, even with edits elsewhere).
        /// </summary>
        public static bool RomContainsPatch(List<PatchRegion> regions, byte[] candidateRomBytes)
        {
            if (regions == null || candidateRomBytes == null) return false;
            foreach (var r in regions)
            {
                byte[]? patchBytes = r.PatchBytes;
                if (patchBytes == null || patchBytes.Length == 0) continue; // unknown own-bytes -> skip
                if (r.Address + patchBytes.Length > candidateRomBytes.Length) continue;

                bool match = true;
                for (int i = 0; i < patchBytes.Length; i++)
                {
                    if (candidateRomBytes[r.Address + i] != patchBytes[i]) { match = false; break; }
                }
                if (match)
                    return true; // candidate still contains the patch's own bytes -> not clean
            }
            return false;
        }

        /// <summary>
        /// Backward-compatible overload. Builds patch-byte info from the loaded ROM when only
        /// (address, length) regions are supplied: each region's patch bytes are taken from the
        /// CURRENT ROM (best-effort), then routed through the faithful
        /// <see cref="RomContainsPatch(List{PatchRegion}, byte[])"/>. Prefer the
        /// <see cref="PatchRegion"/> overload, which carries the patch's true recorded bytes.
        /// </summary>
        public static bool RomContainsPatch(ROM rom, List<(uint address, int length)> regions, byte[] candidateRomBytes)
        {
            if (rom == null || regions == null || candidateRomBytes == null) return false;
            var rich = new List<PatchRegion>(regions.Count);
            foreach (var (address, length) in regions)
            {
                if (length <= 0 || address + length > rom.Data.Length) continue;
                rich.Add(new PatchRegion(address, length, rom.getBinaryData(address, length)));
            }
            return RomContainsPatch(rich, candidateRomBytes);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="candidateRomBytes"/> matches the
        /// ROM's pristine <c>orignal_crc32</c> (a vanilla, never-modified ROM).
        /// Advisory only — a clean-but-otherwise-modified pre-patch ROM is also a
        /// valid uninstall source (the real gate is <see cref="RomContainsPatch"/>).
        /// </summary>
        public static bool IsVanillaRom(ROM rom, byte[] candidateRomBytes)
        {
            if (rom == null || candidateRomBytes == null) return false;
            uint orignalCrc32 = rom.RomInfo.orignal_crc32;
            if (orignalCrc32 == 0) return false; // unknown baseline -> can't assert vanilla
            var crc32 = new U.CRC32();
            return crc32.Calc(candidateRomBytes) == orignalCrc32;
        }

        /// <summary>
        /// Compatibility gate (review #1462): the candidate clean ROM must be the SAME
        /// game/version family as the loaded ROM. Compares the 4-byte GBA header game
        /// code at 0xAC and the 12-byte game title at 0xA0. A wrong game/version that
        /// merely lacks the patch bytes must fail closed BEFORE any mutation.
        /// </summary>
        public static bool IsCompatibleRom(ROM rom, byte[] candidateRomBytes)
        {
            if (rom == null || candidateRomBytes == null) return false;
            // GBA cartridge header: game title @0xA0 (12 bytes) + game code @0xAC (4 bytes).
            const uint HEADER_END = 0xB0;
            if (rom.Data.Length < HEADER_END || candidateRomBytes.Length < HEADER_END) return false;
            for (uint a = 0xA0; a < HEADER_END; a++)
            {
                if (rom.Data[a] != candidateRomBytes[a]) return false;
            }
            return true;
        }

        /// <summary>
        /// Uninstall a BIN patch by diff-restoring its touched regions from a
        /// user-supplied patch-free ("clean") ROM. Port of WinForms
        /// <c>PatchForm.UninstallPatchInner</c> (correction-only restore).
        ///
        /// Validation (ALL before any mutation):
        ///  * the patch must expose at least one fixed-address region;
        ///  * the clean ROM must be header-compatible (<see cref="IsCompatibleRom"/>) —
        ///    fail closed for a wrong game/version;
        ///  * the clean ROM must COVER every traced region (preflight size gate) — fail closed
        ///    for a truncated / pre-expansion ROM rather than silently skipping regions;
        ///  * the clean ROM must NOT still contain the patch's OWN bytes
        ///    (<see cref="RomContainsPatch(List{PatchRegion}, byte[])"/>, faithful to WF
        ///    SearchContainThisPatchBy's memcmp-against-t.bin).
        ///
        /// Only bytes that DIFFER between the current ROM and the clean ROM inside each
        /// traced region are written (so an over-estimated length is a safe no-op).
        /// When <paramref name="undoData"/> is supplied every restored region is recorded
        /// so the caller can <c>Push</c> on success or <c>Rollback</c> to byte-identity on failure.
        /// If the patch has untraceable entries the result is reported as PARTIAL, never
        /// an over-claimed full success.
        /// </summary>
        public static PatchApplyResult UninstallPatchWithCleanRom(
            ROM rom, string patchFilePath, byte[] cleanRomBytes, Undo.UndoData? undoData)
        {
            if (rom == null) return PatchApplyResult.Fail("No ROM loaded.");
            if (!File.Exists(patchFilePath)) return PatchApplyResult.Fail("Patch file not found.");
            if (cleanRomBytes == null || cleanRomBytes.Length == 0)
                return PatchApplyResult.Fail("Clean ROM is empty or could not be read.");

            var regions = CollectPatchRegionsWithBytes(rom, patchFilePath, out int untraceableCount);
            if (regions.Count == 0)
            {
                return PatchApplyResult.Fail(
                    "This patch exposes no fixed-address regions to uninstall via a clean ROM. " +
                    "EA patches and patches that use only $FREEAREA/$GREP regions need the WinForms patch manager.");
            }

            // Compatibility gate: wrong game/version must fail closed before any mutation.
            if (!IsCompatibleRom(rom, cleanRomBytes))
            {
                return PatchApplyResult.Fail(
                    "The selected ROM is a different game or version (GBA header mismatch). " +
                    "Choose the patch-free ROM that matches the loaded game.");
            }

            // PREFLIGHT SIZE GATE (before any mutation): the clean ROM must cover EVERY traced
            // region. A truncated / pre-expansion clean ROM would otherwise silently skip whole
            // regions yet still report success, leaving the patch effectively installed.
            foreach (var r in regions)
            {
                if (r.Length <= 0) continue;
                if (r.Address + r.Length > cleanRomBytes.Length)
                {
                    return PatchApplyResult.Fail(
                        $"The selected clean ROM is too small: it does not cover the patched region at " +
                        $"0x{r.Address:X} ({r.Length} bytes). It may predate a ROM expansion. " +
                        "Choose a patch-free ROM at least as large as the current one.");
                }
            }

            // Patch-absence check: the picked ROM must NOT still contain the patch's OWN bytes.
            if (RomContainsPatch(regions, cleanRomBytes))
            {
                return PatchApplyResult.Fail(
                    "The selected ROM still contains this patch. Choose the ROM from BEFORE the patch was installed.");
            }

            try
            {
                int totalBytes = 0;
                foreach (var r in regions)
                {
                    uint address = r.Address;
                    int length = r.Length;
                    if (length <= 0) continue;
                    // Clamp to bytes that exist in BOTH ROMs. (The preflight gate already
                    // guaranteed the clean ROM covers each region; the current ROM is the
                    // only remaining bound and matches in the common case.)
                    int safeLen = length;
                    if (address + safeLen > rom.Data.Length)
                        safeLen = (int)(rom.Data.Length - address);
                    if (address + safeLen > cleanRomBytes.Length)
                        safeLen = (int)(cleanRomBytes.Length - address);
                    if (safeLen <= 0) continue;

                    // CORRECTION-ONLY: restore only the bytes that actually differ. Identical
                    // bytes are skipped, so an over-estimated length never clobbers neighbours.
                    // Consecutive differing bytes are batched into a single write_range so a
                    // large region produces few undo records / write calls (not one per byte).
                    byte[] current = rom.getBinaryData(address, safeLen);
                    int run = 0; // length of the current differing run
                    for (int i = 0; i <= safeLen; i++)
                    {
                        bool differs = i < safeLen && cleanRomBytes[address + i] != current[i];
                        if (differs)
                        {
                            run++;
                            continue;
                        }
                        if (run > 0)
                        {
                            uint runStart = address + (uint)(i - run);
                            byte[] block = new byte[run];
                            Array.Copy(cleanRomBytes, runStart, block, 0, run);
                            if (undoData != null)
                                rom.write_range(runStart, block, undoData);
                            else
                                rom.write_range(runStart, block);
                            totalBytes += run;
                            run = 0;
                        }
                    }
                }

                if (untraceableCount > 0)
                {
                    return PatchApplyResult.Ok(
                        $"Patch partially uninstalled (clean-ROM diff): {totalBytes} bytes restored. " +
                        $"{untraceableCount} entr{(untraceableCount == 1 ? "y" : "ies")} could not be traced " +
                        "(EA/$FREEAREA/$GREP); a few hundred bytes of residual data may remain. " +
                        "Use the WinForms patch manager for a complete uninstall.",
                        totalBytes);
                }

                return PatchApplyResult.Ok(
                    $"Patch uninstalled successfully (clean-ROM diff). {totalBytes} bytes restored.",
                    totalBytes);
            }
            catch (Exception ex)
            {
                return PatchApplyResult.Fail("Patch uninstall error: " + ex.Message);
            }
        }

        /// <summary>Process a single BIN: entry.</summary>
        static PatchApplyResult ApplyBinEntry(ROM rom, string patchDir, PatchParam param,
            Dictionary<string, uint> binBlocks, Undo.UndoData? undoData)
        {
            // param.KeyParts: ["BIN", "0x2900"] or ["BIN", "$FREEAREA"] or ["BIN", "$FREEAREA", "1"]
            // param.Value: filename (e.g. "Arena_NotDie.dmp" or "2900.bin")
            string addrPart = param.KeyParts.Length > 1 ? param.KeyParts[1] : "";
            string filename = param.Value;
            string filePath = Path.Combine(patchDir, filename);

            if (!File.Exists(filePath))
                return PatchApplyResult.Fail($"Binary file not found: {filePath}");

            byte[] binData = File.ReadAllBytes(filePath);
            if (binData.Length == 0)
                return PatchApplyResult.Fail($"Binary file is empty: {filePath}");

            uint addr = ResolveBinAddress(rom, addrPart, (uint)binData.Length);
            if (addr == U.NOT_FOUND)
                return PatchApplyResult.Fail($"Could not resolve address '{addrPart}' for {filename}.");

            // Expand ROM if needed
            if (addr + binData.Length > rom.Data.Length)
            {
                bool ok = rom.write_resize_data((uint)(addr + binData.Length));
                if (!ok)
                    return PatchApplyResult.Fail($"Cannot expand ROM to fit data at 0x{addr:X}.");
            }

            // Write data
            if (undoData != null)
                rom.write_range(addr, binData, undoData);
            else
                rom.write_range(addr, binData);

            // Track placement for JUMP resolution
            binBlocks[filePath] = addr;

            return PatchApplyResult.Ok("", binData.Length);
        }

        /// <summary>Process a single JUMP: entry.</summary>
        static PatchApplyResult ApplyJumpEntry(ROM rom, string patchDir, PatchParam param,
            Dictionary<string, uint> binBlocks, Undo.UndoData? undoData)
        {
            // JUMP:0x032984:$r3=Arena_NotDie.dmp
            // KeyParts: ["JUMP", "0x032984", "$r3"]
            // Value: "Arena_NotDie.dmp"
            if (param.KeyParts.Length < 2)
                return PatchApplyResult.Fail("JUMP entry missing address.");

            string addrStr = param.KeyParts[1];
            uint injectionAddr = ParseHexAddress(addrStr);
            if (injectionAddr == U.NOT_FOUND)
                return PatchApplyResult.Fail($"Invalid JUMP injection address: {addrStr}");

            if (injectionAddr % 2 != 0)
                return PatchApplyResult.Fail($"JUMP address 0x{injectionAddr:X} must be even.");

            // Parse register (default r3)
            uint useReg = 3;
            bool isNone = false, isBL = false, isB = false;
            if (param.KeyParts.Length > 2)
            {
                string regStr = param.KeyParts[2];
                if (regStr == "$NONE") isNone = true;
                else if (regStr == "$BL") isBL = true;
                else if (regStr == "$B") isB = true;
                else if (regStr.StartsWith("$r") && regStr.Length >= 3 &&
                         regStr[2] >= '0' && regStr[2] <= '7')
                    useReg = (uint)(regStr[2] - '0');
                else
                    return PatchApplyResult.Fail($"Invalid JUMP register: {regStr}");
            }

            // Parse optional address offset
            int addOffset = 0;
            if (param.KeyParts.Length > 3)
            {
                string offsetStr = param.KeyParts[3];
                if (!string.IsNullOrEmpty(offsetStr))
                {
                    if (offsetStr[0] == '+')
                        addOffset = (int)U.atoi0x(offsetStr.Substring(1));
                    else if (offsetStr[0] == '-')
                        addOffset = -(int)U.atoi0x(offsetStr.Substring(1));
                    else
                        addOffset = (int)U.atoi0x(offsetStr);
                }
            }

            // Resolve jump target: look up where the BIN file was placed
            string filename = param.Value;
            string filePath = Path.Combine(patchDir, filename);
            uint routineAddr;
            if (binBlocks.TryGetValue(filePath, out uint blockAddr))
            {
                routineAddr = (uint)((int)blockAddr + addOffset);
            }
            else
            {
                // Try as a hex address
                routineAddr = ParseHexAddress(filename);
                if (routineAddr == U.NOT_FOUND)
                    return PatchApplyResult.Fail($"JUMP target file '{filename}' was not placed by a BIN entry.");
            }

            // Generate jump code
            byte[] jumpCode;
            if (isNone)
            {
                jumpCode = new byte[4];
                U.write_p32(jumpCode, 0, routineAddr);
            }
            else if (isB)
            {
                jumpCode = DisassemblerTrumb.MakeBJump(injectionAddr, routineAddr);
            }
            else if (isBL)
            {
                jumpCode = DisassemblerTrumb.MakeBLJump(injectionAddr, routineAddr);
            }
            else
            {
                jumpCode = DisassemblerTrumb.MakeInjectJump(injectionAddr, routineAddr, useReg);
            }

            if (jumpCode == null || jumpCode.Length == 0)
                return PatchApplyResult.Fail("Failed to generate jump code.");

            // Write jump code
            if (undoData != null)
                rom.write_range(injectionAddr, jumpCode, undoData);
            else
                rom.write_range(injectionAddr, jumpCode);

            return PatchApplyResult.Ok("", jumpCode.Length);
        }

        /// <summary>
        /// Resolve a BIN address string. Handles "0xADDR" (fixed) and "$FREEAREA" (auto-find).
        /// </summary>
        static uint ResolveBinAddress(ROM rom, string addrStr, uint dataSize)
        {
            if (string.IsNullOrEmpty(addrStr)) return U.NOT_FOUND;

            // $FREEAREA or $FREEAREA:1 etc.
            if (addrStr.StartsWith("$FREEAREA", StringComparison.OrdinalIgnoreCase))
            {
                return FindFreeSpace(rom, dataSize);
            }

            return ParseHexAddress(addrStr);
        }

        /// <summary>Parse a hex address string like "0x2900" or "2900".</summary>
        static uint ParseHexAddress(string addrStr)
        {
            if (string.IsNullOrEmpty(addrStr)) return U.NOT_FOUND;

            string hex = addrStr.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);

            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                return addr;

            return U.NOT_FOUND;
        }

        /// <summary>
        /// Find free space in the ROM for patch data. Searches for runs of 0x00 or 0xFF.
        /// Adds padding for safety (16 bytes lead-in).
        /// </summary>
        public static uint FindFreeSpace(ROM rom, uint dataSize)
        {
            const uint LEAD_IN = 16;
            uint needSize = U.Padding4(LEAD_IN + dataSize);

            // First try the ROM's built-in search
            uint addr = rom.FindFreeSpace(0x100, needSize);
            if (addr != U.NOT_FOUND)
                return addr + LEAD_IN;

            // Fallback: append at end of ROM
            uint endAddr = U.Padding4((uint)rom.Data.Length);
            if (endAddr + dataSize < 0x02000000) // 32MB limit
                return endAddr;

            return U.NOT_FOUND;
        }

        /// <summary>
        /// Pre-scan BIN entries to collect (address, length) regions for backup.
        /// Skips $FREEAREA entries since those write to free space (no valuable data to back up).
        /// </summary>
        static void CollectBinRegions(ROM rom, string patchDir, List<PatchParam> binEntries,
            List<(uint address, int length)> regions)
        {
            foreach (var param in binEntries)
            {
                string addrPart = param.KeyParts.Length > 1 ? param.KeyParts[1] : "";
                string filename = param.Value;
                string filePath = Path.Combine(patchDir, filename);

                // Skip $FREEAREA — those write to empty space, nothing to back up
                if (addrPart.StartsWith("$FREEAREA", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(filePath)) continue;

                uint addr = ParseHexAddress(addrPart);
                if (addr == U.NOT_FOUND) continue;

                int dataLen = (int)new FileInfo(filePath).Length;
                if (dataLen > 0 && addr + dataLen <= rom.Data.Length)
                    regions.Add((addr, dataLen));
            }
        }

        /// <summary>
        /// Pre-scan JUMP entries to collect injection-point regions for backup.
        /// Jump code is typically 4-8 bytes at the injection address.
        /// We use a conservative 8-byte estimate since exact size depends on register choice.
        /// </summary>
        static void CollectJumpRegions(ROM rom, string patchDir, List<PatchParam> jumpEntries,
            List<PatchParam> binEntries, List<(uint address, int length)> regions)
        {
            foreach (var param in jumpEntries)
            {
                if (param.KeyParts.Length < 2) continue;

                uint injectionAddr = ParseHexAddress(param.KeyParts[1]);
                if (injectionAddr == U.NOT_FOUND) continue;

                // Estimate jump code size: $NONE=4, $B/$BL=4, register-based=8
                int jumpSize = 8;
                if (param.KeyParts.Length > 2)
                {
                    string regStr = param.KeyParts[2];
                    if (regStr == "$NONE" || regStr == "$BL" || regStr == "$B")
                        jumpSize = 4;
                }

                if (injectionAddr + jumpSize <= rom.Data.Length)
                    regions.Add((injectionAddr, jumpSize));
            }
        }
    }
}
