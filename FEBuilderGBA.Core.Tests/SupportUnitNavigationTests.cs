// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SupportUnitNavigation (#358) — cross-editor navigation helpers
// that mirror WinForms UnitForm.GetUnitIDWhereSupportAddr / GetSupportAddrWhereUnitID.
//
// These tests use a synthetic FE8U ROM blob; we plant unit table entries
// with controlled support pointers and verify the lookups land on the right
// row.  No real ROM file is required and no Huffman/text init is performed.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SupportUnitNavigationTests
    {
        // ---- helpers ---------------------------------------------------------

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

        // Plant the unit_pointer field at rom.RomInfo.unit_pointer to point at
        // a synthetic unit table at the chosen base file-offset.  Then write
        // the support pointer at +44 inside the (unitIndex)-th unit struct.
        // For FE6 the WinForms table effectively starts at p32(unit_pointer) +
        // unit_datasize so we deliberately leave the index-0 slot empty for
        // those tests.
        static void PlantSupportPointer(ROM rom, uint unitTableBase, uint unitIndex, uint supportFileOffset)
        {
            uint unitPtr = rom.RomInfo.unit_pointer;
            uint dataSize = rom.RomInfo.unit_datasize;
            // unit_pointer is the file offset of a u32 GBA pointer to unit table base.
            WriteU32(rom.Data, unitPtr, unitTableBase | 0x08000000);
            // support_ptr (GBA-form) at +44 in the chosen unit struct
            uint structAddr = unitTableBase + unitIndex * dataSize;
            uint gbaPtr = supportFileOffset == 0 ? 0 : (supportFileOffset | 0x08000000);
            WriteU32(rom.Data, structAddr + SupportUnitNavigation.SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT, gbaPtr);
        }

        // ---- GetUnitIdAtSupportAddr -----------------------------------------

        [Fact]
        public void GetUnitIdAtSupportAddr_NullRom_ReturnsNull()
        {
            Assert.Null(SupportUnitNavigation.GetUnitIdAtSupportAddr(null!, 0x800000));
        }

        [Fact]
        public void GetUnitIdAtSupportAddr_ZeroAddr_ReturnsNull()
        {
            var rom = MakeRom();
            Assert.Null(SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, 0));
        }

        [Fact]
        public void GetUnitIdAtSupportAddr_FindsOwnerFE8U()
        {
            // FE8U: unit_pointer dereferences directly (no first-entry skip).
            var rom = MakeRom("BE8E01");
            uint unitTableBase = 0x200000;
            uint supportOffset = 0x500000;
            PlantSupportPointer(rom, unitTableBase, unitIndex: 5, supportFileOffset: supportOffset);

            uint? uid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, supportOffset);
            Assert.Equal((uint?)5, uid);
        }

        [Fact]
        public void GetUnitIdAtSupportAddr_ReturnsNullForUnowned()
        {
            var rom = MakeRom("BE8E01");
            uint unitTableBase = 0x200000;
            PlantSupportPointer(rom, unitTableBase, unitIndex: 5, supportFileOffset: 0x500000);

            // Nobody points at 0x600000.
            uint? uid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, 0x600000);
            Assert.Null(uid);
        }

        // Pointer normalization (Copilot review point 5): the function MUST
        // accept both 0x08xxxxxx raw GBA pointers and already-normalized file
        // offsets and return the same UID for both.
        [Fact]
        public void GetUnitIdAtSupportAddr_AcceptsBothRawPointerAndFileOffset()
        {
            var rom = MakeRom("BE8E01");
            uint unitTableBase = 0x200000;
            uint supportOffset = 0x500000;
            PlantSupportPointer(rom, unitTableBase, unitIndex: 7, supportFileOffset: supportOffset);

            uint? viaOffset = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, supportOffset);
            uint? viaRaw = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, supportOffset | 0x08000000);
            Assert.Equal(viaOffset, viaRaw);
            Assert.Equal((uint?)7, viaRaw);
        }

        // FE6 has a dummy unit at index 0 of the raw table — WinForms skips it
        // by adding unit_datasize to the base.  A support pointer that owners
        // would be "1" in the raw table is index "0" in the FE6-skipped view.
        [Fact]
        public void GetUnitIdAtSupportAddr_FE6_SkipsDummyFirstEntry()
        {
            var rom = MakeRom("AFEJ01");
            uint dataSize = rom.RomInfo.unit_datasize;
            // unit_pointer -> rawBase; rawBase index 1 (= FE6 logical index 0)
            // points at supportOffset.
            uint rawBase = 0x200000;
            uint supportOffset = 0x500000;
            uint unitPtr = rom.RomInfo.unit_pointer;
            WriteU32(rom.Data, unitPtr, rawBase | 0x08000000);
            WriteU32(rom.Data, rawBase + dataSize + SupportUnitNavigation.SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT, supportOffset | 0x08000000);

            uint? uid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, supportOffset);
            Assert.Equal((uint?)0, uid);  // FE6 logical 0 (raw index 1).
        }

        // ---- GetSupportAddrForUnitId ----------------------------------------

        [Fact]
        public void GetSupportAddrForUnitId_NullRom_ReturnsNull()
        {
            Assert.Null(SupportUnitNavigation.GetSupportAddrForUnitId(null!, 1));
        }

        [Fact]
        public void GetSupportAddrForUnitId_ZeroId_ReturnsNull()
        {
            var rom = MakeRom();
            Assert.Null(SupportUnitNavigation.GetSupportAddrForUnitId(rom, 0));
        }

        [Fact]
        public void GetSupportAddrForUnitId_ReturnsPointer()
        {
            var rom = MakeRom("BE8E01");
            uint unitTableBase = 0x200000;
            uint supportOffset = 0x500000;
            // 1-based ID 6 maps to 0-based index 5.
            PlantSupportPointer(rom, unitTableBase, unitIndex: 5, supportFileOffset: supportOffset);

            uint? result = SupportUnitNavigation.GetSupportAddrForUnitId(rom, 6);
            Assert.Equal((uint?)supportOffset, result);
        }

        [Fact]
        public void GetSupportAddrForUnitId_OutOfRange_ReturnsNull()
        {
            var rom = MakeRom("BE8E01");
            uint? result = SupportUnitNavigation.GetSupportAddrForUnitId(rom, 0xFFFF);
            Assert.Null(result);
        }

        // ---- Round-trip -----------------------------------------------------

        [Fact]
        public void RoundTrip_GetUnitId_And_GetSupportAddr_AreInverse()
        {
            var rom = MakeRom("BE8E01");
            uint unitTableBase = 0x200000;
            uint supportOffset = 0x600000;
            PlantSupportPointer(rom, unitTableBase, unitIndex: 12, supportFileOffset: supportOffset);

            uint? uid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, supportOffset);
            Assert.NotNull(uid);
            uint? backToOffset = SupportUnitNavigation.GetSupportAddrForUnitId(rom, uid!.Value + 1);
            Assert.Equal((uint?)supportOffset, backToOffset);
        }

        // ---- FormatSupportRowLabel ------------------------------------------

        [Fact]
        public void FormatSupportRowLabel_UnownedAddress_ReturnsEmptyMarker()
        {
            var rom = MakeRom("BE8E01");
            string label = SupportUnitNavigation.FormatSupportRowLabel(rom, 0x900000);
            Assert.Equal("-EMPTY-", label);
        }

        [Fact]
        public void FormatSupportRowLabel_OwnedAddress_StartsWithHexOneBasedId()
        {
            var rom = MakeRom("BE8E01");
            uint unitTableBase = 0x200000;
            uint supportOffset = 0x500000;
            PlantSupportPointer(rom, unitTableBase, unitIndex: 0x05, supportFileOffset: supportOffset);

            string label = SupportUnitNavigation.FormatSupportRowLabel(rom, supportOffset);
            // U.ToHexString(6) returns "06" (2 hex digits, no prefix); make
            // sure label starts with the 1-based hex id.
            Assert.StartsWith("06 ", label);
        }

        // ---- EnumerateSupportEntries ----------------------------------------

        [Fact]
        public void EnumerateSupportEntries_NullRom_ReturnsEmpty()
        {
            using var en = SupportUnitNavigation.EnumerateSupportEntries(null!, blockSize: 24, firstFieldByteWidth: 2).GetEnumerator();
            Assert.False(en.MoveNext());
        }

        [Fact]
        public void EnumerateSupportEntries_KeepsOwnedAllZeroRow()
        {
            // The owned-but-all-zero row regression — Copilot review point 1.
            // Plant a support entry that's all-zero but is "owned" by a unit
            // whose +44 points at that file offset.  The enumeration MUST
            // include the row, even though the first u16 is 0.
            var rom = MakeRom("BE8E01");
            uint baseSupport = 0x300000;
            uint unitTableBase = 0x200000;
            uint supportPtr = rom.RomInfo.support_unit_pointer;
            WriteU32(rom.Data, supportPtr, baseSupport | 0x08000000);

            // Owner: unit index 0x09 points at baseSupport (the all-zero row).
            PlantSupportPointer(rom, unitTableBase, unitIndex: 0x09, supportFileOffset: baseSupport);

            // First row at baseSupport is all-zero (Data[] is zero by default).
            // The enumeration MUST still include this row because the owner-key
            // lookup finds a matching unit.
            var rows = new System.Collections.Generic.List<(uint addr, uint? ownerUid)>(
                SupportUnitNavigation.EnumerateSupportEntries(rom, blockSize: 24, firstFieldByteWidth: 2));

            Assert.NotEmpty(rows);
            Assert.Equal(baseSupport, rows[0].addr);
            Assert.Equal((uint?)0x09, rows[0].ownerUid);
        }

        [Fact]
        public void EnumerateSupportEntries_StopsAtUnownedRunOfZeros()
        {
            // After the owned row there's a run of unowned all-zero rows.
            // The enumeration must terminate (not run all the way to maxRows).
            var rom = MakeRom("BE8E01");
            uint baseSupport = 0x300000;
            uint unitTableBase = 0x200000;
            uint supportPtr = rom.RomInfo.support_unit_pointer;
            WriteU32(rom.Data, supportPtr, baseSupport | 0x08000000);

            // First row: owned.  Subsequent rows: unowned + all zero.
            PlantSupportPointer(rom, unitTableBase, unitIndex: 0x09, supportFileOffset: baseSupport);

            var rows = new System.Collections.Generic.List<(uint addr, uint? ownerUid)>(
                SupportUnitNavigation.EnumerateSupportEntries(rom, blockSize: 24, firstFieldByteWidth: 2, maxRows: 1000));

            // Should be exactly 1 row: owned[0] then 4-ahead lookahead finds
            // nothing else, so enumeration stops.
            Assert.Single(rows);
        }
    }
}
