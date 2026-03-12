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

            try
            {
                // Process BIN entries first, then JUMP entries
                var binEntries = actionParams.Where(p => binKeywords.Contains(p.Keyword)).ToList();
                var jumpEntries = actionParams.Where(p => p.Keyword == "JUMP").ToList();

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
        /// Uninstall is not supported in the cross-platform port.
        /// Uninstallation requires the original (vanilla) ROM for reference,
        /// which is a WinForms-only feature.
        /// </summary>
        public static PatchApplyResult UninstallPatch(ROM rom, string patchFilePath)
        {
            return PatchApplyResult.Fail(
                "Patch uninstallation is not yet supported in the Avalonia port. " +
                "Uninstalling patches requires a clean (vanilla) ROM for reference. " +
                "Use the WinForms version for uninstallation, or restore from a backup.");
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
    }
}
