// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform execution paths for the ToolTranslateROM dialog (#536).
//
// Carved out from the WinForms `ToolTranslateROM.cs` / `ToolTranslateROMFont.cs` /
// `ToolTranslateROMWipeJP*.cs` / `MainFormUtil.FindOrignalROMByLang` so the
// Avalonia view's deferred buttons can wire to real Core execution without
// pulling in `System.Windows.Forms.Form`, `System.Drawing.Bitmap`, or
// `InputFormRef.AutoPleaseWait`.
//
// What's in this file:
//  - GetROMBaseTable / FindOrignalROMByLang / FindOrignalROMByCRC32 - ROM file
//    discovery helpers; no Form / OptionForm dependency.
//  - MakeROMName(version, isMultibyte) - single source-of-truth FROM/TO label
//    pair used by both the WinForms designer and the Avalonia ViewModel.
//  - ParseLanguageKey(text) - wraps `U.InnerSplit(text, "=", 0)` for the WF
//    combo `lang=Name` parsing.
//  - ApplyTranslatePatch / ChangeMainMenuWidth / ChangeStatusScreenSkill -
//    ROM byte writes that adjust menu width and status-screen skill table
//    based on the target language.
//  - ExportTextsToFile - dumps text IDs (plus multibyte menu/terrain/sound-room
//    /other-text pointer entries) to a `[XXXX]\ntext\n\n` formatted file.
//  - ImportTextsFromFile - reads `[XXXX]\ntext\n\n` and writes back via
//    Huffman/UnHuffman/CString depending on the entry kind.
//  - ApplyAITrTranslateMenu / ApplyAITrTranslateStatus - the inner pieces
//    matching `ToolTranslateROM.ApplyTranslatePatch`.
//
// What's intentionally NOT here (KnownGap, documented in PR Known Limitations):
//  - Bitmap font auto-generation (`ImageUtil.AutoGenerateFont` is
//    System.Drawing-bound; lives in WinForms).
//  - WipeJPFont / WipeJPTitle / WipeJPClassReelFont orchestration (depend on
//    WF `HowDoYouLikePatchForm` dialog popups + WF `FontForm` static methods
//    that haven't been migrated yet). The Core PriorityCode + FontCore helpers
//    *are* now in Core (see PriorityCode.cs / FontCore.cs), so a follow-up PR
//    can wire WipeJP via delegate hooks once `HowDoYouLikePatchForm` is
//    abstracted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Core helper for the ROM translation tool dialog.
    /// Hosts the path-lookup, label, language-key, translate-patch, and
    /// text export/import drivers that were previously WinForms-only.
    /// </summary>
    public static class ToolTranslateROMCore
    {
        // ============================================================
        // ROMBaseTableSt - port from MainFormUtil.cs (#536)
        // ============================================================

        /// <summary>
        /// Identifies an unmodified base ROM by name, version, language,
        /// internal header string, file size, and CRC32. Used by
        /// FindOrignalROMByLang / FindOrignalROMByCRC32 to locate the
        /// reference ROM file for translation.
        /// </summary>
        public struct ROMBaseTableSt
        {
            public string name;
            public uint ver;
            public string lang;
            public string header;
            public uint romsize;
            public uint crc32;
        }

        /// <summary>
        /// The 10 known unmodified-ROM entries (FE6 / FE7 / FE8 J/U/CN/KR).
        /// Mirrors WF `MainFormUtil.GetROMBaseTable()`.
        /// </summary>
        public static ROMBaseTableSt[] GetROMBaseTable()
        {
            return new ROMBaseTableSt[]
            {
                new ROMBaseTableSt{ name="FE6J",  ver=6, lang="ja", header="AFEJ01", romsize=0x800000,  crc32=0xd38763e1 },
                new ROMBaseTableSt{ name="FE6U",  ver=6, lang="en", header="AFEJ01", romsize=0x1000000, crc32=0x35F5B06B },
                new ROMBaseTableSt{ name="FE6CN", ver=6, lang="zh", header="AFEJ01", romsize=0x800000,  crc32=0x1F19D989 },
                new ROMBaseTableSt{ name="FE7J",  ver=7, lang="ja", header="AE7J01", romsize=0x1000000, crc32=0xf0c10e72 },
                new ROMBaseTableSt{ name="FE7U",  ver=7, lang="en", header="AE7E01", romsize=0x1000000, crc32=0x2a524221 },
                new ROMBaseTableSt{ name="FE7CN", ver=7, lang="zh", header="AE7J01", romsize=0x1000000, crc32=0x5F286460 },
                new ROMBaseTableSt{ name="FE8J",  ver=8, lang="ja", header="BE8J01", romsize=0x1000000, crc32=0x9d76826f },
                new ROMBaseTableSt{ name="FE8U",  ver=8, lang="en", header="BE8E01", romsize=0x1000000, crc32=0xa47246ae },
                new ROMBaseTableSt{ name="FE8CN", ver=8, lang="zh", header="BE8J01", romsize=0x1000000, crc32=0x79609D14 },
                new ROMBaseTableSt{ name="FE8KR", ver=8, lang="kr", header="BE8J01", romsize=0x1193960, crc32=0x4F33E94C },
            };
        }

        // ============================================================
        // Find original-ROM helpers - port from MainFormUtil.cs (#536)
        // ============================================================

        static bool IsBackupOrEmulatorFilename(string filename)
        {
            return filename.IndexOf(".backup.")          >= 0
                || filename.IndexOf(".emulator.")        >= 0
                || filename.IndexOf(".emulator2.")       >= 0
                || filename.IndexOf(".sappy.")           >= 0
                || filename.IndexOf(".binary_editor.")   >= 0;
        }

        static string FindOrignalROMLowByLang(string dir, string lang, int currentVersion,
            SearchOption searchOption)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return string.Empty;
            if (lang == "zh-CH") lang = "zh";

            string orignalHeader = string.Empty;
            uint orignalSize = 0;
            uint orignalCrc32 = 0;

            foreach (ROMBaseTableSt t in GetROMBaseTable())
            {
                if (currentVersion != 0 && currentVersion != t.ver) continue;
                if (!string.IsNullOrEmpty(lang) && lang != t.lang) continue;
                orignalHeader = t.header;
                orignalSize = t.romsize;
                orignalCrc32 = t.crc32;
            }

            if (orignalSize == 0 || orignalCrc32 == 0) return string.Empty;

            U.CRC32 crc32 = new U.CRC32();
            string[] files;
            try
            {
                files = U.Directory_GetFiles_Safe(dir, "*.gba", searchOption);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            foreach (string filepath in files)
            {
                string filename = Path.GetFileName(filepath);
                if (IsBackupOrEmulatorFilename(filename)) continue;
                if (U.GetFileSize(filepath) != orignalSize) continue;

                byte[] file;
                try
                {
                    file = File.ReadAllBytes(filepath);
                }
                catch (System.UnauthorizedAccessException) { continue; }
                catch (System.IO.IOException) { continue; }

                if (U.getASCIIString(file, 0xAC, 6) != orignalHeader) continue;
                if (crc32.Calc(file) != orignalCrc32) continue;
                return filepath;
            }
            return string.Empty;
        }

        /// <summary>
        /// Find an unmodified ROM matching the given language. Searches in
        /// (1) currentDir, (2) romBaseDirectory, (3) directory of lastROMFilename,
        /// (4) currentDir recursively. Returns empty string when none found.
        /// </summary>
        public static string FindOrignalROMByLang(string currentDir, string lang,
            int currentVersion, string romBaseDirectory, string lastROMFilename)
        {
            var searched = new List<string>();
            string result;

            // Pass 1: currentDir (top-level only).
            result = FindOrignalROMLowByLang(currentDir, lang, currentVersion, SearchOption.TopDirectoryOnly);
            searched.Add(currentDir);
            if (!string.IsNullOrEmpty(result)) return result;

            // Pass 2: romBaseDirectory (FEBuilderGBA install dir, top-level).
            if (!string.IsNullOrEmpty(romBaseDirectory) && !searched.Contains(romBaseDirectory))
            {
                result = FindOrignalROMLowByLang(romBaseDirectory, lang, currentVersion, SearchOption.TopDirectoryOnly);
                searched.Add(romBaseDirectory);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            // Pass 3: directory of lastROMFilename (top-level).
            if (!string.IsNullOrEmpty(lastROMFilename))
            {
                string lastDir = Path.GetDirectoryName(lastROMFilename) ?? string.Empty;
                if (!string.IsNullOrEmpty(lastDir) && !searched.Contains(lastDir))
                {
                    result = FindOrignalROMLowByLang(lastDir, lang, currentVersion, SearchOption.TopDirectoryOnly);
                    searched.Add(lastDir);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }

            // Pass 4: currentDir recursive.
            return FindOrignalROMLowByLang(currentDir, lang, currentVersion, SearchOption.AllDirectories);
        }

        static string FindOrignalROMLowByCrc(string dir, uint searchCrc, SearchOption searchOption)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return string.Empty;

            string orignalHeader = string.Empty;
            uint orignalSize = 0;
            foreach (ROMBaseTableSt t in GetROMBaseTable())
            {
                if (t.crc32 != searchCrc) continue;
                orignalHeader = t.header;
                orignalSize = t.romsize;
                break;
            }
            if (orignalSize == 0) return string.Empty;

            U.CRC32 crc32 = new U.CRC32();
            string[] files;
            try
            {
                files = U.Directory_GetFiles_Safe(dir, "*.gba", searchOption);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            foreach (string filepath in files)
            {
                string filename = Path.GetFileName(filepath);
                if (IsBackupOrEmulatorFilename(filename)) continue;
                if (U.GetFileSize(filepath) != orignalSize) continue;

                byte[] file;
                try
                {
                    file = File.ReadAllBytes(filepath);
                }
                catch (System.UnauthorizedAccessException) { continue; }
                catch (System.IO.IOException) { continue; }

                if (U.getASCIIString(file, 0xAC, 6) != orignalHeader) continue;
                if (crc32.Calc(file) != searchCrc) continue;
                return filepath;
            }
            return string.Empty;
        }

        /// <summary>Find an unmodified ROM matching the given CRC32.</summary>
        public static string FindOrignalROMByCRC32(string currentDir, uint searchCrc,
            string romBaseDirectory, string lastROMFilename)
        {
            var searched = new List<string>();
            string result;

            result = FindOrignalROMLowByCrc(currentDir, searchCrc, SearchOption.TopDirectoryOnly);
            searched.Add(currentDir);
            if (!string.IsNullOrEmpty(result)) return result;

            if (!string.IsNullOrEmpty(romBaseDirectory) && !searched.Contains(romBaseDirectory))
            {
                result = FindOrignalROMLowByCrc(romBaseDirectory, searchCrc, SearchOption.TopDirectoryOnly);
                searched.Add(romBaseDirectory);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            if (!string.IsNullOrEmpty(lastROMFilename))
            {
                string lastDir = Path.GetDirectoryName(lastROMFilename) ?? string.Empty;
                if (!string.IsNullOrEmpty(lastDir) && !searched.Contains(lastDir))
                {
                    result = FindOrignalROMLowByCrc(lastDir, searchCrc, SearchOption.TopDirectoryOnly);
                    searched.Add(lastDir);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }

            return FindOrignalROMLowByCrc(currentDir, searchCrc, SearchOption.AllDirectories);
        }

        // ============================================================
        // MakeROMName - port from ToolTranslateROMForm.cs (#536)
        // ============================================================

        /// <summary>
        /// Return the FROM/TO ROM label pair for the given ROM version +
        /// multibyte flag. Mirrors WF `ToolTranslateROMForm.MakeROMName()`.
        /// Returns ("","") for unrecognised combinations (e.g. FE6 / version 0).
        /// </summary>
        public static (string fromLabel, string toLabel) MakeROMName(int version, bool isMultibyte)
        {
            if (version == 8)
            {
                return isMultibyte ? ("無改造 FE8J", "無改造 FE8U")
                                   : ("無改造 FE8U", "無改造 FE8J");
            }
            if (version == 7)
            {
                return isMultibyte ? ("無改造 FE7U", "無改造 FE7J")
                                   : ("無改造 FE7J", "無改造 FE7U");
            }
            return (string.Empty, string.Empty);
        }

        // ============================================================
        // Language combo parsing
        // ============================================================

        /// <summary>
        /// Extract the language key from a `lang=Name` combo item string.
        /// Wraps WF `U.InnerSplit(text, "=", 0)`. Returns the entire input
        /// unchanged when no `=` is present.
        /// </summary>
        public static string ParseLanguageKey(string comboItemText)
        {
            if (string.IsNullOrEmpty(comboItemText)) return string.Empty;
            return U.InnerSplit(comboItemText, "=", 0);
        }

        // ============================================================
        // Translate-patch helpers - port from ToolTranslateROM.cs (#536)
        // ============================================================

        /// <summary>
        /// Adjust the main-menu cell width based on the target language.
        /// CJK languages (ja / zh / ko) need a 6-cell minimum; non-CJK
        /// languages get 8 cells. Mirrors WF
        /// `ToolTranslateROM.ChangeMainMenuWidth(string to)`.
        /// </summary>
        public static void ChangeMainMenuWidth(ROM rom, string to, Undo.UndoData undo)
        {
            if (rom?.RomInfo == null) return;
            uint addr = rom.RomInfo.main_menu_width_address;
            if (!U.isSafetyOffset(addr, rom)) return;

            uint length = rom.u8(addr);
            if (to == "ja" || to == "zh" || to == "ko")
            {
                if (length <= 6)
                {
                    if (undo != null) rom.write_u8(addr, 6, undo);
                    else rom.write_u8(addr, 6);
                }
            }
            else
            {
                if (length <= 8)
                {
                    if (undo != null) rom.write_u8(addr, 8, undo);
                    else rom.write_u8(addr, 8);
                }
            }
        }

        /// <summary>
        /// Reset the status-screen skill cell length when targeting a non-CJK
        /// language. Mirrors WF `ToolTranslateROM.ChangeStatusScreenSkill(string to)`.
        /// </summary>
        public static void ChangeStatusScreenSkill(ROM rom, string to, Undo.UndoData undo)
        {
            if (rom?.RomInfo == null) return;
            uint statusBase = rom.p32(rom.RomInfo.status_param1_pointer);
            if (!U.isSafetyOffset(statusBase, rom)) return;

            uint length = rom.u8(statusBase + 0x09);
            if (to == "ja" || to == "zh" || to == "ko")
            {
                // CJK: keep length unchanged.
            }
            else
            {
                if (length == 4)
                {
                    if (undo != null) rom.write_u8(statusBase + 0x09, 0, undo);
                    else rom.write_u8(statusBase + 0x09, 0);
                }
            }
        }

        /// <summary>
        /// Apply the translate-patch byte writes: main-menu width + status-screen
        /// skill cell. Equivalent to WF `ToolTranslateROM.ApplyTranslatePatch(string to)`
        /// minus the `CheckTextImportPatch` popup (which depends on WF
        /// `HowDoYouLikePatchForm` and stays in the WinForms-only path).
        /// </summary>
        public static void ApplyTranslatePatch(ROM rom, string to, Undo.UndoData undo)
        {
            ChangeMainMenuWidth(rom, to, undo);
            ChangeStatusScreenSkill(rom, to, undo);
        }

        // ============================================================
        // Text export driver - port from ToolTranslateROM.ExportallText (#536)
        // ============================================================

        /// <summary>
        /// Dump every text entry the WF `ExportallText` would emit to a
        /// `[XXXX]\ntext\n\n`-formatted file. Includes text-ID entries plus
        /// (when the ROM is multibyte) menu-command / map-terrain / FE7
        /// sound-room / other-text pointer-backed entries.
        /// Returns the number of entries written.
        /// </summary>
        public static int ExportTextsToFile(ROM rom, string outputPath, bool isOneLiner,
            Action<string> progressCallback)
        {
            if (rom?.RomInfo == null) return 0;
            if (string.IsNullOrEmpty(outputPath)) return 0;

            int count = 0;
            using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
            {
                var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);

                // --- Text IDs (Huffman/UnHuffman dictionary) ---
                uint textCount = TranslateCore.GetTextCount(rom);
                if (textCount > 0xFFFF) textCount = 0xFFFF;
                for (uint id = 0; id < textCount; id++)
                {
                    string text;
                    try { text = decoder.Decode(id) ?? string.Empty; }
                    catch { text = string.Empty; }

                    progressCallback?.Invoke($"Text:{U.To0xHexString(id)}");
                    WriteExportEntry(writer, U.ToHexString(id), text, isOneLiner, rom.RomInfo.is_multibyte);
                    count++;
                }

                if (rom.RomInfo.is_multibyte)
                {
                    // --- Menu commands (multibyte ROMs only) ---
                    var menuDefList = TextSourceListCore.MakeMenuDefinitionList(rom);
                    foreach (var menuDef in menuDefList)
                    {
                        if (!U.isSafetyOffset(menuDef.addr + 8, rom)) continue;
                        uint p = menuDef.addr + 8;
                        if (!U.isSafetyOffset(rom.p32(p), rom)) continue;
                        var commands = TextSourceListCore.MakeMenuCommandList(rom, p);
                        foreach (var cmd in commands)
                        {
                            if (!U.isSafetyOffset(cmd.addr, rom)) continue;
                            uint textPointer = cmd.addr + 0;
                            uint textid = rom.u32(textPointer);
                            string str = decoder.Decode(textid);
                            if (string.IsNullOrWhiteSpace(str)) continue;

                            progressCallback?.Invoke($"Menu:{U.To0xHexString(textid)}");
                            WriteExportEntry(writer, U.ToHexString(U.toPointer(textPointer)),
                                str, isOneLiner, rom.RomInfo.is_multibyte);
                            count++;
                        }
                    }

                    // --- Map terrain names ---
                    var terrainList = TextSourceListCore.MakeMapTerrainNameList(rom);
                    foreach (var t in terrainList)
                    {
                        if (!U.isSafetyOffset(t.addr, rom)) continue;
                        uint textid = rom.u32(t.addr);
                        string str = decoder.Decode(textid);
                        if (string.IsNullOrWhiteSpace(str)) continue;

                        progressCallback?.Invoke($"Terrain:{U.To0xHexString(textid)}");
                        WriteExportEntry(writer, U.ToHexString(U.toPointer(t.addr)),
                            str, isOneLiner, rom.RomInfo.is_multibyte);
                        count++;
                    }

                    // --- Sound room (FE7 multibyte only) ---
                    if (rom.RomInfo.version == 7)
                    {
                        var srList = TextSourceListCore.MakeSoundRoomList(rom);
                        foreach (var sr in srList)
                        {
                            if (!U.isSafetyOffset(sr.addr, rom)) continue;
                            uint textPointer = sr.addr + 12;
                            uint textid = rom.u32(textPointer);
                            string str = decoder.Decode(textid);
                            if (string.IsNullOrWhiteSpace(str)) continue;

                            progressCallback?.Invoke($"SoundRoom:{U.To0xHexString(textid)}");
                            WriteExportEntry(writer, U.ToHexString(U.toPointer(textPointer)),
                                str, isOneLiner, rom.RomInfo.is_multibyte);
                            count++;
                        }
                    }
                }

                // --- Other-text pointer entries (all ROMs) ---
                var otherList = TextSourceListCore.MakeOtherTextList(rom);
                foreach (var o in otherList)
                {
                    if (!U.isSafetyOffset(o.addr, rom)) continue;
                    uint pStr = rom.p32(o.addr);
                    string str = U.isSafetyOffset(pStr, rom) ? rom.getString(pStr) : string.Empty;
                    if (string.IsNullOrWhiteSpace(str)) continue;

                    progressCallback?.Invoke($"Other:{U.To0xHexString(pStr)}");
                    WriteExportEntry(writer, U.ToHexString(U.toPointer(o.addr)),
                        str, isOneLiner, rom.RomInfo.is_multibyte);
                    count++;
                }
            }
            return count;
        }

        static void WriteExportEntry(StreamWriter writer, string idHex, string text,
            bool isOneLiner, bool isMultibyte)
        {
            if (isMultibyte)
            {
                // Multibyte ROMs use `@001F` as a padding token; strip it on export.
                text = text.Replace("@001F", string.Empty);
            }

            if (isOneLiner)
            {
                writer.Write(text.Replace("\r\n", "\\r\\n"));
                writer.Write("\r\n");
                return;
            }

            writer.Write("[");
            writer.Write(idHex);
            writer.Write("]\r\n");
            writer.Write(text);
            writer.Write("\r\n");
        }

        // ============================================================
        // Text import driver - port from ToolTranslateROM.ImportAllText (#536)
        // ============================================================

        /// <summary>
        /// Read a `[XXXX]\ntext\n\n`-formatted file and write each entry back
        /// into the ROM. Handles text IDs (Huffman/UnHuffman dictionary) and
        /// safety pointers (C-string write path), matching WF `ImportAllText`.
        /// Returns the number of entries successfully written.
        /// </summary>
        public static int ImportTextsFromFile(ROM rom, string filename,
            RecycleAddress recycle, Undo.UndoData undo, Action<string> progressCallback)
        {
            if (rom?.RomInfo == null) return 0;
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename)) return 0;
            if (recycle == null) return 0;

            uint maxTextCount = TranslateCore.GetTextCount(rom);
            uint textBaseAddress = rom.p32(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(textBaseAddress, rom)) return 0;

            var textDataDic = ReadTextDataDic(filename);

            // First pass: register affected text addresses with the recycler
            // (so we can re-pack them).
            var list = new List<Address>();
            foreach (var pair in textDataDic)
            {
                AddRecycle(rom, pair.Key, list, maxTextCount, textBaseAddress);
            }
            recycle.AddRecycle(list);
            recycle.RecycleOptimize();

            int written = 0;
            foreach (var pair in textDataDic)
            {
                progressCallback?.Invoke($"Write:{U.To0xHexString(pair.Key)}");
                if (WriteText(rom, pair.Key, pair.Value, recycle, undo, maxTextCount, textBaseAddress))
                {
                    written++;
                }
            }
            return written;
        }

        static Dictionary<uint, string> ReadTextDataDic(string filename)
        {
            var result = new Dictionary<uint, string>();
            string[] lines = File.ReadAllLines(filename);
            uint id = U.NOT_FOUND;
            string text = string.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.IsCommentSlashOnly(line) || U.OtherLangLine(line)) continue;
                line = U.ClipComment(line);
                if (line.Length <= 0) continue;

                if (!IsTextIDCode(line))
                {
                    text += line + "\r\n";
                    continue;
                }

                if (id != U.NOT_FOUND)
                {
                    result[id] = text;
                }

                id = U.atoh(U.substr(line, 1));
                text = string.Empty;
            }

            // Last entry.
            if (id != U.NOT_FOUND)
            {
                result[id] = text;
            }
            return result;
        }

        static bool IsTextIDCode(string line)
        {
            if (line.Length < 4 || line.Length > 11) return false;
            if (line[0] != '[' || line[line.Length - 1] != ']') return false;
            if (line == "[XXXX]") return true;
            for (int i = 1; i < line.Length - 1; i++)
            {
                if (!U.ishex(line[i])) return false;
            }
            return true;
        }

        static void AddRecycle(ROM rom, uint id, List<Address> list,
            uint maxTextCount, uint textBaseAddress)
        {
            if (id <= 0) return;
            if (id >= maxTextCount) return;

            uint pointerAddr = textBaseAddress + (id * 4);
            uint paddr = rom.u32(pointerAddr);
            // Skip RAM pointers (text is in EWRAM/IWRAM, not eligible for ROM recycling).
            if (FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(paddr)) return;
            if (FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(paddr)) return;

            var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
            uint dataAddr;
            int length;
            if (FETextEncode.IsUnHuffmanPatchPointer(paddr))
            {
                uint unhuffmanAddr = U.toOffset(FETextEncode.ConvertUnHuffmanPatchToPointer(paddr));
                dataAddr = unhuffmanAddr;
                decoder.UnHffmanPatchDecode(unhuffmanAddr, out length);
                if (length <= 0) return;
            }
            else if (U.isPointer(paddr))
            {
                dataAddr = U.toOffset(paddr);
                decoder.huffman_decode(dataAddr, out length);
                if (length <= 1) return;
            }
            else
            {
                return;
            }

            uint textId0Addr = rom.p32(textBaseAddress);
            if (dataAddr == textId0Addr) return;

            FEBuilderGBA.Address.AddAddress(list, dataAddr, (uint)length, U.NOT_FOUND,
                "text " + U.ToHexString(id), FEBuilderGBA.Address.DataTypeEnum.BIN);
        }

        static bool WriteText(ROM rom, uint id, string text, RecycleAddress recycle,
            Undo.UndoData undo, uint maxTextCount, uint textBaseAddress)
        {
            if (id <= 0) return false;
            if (text == null || text.Length < 2) return false;

            // Strip the trailing CRLF the WF reader appends to each entry.
            string writetext = U.substr(text, 0, text.Length - 2);

            if (id < maxTextCount)
            {
                return WriteTextUnHffman(rom, id, writetext, recycle, undo, textBaseAddress);
            }
            else if (U.isSafetyPointer(id))
            {
                return WriteCString(rom, id, writetext, recycle, undo);
            }
            return false;
        }

        static bool WriteTextUnHffman(ROM rom, uint id, string text,
            RecycleAddress recycle, Undo.UndoData undo, uint textBaseAddress)
        {
            uint pointerAddr = textBaseAddress + (id * 4);
            uint paddr = rom.u32(pointerAddr);
            if (FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(paddr)) return false;
            if (FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(paddr)) return false;
            if (CoreState.FETextEncoder == null) return false;

            byte[] encoded;
            CoreState.FETextEncoder.UnHuffmanEncode(text, out encoded);
            if (encoded == null || encoded.Length == 0) return false;

            uint newAddr = recycle.Write(encoded, undo);
            if (newAddr == U.NOT_FOUND) return false;

            uint newPointer = U.toPointer(newAddr);
            newPointer = FETextEncode.ConvertPointerToUnHuffmanPatchPointer(newPointer);
            if (undo != null) rom.write_u32(pointerAddr, newPointer, undo);
            else rom.write_u32(pointerAddr, newPointer);
            return true;
        }

        static bool WriteCString(ROM rom, uint pointer, string text,
            RecycleAddress recycle, Undo.UndoData undo)
        {
            if (CoreState.SystemTextEncoder == null) return false;

            byte[] stringBytes = CoreState.SystemTextEncoder.Encode(text);
            stringBytes = U.ArrayAppend(stringBytes, new byte[] { 0x00 });

            uint newAddr = recycle.Write(stringBytes, undo);
            if (newAddr == U.NOT_FOUND) return false;

            if (undo != null) rom.write_p32(U.toOffset(pointer), newAddr, undo);
            else rom.write_p32(U.toOffset(pointer), newAddr);
            return true;
        }
    }
}
