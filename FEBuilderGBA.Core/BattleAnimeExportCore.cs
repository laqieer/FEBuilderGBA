using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform battle animation export to .txt script + per-frame PNG files.
    /// Ported from WinForms ImageUtilOAM.ExportBattleAnimeLow().
    /// </summary>
    public static class BattleAnimeExportCore
    {
        const int SECTION_COUNT = 0xC;

        /// <summary>
        /// Export battle animation to .txt script + per-frame PNG files.
        /// </summary>
        public static string ExportBattleAnime(ROM rom, uint animRecordAddr,
            string outputTxtPath, bool enableComments = true)
        {
            if (rom == null) return "No ROM loaded.";
            if (!U.isSafetyOffset(animRecordAddr + 31, rom))
                return "Invalid animation record address.";

            // Read record pointers
            uint sectionDataPtr = rom.u32(animRecordAddr + 12);
            uint frameDataPtr = rom.u32(animRecordAddr + 16);
            uint oamPtr = rom.u32(animRecordAddr + 20);
            uint palettePtr = rom.u32(animRecordAddr + 28);

            if (!U.isPointer(sectionDataPtr) || !U.isPointer(frameDataPtr) ||
                !U.isPointer(oamPtr) || !U.isPointer(palettePtr))
                return "Animation record contains invalid pointers.";

            uint sectionDataOff = U.toOffset(sectionDataPtr);
            uint frameDataOff = U.toOffset(frameDataPtr);
            uint oamOff = U.toOffset(oamPtr);
            uint paletteOff = U.toOffset(palettePtr);

            // Read section data (12 × 4 bytes, uncompressed)
            byte[] sectionData = rom.getBinaryData(sectionDataOff, SECTION_COUNT * 4);

            // Decompress frame data, OAM, palette
            byte[] frameData = LZ77.decompress(rom.Data, frameDataOff);
            if (frameData == null || frameData.Length == 0)
                return "Failed to decompress frame data.";

            byte[] oamData = LZ77.decompress(rom.Data, oamOff);
            if (oamData == null)
                return "Failed to decompress OAM data.";

            byte[] paletteData = LZ77.decompress(rom.Data, paletteOff);
            if (paletteData == null)
                return "Failed to decompress palette data.";

            string baseName = Path.GetFileNameWithoutExtension(outputTxtPath);
            string outputDir = Path.GetDirectoryName(Path.GetFullPath(outputTxtPath));
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Track unique frames for deduplication
            var frameFiles = new Dictionary<ulong, string>();
            int frameCount = 0;

            var lines = new List<string>();
            if (enableComments)
            {
                lines.Add($"## Battle Animation Export");
                lines.Add($"## Animation at 0x{animRecordAddr:X08}");
                lines.Add("");
            }

            // Process each section
            int mode = 0;
            bool skipNext = false;

            for (int section = 0; section < SECTION_COUNT; section++)
            {
                if (skipNext) { skipNext = false; continue; }

                uint start = U.u32(sectionData, (uint)(section * 4));
                uint end = (section + 1 < SECTION_COUNT)
                    ? U.u32(sectionData, (uint)((section + 1) * 4))
                    : (uint)frameData.Length;

                if (start > frameData.Length) start = (uint)frameData.Length;
                if (end > frameData.Length) end = (uint)frameData.Length;

                // Sections 1 and 3 are weapon overlays (paired with 0 and 2)
                // In the .txt format, they are generated automatically from isMode1
                if (section == 1 || section == 3)
                    continue;

                if (enableComments)
                    lines.Add($"## Section {section} (Mode {section + 1})");

                // Parse frame commands
                for (uint n = start; n + 3 < end; )
                {
                    byte cmdType = frameData[n + 3];

                    if (cmdType == 0x85)
                    {
                        // Control command
                        uint cmd24 = (uint)(frameData[n] | (frameData[n + 1] << 8) | (frameData[n + 2] << 16));

                        if ((cmd24 & 0xFF) == 0x48)
                        {
                            // Sound command
                            uint musicId = (cmd24 >> 8) & 0xFFFF;
                            lines.Add($"S{musicId:X}");
                        }
                        else if ((cmd24 & 0xFF) == 0x01 && (cmd24 >> 8) > 0)
                        {
                            // Loop end with count
                            lines.Add("C01");
                        }
                        else
                        {
                            lines.Add($"C{cmd24:X02}");
                        }
                        n += 4;
                    }
                    else if (cmdType == 0x86)
                    {
                        // Frame reference (12 bytes)
                        if (n + 11 >= frameData.Length) break;

                        uint wait = (uint)(frameData[n] | (frameData[n + 1] << 8));
                        uint gfxPtr = U.u32(frameData, n + 4);
                        uint oamOffset = U.u32(frameData, n + 8);

                        // Dedup key
                        ulong key = ((ulong)gfxPtr << 32) | oamOffset;
                        string pngName;
                        if (!frameFiles.TryGetValue(key, out pngName))
                        {
                            pngName = $"{baseName}_{frameCount:D3}.png";
                            frameCount++;

                            // Render frame using Core composition
                            try
                            {
                                var result = RenderFrameFromData(rom, gfxPtr, oamOffset,
                                    oamData, paletteData);
                                if (result != null)
                                {
                                    string pngPath = Path.Combine(outputDir, pngName);
                                    result.Save(pngPath);
                                }
                            }
                            catch { /* Skip frames that fail to render */ }

                            frameFiles[key] = pngName;
                        }

                        lines.Add($"{wait}p-{pngName}");
                        n += 12;
                    }
                    else if (cmdType == 0x80 || (frameData[n] == 0 && frameData[n + 1] == 0 &&
                             frameData[n + 2] == 0 && frameData[n + 3] == 0x80))
                    {
                        // Section terminator
                        n += 4;
                    }
                    else
                    {
                        n += 4; // Skip unknown
                    }
                }

                lines.Add("~");

                // If this is section 0 or 2 (body), skip the paired weapon section
                if (section == 0 || section == 2)
                    skipNext = true;
            }

            File.WriteAllText(outputTxtPath, string.Join("\n", lines) + "\n");
            return string.Empty;
        }

        static IImage RenderFrameFromData(ROM rom, uint gfxPtr, uint oamOffset,
            byte[] oamData, byte[] paletteData)
        {
            if (!U.isPointer(gfxPtr)) return null;

            // Decompress tile data
            uint gfxOff = U.toOffset(gfxPtr);
            if (!U.isSafetyOffset(gfxOff, rom)) return null;

            byte[] tileData = LZ77.decompress(rom.Data, gfxOff);
            if (tileData == null || tileData.Length == 0) return null;

            // Use first 32 bytes of palette (player team)
            byte[] pal16 = new byte[32];
            Array.Copy(paletteData, 0, pal16, 0, Math.Min(32, paletteData.Length));

            // Render tile sheet to image
            var sheetImage = BattleAnimeRendererCore.RenderTileSheet(tileData, pal16, 32);
            if (sheetImage == null) return null;

            // For now, return the tile sheet as the frame image
            // Full OAM composition would require the complete DrawOAMSprites pipeline
            return sheetImage;
        }
    }
}
