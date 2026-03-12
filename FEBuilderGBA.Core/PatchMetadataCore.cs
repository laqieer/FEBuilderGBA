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
    }
}
