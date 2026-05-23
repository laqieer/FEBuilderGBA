// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform helpers for the CSA Magic Creator / FEditor magic engines.
// Ports the read-only detection / scan logic out of the WinForms
// `ImageUtilMagic` static class so Avalonia (and any future headless tool)
// can resolve the magic system kind, dim/no-dim addresses, the CSA spell
// table address, and walk the CSA entries without depending on
// System.Windows.Forms or any WF-only helpers.
//
// Mirrors:
//   FEBuilderGBA/ImageUtilMagic.cs (lines 32-183)
//
// Closes the Copilot CLI plan-review #2 blocker on issue #417 (gap-sweep
// ImageMagicCSACreatorForm parity).
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Magic engine kind detected by <see cref="MagicCSACore.SearchMagicSystem"/>.
    /// Mirrors WinForms <c>ImageUtilMagic.magic_system_enum</c> values; the
    /// <c>NoCache</c> sentinel is intentionally omitted because the Core
    /// helper does not maintain a cache (callers cache externally if needed).
    /// </summary>
    public enum MagicSystemKind
    {
        None = 0,
        FEditor = 1,
        CsaCreator = 2,
    }

    /// <summary>One row of the CSA spell table.</summary>
    public sealed class CsaEntry
    {
        /// <summary>Address of the CSA struct (the row that owns P0..P16).</summary>
        public uint Addr { get; set; }

        /// <summary>Address of the pointer-table slot (where the dim/no-dim/empty pointer lives).</summary>
        public uint TagAddr { get; set; }

        /// <summary>
        /// Display name. The Core helper emits the hex id (e.g.
        /// <c>"0x49"</c>) plus an <c>" EMPTY"</c> suffix for post-original
        /// data=0 slots. The richer WF <c>"0xNN effectName"</c> format
        /// (which resolves the effect name from the <c>item_anime_effect_</c>
        /// dictionary or <c>CommentCache</c>) requires a name resolver that
        /// is callable from Core; that hook is out of scope here and a
        /// follow-up for #500 (tracked as a KnownGap on the Avalonia view).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>The data pointer stored at <see cref="TagAddr"/> (raw GBA pointer or 0).</summary>
        public uint DataAddr { get; set; }

        /// <summary>True if the slot is an extension EMPTY (after the original magic count).</summary>
        public bool IsEmpty { get; set; }
    }

    /// <summary>
    /// Stateless read-only helpers for the CSA Magic Creator / FEditor
    /// detection + spell-table walk. All methods take a <see cref="ROM"/>
    /// explicitly and never touch global state.
    /// </summary>
    public static class MagicCSACore
    {
        // ---- detection table (mirrors WF MagicPatchTableSt list) ----

        readonly struct MagicPatch
        {
            public string Name { get; init; }
            public string Version { get; init; }
            public uint Addr { get; init; }
            public byte[] Data { get; init; }
            public uint Dim { get; init; }
            public uint NoDim { get; init; }
        }

        static readonly MagicPatch[] Patches = new[]
        {
            new MagicPatch{ Name="SCA_Creator", Version="FE8U", Addr=0x95d780, Data=new byte[]{0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0xD9,0xD8,0x95,0x08}, Dim=0x95d7ed, NoDim=0x95d899 },
            new MagicPatch{ Name="FEditor",     Version="FE8U", Addr=0x95d780, Data=new byte[]{0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0x39,0xD9,0x95,0x08}, Dim=0x95D7ED, NoDim=0x95D8EF },
            new MagicPatch{ Name="SCA_Creator", Version="FE8J", Addr=0x9cd3bc, Data=new byte[]{0x01,0x00,0x00,0x00,0xCC,0xD3,0x9C,0x08,0x03,0x00,0x00,0x00,0x15,0xD5,0x9C,0x08}, Dim=0x9CD429, NoDim=0x9CD4D5 },
            new MagicPatch{ Name="SCA_Creator", Version="FE8J", Addr=0x5BDC80, Data=new byte[]{0x01,0x00,0x00,0x00,0xCC,0xD3,0x9C,0x08,0x03,0x00,0x00,0x00,0x15,0xD5,0x9C,0x08}, Dim=0x5BDCED, NoDim=0x5BDD99 },
            new MagicPatch{ Name="FEditor",     Version="FE8J", Addr=0xEFBE00, Data=new byte[]{0x01,0x00,0x00,0x00,0x10,0xBE,0xEF,0x08,0x03,0x00,0x00,0x00,0xB9,0xBF,0xEF,0x08}, Dim=0xEFBE6D, NoDim=0xEFBF6F },
            new MagicPatch{ Name="SCA_Creator", Version="FE7U", Addr=0xCB680,  Data=new byte[]{0x19,0x00,0x00,0x00,0x90,0xB6,0x0C,0x08,0x03,0x00,0x00,0x00,0xD9,0xB7,0x0C,0x08}, Dim=0xCB6ED, NoDim=0xCB799 },
            new MagicPatch{ Name="FEditor",     Version="FE7U", Addr=0xCB680,  Data=new byte[]{0x19,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0x00,0x00,0x00,0xD9,0xB7,0x0C,0x08}, Dim=0xCB699, NoDim=0xCB787 },
            new MagicPatch{ Name="FEditor",     Version="FE7J", Addr=0xC69B4,  Data=new byte[]{0x19,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0x00,0x00,0x00,0x29,0x6B,0x0C,0x08}, Dim=0xC69CD, NoDim=0xC69CD },
            new MagicPatch{ Name="SCA_Creator", Version="FE6",  Addr=0x2DC078, Data=new byte[]{0x19,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0x00,0x00,0x00,0x61,0xC1,0x2D,0x08}, Dim=0x2DC091, NoDim=0x2dc129 },
            new MagicPatch{ Name="FEditor",     Version="FE6",  Addr=0x2DC078, Data=new byte[]{0x19,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0x00,0x00,0x00,0xC5,0xC1,0x2D,0x08}, Dim=0x2dc091, NoDim=0x2DC17F },
        };

        readonly struct SpellTablePatch
        {
            public string Name { get; init; }
            public string Version { get; init; }
            public byte[] Data { get; init; }
        }

        static readonly SpellTablePatch[] SpellTables = new[]
        {
            new SpellTablePatch{ Name="SCA_Creator", Version="FE8U", Data=new byte[]{0x1C,0x58,0x05,0x08,0x00,0x01,0x00,0x80,0xED,0xD7,0x95,0x08,0x99,0xD8,0x95,0x08} },
            new SpellTablePatch{ Name="FEditor",     Version="FE8U", Data=new byte[]{0x01,0xB4,0x7D,0xE7,0x34,0xFF,0x03,0x02,0x80,0xD7,0x95,0x08,0x1A,0xE1,0x03,0x02} },
            new SpellTablePatch{ Name="SCA_Creator", Version="FE8J", Data=new byte[]{0xB8,0x67,0x05,0x08,0x00,0x01,0x00,0x80,0x29,0xD4,0x9C,0x08,0xD5,0xD4,0x9C,0x08} },
            new SpellTablePatch{ Name="FEditor",     Version="FE8J", Data=new byte[]{0x01,0xB4,0x7D,0xE7,0x34,0xFF,0x03,0x02,0x00,0xBE,0xEF,0x08,0x16,0xE1,0x03,0x02} },
            new SpellTablePatch{ Name="SCA_Creator", Version="FE7U", Data=new byte[]{0x0C,0x06,0x05,0x08,0x00,0x01,0x00,0x80,0xED,0xB6,0x0C,0x08,0x99,0xB7,0x0C,0x08} },
            new SpellTablePatch{ Name="FEditor",     Version="FE7U", Data=new byte[]{0x00,0x28,0x17,0xD1,0x18,0xE0,0x70,0xB5,0x05,0x1C,0x00,0x20,0x01,0xB4,0x87,0xE7,0x34,0xFF,0x03,0x02,0x80,0xB6,0x0C,0x08,0x26,0xE0,0x03,0x02} },
            new SpellTablePatch{ Name="FEditor",     Version="FE7J", Data=new byte[]{0x01,0xB4,0x79,0xE7,0x34,0xFF,0x03,0x02,0xB4,0x69,0x0C,0x08,0xFE,0xDF,0x03,0x02} },
            new SpellTablePatch{ Name="SCA_Creator", Version="FE6",  Data=new byte[]{0x48,0x19,0x02,0x02,0x00,0x01,0x00,0x80,0x91,0xC0,0x2D,0x08,0x29,0xC1,0x2D,0x08} },
            new SpellTablePatch{ Name="FEditor",     Version="FE6",  Data=new byte[]{0xE7,0x7D,0xB4,0x01,0x34,0xFF,0x03,0x02,0x80,0xD7,0x95,0x08,0x1A,0xE1,0x03,0x02} },
        };

        // ---- public API ----

        /// <summary>
        /// Detect the magic engine kind installed in <paramref name="rom"/>.
        /// Returns <see cref="MagicSystemKind.None"/> when no signature
        /// matches; in that case all <c>out</c> parameters are set to
        /// <see cref="U.NOT_FOUND"/>. The returned <c>csaSpellTable</c> and
        /// <c>csaSpellTablePointer</c> reflect the CSA spell-table location
        /// (the table itself, and the pointer slot containing the table addr).
        /// </summary>
        public static MagicSystemKind SearchMagicSystem(
            ROM rom,
            out uint baseAddr,
            out uint dimAddr,
            out uint noDimAddr,
            out uint csaSpellTable,
            out uint csaSpellTablePointer)
        {
            baseAddr = U.NOT_FOUND;
            dimAddr = U.NOT_FOUND;
            noDimAddr = U.NOT_FOUND;
            csaSpellTable = U.NOT_FOUND;
            csaSpellTablePointer = U.NOT_FOUND;

            if (rom == null || rom.RomInfo == null || rom.Data == null) return MagicSystemKind.None;
            string version = rom.RomInfo.VersionToFilename;
            if (string.IsNullOrEmpty(version)) return MagicSystemKind.None;

            foreach (var p in Patches)
            {
                if (!string.Equals(p.Version, version, StringComparison.Ordinal)) continue;
                if (p.Addr + (uint)p.Data.Length > (uint)rom.Data.Length) continue;
                byte[] data = rom.getBinaryData(p.Addr, p.Data.Length);
                if (U.memcmp(p.Data, data) != 0) continue;

                uint tableAddr = FindCSASpellTable(rom, p.Name, version, out uint tablePtr);
                if (tablePtr == U.NOT_FOUND) continue;

                baseAddr = p.Addr;
                dimAddr = p.Dim;
                noDimAddr = p.NoDim;
                csaSpellTable = tableAddr;
                csaSpellTablePointer = tablePtr;

                return p.Name == "FEditor" ? MagicSystemKind.FEditor : MagicSystemKind.CsaCreator;
            }

            return MagicSystemKind.None;
        }

        /// <summary>
        /// Convenience overload that returns just the spell-table address.
        /// Mirrors WF <c>ImageUtilMagic.GetCSASpellTableAddr</c>.
        /// </summary>
        public static uint GetCSASpellTableAddr(ROM rom)
        {
            SearchMagicSystem(rom, out _, out _, out _, out uint csa, out _);
            return csa;
        }

        /// <summary>
        /// Convenience overload that returns just the spell-table pointer slot.
        /// Mirrors WF <c>ImageUtilMagic.GetCSASpellTablePointer</c>.
        /// </summary>
        public static uint GetCSASpellTablePointer(ROM rom)
        {
            SearchMagicSystem(rom, out _, out _, out _, out _, out uint ptr);
            return ptr;
        }

        /// <summary>
        /// Compute the "spell data count" used by the WF CSA editor to bound
        /// the entry list. Walks the magic-effect pointer table starting at
        /// <c>magic_effect_original_data_count</c> until it hits a non-pointer
        /// non-null u32, returning <c>(distance / 4) - 1</c> with a cap of
        /// <c>0xFD</c>. Mirrors WF <c>ImageUtilMagicFEditor.SpellDataCount</c>.
        /// </summary>
        public static uint ComputeSpellDataCount(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return 0;
            uint baseAddr = rom.p32(rom.RomInfo.magic_effect_pointer);
            if (baseAddr == 0) return 0;
            uint baseId = rom.RomInfo.magic_effect_original_data_count;
            uint p = baseAddr + (baseId * 4);
            uint romLen = (uint)rom.Data.Length;
            for (; p + 4 < romLen; p += 4)
            {
                uint d = rom.u32(p);
                if (U.isPointerOrNULL(d)) continue;
                break;
            }
            uint count = (p - baseAddr) / 4;
            if (count == 0) return 0;
            uint a = count - 1;
            if (a >= 0xFD) return 0xFD;
            return count - 1;
        }

        /// <summary>
        /// Walk the CSA spell table and yield one <see cref="CsaEntry"/> per
        /// slot. Mirrors the WF <c>InputFormRef.MakeList</c> + reader lambda
        /// inside <c>ImageMagicCSACreatorForm.Init</c>.
        /// </summary>
        /// <param name="rom">Target ROM (must not be null).</param>
        /// <param name="kind">Detected magic system kind. Must be
        /// <see cref="MagicSystemKind.CsaCreator"/>; any other value returns
        /// an empty list (FEditor uses a different layout).</param>
        /// <param name="dimAddr">Dim address (from SearchMagicSystem).</param>
        /// <param name="noDimAddr">No-dim address (from SearchMagicSystem).</param>
        /// <param name="csaSpellTable">CSA spell-table address (from SearchMagicSystem).</param>
        /// <param name="spellDataCount">Spell-data count (from
        /// <see cref="ComputeSpellDataCount"/>; mirrors WF
        /// <c>ImageUtilMagicFEditor.SpellDataCount</c>). Pass 0 to fall back
        /// to the legacy <c>0xFE</c> cap.</param>
        /// <param name="maxRows">Hard cap on rows (matches WF <c>0xFE</c>).</param>
        public static List<CsaEntry> ScanCsaEntries(
            ROM rom,
            MagicSystemKind kind,
            uint dimAddr,
            uint noDimAddr,
            uint csaSpellTable,
            uint spellDataCount = 0,
            int maxRows = 0xFE)
        {
            var result = new List<CsaEntry>();
            if (rom == null || rom.RomInfo == null) return result;
            if (kind != MagicSystemKind.CsaCreator) return result;
            if (csaSpellTable == U.NOT_FOUND) return result;

            uint pointerTable = rom.RomInfo.magic_effect_pointer;
            if (pointerTable == 0) return result;
            uint pointerTableAddr = rom.p32(pointerTable);
            if (pointerTableAddr == 0) return result;

            uint originalCount = rom.RomInfo.magic_effect_original_data_count;
            uint romLen = (uint)rom.Data.Length;

            // Effective row cap mirrors WF: spellDataCount AND 0xFE.
            int cap = maxRows;
            if (spellDataCount > 0 && spellDataCount < (uint)cap) cap = (int)spellDataCount;

            for (int i = 0; i < cap; i++)
            {
                uint slotPtr = pointerTableAddr + (uint)(4 * i);
                if (slotPtr + 4 > romLen) break;
                uint dataAddr = rom.p32(slotPtr);

                bool isExtensionEmpty = false;
                if (dataAddr == 0)
                {
                    if (i < originalCount)
                    {
                        // Legacy slot - mirrors WF: skip without adding.
                        continue;
                    }
                    isExtensionEmpty = true;
                }
                else if (dataAddr != dimAddr && dataAddr != noDimAddr)
                {
                    // Not a CSA-creator slot (probably the FEditor format).
                    continue;
                }

                uint csaAddr = csaSpellTable + (uint)(20 * i);
                if (csaAddr + 20 > romLen) break;

                string name = $"{U.ToHexString(i)}{(isExtensionEmpty ? " EMPTY" : "")}";

                result.Add(new CsaEntry
                {
                    Addr = csaAddr,
                    TagAddr = slotPtr,
                    Name = name,
                    DataAddr = dataAddr,
                    IsEmpty = isExtensionEmpty,
                });
            }

            return result;
        }

        // ---- private helpers ----

        static uint FindCSASpellTable(ROM rom, string type, string version, out uint outPointer)
        {
            outPointer = U.NOT_FOUND;

            foreach (var t in SpellTables)
            {
                if (t.Name != type) continue;
                if (t.Version != version) continue;

                uint start = 0x10000;
                uint f = U.Grep(rom.Data, t.Data, start, 0, 4);
                if (f == U.NOT_FOUND) continue;

                uint pointer = f + (uint)t.Data.Length;
                uint table = rom.p32(pointer);
                if (!U.isSafetyOffset(table, rom))
                {
                    // Copilot CLI inline review on PR #547: do NOT leak a
                    // matched-but-unsafe pointer out of the loop. Reset and
                    // try the next candidate.
                    continue;
                }

                outPointer = pointer;
                return table;
            }
            return U.NOT_FOUND;
        }
    }
}
