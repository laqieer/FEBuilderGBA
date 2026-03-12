using System;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Singleton service that caches ROM patch detection results for the Avalonia UI.
    /// Wraps existing Core detection methods (PatchDetection, MagicSplitUtil) and
    /// adds byte-pattern checks for patches not yet in Core (SkillSystem, Vennou, etc.).
    /// Call <see cref="Refresh"/> after ROM load or patch install to re-scan.
    /// </summary>
    public sealed class PatchDetectionService
    {
        // ---- Singleton ----
        public static PatchDetectionService Instance { get; } = new PatchDetectionService();
        private PatchDetectionService() { }

        // ---- Cached results (default: not detected) ----

        /// <summary>Skill system variant installed in the ROM.</summary>
        public SkillSystemType SkillSystem { get; private set; } = SkillSystemType.None;

        /// <summary>Magic split patch variant.</summary>
        public MagicSplitType MagicSplit { get; private set; } = MagicSplitType.None;

        /// <summary>True if Vennou weapon lock array patch is installed (FE8U only).</summary>
        public bool VennouWeaponLock { get; private set; }

        /// <summary>True if Item Effect Range (IER) patch is installed.</summary>
        public bool ItemEffectRange { get; private set; }

        /// <summary>Portrait extension type (HALFBODY, MUG_EXCEED, or none).</summary>
        public PortraitExtendsType PortraitExtends { get; private set; } = PortraitExtendsType.None;

        /// <summary>True if BG 256-color patch is installed.</summary>
        public bool BG256Color { get; private set; }

        /// <summary>True if anti-Huffman text patch is installed.</summary>
        public bool AntiHuffman { get; private set; }

        /// <summary>SkillSystems effectiveness rework class type extension.</summary>
        public bool SkillSystemsClassTypeRework { get; private set; }

        /// <summary>Draw font patch type from Core PatchDetection.</summary>
        public PatchDetection.draw_font_enum DrawFont { get; private set; } = PatchDetection.draw_font_enum.NO;

        /// <summary>Text engine rework from Core PatchDetection.</summary>
        public PatchDetection.TextEngineRework_enum TextEngineRework { get; private set; } = PatchDetection.TextEngineRework_enum.NO;

        // ---- Enums ----

        public enum SkillSystemType
        {
            None,
            SkillSystem,        // FE8U SkillSystems
            FE8N,               // FE8J base
            FE8N_Ver2,          // FE8J version 2
            FE8N_Ver3,          // FE8J version 3
            Yugudora,           // FE8J yugudora
            Midori,             // FE8J midori
            CSkillSys09x,       // FE8U CSkillSys 0.9x
            CSkillSys300,       // FE8U CSkillSys 3.00+
        }

        public enum MagicSplitType
        {
            None,
            FE8N,       // FE8J FE8N magic split
            FE7U,       // FE7U magic split
            FE8U,       // FE8U magic split
        }

        public enum PortraitExtendsType
        {
            None,
            MugExceed,  // tiki portrait extension
            HalfBody,   // upper body extension
        }

        // ---- Refresh (re-scan ROM) ----

        /// <summary>
        /// Re-scan the currently loaded ROM for all known patches.
        /// Should be called after ROM load or patch install/uninstall.
        /// </summary>
        public void Refresh()
        {
            // Reset everything
            SkillSystem = SkillSystemType.None;
            MagicSplit = MagicSplitType.None;
            VennouWeaponLock = false;
            ItemEffectRange = false;
            PortraitExtends = PortraitExtendsType.None;
            BG256Color = false;
            AntiHuffman = false;
            SkillSystemsClassTypeRework = false;
            DrawFont = PatchDetection.draw_font_enum.NO;
            TextEngineRework = PatchDetection.TextEngineRework_enum.NO;

            // Clear Core caches so they re-evaluate
            PatchDetection.ClearAllCaches();
            MagicSplitUtil.ClearCache();

            if (CoreState.ROM?.RomInfo == null)
                return;

            // Use Core methods where available
            DrawFont = PatchDetection.SearchDrawFontPatch();
            TextEngineRework = PatchDetection.SearchTextEngineReworkPatch();
            AntiHuffman = DetectAntiHuffman();

            // Detect patches via ROM byte patterns
            SkillSystem = DetectSkillSystem();
            MagicSplit = DetectMagicSplit();
            VennouWeaponLock = DetectVennouWeaponLock();
            ItemEffectRange = DetectIER();
            PortraitExtends = DetectPortraitExtends();
            BG256Color = DetectBG256Color();
            SkillSystemsClassTypeRework = DetectClassTypeRework();
        }

        // ---- Private detection methods ----

        static SkillSystemType DetectSkillSystem()
        {
            ROM rom = CoreState.ROM!;
            string version = rom.RomInfo.VersionToFilename;

            // Byte-pattern table (mirrors WinForms PatchUtil.SearchSkillSystemLow)
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt { name = "yugudora",     ver = "FE8J", addr = 0xEE594, data = new byte[] { 0x4B, 0xFA, 0x2F, 0x59 } },
                new PatchDetection.PatchTableSt { name = "FE8N",         ver = "FE8J", addr = 0x89268, data = new byte[] { 0x00, 0x4B, 0x9F, 0x46 } },
                new PatchDetection.PatchTableSt { name = "midori",       ver = "FE8J", addr = 0xFE58E0, data = new byte[] { 0x05, 0x1C, 0x00, 0xF0, 0x25, 0xF8, 0x01, 0x29, 0x04, 0xD0, 0x28, 0x1C, 0x00, 0xF0, 0x28, 0xF8 } },
                new PatchDetection.PatchTableSt { name = "SkillSystem",  ver = "FE8U", addr = 0x2ACF8, data = new byte[] { 0x70, 0x47 } },
                new PatchDetection.PatchTableSt { name = "CSkillSys09x", ver = "FE8U", addr = 0xB2A604, data = new byte[] { 0x43, 0x53, 0x4B, 0x49, 0x4C, 0x4C, 0x53, 0x59, 0x53, 0x5F, 0x4B, 0x2D, 0x30, 0x39, 0x78, 0x00 } },
                new PatchDetection.PatchTableSt { name = "CSkillSys100", ver = "FE8U", addr = 0xB2A604, data = new byte[] { 0x43, 0x53, 0x4B, 0x49, 0x4C, 0x4C, 0x53, 0x59, 0x53, 0x5F, 0x4B, 0x2D, 0x31, 0x30, 0x30, 0x00 } },
                new PatchDetection.PatchTableSt { name = "CSkillSys300", ver = "FE8U", addr = 0xB2A604, data = new byte[] { 0x43, 0x53, 0x4B, 0x49, 0x4C, 0x4C, 0x53, 0x59, 0x53, 0x5F, 0x4B, 0x2D, 0x33 } },
            };

            foreach (var t in table)
            {
                if (t.ver != version) continue;
                byte[] data = rom.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;

                switch (t.name)
                {
                    case "yugudora":     return SkillSystemType.Yugudora;
                    case "FE8N":         return DetectFE8NVariant(rom);
                    case "midori":       return SkillSystemType.Midori;
                    case "SkillSystem":  return SkillSystemType.SkillSystem;
                    case "CSkillSys09x": return SkillSystemType.CSkillSys09x;
                    case "CSkillSys100": return SkillSystemType.CSkillSys09x; // same as 09x per WinForms
                    case "CSkillSys300": return SkillSystemType.CSkillSys300;
                }
            }

            return SkillSystemType.None;
        }

        /// <summary>
        /// Distinguish FE8N ver1/ver2/ver3 by checking icon pointer patterns.
        /// Simplified from WinForms SkillConfigFE8NVer2/3SkillForm.IsFE8NVer2/3().
        /// Ver3 has a specific pattern at 0x89280; Ver2 has pointers at 0x89274.
        /// Falls back to base FE8N if neither matches.
        /// </summary>
        static SkillSystemType DetectFE8NVariant(ROM rom)
        {
            // Ver3 check: look for valid pointer at 0x89280 (icon table extension)
            if (rom.RomInfo.VersionToFilename == "FE8J")
            {
                uint ver3Addr = 0x89280;
                if (U.isSafetyOffset(ver3Addr + 4) && ver3Addr + 4 < rom.Data.Length)
                {
                    uint p = rom.p32(ver3Addr);
                    if (U.isSafetyOffset(p))
                        return SkillSystemType.FE8N_Ver3;
                }

                // Ver2 check: look for valid pointers at 0x89274 (5 pointers)
                uint ver2Addr = 0x89274;
                if (U.isSafetyOffset(ver2Addr + 20) && ver2Addr + 20 < rom.Data.Length)
                {
                    bool allValid = true;
                    for (int i = 0; i < 5; i++)
                    {
                        uint p = rom.p32(ver2Addr + (uint)(i * 4));
                        if (!U.isSafetyOffset(p))
                        {
                            allValid = false;
                            break;
                        }
                    }
                    if (allValid)
                        return SkillSystemType.FE8N_Ver2;
                }
            }

            return SkillSystemType.FE8N;
        }

        static MagicSplitType DetectMagicSplit()
        {
            // Delegate to Core's MagicSplitUtil
            var result = MagicSplitUtil.SearchMagicSplit();
            return result switch
            {
                MagicSplitUtil.magic_split_enum.FE8NMAGIC => MagicSplitType.FE8N,
                MagicSplitUtil.magic_split_enum.FE7UMAGIC => MagicSplitType.FE7U,
                MagicSplitUtil.magic_split_enum.FE8UMAGIC => MagicSplitType.FE8U,
                _ => MagicSplitType.None,
            };
        }

        static bool DetectVennouWeaponLock()
        {
            ROM rom = CoreState.ROM!;
            // FE8U only
            if (rom.RomInfo.version != 8 || rom.RomInfo.is_multibyte)
                return false;

            // Check the hook signature at 0x16DD8
            uint data = rom.u32(0x16DD8);
            return data == 0xFF3D3C00;
        }

        static bool DetectIER()
        {
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt
                {
                    name = "IER", ver = "FE8U", addr = 0x28E80,
                    data = new byte[] { 0x03, 0x4B, 0x14, 0x22, 0x50, 0x43, 0x40, 0x18, 0xC0, 0x18, 0x00, 0x68, 0x70, 0x47, 0x00, 0x00 }
                },
            };
            return PatchDetection.SearchPatchBool(table);
        }

        static PortraitExtendsType DetectPortraitExtends()
        {
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt { name = "MUG_EXCEED", ver = "FE8J", addr = 0x54da, data = new byte[] { 0xC0, 0x46, 0x01, 0xB0, 0x03, 0x4B } },
                new PatchDetection.PatchTableSt { name = "MUG_EXCEED", ver = "FE8U", addr = 0x55D2, data = new byte[] { 0xC0, 0x46, 0x01, 0xB0, 0x03, 0x4B } },
                new PatchDetection.PatchTableSt { name = "MUG_EXCEED", ver = "FE7U", addr = 0x6BCA, data = new byte[] { 0xC0, 0x46, 0x01, 0xB0, 0x03, 0x4B } },
                new PatchDetection.PatchTableSt { name = "MUG_EXCEED", ver = "FE7J", addr = 0x6A5A, data = new byte[] { 0xC0, 0x46, 0x01, 0xB0, 0x03, 0x4B } },
                new PatchDetection.PatchTableSt { name = "HALFBODY",   ver = "FE8U", addr = 0x8540, data = new byte[] { 0x0A, 0x1C } },
                new PatchDetection.PatchTableSt { name = "HALFBODY",   ver = "FE8J", addr = 0x843C, data = new byte[] { 0x0A, 0x1C } },
                new PatchDetection.PatchTableSt { name = "HALFBODY",   ver = "FE8U", addr = 0x8540, data = new byte[] { 0x01, 0x3A } },
                new PatchDetection.PatchTableSt { name = "HALFBODY",   ver = "FE8J", addr = 0x843C, data = new byte[] { 0x01, 0x3A } },
            };

            ROM rom = CoreState.ROM!;
            string version = rom.RomInfo.VersionToFilename;
            foreach (var t in table)
            {
                if (t.ver != version) continue;
                byte[] data = rom.getBinaryData(t.addr, t.data.Length);
                if (U.memcmp(t.data, data) != 0) continue;
                if (t.name == "MUG_EXCEED") return PortraitExtendsType.MugExceed;
                if (t.name == "HALFBODY") return PortraitExtendsType.HalfBody;
            }
            return PortraitExtendsType.None;
        }

        static bool DetectBG256Color()
        {
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt { name = "BG256Color", ver = "FE8J", addr = 0xE532, data = new byte[] { 0xC0, 0x46, 0xC0, 0x46 } },
                new PatchDetection.PatchTableSt { name = "BG256Color", ver = "FE8U", addr = 0xE2DA, data = new byte[] { 0xC0, 0x46, 0xC0, 0x46 } },
            };
            return PatchDetection.SearchPatchBool(table);
        }

        static bool DetectAntiHuffman()
        {
            var table = new PatchDetection.PatchTableSt[]
            {
                new PatchDetection.PatchTableSt { name = "AntiHuffman",        ver = "FE6",  addr = 0x384c,  data = new byte[] { 0x03, 0xB5, 0x02, 0xB0 } },
                new PatchDetection.PatchTableSt { name = "AntiHuffman",        ver = "FE7J", addr = 0x13324, data = new byte[] { 0x02, 0x49, 0x28, 0x1C } },
                new PatchDetection.PatchTableSt { name = "AntiHuffman",        ver = "FE8J", addr = 0x2af4,  data = new byte[] { 0x00, 0xB5, 0xC2, 0x0F } },
                new PatchDetection.PatchTableSt { name = "AntiHuffman",        ver = "FE7U", addr = 0x12C6C, data = new byte[] { 0x02, 0x49, 0x28, 0x1C } },
                new PatchDetection.PatchTableSt { name = "AntiHuffman",        ver = "FE8U", addr = 0x2BA4,  data = new byte[] { 0x00, 0xB5, 0xC2, 0x0F } },
                new PatchDetection.PatchTableSt { name = "AntiHuffman_snake1", ver = "FE8U", addr = 0x2ba4,  data = new byte[] { 0x78, 0x47, 0xC0, 0x46 } },
            };
            return PatchDetection.SearchPatchBool(table);
        }

        static bool DetectClassTypeRework()
        {
            ROM rom = CoreState.ROM!;
            if (rom.RomInfo.version != 8 || rom.RomInfo.is_multibyte)
                return false;

            // Two known signatures at 0x2AAEC
            if (rom.CompareByte(0x2AAEC, new byte[] { 0x00, 0x25, 0x00, 0x28, 0x00, 0xD0, 0x05, 0x1C }))
                return true;
            if (rom.CompareByte(0x2AAEC, new byte[] { 0x01, 0x4B, 0xA6, 0xF0, 0xED, 0xFE, 0x01, 0xE0 }))
                return true;

            return false;
        }

        // ---- Convenience helpers ----

        /// <summary>True if any skill system is installed.</summary>
        public bool HasSkillSystem => SkillSystem != SkillSystemType.None;

        /// <summary>True if a CSkillSys variant is installed.</summary>
        public bool IsCSkillSys =>
            SkillSystem == SkillSystemType.CSkillSys09x ||
            SkillSystem == SkillSystemType.CSkillSys300;

        /// <summary>True if any magic split patch is installed.</summary>
        public bool HasMagicSplit => MagicSplit != MagicSplitType.None;

        /// <summary>True if any portrait extension is installed.</summary>
        public bool HasPortraitExtends => PortraitExtends != PortraitExtendsType.None;

        /// <summary>True if the HALFBODY portrait extension is installed.</summary>
        public bool IsHalfBody => PortraitExtends == PortraitExtendsType.HalfBody;
    }
}
