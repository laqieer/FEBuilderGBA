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
        /// TODO: FETextEncode.Encode() returns encoded bytes via out parameter, but
        /// writing them back to the ROM text table requires updating the pointer table
        /// and finding free space for the new encoded data. This is complex logic that
        /// lives in the WinForms TextForm.WriteText() method and depends on InputFormRef
        /// for free-space allocation. A full implementation would need to extract that
        /// free-space allocation logic to Core first.
        /// </summary>
        public static int WriteTexts(ROM rom, List<(uint textId, string text)> entries)
        {
            if (rom?.RomInfo == null) return 0;
            if (CoreState.FETextEncoder == null) return 0;

            // TODO: Writing text back to ROM requires:
            // 1. Encode text via FETextEncode.Encode() or UnHuffmanEncode()
            // 2. Find free space in ROM for the encoded data
            // 3. Update the pointer in the text table
            // This logic is currently tied to WinForms InputFormRef free-space management.
            // For now, this is a no-op placeholder.
            return 0;
        }
    }
}
