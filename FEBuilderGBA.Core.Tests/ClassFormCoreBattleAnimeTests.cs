// SPDX-License-Identifier: GPL-3.0-or-later
// #840 tests for the cross-platform ports of:
//   - ClassFormCore.GetBattleAnimeAddrWhereID  (WF ClassForm.GetBattleAnimeAddrWhereID:
//       p32(classAddr + 48) FE6 / p32(classAddr + 52) FE7-8)
//   - ClassFormCore.GetAnimeIDByAnimeSettingPointer (WF
//       ImageBattleAnimeForm.GetAnimeIDByAnimeSettingPointer: u16(ptr + 2))
//   - ClassFormCore.GetAnimeIDByClassID (the composed p32 + u16(ptr+2) chain)
//   - BattleAnimeRendererCore.GetUnitPaletteAddr (WF
//       ImageUnitPaletteForm.GetPaletteAddr: p32(IDToAddr(paletteno-1) + 12),
//       unit-palette table base = p32(image_unit_palette_pointer), stride 16)
//
// Uses a synthetic ROM with a custom RomInfo (StubRomInfo) so the class /
// anime-list / unit-palette table pointers are deterministic. The version field
// drives the FE6 (+48) vs FE7-8 (+52) class-anime-setting offset split.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ClassFormCoreBattleAnimeTests : IDisposable
    {
        readonly ROM _prevRom;

        public ClassFormCoreBattleAnimeTests()
        {
            _prevRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
        }

        // Synthetic ROM layout (arbitrary free-space offsets).
        const uint CLASS_PTR_SLOT     = 0x100;   // RomInfo.class_pointer -> CLASS_BASE
        const uint CLASS_BASE         = 0x1000;  // class table base
        const uint CLASS_DATASIZE     = 84;      // FE8 class entry size (84 bytes)
        const uint ANIME_SETTING_PTR  = 0x4000;  // the anime-setting block for class 5
        const uint UNITPAL_PTR_SLOT   = 0x200;   // RomInfo.image_unit_palette_pointer -> UNITPAL_BASE
        const uint UNITPAL_BASE       = 0x6000;  // unit-palette table base
        const uint UNITPAL_BLOCK      = 0x7000;  // a unit-palette LZ77 block (pointed to from +12)

        const int  TEST_CLASS_ID      = 5;
        const ushort TEST_ANIME_ID    = 0x2A;    // the u16 stored at anime-setting +2

        /// <summary>
        /// Build a synthetic ROM whose class <see cref="TEST_CLASS_ID"/> resolves
        /// to anime ID <see cref="TEST_ANIME_ID"/> for the given
        /// <paramref name="version"/> (6 => +48, else +52).
        /// </summary>
        static ROM MakeRom(int version)
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");
            SetRomInfo(rom, new StubRomInfo(version, classPointer: CLASS_PTR_SLOT,
                classDataSize: CLASS_DATASIZE, unitPalettePointer: UNITPAL_PTR_SLOT));

            // class_pointer slot -> CLASS_BASE (GBA pointer).
            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(CLASS_BASE));

            // Class TEST_CLASS_ID's entry holds the anime-setting pointer at the
            // version-specific offset (+48 FE6, +52 FE7-8).
            uint classAddr = CLASS_BASE + (uint)TEST_CLASS_ID * CLASS_DATASIZE;
            uint settingOffsetInEntry = version == 6 ? 48u : 52u;
            U.write_u32(rom.Data, classAddr + settingOffsetInEntry, U.toPointer(ANIME_SETTING_PTR));

            // The anime-setting block stores the anime ID as u16 at +2.
            U.write_u16(rom.Data, ANIME_SETTING_PTR + 2, TEST_ANIME_ID);

            return rom;
        }

        // ================================================================
        // GetBattleAnimeAddrWhereID — the version-split p32 indirection
        // ================================================================

        [Fact]
        public void GetBattleAnimeAddrWhereID_FE8_UsesPlus52()
        {
            // GetBattleAnimeAddrWhereID returns p32(...) which is an OFFSET
            // (U.toOffset applied), matching WF Program.ROM.p32 semantics.
            ROM rom = MakeRom(8);
            uint off = ClassFormCore.GetBattleAnimeAddrWhereID(rom, TEST_CLASS_ID);
            Assert.Equal(ANIME_SETTING_PTR, off);
        }

        [Fact]
        public void GetBattleAnimeAddrWhereID_FE6_UsesPlus48()
        {
            ROM rom = MakeRom(6);
            uint off = ClassFormCore.GetBattleAnimeAddrWhereID(rom, TEST_CLASS_ID);
            Assert.Equal(ANIME_SETTING_PTR, off);
        }

        [Fact]
        public void GetBattleAnimeAddrWhereID_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetBattleAnimeAddrWhereID(null, TEST_CLASS_ID));
        }

        [Fact]
        public void GetBattleAnimeAddrWhereID_NegativeClass_ReturnsNotFound()
        {
            ROM rom = MakeRom(8);
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetBattleAnimeAddrWhereID(rom, -1));
        }

        [Fact]
        public void GetBattleAnimeAddrWhereID_ClassZero_ReturnsNotFound()
        {
            // classID 0 is "none" for the other class-ID APIs — must NOT resolve
            // entry 0 (PR #842 review #1).
            ROM rom = MakeRom(8);
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetBattleAnimeAddrWhereID(rom, 0));
        }

        [Fact]
        public void GetBattleAnimeAddrWhereID_SettingSlotNearEOF_ReturnsNotFound_NoThrow()
        {
            // PR #842 review #1: rom.p32(settingSlot) reads 4 bytes — if the slot
            // lands in the LAST 1-3 bytes (where isSafetyOffset still passes), the
            // u32 read would throw IndexOutOfRange. The 4-byte EOF guard must
            // return NOT_FOUND instead. Position the class table base so that
            // classAddr + 52 (FE8) sits 2 bytes before EOF.
            const int version = 8;
            const uint dataSize = 84; // CLASS_DATASIZE
            uint romLen = 0x10000;    // 64 KiB, < 0x02000000 so isSafetyOffset passes

            // We want classAddr + 52 == romLen - 2 for TEST_CLASS_ID (5).
            // classAddr = base + 5*84 = base + 420. So base = (romLen-2) - 52 - 420.
            uint settingSlot = romLen - 2;
            uint classAddr = settingSlot - 52;
            uint classBase = classAddr - (uint)TEST_CLASS_ID * dataSize;

            var rom = new ROM();
            byte[] data = new byte[romLen];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");
            SetRomInfo(rom, new StubRomInfo(version, classPointer: CLASS_PTR_SLOT,
                classDataSize: dataSize, unitPalettePointer: UNITPAL_PTR_SLOT));

            // class_pointer slot -> classBase (4-byte write fits — classBase is
            // well inside the ROM).
            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(classBase));

            // No throw, returns NOT_FOUND (the 4-byte span [romLen-2, romLen+2)
            // overruns by 2 bytes).
            uint result = ClassFormCore.GetBattleAnimeAddrWhereID(rom, TEST_CLASS_ID);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetAnimeIDByClassID_SettingSlotNearEOF_ReturnsZero_NoThrow()
        {
            // The chained entry point must also be EOF-safe: an unresolvable
            // (NOT_FOUND) setting slot yields anime id 0, never a throw.
            const int version = 8;
            const uint dataSize = 84;
            uint romLen = 0x10000;
            uint settingSlot = romLen - 1; // 3 bytes short of a u32
            uint classAddr = settingSlot - 52;
            uint classBase = classAddr - (uint)TEST_CLASS_ID * dataSize;

            var rom = new ROM();
            byte[] data = new byte[romLen];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");
            SetRomInfo(rom, new StubRomInfo(version, classPointer: CLASS_PTR_SLOT,
                classDataSize: dataSize, unitPalettePointer: UNITPAL_PTR_SLOT));
            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(classBase));

            Assert.Equal(0u, ClassFormCore.GetAnimeIDByClassID(rom, TEST_CLASS_ID));
        }

        // ================================================================
        // GetIDWhereBattleAnimeAddr — reverse lookup (#1377): setting pointer -> cid
        // ================================================================

        /// <summary>
        /// The reverse lookup iterates the class table by row count (the WF
        /// read-max rule: class 0 always counts, then <c>u8(addr+4)!=0</c>). The
        /// base <see cref="MakeRom"/> leaves entries 1..N all-zero, so the count
        /// stops at 1 and <see cref="TEST_CLASS_ID"/> is never reached. Plant a
        /// non-zero <c>+4</c> "valid class" marker for rows 1..TEST_CLASS_ID so
        /// the scan reaches the owning class.
        /// </summary>
        static void MakeClassRowsValid(ROM rom, uint classBase, uint datasize, int upToInclusive)
        {
            for (int i = 1; i <= upToInclusive; i++)
                U.write_u8(rom.Data, classBase + (uint)i * datasize + 4, 1);
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_FE8_FindsOwningClass_ViaPlus52()
        {
            // class TEST_CLASS_ID's +52 holds the setting pointer ANIME_SETTING_PTR;
            // the reverse lookup must return TEST_CLASS_ID for that offset.
            ROM rom = MakeRom(8);
            MakeClassRowsValid(rom, CLASS_BASE, CLASS_DATASIZE, TEST_CLASS_ID);
            uint cid = ClassFormCore.GetIDWhereBattleAnimeAddr(rom, U.toPointer(ANIME_SETTING_PTR));
            Assert.Equal((uint)TEST_CLASS_ID, cid);
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_AcceptsRawPointerOrOffset()
        {
            // The lookup normalizes through U.toOffset, so passing the OFFSET
            // directly (not the GBA pointer) resolves the same class.
            ROM rom = MakeRom(8);
            MakeClassRowsValid(rom, CLASS_BASE, CLASS_DATASIZE, TEST_CLASS_ID);
            Assert.Equal((uint)TEST_CLASS_ID,
                ClassFormCore.GetIDWhereBattleAnimeAddr(rom, ANIME_SETTING_PTR));
            Assert.Equal((uint)TEST_CLASS_ID,
                ClassFormCore.GetIDWhereBattleAnimeAddr(rom, U.toPointer(ANIME_SETTING_PTR)));
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_FE6_FindsOwningClass_ViaPlus48()
        {
            ROM rom = MakeRom(6);
            MakeClassRowsValid(rom, CLASS_BASE, CLASS_DATASIZE, TEST_CLASS_ID);
            uint cid = ClassFormCore.GetIDWhereBattleAnimeAddr(rom, U.toPointer(ANIME_SETTING_PTR));
            Assert.Equal((uint)TEST_CLASS_ID, cid);
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_NoOwningClass_ReturnsNotFound()
        {
            // A valid in-ROM offset that no class's +52 setting pointer references.
            ROM rom = MakeRom(8);
            MakeClassRowsValid(rom, CLASS_BASE, CLASS_DATASIZE, TEST_CLASS_ID);
            uint unowned = ANIME_SETTING_PTR + 0x1000; // safe offset, not any class's pointer
            Assert.Equal(U.NOT_FOUND,
                ClassFormCore.GetIDWhereBattleAnimeAddr(rom, U.toPointer(unowned)));
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_FE6vsFE8_OffsetSplitMatters()
        {
            // Plant the setting pointer ONLY at the FE6 (+48) slot, but ask an
            // FE8 ROM (reads +52) -> must NOT resolve TEST_CLASS_ID.
            ROM rom = MakeRom(8);
            MakeClassRowsValid(rom, CLASS_BASE, CLASS_DATASIZE, TEST_CLASS_ID);
            uint classAddr = CLASS_BASE + (uint)TEST_CLASS_ID * CLASS_DATASIZE;
            U.write_u32(rom.Data, classAddr + 52, 0);
            U.write_u32(rom.Data, classAddr + 48, U.toPointer(ANIME_SETTING_PTR));
            Assert.Equal(U.NOT_FOUND,
                ClassFormCore.GetIDWhereBattleAnimeAddr(rom, U.toPointer(ANIME_SETTING_PTR)));
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND,
                ClassFormCore.GetIDWhereBattleAnimeAddr(null, U.toPointer(ANIME_SETTING_PTR)));
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_UnsafeFindAddress_ReturnsNotFound()
        {
            // 0x12345678 -> offset 0x12345678 is out of a 16MB ROM -> unsafe -> NOT_FOUND, no throw.
            ROM rom = MakeRom(8);
            Assert.Equal(U.NOT_FOUND,
                ClassFormCore.GetIDWhereBattleAnimeAddr(rom, 0x12345678));
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_ZeroAddress_ReturnsNotFound()
        {
            // toOffset(0) = 0, which is below the isSafetyOffset floor (0x200) -> NOT_FOUND.
            ROM rom = MakeRom(8);
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetIDWhereBattleAnimeAddr(rom, 0));
        }

        [Fact]
        public void GetIDWhereBattleAnimeAddr_SettingSlotNearEOF_ReturnsNotFound_NoThrow()
        {
            // The per-row +52 p32 read must be EOF-safe: if a class's setting slot
            // lands in the last 1-3 bytes, the scan stops (break) without throwing.
            const int version = 8;
            const uint dataSize = 84;
            uint romLen = 0x10000;
            // Position the class table so class 0's +52 sits 2 bytes before EOF;
            // class 0 always counts (read-max rule), so the loop reaches it.
            uint settingSlot = romLen - 2;
            uint classBase = settingSlot - 52; // class 0 entry at classBase

            var rom = new ROM();
            byte[] data = new byte[romLen];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");
            SetRomInfo(rom, new StubRomInfo(version, classPointer: CLASS_PTR_SLOT,
                classDataSize: dataSize, unitPalettePointer: UNITPAL_PTR_SLOT));
            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(classBase));

            // Any safe target offset: no throw, returns NOT_FOUND.
            uint result = ClassFormCore.GetIDWhereBattleAnimeAddr(rom, U.toPointer(0x4000));
            Assert.Equal(U.NOT_FOUND, result);
        }

        // ================================================================
        // GetAnimeIDByAnimeSettingPointer — the u16(ptr + 2) read
        // ================================================================

        [Fact]
        public void GetAnimeIDByAnimeSettingPointer_ReadsU16AtPlus2()
        {
            ROM rom = MakeRom(8);
            uint id = ClassFormCore.GetAnimeIDByAnimeSettingPointer(rom, U.toPointer(ANIME_SETTING_PTR));
            Assert.Equal((uint)TEST_ANIME_ID, id);
        }

        [Fact]
        public void GetAnimeIDByAnimeSettingPointer_UnsafePointer_ReturnsZero()
        {
            ROM rom = MakeRom(8);
            // 0x12345678 is not a valid GBA pointer -> WF safety-guard returns 0.
            Assert.Equal(0u, ClassFormCore.GetAnimeIDByAnimeSettingPointer(rom, 0x12345678));
        }

        [Fact]
        public void GetAnimeIDByAnimeSettingPointer_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, ClassFormCore.GetAnimeIDByAnimeSettingPointer(null, U.toPointer(ANIME_SETTING_PTR)));
        }

        // ================================================================
        // GetAnimeIDByClassID — the composed chain (p32 + u16(ptr+2))
        // ================================================================

        [Fact]
        public void GetAnimeIDByClassID_FE8_ResolvesCorrectAnime()
        {
            ROM rom = MakeRom(8);
            uint id = ClassFormCore.GetAnimeIDByClassID(rom, TEST_CLASS_ID);
            Assert.Equal((uint)TEST_ANIME_ID, id);
        }

        [Fact]
        public void GetAnimeIDByClassID_FE6_ResolvesCorrectAnime_ViaPlus48()
        {
            ROM rom = MakeRom(6);
            uint id = ClassFormCore.GetAnimeIDByClassID(rom, TEST_CLASS_ID);
            Assert.Equal((uint)TEST_ANIME_ID, id);
        }

        [Fact]
        public void GetAnimeIDByClassID_FE6_vs_FE8_OffsetSplit_Matters()
        {
            // Plant the anime-setting pointer ONLY at the FE6 (+48) slot, but ask
            // an FE8 ROM (which reads +52) -> it must NOT resolve TEST_ANIME_ID
            // (proving the version split is honored, not a fixed offset).
            ROM rom = MakeRom(8);
            // Wipe the +52 slot, plant the setting pointer at +48 instead.
            uint classAddr = CLASS_BASE + (uint)TEST_CLASS_ID * CLASS_DATASIZE;
            U.write_u32(rom.Data, classAddr + 52, 0);
            U.write_u32(rom.Data, classAddr + 48, U.toPointer(ANIME_SETTING_PTR));

            // FE8 reads +52 (now 0) -> NOT_FOUND -> anime id 0.
            Assert.Equal(0u, ClassFormCore.GetAnimeIDByClassID(rom, TEST_CLASS_ID));
        }

        [Fact]
        public void GetAnimeIDByClassID_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, ClassFormCore.GetAnimeIDByClassID(null, TEST_CLASS_ID));
        }

        [Fact]
        public void GetAnimeIDByClassID_AbsentSettingPointer_ReturnsZero()
        {
            ROM rom = MakeRom(8);
            // Wipe the class's anime-setting pointer slot -> p32 reads 0 -> the
            // setting pointer is unsafe -> id 0 (no crash).
            uint classAddr = CLASS_BASE + (uint)TEST_CLASS_ID * CLASS_DATASIZE;
            U.write_u32(rom.Data, classAddr + 52, 0);
            Assert.Equal(0u, ClassFormCore.GetAnimeIDByClassID(rom, TEST_CLASS_ID));
        }

        // ================================================================
        // GetUnitPaletteAddr — p32(IDToAddr(paletteno-1) + 12), stride 16
        // ================================================================

        [Fact]
        public void GetUnitPaletteAddr_ResolvesP32OfEntryPlus12()
        {
            // GetUnitPaletteAddr returns p32(...) which is an OFFSET (U.toOffset
            // applied), matching WF GetPaletteAddr (Program.ROM.p32) semantics.
            ROM rom = MakeRom(8);
            // unit-palette table base.
            U.write_u32(rom.Data, UNITPAL_PTR_SLOT, U.toPointer(UNITPAL_BASE));
            // paletteno = 3 -> IDToAddr(2) = base + 2*16; +12 holds the block pointer.
            const int paletteno = 3;
            uint entryAddr = UNITPAL_BASE + (uint)(paletteno - 1) * 16;
            U.write_u32(rom.Data, entryAddr + 12, U.toPointer(UNITPAL_BLOCK));

            uint result = BattleAnimeRendererCore.GetUnitPaletteAddr(rom, paletteno);
            Assert.Equal(UNITPAL_BLOCK, result);
        }

        [Fact]
        public void GetUnitPaletteAddr_Slot1_UsesEntryZero()
        {
            ROM rom = MakeRom(8);
            U.write_u32(rom.Data, UNITPAL_PTR_SLOT, U.toPointer(UNITPAL_BASE));
            // paletteno = 1 -> IDToAddr(0) = base + 0; +12 holds the block pointer.
            U.write_u32(rom.Data, UNITPAL_BASE + 12, U.toPointer(UNITPAL_BLOCK));

            uint result = BattleAnimeRendererCore.GetUnitPaletteAddr(rom, 1);
            Assert.Equal(UNITPAL_BLOCK, result);
        }

        [Fact]
        public void GetUnitPaletteAddr_ZeroOrNegative_ReturnsNotFound()
        {
            ROM rom = MakeRom(8);
            U.write_u32(rom.Data, UNITPAL_PTR_SLOT, U.toPointer(UNITPAL_BASE));
            Assert.Equal(U.NOT_FOUND, BattleAnimeRendererCore.GetUnitPaletteAddr(rom, 0));
            Assert.Equal(U.NOT_FOUND, BattleAnimeRendererCore.GetUnitPaletteAddr(rom, -2));
        }

        [Fact]
        public void GetUnitPaletteAddr_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, BattleAnimeRendererCore.GetUnitPaletteAddr(null, 1));
        }

        // ================================================================
        // GetBattleAnimeSettingRows — class-centric left-list source (#1377)
        // ================================================================

        /// <summary>
        /// Build a synthetic ROM with <paramref name="classCount"/> valid classes
        /// (0..classCount-1; row i marked valid by a non-zero +4 byte), each
        /// class i's battle-anime setting slot (+52 FE8 / +48 FE6) pointing at a
        /// distinct safe SP-record region <c>SP_BASE + i*16</c>. Class 0's setting
        /// slot is left NULL so it is skipped (its pointer is unsafe) — exercising
        /// the "skip unloadable rows" contract.
        /// </summary>
        static ROM MakeMultiClassRom(int version, int classCount)
        {
            const uint dataSize = 84;
            const uint spBase = 0x4000; // per-class SP-record region base
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");
            SetRomInfo(rom, new StubRomInfo(version, classPointer: CLASS_PTR_SLOT,
                classDataSize: dataSize, unitPalettePointer: UNITPAL_PTR_SLOT));

            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(CLASS_BASE));
            uint settingOffsetInEntry = version == 6 ? 48u : 52u;

            for (int i = 1; i < classCount; i++)
            {
                uint classAddr = CLASS_BASE + (uint)i * dataSize;
                // Mark the row valid (read-max rule: u8(addr+4) != 0).
                U.write_u8(rom.Data, classAddr + 4, 1);
                // Distinct safe setting pointer per class; store anime id = i at +2.
                uint sp = spBase + (uint)i * 16;
                U.write_u32(rom.Data, classAddr + settingOffsetInEntry, U.toPointer(sp));
                U.write_u16(rom.Data, sp + 2, (ushort)i);
            }
            return rom;
        }

        [Fact]
        public void GetBattleAnimeSettingRows_NullRom_ReturnsEmpty_NoThrow()
        {
            var rows = ClassFormCore.GetBattleAnimeSettingRows(null);
            Assert.Empty(rows);
        }

        [Fact]
        public void GetBattleAnimeSettingRows_EachRowOffset_MatchesGetBattleAnimeAddrWhereID_FE8()
        {
            // The load-bearing contract: every row's settingOffset is EXACTLY the
            // address the editor's LoadEntry dereferences, i.e. equals
            // GetBattleAnimeAddrWhereID(rom, classId). This is what makes clicking
            // a list row load the correct SP record (the #1377 fix).
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            var rows = ClassFormCore.GetBattleAnimeSettingRows(rom);
            Assert.NotEmpty(rows);
            foreach (var (classId, settingOffset) in rows)
            {
                uint expected = ClassFormCore.GetBattleAnimeAddrWhereID(rom, classId);
                Assert.NotEqual(U.NOT_FOUND, expected);
                Assert.Equal(expected, settingOffset);
            }
        }

        [Fact]
        public void GetBattleAnimeSettingRows_RowOwningClass_RoundTrips_ViaReverseLookup()
        {
            // A Class-Editor jump that passes a row's settingOffset must resolve
            // the SAME class via GetIDWhereBattleAnimeAddr (the NavigateTo gate),
            // proving the list row and the jump target are the same address space.
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            var rows = ClassFormCore.GetBattleAnimeSettingRows(rom);
            Assert.NotEmpty(rows);
            foreach (var (classId, settingOffset) in rows)
            {
                uint cid = ClassFormCore.GetIDWhereBattleAnimeAddr(rom, settingOffset);
                // GetIDWhereBattleAnimeAddr returns the FIRST class with that
                // pointer; our pointers are distinct, so it must be this class.
                Assert.Equal((uint)classId, cid);
            }
        }

        [Fact]
        public void GetBattleAnimeSettingRows_SkipsClassesWithUnsafeSettingPointer()
        {
            // Class 0's setting slot is left NULL (toOffset(0)=0 < safety floor),
            // so it must NOT appear as a row. Every returned row must be a safe,
            // 4-byte-readable offset.
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            var rows = ClassFormCore.GetBattleAnimeSettingRows(rom);
            Assert.DoesNotContain(rows, r => r.classId == 0);
            foreach (var (_, settingOffset) in rows)
            {
                Assert.True(U.isSafetyOffset(settingOffset, rom));
                Assert.True((ulong)settingOffset + 4 <= (ulong)rom.Data.Length);
            }
        }

        [Fact]
        public void GetBattleAnimeSettingRows_FE6_UsesPlus48()
        {
            // FE6 reads +48; the rows must resolve through the +48 slot. Plant
            // nothing at +52 (MakeMultiClassRom writes only +48 for version 6) and
            // assert the rows still resolve via GetBattleAnimeAddrWhereID(+48).
            ROM rom = MakeMultiClassRom(6, classCount: 5);
            var rows = ClassFormCore.GetBattleAnimeSettingRows(rom);
            Assert.NotEmpty(rows);
            foreach (var (classId, settingOffset) in rows)
                Assert.Equal(ClassFormCore.GetBattleAnimeAddrWhereID(rom, classId), settingOffset);
        }

        [Fact]
        public void GetBattleAnimeSettingRows_LoadEntryReadsSameAnimeId_AsClassJump()
        {
            // The editor's LoadEntry reads the anime id from settingOffset + 2.
            // The Class-Editor jump shows GetAnimeIDByClassID(classId). For every
            // row these must agree (no more "mid-record" misreads).
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            var rows = ClassFormCore.GetBattleAnimeSettingRows(rom);
            Assert.NotEmpty(rows);
            foreach (var (classId, settingOffset) in rows)
            {
                uint rowAnimeId = rom.u16(settingOffset + 2);
                uint jumpAnimeId = ClassFormCore.GetAnimeIDByClassID(rom, classId);
                Assert.Equal(jumpAnimeId, rowAnimeId);
            }
        }

        // ================================================================
        // GetFirstClassSettingPointerByAnimeId — Mant-Animation jump (#1377)
        // ================================================================

        [Fact]
        public void GetFirstClassSettingPointerByAnimeId_FindsOwningClassRow()
        {
            // Each class i in MakeMultiClassRom uses anime id i. The first class
            // whose anime == i must resolve to its own setting pointer (a row).
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            for (uint animeId = 1; animeId <= 5; animeId++)
            {
                uint settingOffset = ClassFormCore.GetFirstClassSettingPointerByAnimeId(rom, animeId);
                Assert.NotEqual(U.NOT_FOUND, settingOffset);
                // It must equal that class's setting pointer (a real list row).
                Assert.Equal(ClassFormCore.GetBattleAnimeAddrWhereID(rom, (int)animeId), settingOffset);
                // And the resolved class's anime id matches the requested one.
                Assert.Equal(animeId, ClassFormCore.GetAnimeIDByAnimeSettingPointer(rom, settingOffset));
            }
        }

        [Fact]
        public void GetFirstClassSettingPointerByAnimeId_NoClassUsesId_ReturnsNotFound()
        {
            // anime id 0x999 is used by no class in the fixture.
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetFirstClassSettingPointerByAnimeId(rom, 0x999));
        }

        [Fact]
        public void GetFirstClassSettingPointerByAnimeId_ZeroId_NullRom_ReturnsNotFound_NoThrow()
        {
            ROM rom = MakeMultiClassRom(8, classCount: 6);
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetFirstClassSettingPointerByAnimeId(rom, 0));
            Assert.Equal(U.NOT_FOUND, ClassFormCore.GetFirstClassSettingPointerByAnimeId(null, 1));
        }

        // ================================================================
        // Stub RomInfo — drives version + the three table pointers.
        // ================================================================

        sealed class StubRomInfo : ROMFEINFO
        {
            public StubRomInfo(int version, uint classPointer, uint classDataSize, uint unitPalettePointer)
            {
                this.version = version;
                this.class_pointer = classPointer;
                this.class_datasize = classDataSize;
                this.image_unit_palette_pointer = unitPalettePointer;
            }
        }

        static void SetRomInfo(ROM rom, ROMFEINFO info)
        {
            var prop = typeof(ROM).GetProperty("RomInfo");
            prop?.GetSetMethod(true)?.Invoke(rom, new object[] { info });
        }
    }
}
