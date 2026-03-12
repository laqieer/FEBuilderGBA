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
        /// Enumerate all patch directories for a given ROM version and parse metadata.
        /// </summary>
        /// <param name="patchBaseDir">The config/patch2/{version} directory.</param>
        /// <param name="rom">Current ROM for installation detection.</param>
        /// <param name="lang">Language suffix ("en", "zh", or "" for Japanese).</param>
        /// <returns>List of parsed patches, sorted by directory name.</returns>
        public static List<PatchInfo> EnumeratePatches(string patchBaseDir, ROM rom, string lang)
        {
            var result = new List<PatchInfo>();
            if (!Directory.Exists(patchBaseDir))
                return result;

            var dirs = Directory.GetDirectories(patchBaseDir);
            foreach (string dir in dirs.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                string dirName = Path.GetFileName(dir);
                var patchFiles = Directory.GetFiles(dir, "PATCH_*.txt");
                if (patchFiles.Length == 0)
                    continue;

                var info = ParsePatchFile(patchFiles[0], dirName, rom, lang);
                result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// Parse a PATCH_*.txt metadata file.
        /// </summary>
        public static PatchInfo ParsePatchFile(string patchFilePath, string dirName, ROM rom, string lang)
        {
            var info = new PatchInfo
            {
                Name = dirName,
                DirectoryName = dirName,
                DirectoryPath = Path.GetDirectoryName(patchFilePath) ?? "",
                PatchFilePath = patchFilePath,
            };

            try
            {
                string[] lines = File.ReadAllLines(patchFilePath);
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
                    info.Status = CheckPatchInstalled(patchedIf, rom);

                // Check dependencies (IF: lines)
                var allDeps = GetPatchDependencies(patchFilePath, lang);
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
            }
            catch (Exception ex)
            {
                Log.Error("PatchMetadataCore: Failed to parse {0}: {1}", patchFilePath, ex.Message);
            }

            return info;
        }

        /// <summary>
        /// Check if a patch is installed by evaluating a PATCHED_IF condition string.
        /// Supports fixed-address checks (0xADDR=0xBB 0xBB ...).
        /// Returns Unknown for GREP-style conditions.
        /// </summary>
        public static PatchStatus CheckPatchInstalled(string condition, ROM rom)
        {
            try
            {
                if (condition.Contains("$GREP", StringComparison.OrdinalIgnoreCase) ||
                    condition.Contains("$FGREP", StringComparison.OrdinalIgnoreCase))
                {
                    return PatchStatus.Unknown;
                }

                int eqIdx = condition.IndexOf('=');
                if (eqIdx < 0) return PatchStatus.Unknown;

                string addrStr = condition.Substring(0, eqIdx).Trim();
                string dataStr = condition.Substring(eqIdx + 1).Trim();

                if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addrStr = addrStr.Substring(2);
                if (!uint.TryParse(addrStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                    return PatchStatus.Unknown;

                byte[] expected = ParseByteArray(dataStr);
                if (expected.Length == 0) return PatchStatus.Unknown;

                if (addr + expected.Length > rom.Data.Length)
                    return PatchStatus.NotInstalled;

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
            var result = new List<PatchDependency>();
            if (!File.Exists(patchFilePath)) return result;

            try
            {
                string[] lines = File.ReadAllLines(patchFilePath);
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

                    // IF: lines use colon-separated key format: IF:address=bytes
                    if (!line.StartsWith("IF:", StringComparison.OrdinalIgnoreCase)) continue;

                    // Extract the condition part after "IF:"
                    string condition = line.Substring(3).Trim();

                    // Strip trailing inline comments (e.g. "//need Anti-Huffman")
                    int commentIdx = condition.IndexOf("//");
                    string inlineComment = "";
                    if (commentIdx >= 0)
                    {
                        inlineComment = condition.Substring(commentIdx + 2).Trim();
                        condition = condition.Substring(0, commentIdx).Trim();
                    }

                    // Use inline comment if IF_COMMENT is not present
                    string depComment = !string.IsNullOrEmpty(resolvedComment) ? resolvedComment
                        : !string.IsNullOrEmpty(inlineComment) ? inlineComment : "";

                    result.Add(new PatchDependency
                    {
                        Condition = condition,
                        Comment = depComment,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("PatchMetadataCore.GetPatchDependencies: {0}: {1}", patchFilePath, ex.Message);
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
        /// The backup file is created during patch installation.
        /// </summary>
        public static PatchApplyResult UninstallPatch(ROM rom, string patchFilePath)
        {
            if (rom == null) return PatchApplyResult.Fail("No ROM loaded.");
            if (!File.Exists(patchFilePath)) return PatchApplyResult.Fail("Patch file not found.");

            string backupPath = GetBackupFilePath(patchFilePath);
            if (!File.Exists(backupPath))
            {
                return PatchApplyResult.Fail(
                    "No backup file found for this patch. Uninstall is only possible for patches " +
                    "installed through the Avalonia port (which saves a backup automatically). " +
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
                    rom.write_range(address, data);
                    totalBytes += data.Length;
                }

                // Delete backup file after successful restore
                File.Delete(backupPath);

                return PatchApplyResult.Ok(
                    $"Patch uninstalled successfully. {totalBytes} bytes restored.",
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
