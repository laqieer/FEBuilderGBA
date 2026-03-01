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

        // ---- Generic patch search helpers ----

        public static bool SearchPatchBool(PatchTableSt[] table)
        {
            PatchTableSt p = SearchPatch(table);
            return p.addr != 0;
        }

        public static PatchTableSt SearchPatch(PatchTableSt[] table)
        {
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
        }
    }
}
