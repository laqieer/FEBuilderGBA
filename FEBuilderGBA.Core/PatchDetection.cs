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

        // ---- Class skill extends (SkillSystem) ----
        // The CSkillSys "class skill extends" patch detector now lives in
        // FEBuilderGBA.Core/SkillSystemPatchScanner.cs as
        // SkillSystemPatchScanner.IsClassSkillExtends(rom) — see the
        // duplicate-source consolidation done by gap-sweep #415 +
        // gap-sweep #416. Both WinForms (SkillConfigSkillSystemForm.cs)
        // and Avalonia (SkillAssignmentClassCSkillSysViewModel,
        // SkillAssignmentClassSkillSystemViewModel) route through that
        // single helper.
    }
}
