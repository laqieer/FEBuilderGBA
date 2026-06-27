// #1605 — Core round-trip tests for the Summon Unit (FE8) List Expand
// (FIRST-fill + full-2-byte-zero terminator + complete reference repointing +
// the SPECIFIC hardcoded +1 pointer refs / count bytes rewritten per FE8J/FE8U).
//
// The orchestrator under test is SummonUnitExpandCore.ExpandSummonUnitTable,
// which composes:
//   - DataExpansionCore.ExpandTableTo(..., ExpandOptions{ Fill = First,
//     Repoint = RawAndLdrAll, FullZeroTerminatorRow = true })
//   - an audit guard (canonical-slot-covered + plausible-count) + a
//     byte-identical (length-aware) fault restore
//   - the WF SummonUnitForm.cs:78-117 fixups (table+1 pointer refs + count
//     bytes), with DIFFERENT engine addresses for FE8J vs FE8U.
//
// Synthetic ROMs:
//   FE8U — signature "BE8E01", 16 MB → ROMFE8U (version 8, is_multibyte false),
//          summon_unit_pointer = 0x02442C.
//   FE8J — signature "BE8J01", 16 MB → ROMFE8JP (version 8, is_multibyte true),
//          summon_unit_pointer = 0x0243E0.
//   FE6  — signature "AFEJ01", 8 MB → ROMFE6JP (version 6, summon_unit_pointer 0).
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SummonUnitExpandCoreTests : System.IDisposable
    {
        // Synthetic table layout. The base lives well past the header danger
        // zone AND past every hardcoded engine address (max 0x07D14C) so the
        // table bytes never overlap the +1/count fixup sites; a 0xFF free
        // region (for the move) sits further out.
        const uint SummonBase = 0x100000u;     // table base before expand
        const uint FreeRegion = 0x200000u;     // ExpandTableTo's FindFreeSpace scan target
        const int FreeRegionSize = 0x8000;     // 32 KiB — fits the grown table

        const uint EntrySize = SummonUnitExpandCore.EntrySize; // 2

        // FE8U hardcoded engine sites (SummonUnitForm.cs:86/107).
        const uint Fe8uPlus1A = 0x0244A0u;
        const uint Fe8uPlus1B = 0x07AE04u;
        const uint Fe8uCountA = 0x07AD66u;
        const uint Fe8uCountB = 0x024436u;

        // FE8J hardcoded engine sites (SummonUnitForm.cs:82/103).
        const uint Fe8jPlus1A = 0x024450u;
        const uint Fe8jPlus1B = 0x07D14Cu;
        const uint Fe8jCountA = 0x07D0B6u;
        const uint Fe8jCountB = 0x0243EAu;

        readonly ROM _savedRom;
        readonly Undo _savedUndo;

        public SummonUnitExpandCoreTests()
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
        // FE8U — grow, +1 refs, count bytes
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandSummonUnit_FE8U_GrowsListAndRewritesPlus1RefsAndCountBytes()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantSummonRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            // Sanity: the REAL enumerator sees exactly `current` rows pre-expand.
            Assert.Equal(current, SummonUnitExpandCore.CountSummonUnits(rom));

            uint pointerAddr = rom.RomInfo.summon_unit_pointer;
            const uint addCount = 4;
            uint expectedNew = current + addCount; // 7

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand")))
            {
                result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, addCount, null, out string err);
                Assert.True(result.Success, err);
            }

            uint newBase = result.NewBaseAddress;
            Assert.NotEqual(SummonBase, newBase);
            Assert.Equal(expectedNew, result.NewCount);

            // Canonical pointer repointed.
            Assert.Equal(U.toPointer(newBase), rom.u32(pointerAddr));
            Assert.Equal(newBase, rom.p32(pointerAddr));

            // The REAL enumerator now reports EXACTLY expectedNew (full 2-byte
            // zero terminator → u8(term)==0 → stops at newCount, not +1).
            Assert.Equal(expectedNew, SummonUnitExpandCore.CountSummonUnits(rom));

            // FE8U table+1 pointer refs → toPointer(newBase + 1).
            Assert.Equal(U.toPointer(newBase + 1), rom.u32(Fe8uPlus1A));
            Assert.Equal(U.toPointer(newBase + 1), rom.u32(Fe8uPlus1B));

            // FE8U count bytes → newCount - 1 == 6.
            Assert.Equal(expectedNew - 1, rom.u8(Fe8uCountA));
            Assert.Equal(expectedNew - 1, rom.u8(Fe8uCountB));
        }

        // ════════════════════════════════════════════════════════════════
        // FE8J — grow, +1 refs, count bytes (DIFFERENT addresses)
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandSummonUnit_FE8J_GrowsListAndRewritesPlus1RefsAndCountBytes()
        {
            ROM rom = MakeFe8jRom();
            Assert.True(rom.RomInfo.is_multibyte, "FE8J ROM must detect as multibyte.");

            uint current = 3;
            PlantSummonRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            Assert.Equal(current, SummonUnitExpandCore.CountSummonUnits(rom));

            uint pointerAddr = rom.RomInfo.summon_unit_pointer;
            const uint addCount = 4;
            uint expectedNew = current + addCount; // 7

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-j")))
            {
                result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, addCount, null, out string err);
                Assert.True(result.Success, err);
            }

            uint newBase = result.NewBaseAddress;
            Assert.NotEqual(SummonBase, newBase);
            Assert.Equal(expectedNew, result.NewCount);
            Assert.Equal(newBase, rom.p32(pointerAddr));
            Assert.Equal(expectedNew, SummonUnitExpandCore.CountSummonUnits(rom));

            // FE8J table+1 pointer refs.
            Assert.Equal(U.toPointer(newBase + 1), rom.u32(Fe8jPlus1A));
            Assert.Equal(U.toPointer(newBase + 1), rom.u32(Fe8jPlus1B));

            // FE8J count bytes.
            Assert.Equal(expectedNew - 1, rom.u8(Fe8jCountA));
            Assert.Equal(expectedNew - 1, rom.u8(Fe8jCountB));
        }

        // ════════════════════════════════════════════════════════════════
        // Reload-count parity
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandSummonUnit_ReloadCountMatches()
        {
            ROM rom = MakeFe8uRom();
            uint current = 5;
            PlantSummonRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            const uint addCount = 3;
            uint expectedNew = current + addCount; // 8

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-reload")))
            {
                result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, addCount, null, out string err);
                Assert.True(result.Success, err);
            }

            // The Core enumerator reports exactly newCount after the expand.
            // (The VM-level LoadSummonUnitList().Count == newCount assertion lives
            // in the Avalonia wiring test — Core.Tests cannot reference the VM.)
            Assert.Equal(expectedNew, result.NewCount);
            Assert.Equal(expectedNew, SummonUnitExpandCore.CountSummonUnits(rom));
        }

        // ════════════════════════════════════════════════════════════════
        // Version gate — FE6/FE7 rejected, ZERO mutation
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandSummonUnit_FE6_Rejected_NoMutation()
        {
            ROM rom = MakeFe6Rom();
            Assert.Equal(6, rom.RomInfo.version);
            Assert.Equal(0u, rom.RomInfo.summon_unit_pointer);

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            var result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 1, null, out string err);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandSummonUnit_FE7_Rejected_NoMutation()
        {
            ROM rom = MakeFe7uRom();
            Assert.Equal(7, rom.RomInfo.version);
            Assert.Equal(0u, rom.RomInfo.summon_unit_pointer);

            byte[] before = (byte[])rom.Data.Clone();
            var result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 1, null, out string err);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data);
        }

        // ════════════════════════════════════════════════════════════════
        // Cap + empty-list + zero-addCount guards
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandSummonUnit_PastMaxRows_Fails_NoMutation()
        {
            ROM rom = MakeFe8uRom();
            uint current = (uint)SummonUnitExpandCore.MaxRows - 1; // 255
            PlantSummonRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            Assert.Equal(current, SummonUnitExpandCore.CountSummonUnits(rom));

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            // current (255) + 2 = 257 > MaxRows (256) → refuse.
            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-overcap")))
            {
                result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 2, null, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }

            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandSummonUnit_ExactlyToMaxRows_Succeeds()
        {
            ROM rom = MakeFe8uRom();
            uint current = (uint)SummonUnitExpandCore.MaxRows - 4; // 252
            PlantSummonRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-tocap")))
            {
                result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 4, null, out string err);
                Assert.True(result.Success, err);
            }
            Assert.Equal((uint)SummonUnitExpandCore.MaxRows, result.NewCount);
        }

        [Fact]
        public void ExpandSummonUnit_EmptyList_Fails_NoMutation()
        {
            ROM rom = MakeFe8uRom();
            // No valid rows planted — CountSummonUnits returns 0 (row 0's u8 is 0).
            Assert.Equal(0u, SummonUnitExpandCore.CountSummonUnits(rom));

            byte[] before = (byte[])rom.Data.Clone();
            var result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 1, null, out string err);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandSummonUnit_ZeroAddCount_Fails_NoMutation()
        {
            ROM rom = MakeFe8uRom();
            PlantSummonRows(rom, 3);
            byte[] before = (byte[])rom.Data.Clone();
            var result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 0, null, out string err);
            Assert.False(result.Success);
            Assert.Equal(before, rom.Data);
        }

        // ════════════════════════════════════════════════════════════════
        // Atomic restore — forced fault + rollback
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void ExpandSummonUnit_ForcedFault_RestoresByteIdentical()
        {
            // No 0xFF free region AND the ROM is at the 32 MB cap, so
            // FindFreeSpace fails AND the resize path is rejected →
            // ExpandTableTo returns failure → the orchestrator restores the
            // snapshot byte-identical (bytes AND length).
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x02000000], "BE8E01"); // exactly 32 MB FE8U
            CoreState.ROM = rom;
            WritePointer(rom, rom.RomInfo.summon_unit_pointer, SummonBase);
            PlantSummonRows(rom, 3);
            // Deliberately NO PlantFreeRegion — the ROM is all 0x00, no 0xFF run.

            byte[] before = (byte[])rom.Data.Clone();
            int lenBefore = rom.Data.Length;

            var undo = new Undo();
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo.NewUndoData("expand-fault")))
            {
                result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 2, null, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }

            Assert.Equal(lenBefore, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ExpandSummonUnit_Rollback_RestoresByteIdentical()
        {
            ROM rom = MakeFe8uRom();
            uint current = 3;
            PlantSummonRows(rom, current);
            PlantFreeRegion(rom, FreeRegion, FreeRegionSize);

            uint pointerAddr = rom.RomInfo.summon_unit_pointer;
            byte[] before = (byte[])rom.Data.Clone();

            var undo = new Undo();
            CoreState.Undo = undo;
            var ud = undo.NewUndoData("expand-rollback");
            using (ROM.BeginUndoScope(ud))
            {
                var result = SummonUnitExpandCore.ExpandSummonUnitTable(rom, 2, null, out string err);
                Assert.True(result.Success, err);
                Assert.NotEqual(SummonBase, rom.p32(pointerAddr));
            }
            undo.Push(ud);
            undo.RunUndo();

            // The expand (move + repoint + the +1/count fixups) is fully reversed.
            Assert.Equal(before, rom.Data);
            Assert.Equal(SummonBase, rom.p32(pointerAddr));
        }

        // ════════════════════════════════════════════════════════════════
        // CountSummonUnits — never throws on garbage bases
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void CountSummonUnits_BaseNearEof_ReturnsZero_NoThrow()
        {
            ROM rom = MakeFe8uRom();
            uint ptr = rom.RomInfo.summon_unit_pointer;
            // Resolve the base to 1 byte from EOF — a +2 row read there would
            // overrun; the safety-offset gate must reject it WITHOUT throwing.
            uint nearEof = (uint)rom.Data.Length - 1;
            WriteU32(rom, ptr, nearEof + 0x08000000);

            uint count = 0;
            var ex = Record.Exception(() => count = SummonUnitExpandCore.CountSummonUnits(rom));
            Assert.Null(ex);
            Assert.Equal(0u, count);
        }

        [Fact]
        public void CountSummonUnits_ZeroPointer_ReturnsZero_NoThrow()
        {
            ROM rom = MakeFe8uRom();
            WriteU32(rom, rom.RomInfo.summon_unit_pointer, 0);
            uint count = 0;
            var ex = Record.Exception(() => count = SummonUnitExpandCore.CountSummonUnits(rom));
            Assert.Null(ex);
            Assert.Equal(0u, count);
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01"); // FE8U, 16 MB
            Assert.Equal(8, rom.RomInfo.version);
            WritePointer(rom, rom.RomInfo.summon_unit_pointer, SummonBase);
            CoreState.ROM = rom; // ambient ROM for NewUndoDataLow's snapshot
            return rom;
        }

        static ROM MakeFe8jRom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8J01"); // FE8J, 16 MB
            Assert.Equal(8, rom.RomInfo.version);
            WritePointer(rom, rom.RomInfo.summon_unit_pointer, SummonBase);
            CoreState.ROM = rom;
            return rom;
        }

        static ROM MakeFe6Rom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x0800000], "AFEJ01"); // FE6, 8 MB
            Assert.Equal(6, rom.RomInfo.version);
            CoreState.ROM = rom;
            return rom;
        }

        static ROM MakeFe7uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01"); // FE7U, 16 MB
            Assert.Equal(7, rom.RomInfo.version);
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>Plant <paramref name="count"/> valid 2-byte summon rows at the
        /// base (each row's first byte is a non-zero unit id), then a clean
        /// 2-byte all-zero (invalid) terminator row.</summary>
        static void PlantSummonRows(ROM rom, uint count)
        {
            for (uint i = 0; i < count; i++)
            {
                uint row = SummonBase + i * EntrySize;
                // Unit id must always be NON-zero so every row is valid; map i
                // into 1..255 (a `(byte)(0x10 + i)` scheme would wrap to 0 at
                // i==240 and prematurely terminate the high-row-count tests).
                rom.Data[(int)(row + 0)] = (byte)((i % 0xFF) + 1); // unit id (non-zero → valid)
                rom.Data[(int)(row + 1)] = 0x00;                   // summoned id
            }
            // Terminator: the row at index `count` is all-zero → u8 == 0 → invalid.
            uint term = SummonBase + count * EntrySize;
            rom.Data[(int)(term + 0)] = 0x00;
            rom.Data[(int)(term + 1)] = 0x00;
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
