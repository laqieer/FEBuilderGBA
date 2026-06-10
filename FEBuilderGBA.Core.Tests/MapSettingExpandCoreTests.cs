// #1085 — Core round-trip tests for the Map Settings List Expand
// (FIRST-fill + complete reference repointing).
//
// The orchestrator under test is MapSettingCore.ExpandMapSettingTable, which
// composes:
//   - DataExpansionCore.ExpandTableTo(..., ExpandOptions{ Fill = First,
//     Repoint = RawAndLdrAll })  — FIRST-fill new rows + all-reference repoint.
//   - an audit guard (canonical-slot-covered + plausible-count) + a
//     byte-identical (length-aware) fault restore.
//
// These tests use a synthetic in-memory FE6 ROM (signature "AFEJ01", version 6,
// map_setting_datasize 68/72) and exercise the REAL MapSettingCore.MakeMapIDList
// / IsMapSettingValid enumeration predicate so the FIRST-fill off-by-one
// (newCount visible rows, terminator at row newCount, list grows by EXACTLY
// newCount - currentCount) is verified end-to-end.
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapSettingExpandCoreTests : System.IDisposable
    {
        // Synthetic table layout. The base lives well past the header danger
        // zone; two 0xFF free regions (one for the move) sit further out.
        const uint MapBase = 0x1000u;          // table base before expand
        const uint FreeRegion = 0x100000u;     // ExpandTableTo's FindFreeSpace default scan start
        const int FreeRegionSize = 0x4000;     // 16 KiB — fits the grown table

        // Secondary engine references to the map-setting base (#1085 all-ref).
        const uint RawSlot = 0x4000u;
        const uint LdrInstr = 0x5000u;         // ARM Thumb LDR r0,[pc,#0] (0x4800)
        const uint LdrSlot = LdrInstr + 4;

        // A non-base pointer used as the per-row D0 so a row is immediately
        // valid (IsMapSettingValid early-returns on a pointer D0) WITHOUT being
        // self-referential (a base-valued D0 would be repointed by the all-ref
        // pass and break the verbatim/FIRST-fill assertions).
        const uint RowD0Pointer = 0x08900000u;

        readonly ROM? _savedRom;
        readonly Undo? _savedUndo;

        public MapSettingExpandCoreTests()
        {
            _savedRom = CoreState.ROM;
            _savedUndo = CoreState.Undo;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Undo = _savedUndo;
        }

        // ════════════════════════════════════════════════════════════════
        // FIRST-fill validity + the exact off-by-one (#1085 finding #1)
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandMapSettingTable_FirstFill_CopiesRow0_GrowsListByExactlyN()
        {
            ROM rom = MakeFe6Rom(out uint entrySize);
            uint current = 3;
            PlantValidRows(rom, entrySize, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            // Sanity: the REAL enumerator sees exactly `current` rows pre-expand.
            Assert.Equal((int)current, MapSettingCore.MakeMapIDList(rom).Count);

            // Capture row 0's bytes before the move (the FIRST-fill source).
            byte[] row0Before = rom.getBinaryData(MapBase, entrySize);

            const uint addCount = 4;
            uint expectedNew = current + addCount;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand")))
            {
                result = MapSettingCore.ExpandMapSettingTable(rom, addCount, null, out string err);
                Assert.True(result.Success, err);
            }

            uint newBase = result.NewBaseAddress;
            Assert.NotEqual(MapBase, newBase);
            Assert.Equal(expectedNew, result.NewCount);

            // Every new visible row [current, expectedNew) == row 0 byte-for-byte.
            for (uint i = current; i < expectedNew; i++)
            {
                byte[] rowBytes = rom.getBinaryData(newBase + i * entrySize, entrySize);
                Assert.Equal(row0Before, rowBytes);
            }

            // The REAL enumerator now grows by EXACTLY addCount — NOT one fewer
            // (the WF off-by-one trap). The pointer slot was repointed to
            // newBase. That the list count is EXACTLY expectedNew (and not
            // expectedNew + 1) proves the terminator row at index newCount is
            // INVALID — MakeMapIDList stops at the first invalid row, so a valid
            // terminator would have produced a longer list.
            var list = MapSettingCore.MakeMapIDList(rom);
            Assert.Equal((int)expectedNew, list.Count);
            Assert.Equal(newBase, list[0].addr);
            // Cross-check: the row index reported for the last entry == newCount-1,
            // so row newCount is the (invalid) terminator the loop stopped at.
            Assert.Equal(expectedNew - 1, list[list.Count - 1].tag);
        }

        [Fact]
        public void ExpandMapSettingTable_AddOne_GrowsByExactlyOne()
        {
            ROM rom = MakeFe6Rom(out uint entrySize);
            uint current = 5;
            PlantValidRows(rom, entrySize, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand1")))
            {
                result = MapSettingCore.ExpandMapSettingTable(rom, 1, null, out string err);
                Assert.True(result.Success, err);
            }

            Assert.Equal(current + 1, result.NewCount);
            Assert.Equal((int)(current + 1), MapSettingCore.MakeMapIDList(rom).Count);
        }

        // ════════════════════════════════════════════════════════════════
        // All-reference repoint: raw + LDR (#1085 finding #3/#5)
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandMapSettingTable_RepointsRawAndLdrRefs_RecordsSlots_WipesOldRegion()
        {
            ROM rom = MakeFe6Rom(out uint entrySize);
            uint current = 3;
            PlantValidRows(rom, entrySize, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);
            PlantSecondaryRefs(rom, RawSlot, LdrInstr, MapBase);

            uint pointerAddr = rom.RomInfo.map_setting_pointer;

            // Sanity: every reference resolves to the old base pre-expand.
            Assert.Equal(MapBase, rom.p32(pointerAddr));
            Assert.Equal(MapBase, rom.p32(RawSlot));
            Assert.Equal(MapBase, rom.p32(LdrSlot));

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-refs")))
            {
                result = MapSettingCore.ExpandMapSettingTable(rom, 2, null, out string err);
                Assert.True(result.Success, err);
            }

            uint newBase = result.NewBaseAddress;

            // ALL references now point at the new base.
            Assert.Equal(newBase, rom.p32(pointerAddr)); // canonical
            Assert.Equal(newBase, rom.p32(RawSlot));     // raw
            Assert.Equal(newBase, rom.p32(LdrSlot));     // LDR literal

            // The result records the repointed slots, INCLUDING the canonical one.
            Assert.Contains(pointerAddr, result.RepointedSlots);
            Assert.Contains(RawSlot, result.RepointedSlots);
            Assert.Contains(LdrSlot, result.RepointedSlots);
            Assert.True(result.RepointedSlots.Count >= 3);

            // The old region was wiped (ExpandTableTo zeroes it).
            byte[] oldRegion = rom.getBinaryData(MapBase, current * entrySize);
            foreach (byte b in oldRegion)
                Assert.Equal(0, b);
        }

        [Fact]
        public void ExpandMapSettingTable_Rollback_RestoresAllRefsAndOldRegion()
        {
            ROM rom = MakeFe6Rom(out uint entrySize);
            uint current = 3;
            PlantValidRows(rom, entrySize, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);
            PlantSecondaryRefs(rom, RawSlot, LdrInstr, MapBase);

            uint pointerAddr = rom.RomInfo.map_setting_pointer;
            byte[] oldRegionBefore = rom.getBinaryData(MapBase, current * entrySize);

            var undo = new Undo();
            CoreState.Undo = undo;
            var ud = undo.NewUndoData("expand-rollback");
            using (ROM.BeginUndoScope(ud))
            {
                var result = MapSettingCore.ExpandMapSettingTable(rom, 2, null, out string err);
                Assert.True(result.Success, err);
                Assert.NotEqual(MapBase, rom.p32(pointerAddr));
            }
            // Push the recorded undo data so RunUndo can replay it (mirrors the
            // Avalonia UndoService.Commit / MagicListExpandCoreTests pattern).
            undo.Push(ud);

            undo.RunUndo();

            // Every reference restored to the old base.
            Assert.Equal(MapBase, rom.p32(pointerAddr));
            Assert.Equal(MapBase, rom.p32(RawSlot));
            Assert.Equal(MapBase, rom.p32(LdrSlot));
            // Old region bytes restored verbatim (the wipe is reversed).
            Assert.Equal(oldRegionBefore, rom.getBinaryData(MapBase, current * entrySize));
        }

        // ════════════════════════════════════════════════════════════════
        // Audit guard — planted false-positive + loud-fail (#1085 finding #4)
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandMapSettingTable_PlantedRawFalsePositive_IsRepointed_WfParity_AndRecorded()
        {
            // A coincidental u32 == base somewhere harmless. WF parity: it gets
            // repointed (the raw scan can't distinguish a real ref from a
            // coincidence). This test makes that accepted behavior explicit and
            // visible via the recorded slot list.
            ROM rom = MakeFe6Rom(out uint entrySize);
            uint current = 3;
            PlantValidRows(rom, entrySize, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            // Plant a single coincidental raw u32 == base (NOT a real engine ref)
            // at a harmless, non-danger-zone slot.
            const uint falsePositiveSlot = 0x6000u;
            WriteU32(rom, falsePositiveSlot, U.toPointer(MapBase));

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-fp")))
            {
                result = MapSettingCore.ExpandMapSettingTable(rom, 1, null, out string err);
                Assert.True(result.Success, err);
            }

            // WF parity: the coincidental slot WAS repointed and IS recorded.
            Assert.Contains(falsePositiveSlot, result.RepointedSlots);
            Assert.Equal(result.NewBaseAddress, rom.p32(falsePositiveSlot));
        }

        [Fact]
        public void ExpandMapSettingTable_ImplausibleRepointFlood_FailsLoudly_NoMutation()
        {
            // Plant MaxPlausibleRepointSlots + a few coincidental raw u32 == base
            // values so the all-ref scan finds a flood — the audit guard must
            // abort with ZERO net change.
            ROM rom = MakeFe6Rom(out uint entrySize);
            uint current = 3;
            PlantValidRows(rom, entrySize, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            int flood = MapSettingCore.MaxPlausibleRepointSlots + 5;
            uint slot = 0x6000u;
            for (int i = 0; i < flood; i++, slot += 4)
                WriteU32(rom, slot, U.toPointer(MapBase));

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-flood")))
            {
                result = MapSettingCore.ExpandMapSettingTable(rom, 1, null, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }

            // ZERO net change (bytes AND length) — the snapshot was restored.
            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandMapSettingTable_EmptyList_Fails_NoMutation()
        {
            ROM rom = MakeFe6Rom(out uint entrySize);
            // No valid rows planted — MakeMapIDList returns 0 (the base row is
            // all-zero → invalid).
            Assert.Empty(MapSettingCore.MakeMapIDList(rom));

            byte[] before = (byte[])rom.Data.Clone();
            var result = MapSettingCore.ExpandMapSettingTable(rom, 1, null, out string err);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandMapSettingTable_ZeroAddCount_Fails_NoMutation()
        {
            ROM rom = MakeFe6Rom(out uint entrySize);
            PlantValidRows(rom, entrySize, 3);
            byte[] before = (byte[])rom.Data.Clone();
            var result = MapSettingCore.ExpandMapSettingTable(rom, 0, null, out string err);
            Assert.False(result.Success);
            Assert.Equal(before, rom.Data);
        }

        // ════════════════════════════════════════════════════════════════
        // Atomic restore — forced mid-expand fault (#1085 finding #5)
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandMapSettingTable_NoFreeSpaceForcedFault_RestoresByteIdentical()
        {
            // No 0xFF free region AND the ROM is at the 32 MB cap, so
            // FindFreeSpace fails AND the resize path is rejected →
            // ExpandTableTo returns failure → the orchestrator restores the
            // snapshot byte-identical (bytes AND length).
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x02000000], "AFEJ01"); // exactly 32 MB
            CoreState.ROM = rom; // ambient ROM for NewUndoDataLow's snapshot
            uint entrySize = rom.RomInfo.map_setting_datasize;
            WritePointer(rom, rom.RomInfo.map_setting_pointer, MapBase);
            PlantValidRows(rom, entrySize, 3);
            // Deliberately NO PlantFreeRegion — the ROM is all 0x00, no 0xFF run.

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-fault")))
            {
                result = MapSettingCore.ExpandMapSettingTable(rom, 2, null, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }

            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        /// <summary>Build a synthetic FE6 ROM with the map-setting pointer set
        /// to <see cref="MapBase"/>. Returns the FE6 map-setting datasize.</summary>
        static ROM MakeFe6Rom(out uint entrySize)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01"); // FE6 JP, 16 MB
            entrySize = rom.RomInfo.map_setting_datasize;
            Assert.True(entrySize == 68 || entrySize == 72);
            WritePointer(rom, rom.RomInfo.map_setting_pointer, MapBase);
            // NewUndoDataLow reads CoreState.ROM.Data.Length for the undo
            // snapshot, so the ROM under test must be the ambient ROM. Dispose
            // restores the saved instance.
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>Plant <paramref name="count"/> valid FE6 map-setting rows at
        /// the base, then a clean all-zero (invalid) terminator row. Each row's
        /// D0 is a (non-base) pointer so IsMapSettingValid early-returns valid,
        /// with a distinct per-row marker in a later field so the verbatim-copy
        /// assertions can tell row 0 from the rest.</summary>
        static void PlantValidRows(ROM rom, uint entrySize, uint count)
        {
            for (uint i = 0; i < count; i++)
            {
                uint row = MapBase + i * entrySize;
                WriteU32(rom, row + 0, RowD0Pointer);    // D0 pointer → row is valid
                // A per-row marker at +4 (also makes the PLIST non-zero) so a
                // FIRST-fill copy (which uses row 0) is distinguishable from the
                // original rows 1..count-1.
                WriteU32(rom, row + 4, 0xA0000000u + i);
            }
            // Terminator: the row at index `count` is all-zero → D0 not a
            // pointer, weather 0, both PLISTs 0 → invalid (the natural stop).
            uint term = MapBase + count * entrySize;
            for (uint b = 0; b < entrySize; b++)
                rom.Data[(int)(term + b)] = 0x00;
        }

        static void PlantSecondaryRefs(ROM rom, uint rawSlot, uint ldrInstr, uint baseAddr)
        {
            WriteU32(rom, rawSlot, U.toPointer(baseAddr));      // raw 32-bit pointer
            int ldrIdx = (int)ldrInstr;
            rom.Data[ldrIdx + 0] = 0x00;                        // ldr r0,[pc,#0]
            rom.Data[ldrIdx + 1] = 0x48;                        // = 0x4800
            WriteU32(rom, ldrInstr + 4, U.toPointer(baseAddr)); // literal-pool slot
        }

        static void PlantFreeRegion(ROM rom, uint start, int length)
        {
            int baseIdx = (int)start;
            for (int i = 0; i < length; i++)
                rom.Data[baseIdx + i] = 0xFF;
        }

        static void WritePointer(ROM rom, uint addr, uint offset)
        {
            WriteU32(rom, addr, offset + 0x08000000);
        }

        static void WriteU32(ROM rom, uint addr, uint value)
        {
            int idx = (int)addr;
            rom.Data[idx + 0] = (byte)(value & 0xFF);
            rom.Data[idx + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[idx + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[idx + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
