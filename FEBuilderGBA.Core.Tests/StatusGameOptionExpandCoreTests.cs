// #1607 — Core round-trip tests for the Status Screen Option (Game Option)
// List Expand (FIRST-fill + full-zero terminator + complete reference
// repointing).
//
// The orchestrator under test is StatusGameOptionCore.ExpandGameOptionTable,
// which composes:
//   - DataExpansionCore.ExpandTableTo(..., ExpandOptions{ Fill = First,
//     Repoint = RawAndLdrAll, FullZeroTerminatorRow = true })
//   - an audit guard (canonical-slot-covered + plausible-count) + a
//     byte-identical (length-aware) fault restore.
//
// These tests use a synthetic in-memory FE8U ROM (signature "BE8E01",
// version 8 → ROMFE8U) and exercise the REAL StatusGameOptionCore.CountGameOptions
// enumeration predicate (a valid pointer at offset +40), so the FIRST-fill +
// full-zero terminator (grow by EXACTLY addCount, stop at exactly newCount)
// is verified end-to-end.
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class StatusGameOptionExpandCoreTests : System.IDisposable
    {
        // Synthetic table layout. The base lives well past the header danger
        // zone; a 0xFF free region (for the move) sits further out.
        const uint OptBase = 0x1000u;          // table base before expand
        const uint FreeRegion = 0x100000u;     // ExpandTableTo's FindFreeSpace default scan start
        const int FreeRegionSize = 0x8000;     // 32 KiB — fits the grown table

        // Secondary engine references to the game-option base (#1085 all-ref).
        const uint RawSlot = 0x4000u;
        const uint LdrInstr = 0x5000u;         // ARM Thumb LDR r0,[pc,#0] (0x4800)
        const uint LdrSlot = LdrInstr + 4;

        // The per-row ASM pointer at +40 that makes a row VALID
        // (U.isPointer(rom.u32(addr+40)) == true) WITHOUT being self-referential
        // (a base-valued slot would be repointed by the all-ref pass and break
        // the verbatim/FIRST-fill assertions).
        const uint RowAsmPointer = 0x08900000u;

        const uint EntrySize = StatusGameOptionCore.EntrySize; // 44

        readonly ROM _savedRom;
        readonly Undo _savedUndo;

        public StatusGameOptionExpandCoreTests()
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
        // FIRST-fill validity + the exact terminator stop (#1607 finding #1)
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandGameOptionTable_FirstFill_CopiesRow0_GrowsListByExactlyN_StopsAtNewCount()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            // Sanity: the REAL enumerator sees exactly `current` rows pre-expand.
            Assert.Equal(current, StatusGameOptionCore.CountGameOptions(rom));

            // Capture row 0's bytes before the move (the FIRST-fill source).
            byte[] row0Before = rom.getBinaryData(OptBase, EntrySize);

            const uint addCount = 4;
            uint expectedNew = current + addCount;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand")))
            {
                result = StatusGameOptionCore.ExpandGameOptionTable(rom, addCount, null, out string err);
                Assert.True(result.Success, err);
            }

            uint newBase = result.NewBaseAddress;
            Assert.NotEqual(OptBase, newBase);
            Assert.Equal(expectedNew, result.NewCount);

            // Every new visible row [current, expectedNew) == row 0 byte-for-byte.
            for (uint i = current; i < expectedNew; i++)
            {
                byte[] rowBytes = rom.getBinaryData(newBase + i * EntrySize, EntrySize);
                Assert.Equal(row0Before, rowBytes);
            }

            // The REAL enumerator now reports EXACTLY expectedNew — the full
            // 44-byte zero terminator at index newCount has +40 == 0 → invalid →
            // the +40 scan stops there (NOT expectedNew + 1).
            Assert.Equal(expectedNew, StatusGameOptionCore.CountGameOptions(rom));
        }

        [Fact]
        public void ExpandGameOptionTable_AddOne_GrowsByExactlyOne()
        {
            ROM rom = MakeFe8uRom();
            uint current = 5;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand1")))
            {
                result = StatusGameOptionCore.ExpandGameOptionTable(rom, 1, null, out string err);
                Assert.True(result.Success, err);
            }

            Assert.Equal(current + 1, result.NewCount);
            Assert.Equal(current + 1, StatusGameOptionCore.CountGameOptions(rom));
        }

        // The plan's >64-row visibility guarantee (#1607 finding #2): expanding
        // past 64 rows must enumerate beyond 64 (the editor caps at 0x100 now).
        [Fact]
        public void ExpandGameOptionTable_PastSixtyFour_EnumeratesBeyondSixtyFour()
        {
            ROM rom = MakeFe8uRom();
            uint current = 60;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            Assert.Equal(current, StatusGameOptionCore.CountGameOptions(rom));

            const uint addCount = 20; // 60 -> 80, crossing the old 64 cap
            uint expectedNew = current + addCount;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-past64")))
            {
                result = StatusGameOptionCore.ExpandGameOptionTable(rom, addCount, null, out string err);
                Assert.True(result.Success, err);
            }

            Assert.Equal(expectedNew, result.NewCount);
            // The enumerator (cap 0x100) reports all 80 rows — past the old 64 cap.
            Assert.True(expectedNew > 64);
            Assert.Equal(expectedNew, StatusGameOptionCore.CountGameOptions(rom));
        }

        // ════════════════════════════════════════════════════════════════
        // All-reference repoint: raw + LDR
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandGameOptionTable_RepointsRawAndLdrRefs_RecordsSlots_WipesOldRegion()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);
            PlantSecondaryRefs(rom, RawSlot, LdrInstr, OptBase);

            uint pointerAddr = rom.RomInfo.status_game_option_pointer;

            // Sanity: every reference resolves to the old base pre-expand.
            Assert.Equal(OptBase, rom.p32(pointerAddr));
            Assert.Equal(OptBase, rom.p32(RawSlot));
            Assert.Equal(OptBase, rom.p32(LdrSlot));

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-refs")))
            {
                result = StatusGameOptionCore.ExpandGameOptionTable(rom, 2, null, out string err);
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
            byte[] oldRegion = rom.getBinaryData(OptBase, current * EntrySize);
            foreach (byte b in oldRegion)
                Assert.Equal(0, b);
        }

        [Fact]
        public void ExpandGameOptionTable_Rollback_RestoresAllRefsAndOldRegion()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);
            PlantSecondaryRefs(rom, RawSlot, LdrInstr, OptBase);

            uint pointerAddr = rom.RomInfo.status_game_option_pointer;
            byte[] oldRegionBefore = rom.getBinaryData(OptBase, current * EntrySize);

            var undo = new Undo();
            CoreState.Undo = undo;
            var ud = undo.NewUndoData("expand-rollback");
            using (ROM.BeginUndoScope(ud))
            {
                var result = StatusGameOptionCore.ExpandGameOptionTable(rom, 2, null, out string err);
                Assert.True(result.Success, err);
                Assert.NotEqual(OptBase, rom.p32(pointerAddr));
            }
            undo.Push(ud);
            undo.RunUndo();

            // Every reference restored to the old base.
            Assert.Equal(OptBase, rom.p32(pointerAddr));
            Assert.Equal(OptBase, rom.p32(RawSlot));
            Assert.Equal(OptBase, rom.p32(LdrSlot));
            // Old region bytes restored verbatim (the wipe is reversed).
            Assert.Equal(oldRegionBefore, rom.getBinaryData(OptBase, current * EntrySize));
        }

        // ════════════════════════════════════════════════════════════════
        // Audit guard — implausible flood + loud-fail
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandGameOptionTable_ImplausibleRepointFlood_FailsLoudly_NoMutation()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            int flood = StatusGameOptionCore.MaxPlausibleRepointSlots + 5;
            uint slot = 0x6000u;
            for (int i = 0; i < flood; i++, slot += 4)
                WriteU32(rom, slot, U.toPointer(OptBase));

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-flood")))
            {
                result = StatusGameOptionCore.ExpandGameOptionTable(rom, 1, null, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }

            // ZERO net change (bytes AND length) — the snapshot was restored.
            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandGameOptionTable_EmptyList_Fails_NoMutation()
        {
            ROM rom = MakeFe8uRom();
            // No valid rows planted — CountGameOptions returns 0 (row 0's +40 is 0).
            Assert.Equal(0u, StatusGameOptionCore.CountGameOptions(rom));

            byte[] before = (byte[])rom.Data.Clone();
            var result = StatusGameOptionCore.ExpandGameOptionTable(rom, 1, null, out string err);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandGameOptionTable_ZeroAddCount_Fails_NoMutation()
        {
            ROM rom = MakeFe8uRom();
            PlantValidRows(rom, 3);
            byte[] before = (byte[])rom.Data.Clone();
            var result = StatusGameOptionCore.ExpandGameOptionTable(rom, 0, null, out string err);
            Assert.False(result.Success);
            Assert.Equal(before, rom.Data);
        }

        // ════════════════════════════════════════════════════════════════
        // Atomic restore — forced mid-expand fault
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandGameOptionTable_NoFreeSpaceForcedFault_RestoresByteIdentical()
        {
            // No 0xFF free region AND the ROM is at the 32 MB cap, so
            // FindFreeSpace fails AND the resize path is rejected →
            // ExpandTableTo returns failure → the orchestrator restores the
            // snapshot byte-identical (bytes AND length).
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x02000000], "BE8E01"); // exactly 32 MB FE8U
            CoreState.ROM = rom; // ambient ROM for NewUndoDataLow's snapshot
            WritePointer(rom, rom.RomInfo.status_game_option_pointer, OptBase);
            PlantValidRows(rom, 3);
            // Deliberately NO PlantFreeRegion — the ROM is all 0x00, no 0xFF run.

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-fault")))
            {
                result = StatusGameOptionCore.ExpandGameOptionTable(rom, 2, null, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }

            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandGameOptionTable_FaultRestore_ClearsUndoList_SoLaterRollbackIsNoOp()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantValidRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);
            int flood = StatusGameOptionCore.MaxPlausibleRepointSlots + 5;
            uint slot = 0x6000u;
            for (int i = 0; i < flood; i++, slot += 4)
                WriteU32(rom, slot, U.toPointer(OptBase));

            byte[] before = (byte[])rom.Data.Clone();

            var undo = new Undo();
            CoreState.Undo = undo;
            var ud = undo.NewUndoData("expand-fault-clear");
            using (ROM.BeginUndoScope(ud))
            {
                var result = StatusGameOptionCore.ExpandGameOptionTable(rom, 1, ud, out string err);
                Assert.False(result.Success);
                // The ambient scope recorded some ranges before the guard failed,
                // but RestoreSnapshot cleared them.
                Assert.Empty(ud.list);
            }
            undo.Push(ud);
            undo.RunUndo();
            Assert.Equal(before, rom.Data);
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        /// <summary>Build a synthetic FE8U ROM with the game-option pointer set
        /// to <see cref="OptBase"/>.</summary>
        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01"); // FE8U, 16 MB
            Assert.Equal(8, rom.RomInfo.version);
            WritePointer(rom, rom.RomInfo.status_game_option_pointer, OptBase);
            // NewUndoDataLow reads CoreState.ROM.Data.Length for the undo
            // snapshot, so the ROM under test must be the ambient ROM. Dispose
            // restores the saved instance.
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>Plant <paramref name="count"/> valid 44-byte game-option rows
        /// at the base, then a clean all-zero (invalid) terminator row. Each
        /// row's ASM pointer at +40 is a (non-base) pointer so the row is valid,
        /// with a distinct per-row marker at +4 so the verbatim-copy assertions
        /// can tell row 0 from the rest.</summary>
        static void PlantValidRows(ROM rom, uint count)
        {
            for (uint i = 0; i < count; i++)
            {
                uint row = OptBase + i * EntrySize;
                WriteU32(rom, row + 40, RowAsmPointer);   // +40 pointer → row is valid
                // A per-row marker at +4 so a FIRST-fill copy (which uses row 0)
                // is distinguishable from the original rows 1..count-1.
                WriteU32(rom, row + 4, 0xA0000000u + i);
            }
            // Terminator: the row at index `count` is all-zero → +40 not a
            // pointer → invalid (the natural stop).
            uint term = OptBase + count * EntrySize;
            for (uint b = 0; b < EntrySize; b++)
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
