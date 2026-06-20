using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, strictly READ-ONLY scanner that collects the skill-config
    /// text ids for the Text Editor "Skills" export filter (#1028 Slice B,
    /// category 6). Faithful port of the WinForms dispatch in
    /// <c>ToolTranslateROM.InitExportFilter</c> (filter == 6):
    ///
    /// <code>
    ///   if (is_multibyte) {                       // FE8J
    ///       SkillConfigFE8NSkillForm.MakeVarsIDArray(list);
    ///       SkillConfigFE8NVer2SkillForm.MakeVarsIDArray(list);
    ///   } else {                                  // FE8U
    ///       SkillConfigSkillSystemForm.MakeVarsIDArray(list);
    ///   }
    /// </code>
    ///
    /// Per-form behaviour (ported verbatim):
    ///  - FE8U <c>SkillConfigSkillSystemForm.MakeVarsIDArray</c>: bail unless
    ///    <see cref="SkillSystemEnum.SkillSystem"/>; resolve the TEXT pointer via
    ///    <see cref="FindSkillTextPointer"/> (byte-signature grep), ReInit at the
    ///    derefed base, entry size 2, count <c>i &lt; 0x100</c> (getBlockDataCount
    ///    over the u16 list), text id at offset 0.
    ///  - FE8J <c>SkillConfigFE8NSkillForm.MakeVarsIDArray</c>: branch on the
    ///    skill system; FE8N/yugudora -> Ver1 icon-pointer chain (offset 2);
    ///    FE8N_ver3 -> return (nothing); FE8N_ver2 -> Ver2 icon-pointer chain
    ///    (offset 2); else return. Entry size 32, count stop on
    ///    <c>u16(addr) == 0xFFFF || u16(addr) == 0</c>.
    ///  - FE8J <c>SkillConfigFE8NVer2SkillForm.MakeVarsIDArray</c>: only when
    ///    FE8N_ver2; ReInit at <c>g_SkillBaseAddress</c>, offset 0.
    ///
    /// Every read is guarded; a version-absent category yields an empty set and
    /// the methods never throw.
    /// </summary>
    public static class SkillSystemTextScanner
    {
        public enum SkillSystemEnum
        {
            NO,
            FE8N,        // FE8J Ver1
            FE8N_ver2,   // FE8J Ver2
            FE8N_ver3,   // FE8J Ver3
            yugudora,    // FE8J Ver1 customisation
            midori,      // FE8J early custom extension
            SkillSystem, // FE8U
            CSkillSys09x, // FE8U
            CSkillSys300, // FE8U
        }

        // ---- WF PatchUtil.SearchSkillSystemLow signature table -------------
        struct Sig { public string Name; public string Ver; public uint Addr; public byte[] Data; }

        static readonly Sig[] SkillSystemSignatures = new[]
        {
            new Sig{ Name="yugudora",    Ver="FE8J", Addr=0xEE594,  Data=new byte[]{0x4B,0xFA,0x2F,0x59}},
            new Sig{ Name="FE8N",        Ver="FE8J", Addr=0x89268,  Data=new byte[]{0x00,0x4B,0x9F,0x46}},
            new Sig{ Name="midori",      Ver="FE8J", Addr=0xFE58E0, Data=new byte[]{0x05,0x1C,0x00,0xF0,0x25,0xF8,0x01,0x29,0x04,0xD0,0x28,0x1C,0x00,0xF0,0x28,0xF8}},
            new Sig{ Name="SkillSystem", Ver="FE8U", Addr=0x2ACF8,  Data=new byte[]{0x70,0x47}},
            new Sig{ Name="CSkillSys09x",Ver="FE8U", Addr=0xB2A604, Data=new byte[]{0x43,0x53,0x4B,0x49,0x4C,0x4C,0x53,0x59,0x53,0x5F,0x4B,0x2D,0x30,0x39,0x78,0x00}},
            new Sig{ Name="CSkillSys100",Ver="FE8U", Addr=0xB2A604, Data=new byte[]{0x43,0x53,0x4B,0x49,0x4C,0x4C,0x53,0x59,0x53,0x5F,0x4B,0x2D,0x31,0x30,0x30,0x00}},
            new Sig{ Name="CSkillSys300",Ver="FE8U", Addr=0xB2A604, Data=new byte[]{0x43,0x53,0x4B,0x49,0x4C,0x4C,0x53,0x59,0x53,0x5F,0x4B,0x2D,0x33}},
        };

        /// <summary>
        /// Port of WinForms <c>PatchUtil.SearchSkillSystemLow</c> — detects the
        /// installed skill system by byte-signature at fixed addresses. No cache
        /// (the export filter runs once); cheap relative to the export itself.
        /// </summary>
        public static SkillSystemEnum SearchSkillSystem(ROM rom)
        {
            if (rom?.RomInfo == null || rom.Data == null) return SkillSystemEnum.NO;
            string version = rom.RomInfo.VersionToFilename;
            foreach (Sig t in SkillSystemSignatures)
            {
                if (t.Ver != version) continue;
                if (t.Addr + (uint)t.Data.Length > (uint)rom.Data.Length) continue;
                byte[] data = rom.getBinaryData(t.Addr, t.Data.Length);
                if (U.memcmp(t.Data, data) != 0) continue;

                if (t.Name == "FE8N")
                {
                    if (IsFE8NVer3(rom)) return SkillSystemEnum.FE8N_ver3;
                    if (IsFE8NVer2(rom)) return SkillSystemEnum.FE8N_ver2;
                    return SkillSystemEnum.FE8N;
                }
                if (t.Name == "yugudora")    return SkillSystemEnum.yugudora;
                if (t.Name == "midori")      return SkillSystemEnum.midori;
                if (t.Name == "SkillSystem") return SkillSystemEnum.SkillSystem;
                if (t.Name == "CSkillSys09x") return SkillSystemEnum.CSkillSys09x;
                if (t.Name == "CSkillSys100") return SkillSystemEnum.CSkillSys09x;
                if (t.Name == "CSkillSys300") return SkillSystemEnum.CSkillSys300;
            }
            return SkillSystemEnum.NO;
        }

        /// <summary>
        /// Collect all skill-config text ids for the export filter (category 6).
        /// Returns an empty set when no skill system is installed / the active
        /// ROM lacks the table (never null, never throws).
        /// </summary>
        public static HashSet<uint> CollectSkillTextIds(ROM rom)
        {
            var ids = new HashSet<uint>();
            if (rom?.RomInfo == null || rom.Data == null) return ids;

            if (rom.RomInfo.is_multibyte)
            {
                CollectFE8NSkillTextIds(rom, ids);   // SkillConfigFE8NSkillForm
                CollectFE8NVer2SkillTextIds(rom, ids); // SkillConfigFE8NVer2SkillForm
            }
            else
            {
                CollectFE8USkillTextIds(rom, ids);   // SkillConfigSkillSystemForm
            }
            return ids;
        }

        // ===================================================================
        // FE8U — SkillConfigSkillSystemForm.MakeVarsIDArray
        // ===================================================================
        static void CollectFE8USkillTextIds(ROM rom, HashSet<uint> ids)
        {
            if (SearchSkillSystem(rom) != SkillSystemEnum.SkillSystem) return;
            uint basetextP = FindSkillTextPointer(rom);
            if (basetextP == U.NOT_FOUND) return;
            // SkillConfigSkillSystemForm.Init: ReInit(basetextP) — basetextP is a
            // pointer FIELD (the slot whose u32 is the table base). UseValsID
            // offset 0 on a u16 list, count predicate i < 0x100 (the form caps at
            // 0x100 entries via getBlockDataCount).
            if (!U.isSafetyOffset(basetextP + 3, rom)) return;
            uint baseAddr = rom.p32(basetextP);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            uint count = rom.getBlockDataCount(baseAddr, 2, (i, addr) => i < 0x100);
            uint p = baseAddr;
            for (uint i = 0; i < count; i++, p += 2)
            {
                if (!U.isSafetyOffset(p + 1, rom)) break;
                AddTextId(ids, rom.u16(p));
            }
        }

        // ---- WF SkillConfigSkillSystemForm.FindSkillPointer("TEXT", 0) ------
        struct SkillSig { public string Name; public uint Skip; public byte[] Data; }

        static readonly SkillSig[] SkillTextSignatures = new[]
        {
            new SkillSig{ Name="TEXT", Skip=16, Data=new byte[]{0x07,0x49,0x40,0x00,0x40,0x18,0x00,0x88,0x00,0x28,0x00,0xD1,0x06,0x48,0x21,0x1C}},
            new SkillSig{ Name="TEXT", Skip=16, Data=new byte[]{0x40,0x5D,0x08,0x49,0x40,0x00,0x40,0x18,0x00,0x88,0x00,0x28,0x00,0xD1,0x07,0x48,0x21,0x1C,0x4C,0x31}},
        };

        /// <summary>
        /// Port of WinForms <c>SkillConfigSkillSystemForm.FindSkillPointer("TEXT", 0)</c>:
        /// grep [0xB00000, 0xC00000) for each TEXT signature, then read the pointer
        /// slot at <c>match + data.Length + skip</c>. Returns the SLOT offset (a
        /// pointer field), or <see cref="U.NOT_FOUND"/>.
        /// </summary>
        public static uint FindSkillTextPointer(ROM rom)
        {
            if (rom?.Data == null) return U.NOT_FOUND;
            const uint start = 0xB00000;
            const uint end = 0xC00000;
            foreach (SkillSig t in SkillTextSignatures)
            {
                // The TEXT signatures have no 0xFFFFFFFF runs, so WF MakeMaskData
                // returns useMask=false -> plain Grep. We mirror that (plain Grep).
                uint found = U.Grep(rom.Data, t.Data, start, end, 4);
                if (found == U.NOT_FOUND) continue;
                uint a = found + (uint)t.Data.Length + t.Skip;
                if (!U.isSafetyOffset(a + 3, rom)) continue;
                uint p = rom.u32(a);
                if (!U.isSafetyPointer(p, rom)) continue;
                return a; // the pointer FIELD (slot) — caller derefs via p32
            }
            return U.NOT_FOUND;
        }

        // ===================================================================
        // FE8J Ver1/Ver2 — SkillConfigFE8NSkillForm.MakeVarsIDArray
        // ===================================================================
        static void CollectFE8NSkillTextIds(ROM rom, HashSet<uint> ids)
        {
            uint[] pointers;
            SkillSystemEnum skill = SearchSkillSystem(rom);
            if (skill == SkillSystemEnum.FE8N || skill == SkillSystemEnum.yugudora)
            {
                pointers = FindSkillFE8NVer1IconPointers(rom);
            }
            else if (skill == SkillSystemEnum.FE8N_ver3)
            {
                return; // Ver3 does not use this form
            }
            else if (skill == SkillSystemEnum.FE8N_ver2)
            {
                pointers = FindSkillFE8NVer2IconPointers(rom, out _);
            }
            else
            {
                return;
            }
            if (pointers == null) return;

            // SkillConfigFE8NSkillForm.Init: entry size 32, count stop on
            // u16(addr) == 0xFFFF || u16(addr) == 0; text id at offset 2.
            foreach (uint slot in pointers)
            {
                if (!U.isSafetyOffset(slot + 3, rom)) continue;
                uint baseAddr = rom.p32(slot); // ReInitPointer(pointer[i])
                if (!U.isSafetyOffset(baseAddr, rom)) continue;
                ScanFE8NEntries(rom, baseAddr, 2, ids);
            }
        }

        // SkillConfigFE8NVer2SkillForm.MakeVarsIDArray: only FE8N_ver2; ReInit at
        // g_SkillBaseAddress (direct base), entry size 32, same terminator, offset 0.
        static void CollectFE8NVer2SkillTextIds(ROM rom, HashSet<uint> ids)
        {
            if (SearchSkillSystem(rom) != SkillSystemEnum.FE8N_ver2) return;
            uint skillBase;
            uint[] pointers = FindSkillFE8NVer2IconPointers(rom, out skillBase);
            if (pointers == null) return;
            if (skillBase == 0) return;
            uint baseAddr = U.toOffset(skillBase);
            if (!U.isSafetyOffset(baseAddr, rom)) return;
            ScanFE8NEntries(rom, baseAddr, 0, ids);
        }

        // Walk a FE8N skill text table: entry size 32, stop on u16(addr) sentinel
        // (0xFFFF or 0), text id at `offset` (u16).
        static void ScanFE8NEntries(ROM rom, uint baseAddr, uint offset, HashSet<uint> ids)
        {
            uint p = baseAddr;
            for (int i = 0; i < 0x400; i++, p += 32)
            {
                if (!U.isSafetyOffset(p + 1, rom)) break;
                uint sentinel = rom.u16(p);
                if (sentinel == 0xFFFF || sentinel == 0) break;
                if (!U.isSafetyOffset(p + offset + 1, rom)) break;
                AddTextId(ids, rom.u16(p + offset));
            }
        }

        // ---- WF SkillConfigFE8NSkillForm.FindSkillFE8NVer1IconPointersLow ----
        public static uint[] FindSkillFE8NVer1IconPointers(ROM rom)
        {
            if (rom?.Data == null) return null;
            if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return null;
            uint iconExPointer = rom.u32(0x89268 + 4);
            if (!U.isSafetyPointer(iconExPointer, rom)) return null;

            byte[] need = new byte[] { 0xF0, 0x40, 0x00, 0x02, 0x00, 0x3B, 0x00, 0x02 };
            uint iconPointers = U.Grep(rom.Data, need, 0xE00000, 0, 4);
            if (iconPointers == U.NOT_FOUND) return null;
            iconPointers = iconPointers + (uint)need.Length + 4 + 4 + 4;

            var pointer = new List<uint>();
            for (uint p = iconPointers; ; p += 4)
            {
                if (!U.isSafetyOffset(p + 3, rom)) break;
                uint pp = rom.u32(p);
                if (!U.isSafetyPointer(pp, rom)) break;
                pp = U.toOffset(pp);
                if (pp < 0xE00000) continue; // distinguish from API pointers
                pointer.Add(p);
            }
            if (pointer.Count <= 0) return null;
            return pointer.ToArray();
        }

        // ---- WF SkillConfigFE8NVer2SkillForm.FindSkillFE8NVer2IconPointersLow ----
        // Returns the pointer slots AND the resolved g_SkillBaseAddress (slot[4]).
        public static uint[] FindSkillFE8NVer2IconPointers(ROM rom, out uint skillBaseAddress)
        {
            skillBaseAddress = 0;
            if (rom?.Data == null) return null;
            if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return null;
            uint iconExPointer = rom.u32(0x89268 + 4);
            if (!U.isSafetyPointer(iconExPointer, rom)) return null;

            byte[] need = new byte[] { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
            uint iconPointers = U.Grep(rom.Data, need, 0xE00000, 0, 4);
            if (iconPointers == U.NOT_FOUND) return null;
            if (iconPointers < (uint)(4 * 5)) return null;
            iconPointers = iconPointers - (4 * 5);

            var pointer = new List<uint>();
            for (uint p = iconPointers; ; p += 4)
            {
                if (!U.isSafetyOffset(p + 3, rom)) break;
                uint pp = rom.u32(p);
                if (!U.isSafetyPointer(pp, rom)) break;
                pp = U.toOffset(pp);
                if (pp < 0xE00000) continue;
                pointer.Add(p);
            }
            if (pointer.Count <= 4) return null;
            skillBaseAddress = pointer[4];
            return pointer.ToArray();
        }

        /// <summary>
        /// The FULL side-effect resolution of WF <c>SkillConfigFE8NVer2SkillForm.
        /// FindSkillFE8NVer2IconPointersLow</c> — the existing
        /// <see cref="FindSkillFE8NVer2IconPointers(ROM, out uint)"/> only returns the pointer slots +
        /// <c>g_SkillBaseAddress</c> (slot[4]); the ROM-rebuild producer ALSO needs the
        /// <c>g_AnimeBaseAddress</c> and <c>g_ICON_LIST_SIZE</c> the WF form computes as side effects (the
        /// main IFR BlockSize, the {4,8,12} vs {4,8,12,16} pointerIndexes, and the anime pointer-list base).
        /// Reproduced VERBATIM (incl. the <c>u16(0x70B96) == 0</c> gate, the 2019-Nov variable-length-struct
        /// branch [slot 8 = anime, iconListSize from slot 11], and the 2018-original branch [slot 3 nazo
        /// pointer appended, then slot 5 = anime]). EOF-HARDENED (each read guarded). Returns false (all outs
        /// zero) when the pointer chain is absent or too short — exactly the WF <c>return null</c>.
        /// </summary>
        public static bool FindSkillFE8NVer2IconPointersFull(ROM rom,
            out uint[] pointers, out uint skillBaseAddress, out uint animeBaseAddress, out uint iconListSize)
        {
            pointers = null;
            skillBaseAddress = 0;
            animeBaseAddress = 0;
            iconListSize = 16; // WF g_ICON_LIST_SIZE default.

            if (rom?.Data == null) return false;
            if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return false;
            uint iconExPointer = rom.u32(0x89268 + 4);
            if (!U.isSafetyPointer(iconExPointer, rom)) return false;

            byte[] need = new byte[] { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
            uint iconPointers = U.Grep(rom.Data, need, 0xE00000, 0, 4);
            if (iconPointers == U.NOT_FOUND) return false;
            if (iconPointers < (uint)(4 * 5)) return false;
            iconPointers = iconPointers - (4 * 5);

            var pointer = new List<uint>();
            for (uint p = iconPointers; ; p += 4)
            {
                if (!U.isSafetyOffset(p + 3, rom)) break;
                uint pp = rom.u32(p);
                if (!U.isSafetyPointer(pp, rom)) break;
                pp = U.toOffset(pp);
                if (pp < 0xE00000) continue; // distinguish from API pointers
                pointer.Add(p);
            }
            if (pointer.Count <= 4) return false;
            skillBaseAddress = pointer[4];

            // WF: if (Program.ROM.u16(0x70B96) == 0x00) { ... resolve ICON_LIST_SIZE + anime ... }
            if (U.isSafetyOffset(0x70B96 + 1, rom) && rom.u16(0x70B96) == 0x00)
            {
                // ICON_LIST_SIZE candidate at slot 11.
                uint p11 = iconPointers + (4 * 11);
                if (U.isSafetyOffset(p11 + 3, rom))
                {
                    uint candidate = rom.u32(p11);
                    if (candidate >= 16 && candidate <= 40 && (candidate % 4 == 0))
                    {//2019 11月後半 開発版 — 構造体が可変長になった
                        iconListSize = candidate;
                        // slot 8 is the anime pointer.
                        uint p8 = iconPointers + (4 * 8);
                        if (U.isSafetyOffset(p8 + 3, rom))
                        {
                            uint anime_pointer = rom.u32(p8);
                            if (U.isSafetyPointer(anime_pointer, rom))
                            {
                                animeBaseAddress = rom.p32(U.toOffset(p8));
                            }
                        }
                    }
                    else
                    {//2018年版を元にしたFEBuilderGBAオリジナル拡張版 anime pointer
                        uint p3 = iconPointers + (4 * 3);
                        if (U.isSafetyOffset(p3 + 3, rom))
                        {
                            uint nazo_pointer = rom.u32(p3);
                            if (U.isSafetyPointer(nazo_pointer, rom))
                            {//バージョンによって、ポインタ数が違うので、参考値程度に・・・
                                pointer.Add(U.toOffset(nazo_pointer));
                            }
                        }
                        if (pointer.Count > 5)
                        {//この場合、5番目のポインタがアニメになります。
                            uint slot5 = pointer[5];
                            if (U.isSafetyOffset(slot5 + 3, rom))
                            {
                                animeBaseAddress = rom.p32(slot5);
                            }
                        }
                    }
                }
            }
            pointers = pointer.ToArray();
            return true;
        }

        /// <summary>
        /// The FULL side-effect resolution of WF <c>SkillConfigFE8NVer3SkillForm.
        /// FindSkillFE8NVer3IconPointersLow</c> — the existing <see cref="IsFE8NVer3"/> is detection-only.
        /// The ROM-rebuild producer needs <c>g_SkillBaseAddress</c> (the fixed slot <c>0x892A8 + 4</c>),
        /// <c>g_ICON_LIST_SIZE</c> (<c>u32(0x892A8 + 8)</c>), and <c>g_AnimeBaseAddress</c>
        /// (<c>toOffset(u32(0x892A8 + 20))</c>). Reproduced VERBATIM (incl. the iconExPointer + skl-table
        /// safety gates and the <c>iconListSize &lt;= 0 || &gt; 100</c> reject). EOF-HARDENED. Returns false
        /// (all outs zero) on any failed gate — exactly the WF <c>return null</c>.
        /// </summary>
        public static bool FindSkillFE8NVer3IconPointersFull(ROM rom,
            out uint skillBaseAddress, out uint animeBaseAddress, out uint iconListSize)
        {
            skillBaseAddress = 0;
            animeBaseAddress = 0;
            iconListSize = 0;

            if (rom?.Data == null) return false;
            if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return false;
            uint iconExPointer = rom.u32(0x89268 + 4);
            if (!U.isSafetyPointer(iconExPointer, rom)) return false;

            if (!U.isSafetyOffset(0x892A8 + 4 + 3, rom)) return false;
            uint skl_table = rom.u32(0x892A8 + 4);
            if (!U.isSafetyPointer(skl_table, rom)) return false;
            skillBaseAddress = 0x892A8 + 4;

            if (!U.isSafetyOffset(0x892A8 + 8 + 3, rom)) return false;
            iconListSize = rom.u32(0x892A8 + 8);
            if (iconListSize <= 0 || iconListSize > 100)
            {
                iconListSize = 0;
                skillBaseAddress = 0;
                return false;
            }

            // skl_anime_table at 0x892A8 + 20 (== 0x892BC).
            if (U.isSafetyOffset(0x892A8 + 20 + 3, rom))
            {
                uint skl_anime_table = rom.u32(0x892A8 + 20);
                if (U.isSafetyPointer(skl_anime_table, rom))
                {
                    animeBaseAddress = U.toOffset(skl_anime_table);
                }
            }
            return true;
        }

        // ---- WF SkillConfigFE8NVer3SkillForm.FindSkillFE8NVer3IconPointersLow ----
        // Detection-only (Ver3 contributes no skill text to the export filter).
        static bool IsFE8NVer3(ROM rom)
        {
            if (rom?.Data == null) return false;
            if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return false;
            uint iconExPointer = rom.u32(0x89268 + 4);
            if (!U.isSafetyPointer(iconExPointer, rom)) return false;
            if (!U.isSafetyOffset(0x892A8 + 4 + 3, rom)) return false;
            uint sklTable = rom.u32(0x892A8 + 4);
            if (!U.isSafetyPointer(sklTable, rom)) return false;
            if (!U.isSafetyOffset(0x892A8 + 8 + 3, rom)) return false;
            uint iconListSize = rom.u32(0x892A8 + 8);
            if (iconListSize <= 0 || iconListSize > 100) return false;
            return true;
        }

        static bool IsFE8NVer2(ROM rom)
        {
            uint[] pointer = FindSkillFE8NVer2IconPointers(rom, out _);
            if (pointer == null) return false;
            if (pointer.Length < 5) return false;
            return true;
        }

        static void AddTextId(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }
    }
}
