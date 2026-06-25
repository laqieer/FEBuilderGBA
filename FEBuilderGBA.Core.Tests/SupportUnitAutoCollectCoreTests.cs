// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SupportUnitAutoCollectCore (#1455) — reciprocal-support init/growth
// mirroring ported from WinForms SupportUnitForm.AutoCollect.
//
// Synthetic FE8U ROM: we plant a unit table with controlled +44 support
// pointers, then two support rows that reference each other, and verify that
// AutoCollect mirrors the editing unit's per-partner init/growth into the
// partner's reciprocal slot — and ONLY there.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SupportUnitAutoCollectCoreTests
    {
        const uint SUPPORT_LIMIT = SupportUnitAutoCollectCore.SUPPORT_LIMIT; // 7
        const uint BLOCK = SupportUnitAutoCollectCore.BLOCK_SIZE;            // 24

        static void WriteU32(byte[] data, uint addr, uint value)
        {
            data[addr + 0] = (byte)(value & 0xFF);
            data[addr + 1] = (byte)((value >> 8) & 0xFF);
            data[addr + 2] = (byte)((value >> 16) & 0xFF);
            data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        static ROM MakeRom(string sig = "BE8E01")
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], sig);
            Assert.NotNull(rom.RomInfo);
            return rom;
        }

        // Point the unit (1-based unitId) at supportFileOffset via its +44 pointer.
        static void PlantUnitSupportPtr(ROM rom, uint unitTableBase, uint unitId1Based, uint supportFileOffset)
        {
            uint unitPtr = rom.RomInfo.unit_pointer;
            uint dataSize = rom.RomInfo.unit_datasize;
            WriteU32(rom.Data, unitPtr, unitTableBase | 0x08000000);
            uint structAddr = unitTableBase + (unitId1Based - 1) * dataSize;
            uint gbaPtr = supportFileOffset == 0 ? 0 : (supportFileOffset | 0x08000000);
            WriteU32(rom.Data, structAddr + SupportUnitNavigation.SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT, gbaPtr);
        }

        // Set partner-slot s (0..6) of the support row at addr to targetUid.
        static void SetPartnerSlot(ROM rom, uint addr, int slot, byte targetUid)
        {
            rom.Data[addr + (uint)slot] = targetUid;
        }

        // ---- happy path: reciprocal mirror -----------------------------------

        [Fact]
        public void AutoCollect_MirrorsInitAndGrowthIntoReciprocalSlot()
        {
            var rom = MakeRom();
            uint unitTableBase = 0x200000;
            uint rowA = 0x500000; // owned by unit 1
            uint rowB = 0x500100; // owned by unit 2

            PlantUnitSupportPtr(rom, unitTableBase, 1, rowA);
            PlantUnitSupportPtr(rom, unitTableBase, 2, rowB);

            // Row A (unit 1) lists unit 2 as partner in slot 0.
            SetPartnerSlot(rom, rowA, 0, 2);
            // Row B (unit 2) must already list unit 1 as a partner (slot 3) for
            // the reciprocal mirror to land — WinForms only updates EXISTING slots.
            SetPartnerSlot(rom, rowB, 3, 1);

            // Editing unit 1's row: partner[0]=2 init=0x11 growth=0x22.
            var partners = new uint[] { 2, 0, 0, 0, 0, 0, 0 };
            var inits = new uint[] { 0x11, 0, 0, 0, 0, 0, 0 };
            var growths = new uint[] { 0x22, 0, 0, 0, 0, 0, 0 };

            int written = SupportUnitAutoCollectCore.AutoCollect(rom, rowA, partners, inits, growths);
            Assert.Equal(1, written);

            // Row B slot 3's init/growth (at +SUPPORT_LIMIT / +2*SUPPORT_LIMIT) must mirror.
            Assert.Equal(0x11u, rom.u8(rowB + 3 + SUPPORT_LIMIT));
            Assert.Equal(0x22u, rom.u8(rowB + 3 + SUPPORT_LIMIT + SUPPORT_LIMIT));
        }

        [Fact]
        public void AutoCollect_LeavesNonMatchingSlotsUntouched()
        {
            var rom = MakeRom();
            uint unitTableBase = 0x200000;
            uint rowA = 0x500000;
            uint rowB = 0x500100;
            PlantUnitSupportPtr(rom, unitTableBase, 1, rowA);
            PlantUnitSupportPtr(rom, unitTableBase, 2, rowB);

            SetPartnerSlot(rom, rowA, 0, 2);
            SetPartnerSlot(rom, rowB, 3, 1);  // matches unit 1
            SetPartnerSlot(rom, rowB, 0, 9);  // unrelated partner; must NOT change

            // Seed unrelated slot 0's init/growth so we can assert they survive.
            rom.Data[rowB + 0 + SUPPORT_LIMIT] = 0xAA;
            rom.Data[rowB + 0 + SUPPORT_LIMIT + SUPPORT_LIMIT] = 0xBB;

            var partners = new uint[] { 2, 0, 0, 0, 0, 0, 0 };
            var inits = new uint[] { 0x11, 0, 0, 0, 0, 0, 0 };
            var growths = new uint[] { 0x22, 0, 0, 0, 0, 0, 0 };
            SupportUnitAutoCollectCore.AutoCollect(rom, rowA, partners, inits, growths);

            // Slot 0 untouched.
            Assert.Equal(0xAAu, rom.u8(rowB + 0 + SUPPORT_LIMIT));
            Assert.Equal(0xBBu, rom.u8(rowB + 0 + SUPPORT_LIMIT + SUPPORT_LIMIT));
            // Slot 3 (the match) updated.
            Assert.Equal(0x11u, rom.u8(rowB + 3 + SUPPORT_LIMIT));
        }

        // ---- no-op cases -----------------------------------------------------

        [Fact]
        public void AutoCollect_ZeroPartner_NoWrite()
        {
            var rom = MakeRom();
            uint unitTableBase = 0x200000;
            uint rowA = 0x500000;
            PlantUnitSupportPtr(rom, unitTableBase, 1, rowA);

            var partners = new uint[] { 0, 0, 0, 0, 0, 0, 0 };
            var inits = new uint[] { 0x11, 0, 0, 0, 0, 0, 0 };
            var growths = new uint[] { 0x22, 0, 0, 0, 0, 0, 0 };
            int written = SupportUnitAutoCollectCore.AutoCollect(rom, rowA, partners, inits, growths);
            Assert.Equal(0, written);
        }

        [Fact]
        public void AutoCollect_UnownedRow_NoWrite()
        {
            var rom = MakeRom();
            uint unitTableBase = 0x200000;
            uint rowB = 0x500100;
            PlantUnitSupportPtr(rom, unitTableBase, 2, rowB);
            SetPartnerSlot(rom, rowB, 3, 1);

            // 0x999000 is owned by nobody.
            var partners = new uint[] { 2, 0, 0, 0, 0, 0, 0 };
            var inits = new uint[] { 0x11, 0, 0, 0, 0, 0, 0 };
            var growths = new uint[] { 0x22, 0, 0, 0, 0, 0, 0 };
            int written = SupportUnitAutoCollectCore.AutoCollect(rom, 0x999000, partners, inits, growths);
            Assert.Equal(0, written);
            // Row B reciprocal slot stays zero.
            Assert.Equal(0u, rom.u8(rowB + 3 + SUPPORT_LIMIT));
        }

        [Fact]
        public void AutoCollect_PartnerHasNoReciprocalSlot_NoWrite()
        {
            var rom = MakeRom();
            uint unitTableBase = 0x200000;
            uint rowA = 0x500000;
            uint rowB = 0x500100;
            PlantUnitSupportPtr(rom, unitTableBase, 1, rowA);
            PlantUnitSupportPtr(rom, unitTableBase, 2, rowB);
            SetPartnerSlot(rom, rowA, 0, 2);
            // Row B does NOT list unit 1 anywhere -> no reciprocal slot.

            var partners = new uint[] { 2, 0, 0, 0, 0, 0, 0 };
            var inits = new uint[] { 0x11, 0, 0, 0, 0, 0, 0 };
            var growths = new uint[] { 0x22, 0, 0, 0, 0, 0, 0 };
            int written = SupportUnitAutoCollectCore.AutoCollect(rom, rowA, partners, inits, growths);
            Assert.Equal(0, written);
        }

        [Fact]
        public void AutoCollectByTargetSupport_TargetZero_NoWrite()
        {
            var rom = MakeRom();
            Assert.False(SupportUnitAutoCollectCore.AutoCollectByTargetSupport(rom, 1, 0, 0x11, 0x22));
        }

        [Fact]
        public void AutoCollectByTargetSupport_NullRom_NoThrow()
        {
            Assert.False(SupportUnitAutoCollectCore.AutoCollectByTargetSupport(null!, 1, 2, 0x11, 0x22));
        }

        // ---- review finding 2: full target-row safety guard -----------------

        [Fact]
        public void AutoCollectByTargetSupport_PartnerRowNearEof_NoWriteNoThrow()
        {
            var rom = MakeRom();
            uint unitTableBase = 0x200000;
            // Partner unit 2's +44 points one byte before EOF: the full 24-byte
            // row does NOT fit. The guard must reject it (no OOB, no throw).
            uint badOffset = (uint)rom.Data.Length - 1;
            PlantUnitSupportPtr(rom, unitTableBase, 2, badOffset);

            bool wrote = SupportUnitAutoCollectCore.AutoCollectByTargetSupport(rom, 1, 2, 0x11, 0x22);
            Assert.False(wrote);
        }

        // ---- partner count ---------------------------------------------------

        [Fact]
        public void RecomputePartnerCount_CountsNonZero()
        {
            Assert.Equal(0u, SupportUnitAutoCollectCore.RecomputePartnerCount(new uint[] { 0, 0, 0, 0, 0, 0, 0 }));
            Assert.Equal(3u, SupportUnitAutoCollectCore.RecomputePartnerCount(new uint[] { 2, 0, 5, 0, 9, 0, 0 }));
            Assert.Equal(7u, SupportUnitAutoCollectCore.RecomputePartnerCount(new uint[] { 1, 2, 3, 4, 5, 6, 7 }));
        }

        [Fact]
        public void RecomputePartnerCount_NullOrShort_Defensive()
        {
            Assert.Equal(0u, SupportUnitAutoCollectCore.RecomputePartnerCount(null!));
            Assert.Equal(2u, SupportUnitAutoCollectCore.RecomputePartnerCount(new uint[] { 1, 5 }));
        }
    }
}
