using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform text translation: dump and import ROM text entries.
    /// Text entries are stored in a pointer table at text_pointer.
    /// Each entry is 4 bytes (a pointer to Huffman-compressed text data).
    /// </summary>
    public static class TranslateCore
    {
        /// <summary>
        /// Count the number of text entries in the ROM by scanning the text pointer table.
        /// </summary>
        public static uint GetTextCount(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;

            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(textBase, rom))
            {
                textBase = rom.RomInfo.text_recover_address;
            }
            if (!U.isSafetyOffset(textBase, rom)) return 0;

            // Scan the pointer table: each entry is 4 bytes, valid if it's a pointer
            // or an un-huffman patch pointer or a RAM pointer
            uint count = rom.getBlockDataCount(textBase, 4, (int i, uint addr) =>
            {
                uint p = rom.u32(addr);
                if (U.isPointer(p)) return true;
                if (FETextEncode.IsUnHuffmanPatchPointer(p)) return true;
                if (FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(p)) return true;
                if (FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(p)) return true;
                if (U.is_03RAMPointer(p)) return true;
                if (U.is_02RAMPointer(p)) return true;
                return false;
            });

            return count;
        }

        /// <summary>
        /// Dump all text entries from the ROM as (textId, decodedText) pairs.
        /// </summary>
        public static List<(uint textId, string text)> DumpTexts(ROM rom)
        {
            var result = new List<(uint, string)>();
            if (rom?.RomInfo == null) return result;

            uint textCount = GetTextCount(rom);
            if (textCount == 0) return result;

            // Cap at a reasonable limit to prevent runaway scanning
            if (textCount > 0xFFFF) textCount = 0xFFFF;

            for (uint i = 0; i < textCount; i++)
            {
                try
                {
                    string text = FETextDecode.Direct(i);
                    result.Add((i, text ?? ""));
                }
                catch
                {
                    result.Add((i, ""));
                }
            }
            return result;
        }

        /// <summary>
        /// Export text entries to a TSV file (textId \t text).
        /// </summary>
        public static void ExportToTSV(List<(uint textId, string text)> entries, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ID\tText");
            foreach (var (id, text) in entries)
            {
                string escaped = text.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
                sb.AppendLine($"{id}\t{escaped}");
            }
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Import text entries from a TSV file.
        /// Returns list of (textId, newText) pairs.
        /// </summary>
        public static List<(uint textId, string text)> ImportFromTSV(string inputPath)
        {
            var result = new List<(uint, string)>();
            if (!File.Exists(inputPath)) return result;

            string[] lines = File.ReadAllLines(inputPath, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++) // skip header
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                int tab = line.IndexOf('\t');
                if (tab < 0) continue;

                string idStr = line.Substring(0, tab).Trim();
                string text = line.Substring(tab + 1);
                text = text.Replace("\\n", "\n").Replace("\\t", "\t");

                if (uint.TryParse(idStr, out uint textId))
                {
                    result.Add((textId, text));
                }
            }
            return result;
        }

        /// <summary>
        /// Write translated text entries back to ROM.
        /// Returns the number of entries successfully written.
        ///
        /// Strategy:
        /// 1. Encode text via Huffman (or UnHuffman fallback if char not in dictionary)
        /// 2. If encoded data fits in the original space, overwrite in place
        /// 3. If too large, append to ROM end and update pointer
        /// </summary>
        public static int WriteTexts(ROM rom, List<(uint textId, string text)> entries)
        {
            if (rom?.RomInfo == null) return 0;
            if (CoreState.FETextEncoder == null) return 0;

            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(textBase, rom))
            {
                textBase = rom.RomInfo.text_recover_address;
            }
            if (!U.isSafetyOffset(textBase, rom)) return 0;

            uint textCount = GetTextCount(rom);
            if (textCount == 0) return 0;

            int written = 0;
            var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);

            foreach (var (textId, text) in entries)
            {
                if (textId >= textCount) continue;

                uint writePointer = textBase + (textId * 4);
                if (!U.isSafetyOffset(writePointer, rom)) continue;

                // Encode the text
                byte[] encoded;
                bool useUnHuffman = false;
                string error = CoreState.FETextEncoder.Encode(text, out encoded);
                if (error != null && error.Length > 0)
                {
                    // Huffman encoding failed — try UnHuffman fallback
                    CoreState.FETextEncoder.UnHuffmanEncode(text, out encoded);
                    useUnHuffman = true;
                }
                if (encoded == null || encoded.Length == 0)
                {
                    // Empty text: point to same as text ID 0
                    uint text0Pointer = rom.u32(textBase);
                    rom.write_u32(writePointer, text0Pointer);
                    written++;
                    continue;
                }

                // Get original data size by decoding current text
                uint currentPointerValue = rom.u32(writePointer);
                uint originalSize = 0;
                bool currentIsUnHuffman = FETextEncode.IsUnHuffmanPatchPointer(currentPointerValue);
                uint currentDataAddr;
                if (currentIsUnHuffman)
                {
                    currentDataAddr = U.toOffset(FETextEncode.ConvertUnHuffmanPatchToPointer(currentPointerValue));
                }
                else if (U.isPointer(currentPointerValue))
                {
                    currentDataAddr = U.toOffset(currentPointerValue);
                }
                else
                {
                    currentDataAddr = 0;
                }

                if (currentDataAddr > 0 && U.isSafetyOffset(currentDataAddr, rom))
                {
                    // Decode to get data size
                    try
                    {
                        int dataSize;
                        if (currentIsUnHuffman)
                            decoder.UnHffmanPatchDecode(currentDataAddr, out dataSize);
                        else
                            decoder.Decode(textId, out dataSize);
                        originalSize = (uint)Math.Max(0, dataSize);
                    }
                    catch
                    {
                        originalSize = 0;
                    }
                }

                if (originalSize > 20000)
                {
                    // Suspiciously large — skip reuse
                    originalSize = 0;
                    currentDataAddr = 0;
                }

                if (currentDataAddr > 0 && originalSize >= (uint)encoded.Length)
                {
                    // Fits in original space — overwrite in place
                    rom.write_range(currentDataAddr, encoded);
                    // Zero-fill remaining space
                    if (originalSize > (uint)encoded.Length)
                    {
                        rom.write_fill(currentDataAddr + (uint)encoded.Length,
                            originalSize - (uint)encoded.Length, 0x00);
                    }
                    // Update pointer (may need to switch huffman/unhuffman flag)
                    if (useUnHuffman)
                        rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(currentDataAddr)));
                    else
                        rom.write_u32(writePointer, U.toPointer(currentDataAddr));
                }
                else
                {
                    // Need new space — append to ROM end
                    uint paddedSize = U.Padding4((uint)encoded.Length) + 4;
                    uint newAddr = U.Padding4((uint)rom.Data.Length);

                    if (newAddr + paddedSize >= 0x02000000)
                    {
                        // Would exceed 32MB limit — skip
                        continue;
                    }

                    // Resize ROM to accommodate new data
                    rom.write_resize_data(newAddr + paddedSize);

                    // Clear old data if we had a valid address
                    if (currentDataAddr > 0 && originalSize > 0 && U.isSafetyOffset(currentDataAddr, rom))
                    {
                        rom.write_fill(currentDataAddr, originalSize, 0x00);
                    }

                    // Write encoded text at new address
                    rom.write_range(newAddr, encoded);

                    // Update pointer
                    if (useUnHuffman)
                        rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(newAddr)));
                    else
                        rom.write_p32(writePointer, newAddr);
                }

                written++;
            }

            return written;
        }
    }
}
