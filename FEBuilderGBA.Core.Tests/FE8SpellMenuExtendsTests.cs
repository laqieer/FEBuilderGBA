// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for the FE8 SkillSystems spell-menu patch (issue #1167):
//   - FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer (SkillSystems202201
//     hard-coded-signature path — no external .dmp needed).
//   - FE8SpellMenuExtendsCore read/write/expand + MakeB0/SplitB0 + Export/Import.
//
// Each test plants the WF byte signature into a synthetic FE8U ROM and asserts
// the scanner + Core helpers resolve/round-trip the planted data. No patched
// ROM file is required.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FE8SpellMenuExtendsTests
    {
        // SkillSystems202201 SpellsGetter signature (the 44-byte run GrepEnd
        // lands AFTER). Must match FE8SpellMenuPatchScanner.s_SpellsGetter202201.
        static readonly byte[] SpellsGetter202201 = new byte[]
        {
            0x9E, 0x42, 0x04, 0xDA, 0x02, 0x34, 0xEF, 0xE7,
            0x00, 0x9A, 0x9A, 0x42, 0xFA, 0xD1, 0x01, 0x9B,
            0x01, 0x33, 0x03, 0xD1, 0x63, 0x78, 0x2B, 0x70,
            0x01, 0x35, 0xF3, 0xE7, 0x60, 0x78, 0xFF, 0xF7,
            0xBB, 0xFF, 0x01, 0x9B, 0x98, 0x42, 0xED, 0xD1,
            0xF4, 0xE7, 0xC0, 0x46,
        };

        // Plant the signature well past FE8U compress_image_borderline_address
        // (0xDB000) and 4-byte aligned (GrepEnd uses blocksize 4).
        const uint SigPos = 0xB10000;
        const uint UnitTableBase = 0xC00000;
        const uint ListBase = 0xC10000;

        static ROM MakeFE8URom(int size = 0x1000000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            rom.LoadLow("synth-fe8spellmenu.gba", data, "BE8E01");
            return rom;
        }

        static void WriteBytes(ROM rom, uint addr, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                rom.write_u8(addr + (uint)i, bytes[i]);
        }

        static void WriteU32(ROM rom, uint addr, uint value)
        {
            rom.write_u8(addr + 0, (byte)(value & 0xFF));
            rom.write_u8(addr + 1, (byte)((value >> 8) & 0xFF));
            rom.write_u8(addr + 2, (byte)((value >> 16) & 0xFF));
            rom.write_u8(addr + 3, (byte)((value >> 24) & 0xFF));
        }

        /// <summary>
        /// Plant the SpellsGetter signature + the assignLevelUpP slot (which
        /// GrepEnd lands on, right after the 44-byte run) pointing at the
        /// per-unit pointer-table base. Returns the assignLevelUpP slot address.
        /// </summary>
        static uint PlantPatch(ROM rom)
        {
            WriteBytes(rom, SigPos, SpellsGetter202201);
            uint assignLevelUpP = SigPos + (uint)SpellsGetter202201.Length; // GrepEnd plus=0
            WriteU32(rom, assignLevelUpP, UnitTableBase | 0x08000000u);
            return assignLevelUpP;
        }

        /// <summary>Plant a per-unit pointer at unitTableBase + 4*unitId -> listBase,
        /// and a 0x0000-terminated [B0|B1] array at listBase.</summary>
        static void PlantUnitList(ROM rom, uint unitId, uint listBase, (byte b0, byte b1)[] entries)
        {
            uint slot = UnitTableBase + unitId * 4;
            WriteU32(rom, slot, listBase | 0x08000000u);
            uint cursor = listBase;
            foreach (var (b0, b1) in entries)
            {
                rom.write_u8(cursor + 0, b0);
                rom.write_u8(cursor + 1, b1);
                cursor += 2;
            }
            rom.write_u16(cursor, 0x0000); // terminator
        }

        // -----------------------------------------------------------------
        // Scanner — resolves the planted assignLevelUpP slot
        // -----------------------------------------------------------------

        [Fact]
        public void FindFE8SpellPatchPointer_ResolvesPlantedSignature()
        {
            ROM rom = MakeFE8URom();
            uint expectedSlot = PlantPatch(rom);

            uint resolved = FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, null);
            Assert.Equal(expectedSlot, resolved);
            Assert.Equal(UnitTableBase, U.toOffset(rom.u32(resolved)));
        }

        [Fact]
        public void FindFE8SpellPatchPointer_NotFoundOnVanillaRom()
        {
            ROM rom = MakeFE8URom();
            Assert.Equal(U.NOT_FOUND, FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, null));
        }

        [Fact]
        public void FindFE8SpellPatchPointer_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(null, null));
        }

        // -----------------------------------------------------------------
        // Guards — version != 8 and is_multibyte both yield NOT_FOUND
        // -----------------------------------------------------------------

        [Fact]
        public void FindFE8SpellPatchPointer_NonFE8Rom_ReturnsNotFound()
        {
            // FE7U: version != 8. Plant the signature anyway — the version guard
            // must short-circuit before the grep.
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            rom.LoadLow("synth-fe7u.gba", data, "AE7E01");
            WriteBytes(rom, SigPos, SpellsGetter202201);

            Assert.NotEqual(8, (int)rom.RomInfo.version);
            Assert.Equal(U.NOT_FOUND, FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, null));
        }

        [Fact]
        public void FindFE8SpellPatchPointer_MultibyteRom_ReturnsNotFound()
        {
            // FE8J is multibyte. The is_multibyte guard must short-circuit.
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            rom.LoadLow("synth-fe8j.gba", data, "BE8J01");
            WriteBytes(rom, SigPos, SpellsGetter202201);

            if (rom.RomInfo.is_multibyte)
            {
                Assert.Equal(U.NOT_FOUND, FE8SpellMenuPatchScanner.FindFE8SpellPatchPointer(rom, null));
            }
        }

        // -----------------------------------------------------------------
        // Core — unit table base + slot resolution + N1 read
        // -----------------------------------------------------------------

        [Fact]
        public void GetUnitTableBase_ResolvesDereferencedBase()
        {
            ROM rom = MakeFE8URom();
            uint slot = PlantPatch(rom);
            Assert.Equal(UnitTableBase, FE8SpellMenuExtendsCore.GetUnitTableBase(rom, slot));
        }

        [Fact]
        public void ReadSpellList_ReadsB0B1UntilTerminator()
        {
            ROM rom = MakeFE8URom();
            PlantPatch(rom);
            PlantUnitList(rom, 3, ListBase, new (byte, byte)[]
            {
                (0x05, 0x10), (0x8A, 0x11), (0x14, 0x12),
            });

            var list = FE8SpellMenuExtendsCore.ReadSpellList(rom, ListBase);
            Assert.Equal(3, list.Count);
            Assert.Equal((0x05u, 0x10u), (list[0].b0, list[0].b1));
            Assert.Equal((0x8Au, 0x11u), (list[1].b0, list[1].b1));
            Assert.Equal((0x14u, 0x12u), (list[2].b0, list[2].b1));
        }

        // -----------------------------------------------------------------
        // MakeB0 / SplitB0 — level | promoted round-trip
        // -----------------------------------------------------------------

        [Fact]
        public void MakeB0_SplitB0_RoundTrip()
        {
            // Promoted level 10 -> 0x8A.
            byte b0 = FE8SpellMenuExtendsCore.MakeB0(10, true);
            Assert.Equal((byte)0x8A, b0);
            FE8SpellMenuExtendsCore.SplitB0(b0, out uint level, out bool promoted);
            Assert.Equal(10u, level);
            Assert.True(promoted);

            // Unpromoted level 20 -> 0x14.
            byte b0b = FE8SpellMenuExtendsCore.MakeB0(20, false);
            Assert.Equal((byte)0x14, b0b);
            FE8SpellMenuExtendsCore.SplitB0(b0b, out uint level2, out bool promoted2);
            Assert.Equal(20u, level2);
            Assert.False(promoted2);
        }

        // -----------------------------------------------------------------
        // WriteN1Entry — round-trips a (B0,B1) pair in place
        // -----------------------------------------------------------------

        [Fact]
        public void WriteN1Entry_RoundTrips()
        {
            ROM rom = MakeFE8URom();
            PlantPatch(rom);
            PlantUnitList(rom, 1, ListBase, new (byte, byte)[] { (0x05, 0x10) });

            uint b0 = FE8SpellMenuExtendsCore.MakeB0(7, true); // 0x87
            Assert.True(FE8SpellMenuExtendsCore.WriteN1Entry(rom, ListBase, b0, 0x22));
            Assert.Equal((byte)0x87, rom.u8(ListBase + 0));
            Assert.Equal((byte)0x22, rom.u8(ListBase + 1));
        }

        // -----------------------------------------------------------------
        // ExpandSpellList — re-terminates with 0x0000 and repoints the slot
        // -----------------------------------------------------------------

        [Fact]
        public void ExpandSpellList_AllocatesTerminatedBlockAndRepoints()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeFE8URom();
                PlantPatch(rom);
                PlantUnitList(rom, 2, ListBase, new (byte, byte)[] { (0x05, 0x10), (0x0A, 0x11) });

                // Copilot #1: ExpandSpellList must allocate/write into the PASSED
                // rom (NOT CoreState.ROM). Point CoreState.ROM at a DIFFERENT ROM
                // and assert the passed rom is the one that changes.
                CoreState.ROM = MakeFE8URom();

                var ud = new Undo.UndoData
                {
                    time = System.DateTime.Now,
                    name = "expand",
                    list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };
                uint newBase = FE8SpellMenuExtendsCore.ExpandSpellList(rom, UnitTableBase, 2, 4, ud);

                Assert.NotEqual(U.NOT_FOUND, newBase);
                // Writes recorded into the EXPLICIT undodata (2: block + pointer).
                Assert.Equal(2, ud.list.Count);

                // The PASSED rom's unit slot must now point at the new block.
                uint slot = UnitTableBase + 2 * 4;
                Assert.Equal(newBase, rom.p32(slot));

                // First two entries preserved; the block is 0x0000-terminated
                // after `newCount` (4) entries.
                Assert.Equal((byte)0x05, rom.u8(newBase + 0));
                Assert.Equal((byte)0x10, rom.u8(newBase + 1));
                Assert.Equal((byte)0x0A, rom.u8(newBase + 2));
                Assert.Equal((byte)0x11, rom.u8(newBase + 3));
                // Entries 3 and 4 are new defaults (level 1, skill 0).
                Assert.Equal((byte)0x01, rom.u8(newBase + 4));
                Assert.Equal((byte)0x01, rom.u8(newBase + 6));
                // Terminator after 4 entries (8 bytes).
                Assert.Equal(0x0000u, rom.u16(newBase + 8));
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ExpandSpellList_DoesNotTouchCoreStateRom_WhenDifferentRomPassed()
        {
            // Copilot #1 regression: the passed rom and CoreState.ROM are distinct
            // instances; only the PASSED rom may be mutated.
            var prev = CoreState.ROM;
            try
            {
                ROM passed = MakeFE8URom();
                PlantPatch(passed);
                PlantUnitList(passed, 1, ListBase, new (byte, byte)[] { (0x05, 0x10) });

                ROM other = MakeFE8URom();   // CoreState.ROM — must stay untouched
                PlantPatch(other);
                PlantUnitList(other, 1, ListBase, new (byte, byte)[] { (0x05, 0x10) });
                CoreState.ROM = other;
                uint otherSlotBefore = other.u32(UnitTableBase + 1 * 4);

                uint newBase = FE8SpellMenuExtendsCore.ExpandSpellList(passed, UnitTableBase, 1, 3, null);
                Assert.NotEqual(U.NOT_FOUND, newBase);

                // Passed rom changed; CoreState.ROM's same slot is byte-identical.
                Assert.Equal(newBase, passed.p32(UnitTableBase + 1 * 4));
                Assert.Equal(otherSlotBefore, other.u32(UnitTableBase + 1 * 4));
            }
            finally { CoreState.ROM = prev; }
        }

        // -----------------------------------------------------------------
        // Export / Import round-trip
        // -----------------------------------------------------------------

        [Fact]
        public void ExportImport_RoundTripsPerUnitEntries()
        {
            ROM rom = MakeFE8URom();
            uint slot = PlantPatch(rom);
            PlantUnitList(rom, 1, ListBase, new (byte, byte)[] { (0x05, 0x10), (0x0A, 0x11) });

            string path = Path.GetTempFileName();
            try
            {
                bool exportOk = FE8SpellMenuExtendsCore.ExportAllData(rom, slot, 4, path);
                Assert.True(exportOk);

                string[] lines = File.ReadAllLines(path);
                Assert.Equal(4, lines.Length);
                // Unit 1's line: offset \t 05 \t 10 \t 0A \t 11
                string[] sp1 = lines[1].Split('\t');
                Assert.True(sp1.Length >= 5);
                Assert.Equal("05", sp1[1]);
                Assert.Equal("10", sp1[2]);
                Assert.Equal("0A", sp1[3]);
                Assert.Equal("11", sp1[4]);

                // Mutate the ROM, then re-import to restore the exported values.
                rom.write_u8(ListBase + 0, 0x77);
                rom.write_u8(ListBase + 1, 0x88);

                bool importOk = FE8SpellMenuExtendsCore.ImportAllData(rom, slot, 4, path);
                Assert.True(importOk);
                Assert.Equal((byte)0x05, rom.u8(ListBase + 0));
                Assert.Equal((byte)0x10, rom.u8(ListBase + 1));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Export_NotFoundPatch_ReturnsFalse()
        {
            ROM rom = MakeFE8URom(); // no signature planted
            Assert.False(FE8SpellMenuExtendsCore.ExportAllData(rom, U.NOT_FOUND, 4, "ignored.tsv"));
        }

        [Fact]
        public void Import_MissingFile_ReturnsFalse()
        {
            ROM rom = MakeFE8URom();
            uint slot = PlantPatch(rom);
            string path = Path.Combine(Path.GetTempPath(), "fe8spell-missing-" + Guid.NewGuid() + ".tsv");
            Assert.False(File.Exists(path));
            Assert.False(FE8SpellMenuExtendsCore.ImportAllData(rom, slot, 4, path));
        }
    }
}
