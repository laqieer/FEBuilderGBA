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
//
//  - WipeJPFont / WipeJPTitle / WipeJPClassReelFont orchestration (#1029) — now
//    fully cross-platform via ToolTranslateROMWipeJPCore.cs + ChapterTitleCore +
//    OPClassFontListCore; the HowDoYouLikePatchForm ChapterNameText popup is
//    replaced by an injected precondition delegate (Core stays UI-free). Consumed
//    by SimpleFireTranslate when opts.OverrideJpFont is set.
//
// What's intentionally NOT here (KnownGap, documented in PR Known Limitations):
//  - Bitmap font auto-generation (`ImageUtil.AutoGenerateFont` is
//    System.Drawing-bound; lives in WinForms).
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
        /// (1) currentDir top-level, (2) romBaseDirectory top-level,
        /// (3) directory of lastROMFilename top-level, (4) emulatorDirectory
        /// recursive. Returns empty string when none found.
        /// Mirrors WF MainFormUtil.FindOrignalROMByLang search order: the
        /// last-resort recursive scan is restricted to the configured emulator
        /// directory (Program.Config.at("emulator")), NOT to currentDir, to
        /// avoid expensive sub-tree scans of arbitrary work directories.
        /// </summary>
        public static string FindOrignalROMByLang(string currentDir, string lang,
            int currentVersion, string romBaseDirectory, string lastROMFilename,
            string emulatorDirectory = "")
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

            // Pass 4: emulator directory recursive (matches WF
            // MainFormUtil.FindOrignalROMByLang last-resort search).
            if (!string.IsNullOrEmpty(emulatorDirectory) && !searched.Contains(emulatorDirectory))
            {
                result = FindOrignalROMLowByLang(emulatorDirectory, lang, currentVersion, SearchOption.AllDirectories);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            return string.Empty;
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

        /// <summary>
        /// Find an unmodified ROM matching the given CRC32. Last-resort
        /// recursive scan targets the emulator directory (matching WF).
        /// </summary>
        public static string FindOrignalROMByCRC32(string currentDir, uint searchCrc,
            string romBaseDirectory, string lastROMFilename,
            string emulatorDirectory = "")
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

            if (!string.IsNullOrEmpty(emulatorDirectory) && !searched.Contains(emulatorDirectory))
            {
                result = FindOrignalROMLowByCrc(emulatorDirectory, searchCrc, SearchOption.AllDirectories);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            return string.Empty;
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
            return ExportTextsToFile(rom, outputPath, isOneLiner,
                isModifiedTextOnly: false, translateFrom: string.Empty, translateTo: string.Empty,
                fromRomPath: string.Empty, toRomPath: string.Empty, progressCallback);
        }

        /// <summary>
        /// Full WF `ExportallText` overload that supports the Detail-tab
        /// translation controls: `isModifiedTextOnly` filters out lines that
        /// haven't been touched (compared against the FROM ROM), and the
        /// translate-from / translate-to language pair drives a fixed
        /// translation dictionary lookup (FROM ROM text -> TO ROM text).
        /// When all translation params are empty, the function degrades to
        /// the simple text-only export (text IDs + multibyte pointer
        /// entries) - matching the no-options-set WF path.
        /// </summary>
        public static int ExportTextsToFile(ROM rom, string outputPath, bool isOneLiner,
            bool isModifiedTextOnly, string translateFrom, string translateTo,
            string fromRomPath, string toRomPath, Action<string> progressCallback)
        {
            if (rom?.RomInfo == null) return 0;
            if (string.IsNullOrEmpty(outputPath)) return 0;

            // Build the FROM-text -> TO-text fixed translation dictionary by
            // loading the FROM and TO ROMs side-by-side and pairing entries at
            // identical text IDs. Mirrors WF `TranslateTextUtil.MakeFixedDic`,
            // minus the Google-Translate path (which is opt-in and stays
            // WinForms-only - see #536 Known Limitations).
            Dictionary<string, string> fixedDic = BuildFixedTranslationDictionary(
                translateFrom, translateTo, fromRomPath, toRomPath);

            // Cache FROM-text per ID so isModifiedTextOnly can compare.
            Dictionary<uint, string> fromRomTexts = null;
            if (isModifiedTextOnly && !string.IsNullOrEmpty(fromRomPath) && File.Exists(fromRomPath))
            {
                fromRomTexts = LoadRomTextsById(fromRomPath);
            }

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

                    // Modified-text-only filter: skip when this ID is identical
                    // to the same ID in the FROM ROM (i.e. untouched).
                    if (isModifiedTextOnly && fromRomTexts != null
                        && fromRomTexts.TryGetValue(id, out string fromText)
                        && fromText == text)
                    {
                        continue;
                    }

                    // Apply fixed-dictionary translation (FROM-text -> TO-text)
                    // when available. Mirrors WF `TranslateTextUtil.TranslateText`
                    // dictionary lookup.
                    if (fixedDic.TryGetValue(text, out string translated))
                    {
                        text = translated;
                    }

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

                    // --- Map terrain names (multibyte ROMs only) ---
                    // This whole block is already inside the multibyte guard
                    // (line 496) so MakeMapTerrainNameList's 4-byte path
                    // applies and rom.u32 is correct. The English path's
                    // 2-byte entries are surfaced by the Avalonia view
                    // (PopulateTerrainCombo) — moving the font-scan + export
                    // outside the guard is tracked separately for #671 v3.
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

            // Apply the WF text-escape conversion (e.g. FEditorAdv mode turns
            // @0010@0XXX into [LoadFace][0xXXX] etc.) so the export file is
            // round-trip compatible with the WinForms tool. Mirrors WF
            // TextForm.ConvertEscapeText. (#536 round-3 review)
            text = ConvertEscapeText(text);

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
        // Escape conversion helpers - port from TextForm.cs (#536 round-3)
        // ============================================================

        /// <summary>
        /// Convert engine escape codes (`@0010@0XXX`, `@XXXX`) into the
        /// user-facing FEditorAdv representations (`[LoadFace][0xXXX]`,
        /// `[0xXXXX]`). Mirrors WF `TextForm.ConvertEscapeText`/
        /// `ConvertEscapeToFEditor` for the FEditorAdv (default) text-escape
        /// mode. When text_escape is configured to `ProjectFEGBA`, returns
        /// the text unchanged.
        /// </summary>
        public static string ConvertEscapeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (CoreState.TextEscape == null) return text;

            // text_escape mode resolution mirrors WF OptionForm.text_escape:
            // default = FEditorAdv (1); ProjectFEGBA = 0.
            uint mode = (CoreState.Config != null)
                ? U.atoi(CoreState.Config.at("func_text_escape", "1"))
                : 1u;
            if (mode != 1) return text;

            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"@0010@0([0-9A-F][0-9A-F][0-9A-F])", "[LoadFace][0x$1]");
            text = CoreState.TextEscape.table_replace(text);
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"@([0-9A-F][0-9A-F][0-9A-F][0-9A-F])", "[0x$1]");
            return text;
        }

        /// <summary>
        /// Convert FEditorAdv user-facing escape representations
        /// (`[LoadFace][0xXXX]`, `[0xXXXX]`, `[N]`, `[X]`) back into engine
        /// escape codes (`@0010@0XXX`, `@XXXX`). Mirrors WF
        /// `TextForm.ConvertFEditorToEscape`. Used on import so files
        /// containing FEditorAdv tokens round-trip correctly.
        /// </summary>
        public static string ConvertFEditorToEscape(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            if (CoreState.TextEscape == null) return str;

            str = System.Text.RegularExpressions.Regex.Replace(str,
                @"\[LoadFace\]\[0x00([0-9A-F][0-9A-F][0-9A-F])\]", "@0010@0$1");
            str = System.Text.RegularExpressions.Regex.Replace(str,
                @"\[LoadFace\]\[0x([0-9A-F][0-9A-F][0-9A-F])\]", "@0010@0$1");
            str = CoreState.TextEscape.table_replace_rev(str);
            str = str.Replace("[N]", string.Empty);
            str = str.Replace("[X]", string.Empty);
            str = System.Text.RegularExpressions.Regex.Replace(str,
                @"\[0x([0-9A-F])\]", "@000$1");
            str = System.Text.RegularExpressions.Regex.Replace(str,
                @"\[0x([0-9A-F][0-9A-F])\]", "@00$1");
            str = System.Text.RegularExpressions.Regex.Replace(str,
                @"\[0x([0-9A-F][0-9A-F][0-9A-F])\]", "@0$1");
            str = System.Text.RegularExpressions.Regex.Replace(str,
                @"\[0x([0-9A-F][0-9A-F][0-9A-F][0-9A-F])\]", "@$1");
            return str;
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

            // Apply FEditorAdv -> engine-escape conversion so files containing
            // `[0x....]`, `[LoadFace]...`, `[N]`, `[X]` round-trip correctly.
            // Mirrors WF ToolTranslateROM.WriteText (#536 round-3 review).
            writetext = ConvertFEditorToEscape(writetext);

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

        // ============================================================
        // Translation-dictionary helpers (#536)
        // ============================================================

        /// <summary>
        /// Build a FROM-text -> TO-text dictionary by loading the two reference
        /// ROMs and pairing entries at identical text IDs. Used by
        /// ExportTextsToFile to apply auto-translation when the Detail-tab
        /// translation controls are populated. Mirrors the offline portion of
        /// WF `TranslateTextUtil.MakeFixedDic` (the Google-Translate online
        /// path stays WinForms-only - see #536 Known Limitations).
        /// </summary>
        public static Dictionary<string, string> BuildFixedTranslationDictionary(
            string translateFrom, string translateTo,
            string fromRomPath, string toRomPath)
        {
            var dic = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(translateFrom) || string.IsNullOrEmpty(translateTo)) return dic;
            if (translateFrom == translateTo) return dic;
            if (!File.Exists(fromRomPath) || !File.Exists(toRomPath)) return dic;

            Dictionary<uint, string> fromTexts = LoadRomTextsById(fromRomPath);
            Dictionary<uint, string> toTexts = LoadRomTextsById(toRomPath);

            foreach (var pair in fromTexts)
            {
                if (string.IsNullOrEmpty(pair.Value)) continue;
                if (toTexts.TryGetValue(pair.Key, out string toText) && !string.IsNullOrEmpty(toText))
                {
                    // First occurrence wins (matches WF dictionary build order).
                    if (!dic.ContainsKey(pair.Value))
                    {
                        dic[pair.Value] = toText;
                    }
                }
            }
            return dic;
        }

        /// <summary>
        /// Load a ROM file from disk and return a textId -> decoded-text map.
        /// Used by BuildFixedTranslationDictionary and the
        /// isModifiedTextOnly filter. Returns an empty dictionary when the
        /// file can't be loaded or parsed.
        /// </summary>
        public static Dictionary<uint, string> LoadRomTextsById(string romPath)
        {
            var result = new Dictionary<uint, string>();
            if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath)) return result;

            try
            {
                var rom = new ROM();
                string version;
                if (!rom.Load(romPath, out version)) return result;
                if (rom.RomInfo == null) return result;

                uint textCount = TranslateCore.GetTextCount(rom);
                if (textCount > 0xFFFF) textCount = 0xFFFF;

                var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
                for (uint id = 0; id < textCount; id++)
                {
                    try
                    {
                        string text = decoder.Decode(id) ?? string.Empty;
                        result[id] = text;
                    }
                    catch
                    {
                        // Swallow per-entry decode errors so we don't lose the
                        // whole batch.
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort - return whatever we got.
            }
            return result;
        }

        // ============================================================
        // SimpleFireTranslate orchestration (#536)
        // ============================================================

        /// <summary>
        /// Options for SimpleFireTranslate - mirrors the inputs WF
        /// SimpleFireButton_Click reads from the dialog.
        /// </summary>
        public class SimpleFireOptions
        {
            public string FromRomPath { get; set; } = string.Empty;
            public string ToRomPath { get; set; } = string.Empty;
            public string ExtraFontRomPath { get; set; } = string.Empty;
            public string TranslateDataFilename { get; set; } = string.Empty;
            public string FromLanguage { get; set; } = string.Empty;
            public string ToLanguage { get; set; } = string.Empty;
            public bool OverrideJpFont { get; set; }

            /// <summary>
            /// Gate for the JP chapter-name wipe (<see cref="WipeJPTitle"/>),
            /// mirroring WF <c>HowDoYouLikePatchForm.CheckAndShowPopupDialog(ChapterNameText)</c>:
            /// return true to wipe the chapter-title images, false to skip. When
            /// null (the default), the wipe falls back to a headless
            /// <see cref="PatchDetection.SearchChapterNameToTextPatch"/> check —
            /// it skips when the ChapterNameToText patch is absent. The Avalonia
            /// host injects a delegate that surfaces the patch recommendation.
            /// Only consulted when <see cref="OverrideJpFont"/> is set.
            /// </summary>
            public Func<bool> ChapterNameTextPrecondition { get; set; }
        }

        /// <summary>
        /// Orchestrates the full WF SimpleFireButton_Click flow against the
        /// Core helpers:
        ///   0. (When opts.OverrideJpFont) WipeJPClassReelFont -> WipeJPTitle ->
        ///      WipeJPFont — the WF wipe order, BEFORE the translate import.
        ///   1. ApplyTranslatePatch (main menu width + status-screen skill)
        ///   2. If TranslateDataFilename is supplied, ImportTextsFromFile from
        ///      that file
        ///   3. Export current ROM texts to a temp file with the
        ///      FROM/TO ROM fixed dictionary applied, then import the temp
        ///      file back into the ROM (auto-translates static texts)
        ///   4. ImportFont from TO ROM (font-copy path; auto-gen stays
        ///      WinForms-only)
        ///   5. BlackOut — clears any leftover recycle ranges with 0x00 (WF
        ///      parity: trans.BlackOut(undodata)). Push undo is the caller's
        ///      responsibility.
        /// Returns the number of text entries imported in the auto-translate
        /// pass; 0 on failure.
        /// </summary>
        public static int SimpleFireTranslate(ROM rom, SimpleFireOptions opts,
            RecycleAddress recycle, Undo.UndoData undo, Action<string> progressCallback)
        {
            if (rom?.RomInfo == null || opts == null) return 0;
            if (opts.FromLanguage == opts.ToLanguage) return 0;
            if (recycle == null) return 0;

            // Step 0: JP-font wipe (Override JP Font). WF order is
            // WipeJPClassReelFont -> WipeJPTitle -> WipeJPFont, BEFORE the import.
            if (opts.OverrideJpFont)
            {
                progressCallback?.Invoke("WipeJP ClassReel Font...");
                WipeJPClassReelFont(rom, recycle, undo);
                progressCallback?.Invoke("WipeJP Title...");
                WipeJPTitle(rom, recycle, undo, opts.ChapterNameTextPrecondition);
                progressCallback?.Invoke("WipeJP Font...");
                WipeJPFont(rom, recycle, undo);
            }

            // Step 1: Apply translate patch.
            ApplyTranslatePatch(rom, opts.ToLanguage, undo);

            // Step 2: Optional translate-data file import.
            int total = 0;
            if (!string.IsNullOrEmpty(opts.TranslateDataFilename) &&
                File.Exists(opts.TranslateDataFilename))
            {
                progressCallback?.Invoke("Importing translate data file...");
                total += ImportTextsFromFile(rom, opts.TranslateDataFilename,
                    recycle, undo, progressCallback);
            }

            // Step 3: Export-then-reimport with FROM/TO dictionary applied.
            if (File.Exists(opts.FromRomPath) && File.Exists(opts.ToRomPath))
            {
                string tempFile = Path.GetTempFileName();
                try
                {
                    progressCallback?.Invoke("Exporting auto-translated texts...");
                    ExportTextsToFile(rom, tempFile,
                        isOneLiner: false,
                        isModifiedTextOnly: false,
                        translateFrom: opts.FromLanguage,
                        translateTo: opts.ToLanguage,
                        fromRomPath: opts.FromRomPath,
                        toRomPath: opts.ToRomPath,
                        progressCallback);

                    progressCallback?.Invoke("Re-importing auto-translated texts...");
                    total += ImportTextsFromFile(rom, tempFile, recycle, undo, progressCallback);
                }
                finally
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                    catch { /* best-effort cleanup */ }
                }
            }

            // Step 4: Font copy from TO ROM (ROM-port only here; cross-platform
            // auto-generation is available via ImportFonts but is driven from
            // the dedicated "Import Font" action so SimpleFire stays
            // rasterizer-free / deterministic).
            if (File.Exists(opts.ToRomPath))
            {
                progressCallback?.Invoke("Copying missing fonts from TO ROM...");
                ImportFontFromROMs(rom, opts.ToRomPath, opts.ExtraFontRomPath,
                    recycle, undo, progressCallback);
            }

            // Step 5: BlackOut — clear any leftover recycle ranges with 0x00 so
            // the freed JP-font / text regions don't leave stale bytes behind
            // (WF parity: trans.BlackOut(undodata) at the end of
            // SimpleFireButton_Click). Threads undo so it rolls back.
            recycle.BlackOut(undo);

            return total;
        }

        // ============================================================
        // WipeJP* orchestration (#1029) — Form-free ports of WF
        // ToolTranslateROM.WipeJPFont / WipeJPTitle / WipeJPClassReelFont.
        // Each mirrors the WF body: helper -> recycle.AddRecycle -> RecycleOptimize
        // (WipeJPFont additionally re-appends the preserved glyphs from the
        // OPTIMIZED recycle pool via WriteBackFont, so order matters).
        // ============================================================

        /// <summary>
        /// Wipe the FE8J Japanese font hash tables, preserving the always-keep
        /// glyphs. Builds the recycle list, optimizes it, then re-appends the
        /// preserved glyphs from the optimized pool. Form-free port of WF
        /// <c>ToolTranslateROM.WipeJPFont</c>. No-op on non-FE8J ROMs.
        /// </summary>
        public static void WipeJPFont(ROM rom, RecycleAddress recycle, Undo.UndoData undo)
        {
            if (rom?.RomInfo == null || recycle == null) return;

            var list = new List<Address>();
            var jpfont = new WipeJPFontHelper(rom, undo);
            jpfont.AddJPFonts(list);

            recycle.AddRecycle(list);
            recycle.RecycleOptimize();

            // WriteBackFont allocates preserved glyphs from the OPTIMIZED pool,
            // so this MUST run after RecycleOptimize.
            jpfont.WriteBackFont(recycle);
        }

        /// <summary>
        /// Wipe the JP chapter-name images (repoint to the last entry, zero the
        /// number/title pointers). Form-free port of WF
        /// <c>ToolTranslateROM.WipeJPTitle</c>. The
        /// <paramref name="chapterNameTextPrecondition"/> gates the wipe (WF
        /// HowDoYouLikePatchForm(ChapterNameText) parity); null = headless patch
        /// check. No-op on non-FE8 ROMs / when the precondition is false.
        /// </summary>
        public static void WipeJPTitle(ROM rom, RecycleAddress recycle, Undo.UndoData undo,
            Func<bool> chapterNameTextPrecondition = null)
        {
            if (rom?.RomInfo == null || recycle == null) return;

            var list = new List<Address>();
            var jpChapter = new WipeJPChapterNameHelper(rom, undo);
            jpChapter.Wipe(list, chapterNameTextPrecondition);

            recycle.AddRecycle(list);
            recycle.RecycleOptimize();
        }

        /// <summary>
        /// Wipe the OP-class JP-name font slots (repoint all-but-first to the
        /// first). Form-free port of WF
        /// <c>ToolTranslateROM.WipeJPClassReelFont</c>. No-op unless the ROM is
        /// FE8J with the OP-class-reel font code signature present.
        /// </summary>
        public static void WipeJPClassReelFont(ROM rom, RecycleAddress recycle, Undo.UndoData undo)
        {
            if (rom?.RomInfo == null || recycle == null) return;

            var list = new List<Address>();
            var jpClassReel = new WipeJPClassReelFontHelper(rom, undo);
            jpClassReel.Wipe(list);

            recycle.AddRecycle(list);
            recycle.RecycleOptimize();
        }

        // ============================================================
        // ImportFonts / ImportFontFromROMs - copy + auto-generate missing
        // fonts (#536 ported glyphs, #796 cross-platform auto-gen)
        // ============================================================

        /// <summary>
        /// Result of <see cref="ImportFonts"/>: how many glyphs were ported
        /// from a source ROM versus rasterized fresh by the
        /// <see cref="IFontRasterizer"/>. Each count tracks per-(char, variant)
        /// appends, matching WF <c>ToolTranslateROMFont</c> which processes the
        /// text and item variant of every character independently.
        /// </summary>
        public readonly struct ImportFontResult
        {
            /// <summary>Glyphs copied from the Font ROM / Extra Font ROM.</summary>
            public int Ported { get; init; }

            /// <summary>Glyphs rasterized fresh via the font rasterizer.</summary>
            public int Generated { get; init; }
        }

        /// <summary>
        /// Copy fonts that the current ROM is missing from the fonts in the
        /// source ROM (and optionally an extra-font ROM), and — when
        /// <paramref name="autoGenEnabled"/> is set and a
        /// <paramref name="rasterizer"/> is supplied — auto-generate any glyph
        /// that is still missing after the port attempt. Iterates every text
        /// glyph present in the current ROM (TextForm + multibyte menu /
        /// terrain / sound-room / other-text); for each missing glyph it first
        /// looks it up in the source ROM(s), and failing that rasterizes both
        /// the text and item variant via the rasterizer and appends them.
        ///
        /// This is the cross-platform port of WF
        /// <c>ToolTranslateROMFont.ImportFont</c>. The bitmap auto-generation
        /// path that used to be WinForms-only (<c>ImageUtil.AutoGenerateFont</c>)
        /// is now driven through the platform-neutral
        /// <see cref="IFontRasterizer"/> seam (#796); the SkiaSharp
        /// implementation reproduces the WF GDI algorithm byte-for-byte.
        ///
        /// Returns the number of glyphs ported and generated. Returns
        /// <c>default</c> (both counts 0) only when there is nothing to do —
        /// i.e. no source ROM AND auto-generation is off.
        /// </summary>
        public static ImportFontResult ImportFonts(ROM rom, string fontRomPath,
            string extraFontRomPath, IFontRasterizer rasterizer, FontSpec autoGenFont,
            bool autoGenEnabled, RecycleAddress recycle, Undo.UndoData undo,
            Action<string> progressCallback)
        {
            if (rom?.RomInfo == null) return default;

            ROM fontRom = null;
            ROM extraFontRom = null;

            if (!string.IsNullOrEmpty(fontRomPath) && File.Exists(fontRomPath))
            {
                fontRom = new ROM();
                string version;
                if (!fontRom.Load(fontRomPath, out version)) fontRom = null;
            }
            if (!string.IsNullOrEmpty(extraFontRomPath) && File.Exists(extraFontRomPath))
            {
                extraFontRom = new ROM();
                string version;
                if (!extraFontRom.Load(extraFontRomPath, out version)) extraFontRom = null;
            }

            // Narrowed early-exit (#796): the auto-gen path needs no source
            // ROM, so only bail when there is BOTH no source ROM AND no
            // enabled rasterizer.
            bool autoGenActive = autoGenEnabled && rasterizer != null;
            if (fontRom == null && extraFontRom == null && !autoGenActive) return default;

            PRIORITY_CODE myPriority = PriorityCodeUtil.SearchPriorityCode(rom);
            PRIORITY_CODE fontRomPriority = fontRom != null
                ? PriorityCodeUtil.SearchPriorityCode(fontRom) : myPriority;
            PRIORITY_CODE extraFontRomPriority = extraFontRom != null
                ? PriorityCodeUtil.SearchPriorityCode(extraFontRom) : myPriority;

            var processed = new HashSet<string>();
            var ctx = new FontPortContext
            {
                Rom = rom,
                FontRom = fontRom,
                ExtraFontRom = extraFontRom,
                Processed = processed,
                MyPriority = myPriority,
                FontRomPriority = fontRomPriority,
                ExtraFontRomPriority = extraFontRomPriority,
                Recycle = recycle,
                Undo = undo,
                Rasterizer = autoGenActive ? rasterizer : null,
                AutoGenFont = autoGenFont,
            };

            // Pass 1: text-ID glyphs (Huffman / UnHuffman dictionary).
            var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
            uint textCount = TranslateCore.GetTextCount(rom);
            if (textCount > 0xFFFF) textCount = 0xFFFF;
            for (uint id = 0; id < textCount; id++)
            {
                string text;
                try { text = decoder.Decode(id) ?? string.Empty; }
                catch { continue; }

                progressCallback?.Invoke($"FontScan:{U.To0xHexString(id)}");
                PortMissingGlyphs(ctx, text);
            }

            // Pass 2: multibyte menu / terrain / sound-room / other-text
            // pointer-backed entries (matches WF ToolTranslateROMFont).
            if (rom.RomInfo.is_multibyte)
            {
                foreach (var menuDef in TextSourceListCore.MakeMenuDefinitionList(rom))
                {
                    if (!U.isSafetyOffset(menuDef.addr + 8, rom)) continue;
                    uint p = menuDef.addr + 8;
                    if (!U.isSafetyOffset(rom.p32(p), rom)) continue;
                    foreach (var cmd in TextSourceListCore.MakeMenuCommandList(rom, p))
                    {
                        if (!U.isSafetyOffset(cmd.addr, rom)) continue;
                        uint textid = rom.u32(cmd.addr + 0);
                        string str = FETextDecode.Direct(textid);
                        if (string.IsNullOrEmpty(str)) continue;
                        progressCallback?.Invoke($"MenuFontScan:{U.To0xHexString(textid)}");
                        PortMissingGlyphs(ctx, str);
                    }
                }

                // This font-scan loop sits inside the multibyte guard
                // above, so MakeMapTerrainNameList's 4-byte path applies
                // and rom.u32 is correct here. English terrain font-port
                // is out of scope for #671 — tracked separately.
                foreach (var terrain in TextSourceListCore.MakeMapTerrainNameList(rom))
                {
                    if (!U.isSafetyOffset(terrain.addr, rom)) continue;
                    uint textid = rom.u32(terrain.addr);
                    string str = FETextDecode.Direct(textid);
                    if (string.IsNullOrEmpty(str)) continue;
                    progressCallback?.Invoke($"TerrainFontScan:{U.To0xHexString(textid)}");
                    PortMissingGlyphs(ctx, str);
                }

                if (rom.RomInfo.version == 7)
                {
                    foreach (var sr in TextSourceListCore.MakeSoundRoomList(rom))
                    {
                        if (!U.isSafetyOffset(sr.addr, rom)) continue;
                        uint textid = rom.u32(sr.addr + 12);
                        string str = FETextDecode.Direct(textid);
                        if (string.IsNullOrEmpty(str)) continue;
                        progressCallback?.Invoke($"SoundRoomFontScan:{U.To0xHexString(textid)}");
                        PortMissingGlyphs(ctx, str);
                    }
                }
            }

            // Other-text entries (all ROMs).
            foreach (var o in TextSourceListCore.MakeOtherTextList(rom))
            {
                if (!U.isSafetyOffset(o.addr, rom)) continue;
                uint pStr = rom.p32(o.addr);
                string str = U.isSafetyOffset(pStr, rom) ? rom.getString(pStr) : string.Empty;
                if (string.IsNullOrEmpty(str)) continue;
                progressCallback?.Invoke($"OtherFontScan:{U.To0xHexString(pStr)}");
                PortMissingGlyphs(ctx, str);
            }

            return new ImportFontResult { Ported = ctx.Ported, Generated = ctx.Generated };
        }

        /// <summary>
        /// Copy fonts that the current ROM is missing from the fonts in the
        /// source ROM (and optionally an extra-font ROM). Binary-compatible
        /// shim retained for #536 callers — delegates to
        /// <see cref="ImportFonts"/> with auto-generation disabled and returns
        /// only the ported count.
        /// </summary>
        public static int ImportFontFromROMs(ROM rom, string fontRomPath,
            string extraFontRomPath, RecycleAddress recycle, Undo.UndoData undo,
            Action<string> progressCallback)
            => ImportFonts(rom, fontRomPath, extraFontRomPath, null, default, false,
                recycle, undo, progressCallback).Ported;

        // Mutable context threaded through the per-string / per-glyph helpers
        // so the Ported / Generated counters accumulate across passes without a
        // long argument list.
        sealed class FontPortContext
        {
            public ROM Rom;
            public ROM FontRom;
            public ROM ExtraFontRom;
            public HashSet<string> Processed;
            public PRIORITY_CODE MyPriority;
            public PRIORITY_CODE FontRomPriority;
            public PRIORITY_CODE ExtraFontRomPriority;
            public RecycleAddress Recycle;
            public Undo.UndoData Undo;
            public IFontRasterizer Rasterizer; // null = auto-gen disabled
            public FontSpec AutoGenFont;
            public int Ported;
            public int Generated;
        }

        static void PortMissingGlyphs(FontPortContext ctx, string text)
        {
            for (int n = 0; n < text.Length; n++)
            {
                if (text[n] == '@') { n += 4; continue; }     // @XXXX escape
                if (text[n] == '\r') { n += 1; continue; }
                if (text[n] == '\n') { continue; }

                string one = text.Substring(n, 1);
                if (ctx.Processed.Contains(one)) continue;
                ctx.Processed.Add(one);

                // Try both isItemFont = false (text font) and true (item font),
                // matching WF FontImporter's two FontImporterOne calls per char.
                for (int kind = 0; kind < 2; kind++)
                {
                    bool isItemFont = (kind == 1);
                    TryPortOrGenerateOneGlyph(ctx, one, isItemFont);
                }
            }
        }

        static void TryPortOrGenerateOneGlyph(FontPortContext ctx, string one, bool isItemFont)
        {
            ROM rom = ctx.Rom;
            uint myMoji = ConvertMojiCharToUnit(one, ctx.MyPriority);
            if (myMoji < 0x20 || myMoji == 0x80) return;

            uint topaddress = FontCore.GetFontPointer(isItemFont, rom);
            uint myFontAddr = FontCore.FindFontData(topaddress, myMoji, out uint myPrevAddr,
                rom, ctx.MyPriority);
            if (myFontAddr != U.NOT_FOUND) return; // Already present.
            if (myPrevAddr == U.NOT_FOUND) return; // No slot to append.

            // Step 1: try to port from fontRom, then extraFontRom.
            byte[] glyphData = TryReadGlyphFromROM(ctx.FontRom, ctx.FontRomPriority, isItemFont, myMoji);
            bool generated = false;
            if (glyphData != null)
            {
                FontCore.TransportFontStruct(glyphData, myMoji, ctx.MyPriority, ctx.FontRomPriority);
            }
            else
            {
                glyphData = TryReadGlyphFromROM(ctx.ExtraFontRom, ctx.ExtraFontRomPriority, isItemFont, myMoji);
                if (glyphData != null)
                {
                    FontCore.TransportFontStruct(glyphData, myMoji, ctx.MyPriority, ctx.ExtraFontRomPriority);
                }
            }

            // Step 2: not in any source ROM -> rasterize a fresh glyph (#796).
            // Mirrors WF FontImporterOne: verticalOffset 0, isSquareFont false
            // (the 5-arg AutoGenerateFont overload that defaults verticalOffset
            // to 0). Builds the 72-byte struct via FontCore.MakeNewFontData with
            // this ROM's own priority code.
            if (glyphData == null && ctx.Rasterizer != null)
            {
                byte[] fontImage;
                int width;
                try
                {
                    fontImage = ctx.Rasterizer.RasterizeGlyph(ctx.AutoGenFont, one,
                        isItemFont, verticalOffset: 0, out width);
                }
                catch
                {
                    return; // a rasterizer fault must never abort the whole run
                }
                if (fontImage == null) return;

                glyphData = FontCore.MakeNewFontData(myMoji, (uint)width, fontImage,
                    rom, ctx.MyPriority);
                generated = true;
            }

            if (glyphData == null) return;

            // Zero the next-pointer (list tail), append, and re-link the chain.
            U.write_u32(glyphData, 0, 0);

            uint newAddr = ctx.Recycle.Write(glyphData, ctx.Undo);
            if (newAddr == U.NOT_FOUND) return;

            if (ctx.Undo != null) rom.write_u32(myPrevAddr, U.toPointer(newAddr), ctx.Undo);
            else rom.write_u32(myPrevAddr, U.toPointer(newAddr));

            if (generated) ctx.Generated++;
            else ctx.Ported++;
        }

        static byte[] TryReadGlyphFromROM(ROM sourceRom, PRIORITY_CODE sourcePriority,
            bool isItemFont, uint moji)
        {
            if (sourceRom == null) return null;
            uint sourceTop = FontCore.GetFontPointer(isItemFont, sourceRom);
            uint sourceAddr = FontCore.FindFontData(sourceTop, moji, out _, sourceRom, sourcePriority);
            if (sourceAddr == U.NOT_FOUND) return null;
            return sourceRom.getBinaryData(sourceAddr, 8 * 64 / 8 + 8);
        }

        /// <summary>
        /// Convert a one-character string to its priority-code-specific moji
        /// unit (the FE engine's internal numeric character code). Mirrors
        /// WF `U.ConvertMojiCharToUnit`:
        /// 1. Apply `FETextEncode.ConvertSPMoji` to handle special characters.
        /// 2. Encode via SystemTextEncoder (NOT UTF-8 directly).
        /// 3. If first byte is '@', parse as `@XXXX` escape via
        ///    `at_code_to_binary`.
        /// 4. Otherwise: UTF8 priority -> ConvertUTF8ToUTF32; SJIS / LAT1 ->
        ///    pack bytes via U.u32/u24/u16/u8 (little-endian byte read).
        /// </summary>
        public static uint ConvertMojiCharToUnit(string one, PRIORITY_CODE priority)
        {
            if (string.IsNullOrEmpty(one)) return 0;
            if (CoreState.SystemTextEncoder == null) return 0;

            // Step 1: apply ConvertSPMoji (replaces special chars with @-codes).
            one = FETextEncode.ConvertSPMoji(one);

            // Step 2: encode via SystemTextEncoder.
            byte[] moji = CoreState.SystemTextEncoder.Encode(one);
            if (moji == null || moji.Length == 0) return 0;

            // Step 3: @XXXX escape pass-through.
            if (moji.Length >= 2 && moji[0] == '@')
            {
                return FETextEncode.at_code_to_binary(moji, 0, out _);
            }

            // Step 4a: UTF8 priority - decode UTF-8 bytes into UTF-32 codepoint.
            if (priority == PRIORITY_CODE.UTF8)
            {
                return ConvertUTF8ToUTF32(moji);
            }

            // Step 4b: SJIS / LAT1 - read bytes as a little-endian uint.
            if (moji.Length >= 4) return U.u32(moji, 0);
            if (moji.Length >= 3) return U.u24(moji, 0);
            if (moji.Length >= 2) return U.u16(moji, 0);
            return U.u8(moji, 0);
        }

        /// <summary>
        /// Decode UTF-8 bytes into a UTF-32 codepoint. Mirrors WF
        /// `U.ConvertUTF8ToUTF32`. Returns 0 on empty input.
        /// </summary>
        static uint ConvertUTF8ToUTF32(byte[] moji)
        {
            if (moji == null || moji.Length <= 0) return 0;
            if (moji[0] < 0x80) return moji[0];
            if (moji[0] >= 0xFC && moji.Length >= 6)
            {
                uint code = (((uint)moji[0]) & 0x01);
                code = (code << 6) | (((uint)moji[1]) & 0x3F);
                code = (code << 6) | (((uint)moji[2]) & 0x3F);
                code = (code << 6) | (((uint)moji[3]) & 0x3F);
                code = (code << 6) | (((uint)moji[4]) & 0x3F);
                code = (code << 6) | (((uint)moji[5]) & 0x3F);
                return code;
            }
            if (moji[0] >= 0xF8 && moji.Length >= 5)
            {
                uint code = (((uint)moji[0]) & 0x03);
                code = (code << 6) | (((uint)moji[1]) & 0x3F);
                code = (code << 6) | (((uint)moji[2]) & 0x3F);
                code = (code << 6) | (((uint)moji[3]) & 0x3F);
                code = (code << 6) | (((uint)moji[4]) & 0x3F);
                return code;
            }
            if (moji[0] >= 0xF0 && moji.Length >= 4)
            {
                uint code = (((uint)moji[0]) & 0x07);
                code = (code << 6) | (((uint)moji[1]) & 0x3F);
                code = (code << 6) | (((uint)moji[2]) & 0x3F);
                code = (code << 6) | (((uint)moji[3]) & 0x3F);
                return code;
            }
            if (moji[0] >= 0xE0 && moji.Length >= 3)
            {
                uint code = (((uint)moji[0]) & 0x0F);
                code = (code << 6) | (((uint)moji[1]) & 0x3F);
                code = (code << 6) | (((uint)moji[2]) & 0x3F);
                return code;
            }
            if (moji[0] >= 0xC0 && moji.Length >= 2)
            {
                uint code = (((uint)moji[0]) & 0x1F);
                code = (code << 6) | (((uint)moji[1]) & 0x3F);
                return code;
            }
            return moji[0];
        }
    }
}
