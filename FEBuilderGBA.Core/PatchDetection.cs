using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Lightweight patch-detection helpers extracted from PatchUtil.
    /// These only do ROM byte-pattern matching — no WinForms dependencies.
    /// PatchUtil in the WinForms project delegates to these methods.
    /// </summary>
    public static class PatchDetection
    {
        public const uint NO_CACHE = 0xFF;

        public struct PatchTableSt
        {
            public string name;
            public string ver;
            public uint addr;
            public byte[] data;
        }

        // ---- Font drawing patch detection ----

        public enum draw_font_enum
        {
            NO,
            DrawMultiByte,
            DrawSingleByte,
            DrawUTF8,
            NoCache = (int)NO_CACHE
        }

        static draw_font_enum g_Cache_draw_font_enum = draw_font_enum.NoCache;

        public static void ClearCacheDrawFont()
        {
            g_Cache_draw_font_enum = draw_font_enum.NoCache;
        }

        public static draw_font_enum SearchDrawFontPatch()
        {
            if (CoreState.ROM?.RomInfo == null) return draw_font_enum.NO;
            if (g_Cache_draw_font_enum == draw_font_enum.NoCache)
            {
                g_Cache_draw_font_enum = SearchDrawFontPatch(CoreState.ROM);
            }
            return g_Cache_draw_font_enum;
        }

        public static draw_font_enum SearchDrawFontPatch(ROM rom)
        {
            PatchTableSt[] table = new PatchTableSt[] {
                new PatchTableSt{ name="DrawSingle", ver = "FE7J", addr = 0x56e2, data = new byte[]{0x00, 0x00, 0x00, 0x49, 0x8F, 0x46}},
                new PatchTableSt{ name="DrawSingle", ver = "FE8J", addr = 0x40c2, data = new byte[]{0x00, 0x00, 0x00, 0x49, 0x8F, 0x46}},
                new PatchTableSt{ name="DrawMulti",  ver = "FE7U", addr = 0x5BD6, data = new byte[]{0x00, 0x00, 0x00, 0x4B, 0x9F, 0x46}},
                new PatchTableSt{ name="DrawMulti",  ver = "FE8U", addr = 0x44D2, data = new byte[]{0x00, 0x00, 0x00, 0x49, 0x8F, 0x46}},
                new PatchTableSt{ name="DrawUTF8",   ver = "FE7U", addr = 0x5B6A, data = new byte[]{0x00, 0x00, 0x00, 0x4B, 0x18, 0x47}},
                new PatchTableSt{ name="DrawUTF8",   ver = "FE8U", addr = 0x44D2, data = new byte[]{0x00, 0x00, 0x00, 0x4B, 0x18, 0x47}},
            };

            string version = rom.RomInfo.VersionToFilename;
            foreach (PatchTableSt t in table)
            {
                if (t.ver != version) continue;
                byte[] data = rom.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;
                if (t.name == "DrawSingle") return draw_font_enum.DrawSingleByte;
                if (t.name == "DrawMulti")  return draw_font_enum.DrawMultiByte;
                if (t.name == "DrawUTF8")   return draw_font_enum.DrawUTF8;
            }
            return draw_font_enum.NO;
        }

        // ---- Text encoding priority code detection ----

        public enum PRIORITY_CODE
        {
            LAT1,
            SJIS,
            UTF8,
        }

        public static PRIORITY_CODE SearchPriorityCode()
        {
            if (CoreState.ROM?.RomInfo == null) return PRIORITY_CODE.LAT1;
            if (CoreState.ROM.RomInfo.is_multibyte)
            {
                return PRIORITY_CODE.SJIS;
            }
            draw_font_enum dfe = SearchDrawFontPatch();
            if (dfe == draw_font_enum.DrawMultiByte) return PRIORITY_CODE.SJIS;
            if (dfe == draw_font_enum.DrawUTF8)      return PRIORITY_CODE.UTF8;
            return PRIORITY_CODE.LAT1;
        }

        public static PRIORITY_CODE SearchPriorityCode(ROM rom)
        {
            if (rom == null) return PRIORITY_CODE.SJIS;
            if (rom.RomInfo.is_multibyte) return PRIORITY_CODE.SJIS;
            draw_font_enum dfe = SearchDrawFontPatch(rom);
            if (dfe == draw_font_enum.DrawMultiByte) return PRIORITY_CODE.SJIS;
            if (dfe == draw_font_enum.DrawUTF8)      return PRIORITY_CODE.UTF8;
            return PRIORITY_CODE.LAT1;
        }

        // ---- TextEngineRework detection ----

        public enum TextEngineRework_enum
        {
            NO,
            TeqTextEngineRework,
            NoCache = (int)NO_CACHE
        }

        static TextEngineRework_enum g_Cache_TextEngineRework_enum = TextEngineRework_enum.NoCache;

        public static void ClearCacheTextEngineRework()
        {
            g_Cache_TextEngineRework_enum = TextEngineRework_enum.NoCache;
        }

        public static TextEngineRework_enum SearchTextEngineReworkPatch()
        {
            if (g_Cache_TextEngineRework_enum == TextEngineRework_enum.NoCache)
            {
                g_Cache_TextEngineRework_enum = SearchTextEngineReworkPatchLow();
            }
            return g_Cache_TextEngineRework_enum;
        }

        static TextEngineRework_enum SearchTextEngineReworkPatchLow()
        {
            PatchTableSt[] table = new PatchTableSt[] {
                new PatchTableSt{ name="TextEngineRework", ver = "FE8U", addr = 0x6FD0, data = new byte[]{0xF0, 0xB5, 0x82, 0xB0}},
            };
            if (SearchPatchBool(table)) return TextEngineRework_enum.TeqTextEngineRework;
            return TextEngineRework_enum.NO;
        }

        // ---- ExtendsBattleBG patch detection ----
        // Mirrors WinForms PatchUtil.SearchExtendsBattleBG. Used by the
        // MapTerrainBGLookupTable / MapTerrainFloorLookupTable editors to
        // decide between the vanilla 21-entry pointer list and the patched
        // extended pointer table. (#442 / #441)

        public enum ExtendsBattleBG_extends
        {
            NO,
            Extends,
            NoCache = (int)NO_CACHE
        }

        static ExtendsBattleBG_extends g_Cache_ExtendsBattleBG = ExtendsBattleBG_extends.NoCache;

        public static void ClearCacheExtendsBattleBG()
        {
            g_Cache_ExtendsBattleBG = ExtendsBattleBG_extends.NoCache;
        }

        /// <summary>
        /// Detect whether the "Extends Battle BG" patch is installed in
        /// <see cref="CoreState.ROM"/>. Cached after first call; call
        /// <see cref="ClearCacheExtendsBattleBG"/> on ROM swap.
        /// </summary>
        public static ExtendsBattleBG_extends SearchExtendsBattleBG()
        {
            if (g_Cache_ExtendsBattleBG == ExtendsBattleBG_extends.NoCache)
            {
                g_Cache_ExtendsBattleBG = SearchExtendsBattleBG(CoreState.ROM);
            }
            return g_Cache_ExtendsBattleBG;
        }

        /// <summary>
        /// ROM-explicit detection (non-caching). Used by Avalonia view models
        /// that want to detect the patch without depending on CoreState.ROM
        /// being set — e.g. during scanner / test runs.
        /// </summary>
        public static ExtendsBattleBG_extends SearchExtendsBattleBG(ROM rom)
        {
            if (rom?.RomInfo == null) return ExtendsBattleBG_extends.NO;
            PatchTableSt[] table = new PatchTableSt[] {
                new PatchTableSt{ name="Extends", ver = "FE8J", addr = 0x58D1C, data = new byte[]{0x00, 0xB5, 0x05, 0x4B, 0xC9, 0x00}},
                new PatchTableSt{ name="Extends", ver = "FE8U", addr = 0x57ED0, data = new byte[]{0x00, 0xB5, 0x05, 0x4B, 0xC9, 0x00}},
            };
            string version = rom.RomInfo.VersionToFilename;
            foreach (PatchTableSt t in table)
            {
                if (t.ver != version) continue;
                byte[] data = rom.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;
                return ExtendsBattleBG_extends.Extends;
            }
            return ExtendsBattleBG_extends.NO;
        }

        // ---- BG256-color patch detection (used by ImageBG editor) ----

        /// <summary>
        /// Static patch table for the "BG256Color" feature (FE8J/FE8U).
        /// Cached so the array literal is allocated once.
        /// </summary>
        static readonly PatchTableSt[] BG256ColorTable = new PatchTableSt[] {
            new PatchTableSt{ name="BG256Color", ver = "FE8J", addr = 0xE532, data = new byte[]{0xC0, 0x46, 0xC0, 0x46}},
            new PatchTableSt{ name="BG256Color", ver = "FE8U", addr = 0xE2DA, data = new byte[]{0xC0, 0x46, 0xC0, 0x46}},
        };

        /// <summary>
        /// True when the "BG256Color" patch (FE8J/FE8U) is installed in
        /// the supplied <paramref name="rom"/>. Mirrors WinForms
        /// <c>PatchUtil.BG256Color() == BG256ColorPatch.BG256Color</c>.
        /// Used by the ImageBG editor to decide whether P4=0/1 are
        /// valid table-row flag values.
        ///
        /// Delegates to <see cref="SearchPatchBool(ROM, PatchTableSt[])"/>
        /// so the patch-table scan stays in one place — Copilot bot
        /// review on PR #517 flagged the inline loop as drift-prone.
        /// </summary>
        public static bool HasBG256ColorPatch(ROM rom)
        {
            return SearchPatchBool(rom, BG256ColorTable);
        }

        /// <summary>
        /// Ambient-ROM overload. Reads <see cref="CoreState.ROM"/> if
        /// available.
        /// </summary>
        public static bool HasBG256ColorPatch()
        {
            return HasBG256ColorPatch(CoreState.ROM);
        }

        // ---- Map second palette (Flag0x28) patch detection ----
        // Mirrors WinForms PatchUtil.SearchFlag0x28ToMapSecondPalettePatch.
        // Controls the per-map record offset of the second-palette PLIST
        // byte (offset 146 vs offset 45). Used by the map-PLIST label
        // resolver (#952) to read PLists.palette2_plist from the correct
        // offset, exactly like WF MapSettingForm.GetMapPListsWhereAddr.

        public enum MapSecondPalette_extends
        {
            NO,             // patch not installed (no second palette)
            Flag0x28_146,   // second-palette PLIST byte at map-record offset 146
            Flag0x28_45,    // second-palette PLIST byte at map-record offset 45
            NoCache = (int)NO_CACHE
        }

        static readonly PatchTableSt[] MapSecondPaletteTable = new PatchTableSt[] {
            new PatchTableSt{ name="Flag0x28_146", ver = "FE8J", addr = 0x19628, data = new byte[]{0x00, 0x4A}},
            new PatchTableSt{ name="Flag0x28_45",  ver = "FE8J", addr = 0x19628, data = new byte[]{0x00, 0x49}},
            new PatchTableSt{ name="Flag0x28_146", ver = "FE8U", addr = 0x19950, data = new byte[]{0x00, 0x4A}},
            new PatchTableSt{ name="Flag0x28_45",  ver = "FE8U", addr = 0x19950, data = new byte[]{0x00, 0x49}},
        };

        static MapSecondPalette_extends g_Cache_MapSecondPalette = MapSecondPalette_extends.NoCache;

        public static void ClearCacheMapSecondPalette()
        {
            g_Cache_MapSecondPalette = MapSecondPalette_extends.NoCache;
        }

        /// <summary>
        /// Ambient-ROM detection (cached). Reads <see cref="CoreState.ROM"/>.
        /// Mirrors WinForms <c>PatchUtil.SearchFlag0x28ToMapSecondPalettePatch()</c>.
        /// </summary>
        public static MapSecondPalette_extends SearchFlag0x28ToMapSecondPalettePatch()
        {
            if (g_Cache_MapSecondPalette == MapSecondPalette_extends.NoCache)
            {
                g_Cache_MapSecondPalette = SearchFlag0x28ToMapSecondPalettePatch(CoreState.ROM);
            }
            return g_Cache_MapSecondPalette;
        }

        /// <summary>
        /// ROM-explicit detection (non-caching). Returns
        /// <see cref="MapSecondPalette_extends.NO"/> on null ROM or any
        /// non-FE8J/FE8U version (the signature table only covers FE8J/FE8U,
        /// matching WF — FE6/FE7 never have the second-palette patch).
        /// </summary>
        public static MapSecondPalette_extends SearchFlag0x28ToMapSecondPalettePatch(ROM rom)
        {
            if (rom?.RomInfo == null) return MapSecondPalette_extends.NO;
            string version = rom.RomInfo.VersionToFilename;
            foreach (PatchTableSt t in MapSecondPaletteTable)
            {
                if (t.ver != version) continue;
                byte[] data = rom.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;
                if (t.name == "Flag0x28_146") return MapSecondPalette_extends.Flag0x28_146;
                if (t.name == "Flag0x28_45")  return MapSecondPalette_extends.Flag0x28_45;
            }
            return MapSecondPalette_extends.NO;
        }

        // ---- Generic patch search helpers ----

        public static bool SearchPatchBool(PatchTableSt[] table)
        {
            if (CoreState.ROM?.RomInfo == null) return false;
            PatchTableSt p = SearchPatch(table);
            return p.addr != 0;
        }

        /// <summary>
        /// ROM-parameterized variant of <see cref="SearchPatchBool(PatchTableSt[])"/>.
        /// Used by ROM-specific detection helpers (e.g.
        /// <see cref="HasBG256ColorPatch(ROM)"/>) that need to avoid
        /// depending on the ambient <see cref="CoreState.ROM"/>.
        /// (Copilot bot review on PR #517 — single source of truth
        /// across all patch detections.)
        /// </summary>
        public static bool SearchPatchBool(ROM rom, PatchTableSt[] table)
        {
            if (rom?.RomInfo == null) return false;
            PatchTableSt p = SearchPatch(rom, table);
            return p.addr != 0;
        }

        /// <summary>
        /// ROM-parameterized variant of <see cref="SearchPatch(PatchTableSt[])"/>.
        /// </summary>
        public static PatchTableSt SearchPatch(ROM rom, PatchTableSt[] table)
        {
            if (rom?.RomInfo == null) return default;
            string version = rom.RomInfo.VersionToFilename;
            foreach (PatchTableSt t in table)
            {
                if (t.ver != version) continue;
                byte[] data = rom.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;
                return t;
            }
            return default;
        }

        public static PatchTableSt SearchPatch(PatchTableSt[] table)
        {
            if (CoreState.ROM?.RomInfo == null) return default;
            string version = CoreState.ROM.RomInfo.VersionToFilename;
            foreach (PatchTableSt t in table)
            {
                if (t.ver != version) continue;
                byte[] data = CoreState.ROM.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;
                return t;
            }
            return default;
        }

        /// <summary>
        /// Clear all cached detection results (called on ROM change).
        /// </summary>
        public static void ClearAllCaches()
        {
            g_Cache_draw_font_enum = draw_font_enum.NoCache;
            g_Cache_TextEngineRework_enum = TextEngineRework_enum.NoCache;
            g_Cache_ExtendsBattleBG = ExtendsBattleBG_extends.NoCache;
            g_Cache_MapSecondPalette = MapSecondPalette_extends.NoCache;
            g_Cache_ItemUsingExtends = ItemUsingExtends_extends.NoCache;
            g_Cache_class_type = class_type_extends.NoCache;
            g_Cache_growth_mod = growth_mod_extends.NoCache;
        }

        // ---- OPClassReel patch detectors (extracted from PatchUtil for gap-sweep #419) ----
        //
        // The corresponding WinForms cache-bearing wrappers in PatchUtil.cs
        // delegate to these methods so there's a single source of truth.
        // No cache here — Avalonia callers re-evaluate per ROM load.

        static readonly PatchTableSt[] OPClassReelAnimationIDOver255Table = new PatchTableSt[] {
            new PatchTableSt{ name="Over255", ver = "FE8J", addr = 0xB86B0, data = new byte[]{0x59, 0x8A}},
        };

        /// <summary>
        /// True when the "Over255" OPClassReel patch (FE8J only) is
        /// installed in <paramref name="rom"/>. Mirrors WinForms
        /// <c>PatchUtil.OPClassReelAnimationIDOver255() == Over255</c>.
        ///
        /// Returns false on null ROM and on any non-FE8J version
        /// (signature table only contains FE8J).
        /// </summary>
        public static bool OPClassReelAnimationIDOver255Detect(ROM rom)
        {
            return SearchPatchBool(rom, OPClassReelAnimationIDOver255Table);
        }

        static readonly PatchTableSt[] OPClassReelSortPatchTable = new PatchTableSt[] {
            new PatchTableSt{ name="OPClassReelSort", ver = "FE8J", addr = 0xB8C80, data = new byte[]{0x04, 0x4B, 0x1B, 0x68}},
            new PatchTableSt{ name="OPClassReelSort", ver = "FE8U", addr = 0xB40EC, data = new byte[]{0x04, 0x4B, 0x1B, 0x68}},
        };

        /// <summary>
        /// True when the "OPClassReelSort" patch (FE8J or FE8U) is
        /// installed in <paramref name="rom"/>. Mirrors WinForms
        /// <c>PatchUtil.OPClassReelSortPatch() == OPClassReelSort</c>.
        /// </summary>
        public static bool OPClassReelSortPatchDetect(ROM rom)
        {
            return SearchPatchBool(rom, OPClassReelSortPatchTable);
        }

        // ---- ChapterNameToText patch detector (#1029) ----
        // Cross-platform port of WinForms PatchUtil.SearchChapterNameToTextPatch.
        // Used as the headless default precondition for the JP chapter-name wipe
        // (ToolTranslateROMCore.WipeJPTitle): the wipe rewrites chapter-title
        // pointers to text only when this patch is installed, mirroring WF
        // HowDoYouLikePatchForm.CheckAndShowPopupDialog(ChapterNameText) which
        // returns true only when the patch is present / freshly installed.

        static readonly PatchTableSt[] ChapterNameToTextPatchTable = new PatchTableSt[] {
            new PatchTableSt{ name="ChapterNameToText", ver = "FE8J", addr = 0x8B894, data = new byte[]{0x00, 0x4B, 0x18, 0x47}},
            new PatchTableSt{ name="ChapterNameToText", ver = "FE8U", addr = 0x89624, data = new byte[]{0x00, 0x4B, 0x18, 0x47}},
        };

        /// <summary>
        /// True when the "ChapterNameToText" patch (FE8J or FE8U) is installed in
        /// <paramref name="rom"/>. Cross-platform port of WinForms
        /// <c>PatchUtil.SearchChapterNameToTextPatch</c>. Returns false on null
        /// ROM and on any non-FE8 version (signature table only contains FE8J/U).
        /// </summary>
        public static bool SearchChapterNameToTextPatch(ROM rom)
        {
            return SearchPatchBool(rom, ChapterNameToTextPatchTable);
        }

        // ---- AntiHuffman (un-Huffman) patch detector (#1028 Slice D) ----
        //
        // Cross-platform port of WinForms PatchUtil.SearchAntiHuffmanPatch_Low
        // (PatchUtil.cs:333-345). Detects whether the un-Huffman text patch is
        // installed so the Text Editor's WriteText flow can decide between the
        // Huffman-encode fallback and the "install AntiHuffman" prompt — exactly
        // like WF TextForm.WriteText / NeedAntiHuffman.
        //
        // The same six signatures previously lived ONLY in Avalonia
        // PatchDetectionService.DetectAntiHuffman; that method now delegates here
        // so there is a single source of truth (no third copy in PatchMetadataCore,
        // which is about config patch metadata). Covers both FE8U signatures at
        // 0x2BA4 — the normal one and AntiHuffman_snake1.

        static readonly PatchTableSt[] AntiHuffmanTable = new PatchTableSt[] {
            new PatchTableSt{ name="AntiHuffman",        ver = "FE6",  addr = 0x384c,  data = new byte[]{0x03, 0xB5, 0x02, 0xB0}},
            new PatchTableSt{ name="AntiHuffman",        ver = "FE7J", addr = 0x13324, data = new byte[]{0x02, 0x49, 0x28, 0x1C}},
            new PatchTableSt{ name="AntiHuffman",        ver = "FE8J", addr = 0x2af4,  data = new byte[]{0x00, 0xB5, 0xC2, 0x0F}},
            new PatchTableSt{ name="AntiHuffman",        ver = "FE7U", addr = 0x12C6C, data = new byte[]{0x02, 0x49, 0x28, 0x1C}},
            new PatchTableSt{ name="AntiHuffman",        ver = "FE8U", addr = 0x2BA4,  data = new byte[]{0x00, 0xB5, 0xC2, 0x0F}},
            new PatchTableSt{ name="AntiHuffman_snake1", ver = "FE8U", addr = 0x2ba4,  data = new byte[]{0x78, 0x47, 0xC0, 0x46}},
        };

        /// <summary>
        /// True when the "AntiHuffman" (un-Huffman) text patch is installed in
        /// <paramref name="rom"/>. Cross-platform port of WinForms
        /// <c>PatchUtil.SearchAntiHuffmanPatch</c>. Returns false on null ROM and
        /// on any version whose signature does not match. Covers both FE8U
        /// signatures at 0x2BA4 (normal + AntiHuffman_snake1).
        /// </summary>
        public static bool SearchAntiHuffmanPatch(ROM rom)
        {
            return SearchPatchBool(rom, AntiHuffmanTable);
        }

        /// <summary>
        /// Ambient-ROM overload. Reads <see cref="CoreState.ROM"/>.
        /// </summary>
        public static bool SearchAntiHuffmanPatch()
        {
            return SearchAntiHuffmanPatch(CoreState.ROM);
        }

        // ---- Class skill extends (SkillSystem) ----
        // The CSkillSys "class skill extends" patch detector now lives in
        // FEBuilderGBA.Core/SkillSystemPatchScanner.cs as
        // SkillSystemPatchScanner.IsClassSkillExtends(rom) — see the
        // duplicate-source consolidation done by gap-sweep #415 +
        // gap-sweep #416. Both WinForms (SkillConfigSkillSystemForm.cs)
        // and Avalonia (SkillAssignmentClassSkillSystemViewModel,
        // SkillAssignmentClassSkillSystemViewModel) route through that
        // single helper.

        // ---- UnitActionRework patch detector (#1261 slice 2ad) ----
        // Cross-platform port of WinForms PatchUtil.SearchUnitActionReworkPatch
        // (PatchUtil.cs:548). Gates UnitActionPointerForm's base resolution
        // (SearchActionPointer): when the rework patch is installed the unit-action
        // function-pointer table moves and its entries are masked (& 0x0FFFFFFF).
        // The producer (RebuildProducerCore.EmitUnitActionPointer) reads this to
        // pick the base + IsDataExists rule. A wrong answer relocates the wrong
        // ASM-pointer table — silent ROM corruption.
        //
        // This is a thin delegate wrapper (NOT a byte-table): every per-version
        // RomInfo subclass already carries the (addr, expected_value) pair via the
        // virtual patch_unitaction_rework_hack(out enable_value) accessor in Core
        // (ROMFE*.cs), so we reproduce the WF logic verbatim — read u32(address),
        // compare to enable_value — guarding the full 4-byte read.

        /// <summary>
        /// True when the "UnitActionRework" patch is installed in <paramref name="rom"/>.
        /// Verbatim port of WinForms <c>PatchUtil.SearchUnitActionReworkPatch</c>: the
        /// per-version RomInfo accessor <c>patch_unitaction_rework_hack(out enable_value)</c>
        /// supplies the (address, expected u32) pair; the patch is present when
        /// <c>u32(address) == enable_value</c>. Returns false on a null ROM or when the
        /// accessor reports address 0 (FE versions without the patch concept).
        /// </summary>
        public static bool SearchUnitActionReworkPatch(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            uint check_value;
            uint address = rom.RomInfo.patch_unitaction_rework_hack(out check_value);
            if (address == 0)
            {
                return false;
            }
            // Guard the full 4-byte u32 read (address + 3) on a truncated/synthetic ROM —
            // WF reads Program.ROM.u32(address) unconditionally; on a valid ROM the patch
            // slot is never near EOF, so this is output-equivalent.
            if (!U.isSafetyOffset(address + 3, rom))
            {
                return false;
            }
            uint a = rom.u32(address);
            return (a == check_value);
        }

        /// <summary>Ambient-ROM overload. Reads <see cref="CoreState.ROM"/>.</summary>
        public static bool SearchUnitActionReworkPatch()
        {
            return SearchUnitActionReworkPatch(CoreState.ROM);
        }

        // ---- ItemUsingExtends (IER) patch detector (#1261 slice 2ad) ----
        // Cross-platform port of WinForms PatchUtil.ItemUsingExtendsPatch
        // (PatchUtil.cs:1936). Gates ItemForm's StatBooster sub-block size: when IER
        // is installed AND the item's passive-booster bit 0x80 is set, the booster
        // block is 20 bytes (vanilla 12). A fixed-address signature table (FE8U only).

        public enum ItemUsingExtends_extends
        {
            NO,
            IER,
            NoCache = (int)NO_CACHE
        }

        static readonly PatchTableSt[] ItemUsingExtendsTable = new PatchTableSt[] {
            new PatchTableSt{ name="IER", ver = "FE8U", addr = 0x28E80, data = new byte[]{0x03, 0x4B, 0x14, 0x22, 0x50, 0x43, 0x40, 0x18, 0xC0, 0x18, 0x00, 0x68, 0x70, 0x47, 0x00, 0x00}},
        };

        /// <summary>
        /// Detect the "ItemUsingExtends" (IER) patch in <paramref name="rom"/>. Verbatim
        /// port of WinForms <c>PatchUtil.ItemUsingExtendsPatchLow</c> — a single FE8U
        /// signature at 0x28E80. Returns <see cref="ItemUsingExtends_extends.NO"/> on a
        /// null ROM or any non-FE8U version (the table only contains FE8U).
        /// </summary>
        public static ItemUsingExtends_extends ItemUsingExtendsPatch(ROM rom)
        {
            if (SearchPatchBool(rom, ItemUsingExtendsTable))
            {
                return ItemUsingExtends_extends.IER;
            }
            return ItemUsingExtends_extends.NO;
        }

        static ItemUsingExtends_extends g_Cache_ItemUsingExtends = ItemUsingExtends_extends.NoCache;

        public static void ClearCacheItemUsingExtends()
        {
            g_Cache_ItemUsingExtends = ItemUsingExtends_extends.NoCache;
        }

        /// <summary>Ambient-ROM overload (cached). Reads <see cref="CoreState.ROM"/>.</summary>
        public static ItemUsingExtends_extends ItemUsingExtendsPatch()
        {
            if (g_Cache_ItemUsingExtends == ItemUsingExtends_extends.NoCache)
            {
                g_Cache_ItemUsingExtends = ItemUsingExtendsPatch(CoreState.ROM);
            }
            return g_Cache_ItemUsingExtends;
        }

        // ---- ClassType (SkillSystems effectiveness rework) detector (#1261 slice 2ad) ----
        // Cross-platform port of WinForms PatchUtil.SearchClassType ->
        // SearchSkillSystemsEffectivenesReworkLow (PatchUtil.cs:280-306). Gates
        // ItemForm's ItemEffectiveness sub-block: when the rework is installed the
        // effectiveness list is a block-4 (class-type) table whose length is
        // (count+1)*4 instead of the vanilla block-1 (count+1). Uses CompareByte at
        // a single fixed address (0x2AAEC) against TWO alternative byte patterns,
        // FE8U-only (version 8 && !is_multibyte) — verbatim.

        public enum class_type_extends
        {
            NO,
            SkillSystems_Rework,
            NoCache = (int)NO_CACHE
        }

        /// <summary>
        /// Detect the "SkillSystems effectiveness rework" (class-type) patch in
        /// <paramref name="rom"/>. Verbatim port of WinForms
        /// <c>PatchUtil.SearchSkillSystemsEffectivenesReworkLow</c>: only on FE8U
        /// (version 8 &amp;&amp; !is_multibyte), <c>CompareByte(0x2AAEC, ..)</c> against
        /// either of two 8-byte patterns. Returns <see cref="class_type_extends.NO"/> on
        /// a null ROM or any other version. CompareByte is itself EOF-safe.
        /// </summary>
        public static class_type_extends SearchClassType(ROM rom)
        {
            if (rom?.RomInfo == null) return class_type_extends.NO;
            if (rom.RomInfo.version == 8 && rom.RomInfo.is_multibyte == false)
            {
                bool r = rom.CompareByte(0x2AAEC,
                    new byte[] { 0x00, 0x25, 0x00, 0x28, 0x00, 0xD0, 0x05, 0x1C });
                if (r)
                {
                    return class_type_extends.SkillSystems_Rework;
                }
                r = rom.CompareByte(0x2AAEC,
                    new byte[] { 0x01, 0x4B, 0xA6, 0xF0, 0xED, 0xFE, 0x01, 0xE0 });
                if (r)
                {
                    return class_type_extends.SkillSystems_Rework;
                }
            }
            return class_type_extends.NO;
        }

        static class_type_extends g_Cache_class_type = class_type_extends.NoCache;

        public static void ClearCacheClassType()
        {
            g_Cache_class_type = class_type_extends.NoCache;
        }

        /// <summary>Ambient-ROM overload (cached). Reads <see cref="CoreState.ROM"/>.</summary>
        public static class_type_extends SearchClassType()
        {
            if (g_Cache_class_type == class_type_extends.NoCache)
            {
                g_Cache_class_type = SearchClassType(CoreState.ROM);
            }
            return g_Cache_class_type;
        }

        // ---- GrowsMod patch detector (#1261 slice 2ad) ----
        // Cross-platform port of WinForms PatchUtil.SearchGrowsMod ->
        // SearchGrowsModLow (PatchUtil.cs:138-169). Gates ItemForm's StatBooster
        // sub-block size: Vennou -> 16 bytes (when the item's +34 flag == 1),
        // SkillSystems -> 20 bytes (same flag). Two-stage detection, verbatim:
        //   1. Vennou: a fixed-address byte table (FE8U @ 0x02BA2A).
        //   2. SkillSystems: a ROM-WIDE U.Grep of a long signature, scanned from
        //      compress_image_borderline_address with alignment 4 (reproduces the WF
        //      GrepPatch(Grep2PatchTableSt[]) helper — same start/blocksize/safety).
        // FE8U-only on both stages (matching the WF table `ver` fields).

        public enum growth_mod_extends
        {
            NO,
            Vennou,
            SkillSystems,
            NoCache = (int)NO_CACHE
        }

        static readonly PatchTableSt[] GrowsModVennouTable = new PatchTableSt[] {
            new PatchTableSt{ name="GrowsMod", ver = "FE8U", addr = 0x02BA2A, data = new byte[]{0x4E, 0x46, 0x45, 0x46, 0x60, 0xB4, 0x8B, 0xB0, 0x07, 0x1C, 0xFF, 0xF7, 0xDE, 0xFF, 0x00, 0x06, 0x00, 0x28, 0x00, 0xD1, 0x8E, 0xE0, 0x78, 0x7A, 0x63, 0x28, 0x00, 0xD8, 0x8A, 0xE0, 0x03, 0x1C, 0x64, 0x3B, 0x7B, 0x72, 0x38, 0x7A, 0x42, 0x1C, 0x3A, 0x72, 0x38, 0x68, 0x79, 0x68, 0x80, 0x6A, 0x89, 0x6A, 0x08, 0x43, 0x80, 0x21, 0x09, 0x03, 0x08, 0x40, 0x00, 0x28, 0x04, 0xD0, 0x10, 0x06, 0x00, 0x16, 0x0A, 0x28, 0x0B, 0xD1, 0x03, 0xE0, 0x10, 0x06, 0x00}},
        };

        // SkillSystems is found by a whole-ROM grep (not a fixed addr) — reproduces the
        // WF Grep2PatchTableSt path. We keep the version + signature here; the scan is
        // done in SearchGrowsMod below (it needs rom.Data + the borderline address).
        static readonly byte[] GrowsModSkillSystemsSignature = new byte[] {
            0x17, 0x49, 0x40, 0x18, 0x22, 0x21, 0x41, 0x5C, 0x01, 0x22, 0x11, 0x42, 0x06, 0xD0, 0xC0, 0x68,
            0x00, 0x28, 0x03, 0xD0, 0x80, 0x57, 0x2D, 0x18, 0x00, 0x2F, 0x02, 0xD0, 0x02, 0x33, 0x08, 0x2B,
            0xE5, 0xDD, 0x20, 0x1C, 0x10, 0x49, 0x0F, 0x4A
        };

        /// <summary>
        /// Detect the "GrowsMod" StatBooster-extension patch in <paramref name="rom"/>.
        /// Verbatim port of WinForms <c>PatchUtil.SearchGrowsModLow</c>:
        /// <list type="number">
        ///   <item><b>Vennou</b> — a fixed FE8U byte table at 0x02BA2A.</item>
        ///   <item><b>SkillSystems</b> — a whole-ROM <see cref="U.Grep"/> of the
        ///   SkillSystems signature, scanned from
        ///   <c>compress_image_borderline_address</c> with blocksize 4, accepted only
        ///   when the hit is a safe offset (reproduces WF <c>GrepPatch</c>). FE8U only.</item>
        /// </list>
        /// Returns <see cref="growth_mod_extends.NO"/> on a null ROM or any non-FE8U
        /// version (both stages are FE8U-gated, matching WF).
        /// </summary>
        public static growth_mod_extends SearchGrowsMod(ROM rom)
        {
            if (rom?.RomInfo == null) return growth_mod_extends.NO;

            // Stage 1 — Vennou (fixed-address table; SearchPatchBool already version-gates).
            if (SearchPatchBool(rom, GrowsModVennouTable))
            {
                return growth_mod_extends.Vennou;
            }

            // Stage 2 — SkillSystems (whole-ROM grep, FE8U only). Reproduces WF
            // GrepPatch(Grep2PatchTableSt[]): U.Grep(rom.Data, signature,
            // compress_image_borderline_address, 0, 4) and require isSafetyOffset(hit).
            if (rom.RomInfo.VersionToFilename == "FE8U")
            {
                uint addr = U.Grep(rom.Data, GrowsModSkillSystemsSignature,
                    rom.RomInfo.compress_image_borderline_address, 0, 4);
                // Use the (addr, rom) safety overload — the no-arg isSafetyOffset(addr) reads
                // CoreState.ROM, which need not be `rom` when a detector runs outside the producer.
                if (addr != U.NOT_FOUND && U.isSafetyOffset(addr, rom))
                {
                    return growth_mod_extends.SkillSystems;
                }
            }

            return growth_mod_extends.NO;
        }

        static growth_mod_extends g_Cache_growth_mod = growth_mod_extends.NoCache;

        public static void ClearCacheGrowsMod()
        {
            g_Cache_growth_mod = growth_mod_extends.NoCache;
        }

        /// <summary>Ambient-ROM overload (cached). Reads <see cref="CoreState.ROM"/>.</summary>
        public static growth_mod_extends SearchGrowsMod()
        {
            if (g_Cache_growth_mod == growth_mod_extends.NoCache)
            {
                g_Cache_growth_mod = SearchGrowsMod(CoreState.ROM);
            }
            return g_Cache_growth_mod;
        }
    }
}
