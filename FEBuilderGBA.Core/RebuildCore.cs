using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform ROM rebuild logic extracted from WinForms ToolROMRebuildMake/Apply.
    /// Defragments modified ROMs by comparing modified vs vanilla data and reorganizing.
    /// </summary>
    public static class RebuildCore
    {
        /// <summary>Result of a rebuild operation.</summary>
        public class RebuildResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int BlocksMoved { get; set; }
            public int BytesSaved { get; set; }
        }

        /// <summary>
        /// Find pointer references in ROM data. Returns map of offset -> GBA pointer value.
        /// </summary>
        public static Dictionary<uint, uint> FindPointers(byte[] romData)
        {
            var pointers = new Dictionary<uint, uint>();
            if (romData == null || romData.Length < 4) return pointers;

            uint romLen = (uint)romData.Length;
            for (uint i = 0; i + 3 < romLen; i += 4)
            {
                uint val = (uint)(romData[i] | (romData[i + 1] << 8) |
                                  (romData[i + 2] << 16) | (romData[i + 3] << 24));
                if (val >= 0x08000000 && val < 0x08000000 + romLen)
                {
                    pointers[i] = val;
                }
            }
            return pointers;
        }

        /// <summary>
        /// Find regions in modified ROM that differ from vanilla ROM.
        /// Returns list of (offset, length) pairs.
        /// </summary>
        public static List<(uint offset, uint length)> FindModifiedRegions(byte[] vanillaData, byte[] modifiedData)
        {
            var regions = new List<(uint, uint)>();
            if (vanillaData == null || modifiedData == null) return regions;

            uint minLen = (uint)Math.Min(vanillaData.Length, modifiedData.Length);
            uint regionStart = uint.MaxValue;

            for (uint i = 0; i < minLen; i++)
            {
                if (vanillaData[i] != modifiedData[i])
                {
                    if (regionStart == uint.MaxValue)
                        regionStart = i;
                }
                else
                {
                    if (regionStart != uint.MaxValue)
                    {
                        regions.Add((regionStart, i - regionStart));
                        regionStart = uint.MaxValue;
                    }
                }
            }

            if (regionStart != uint.MaxValue)
                regions.Add((regionStart, minLen - regionStart));

            // Handle extended data
            if ((uint)modifiedData.Length > minLen)
                regions.Add((minLen, (uint)modifiedData.Length - minLen));

            return regions;
        }

        /// <summary>
        /// Find free space in ROM (regions filled with 0xFF or 0x00).
        /// </summary>
        /// <param name="romData">ROM byte data</param>
        /// <param name="minSize">Minimum free block size to report</param>
        /// <returns>List of (offset, length) free regions</returns>
        public static List<(uint offset, uint length)> FindFreeSpace(byte[] romData, uint minSize = 16)
        {
            var freeRegions = new List<(uint, uint)>();
            if (romData == null) return freeRegions;

            uint romLen = (uint)romData.Length;
            uint regionStart = uint.MaxValue;
            uint regionLen = 0;

            for (uint i = 0; i < romLen; i++)
            {
                if (romData[i] == 0xFF || romData[i] == 0x00)
                {
                    if (regionStart == uint.MaxValue)
                    {
                        regionStart = i;
                        regionLen = 0;
                    }
                    regionLen++;
                }
                else
                {
                    if (regionStart != uint.MaxValue && regionLen >= minSize)
                    {
                        freeRegions.Add((regionStart, regionLen));
                    }
                    regionStart = uint.MaxValue;
                    regionLen = 0;
                }
            }

            if (regionStart != uint.MaxValue && regionLen >= minSize)
                freeRegions.Add((regionStart, regionLen));

            return freeRegions;
        }

        /// <summary>
        /// Rebuild a modified ROM by comparing against vanilla.
        /// This is a simplified version that identifies modified regions and reports stats.
        /// Full rebuild requires InputFormRef which is WinForms-only.
        /// </summary>
        public static RebuildResult Rebuild(byte[] vanillaData, byte[] modifiedData, IProgress<string> progress = null)
        {
            if (vanillaData == null || modifiedData == null)
            {
                return new RebuildResult { Success = false, Message = "Error: ROM data is null." };
            }

            progress?.Report("Finding modified regions...");
            var modifiedRegions = FindModifiedRegions(vanillaData, modifiedData);

            progress?.Report($"Found {modifiedRegions.Count} modified region(s).");

            progress?.Report("Finding free space...");
            var freeSpace = FindFreeSpace(modifiedData);

            uint totalFree = 0;
            foreach (var (_, length) in freeSpace)
                totalFree += length;

            uint totalModified = 0;
            foreach (var (_, length) in modifiedRegions)
                totalModified += length;

            progress?.Report("Rebuild analysis complete.");

            return new RebuildResult
            {
                Success = true,
                Message = $"Modified: {modifiedRegions.Count} regions ({totalModified} bytes), " +
                          $"Free: {freeSpace.Count} regions ({totalFree} bytes)",
                BlocksMoved = modifiedRegions.Count,
                BytesSaved = (int)totalFree,
            };
        }

        /// <summary>
        /// Build the text of a <c>.rebuild</c> analysis report: a CRC32 of the vanilla
        /// ROM, the requested rebuild address, and the lists of modified regions and free
        /// space found in the modified ROM. The header lines (<c>@_CRC32</c> /
        /// <c>@_REBUILDADDRESS</c>) mirror the WinForms <c>ToolROMRebuildMake</c> output so
        /// the report is recognizable, but this is an analysis/report only — it does NOT
        /// contain the full per-struct rebuild commands the WinForms defragment writes
        /// (that path is WinForms-coupled; see the class summary).
        /// </summary>
        public static string BuildRebuildReport(byte[] vanillaData, byte[] modifiedData, uint rebuildAddress)
        {
            var sb = new StringBuilder();

            uint vanillaCrc = vanillaData == null ? 0u : new U.CRC32().Calc(vanillaData);
            sb.Append("@_CRC32 ").Append(U.ToHexString(vanillaCrc))
              .AppendLine(" //vanilla ROM CRC32");
            sb.Append("@_REBUILDADDRESS ").Append(U.ToHexString(rebuildAddress))
              .AppendLine(" //rebuild start address");

            var modifiedRegions = FindModifiedRegions(vanillaData, modifiedData);
            var freeSpace = FindFreeSpace(modifiedData);

            uint totalModified = 0;
            foreach (var (_, length) in modifiedRegions) totalModified += length;
            uint totalFree = 0;
            foreach (var (_, length) in freeSpace) totalFree += length;

            sb.Append("//MODIFIED REGIONS: ").Append(modifiedRegions.Count)
              .Append(" (").Append(totalModified).AppendLine(" bytes)");
            foreach (var (offset, length) in modifiedRegions)
            {
                sb.Append("@MOD ").Append(U.ToHexString8(offset))
                  .Append(" :").Append(U.ToHexString(length)).AppendLine();
            }

            sb.Append("//FREE SPACE: ").Append(freeSpace.Count)
              .Append(" (").Append(totalFree).AppendLine(" bytes)");
            foreach (var (offset, length) in freeSpace)
            {
                sb.Append("@FREE ").Append(U.ToHexString8(offset))
                  .Append(" :").Append(U.ToHexString(length)).AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Run the analysis and write the report to <paramref name="outputPath"/> fault-safely:
        /// the text is written to a sibling temp file first, then atomically moved into place,
        /// so a failure partway never leaves a corrupt half-written <c>.rebuild</c>. Never
        /// throws — returns a <see cref="RebuildResult"/> whose <see cref="RebuildResult.Success"/>
        /// reports the outcome.
        /// </summary>
        public static RebuildResult WriteRebuildReport(byte[] vanillaData, byte[] modifiedData,
            uint rebuildAddress, string outputPath, IProgress<string> progress = null)
        {
            if (vanillaData == null || modifiedData == null)
            {
                return new RebuildResult { Success = false, Message = "Error: ROM data is null." };
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                return new RebuildResult { Success = false, Message = "Error: output path is empty." };
            }

            RebuildResult analysis = Rebuild(vanillaData, modifiedData, progress);
            if (!analysis.Success)
            {
                return analysis;
            }

            progress?.Report("Writing report...");

            string tempPath = outputPath + ".tmp";
            try
            {
                string report = BuildRebuildReport(vanillaData, modifiedData, rebuildAddress);
                File.WriteAllText(tempPath, report);
                // Atomic-ish replace: remove any stale target, then move the fully-written temp.
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                File.Move(tempPath, outputPath);
            }
            catch (Exception ex)
            {
                // Clean up the half-written temp so no corrupt artifact is left behind.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return new RebuildResult { Success = false, Message = "Error: could not write report. " + ex.Message };
            }

            progress?.Report("Report written.");
            analysis.Message = analysis.Message + " -> " + outputPath;
            return analysis;
        }
    }
}
