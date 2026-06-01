// SPDX-License-Identifier: GPL-3.0-or-later
// #837 — Tests for MagicListExpandCore, the shared Magic FEditor + CSA
// Creator "List Expansion" helper.
//
// Proves the all-reference expand mechanism (mirrors WF
// ImageMagicFEditorForm/ImageMagicCSACreatorForm
// MagicListExpandsButton_Click):
//   * BOTH tables (magic-effect 4B + CSA 20B) grow to the fixed 254 rows via
//     DataExpansionCore.ExpandTableTo + RepointAllReferences.
//   * For EACH table, the canonical pointer + a SECOND raw 32-bit pointer +
//     an ARM-Thumb LDR literal-pool load to the old base ALL repoint to the
//     new base after expand, and ALL restore on rollback.
//   * The 0xFFFFFFFF terminator lands at newBase + 254*entrySize and existing
//     rows are copied verbatim.
//   * CSA-pointer NOT_FOUND aborts with ZERO mutation (INCLUDING table-1) and
//     pushes no undo record — proving the load-bearing discovery-before-first-
//     expand ordering.
//   * newCount(254) <= currentCount is rejected.
//
// Synthetic FE8U ROM recipe mirrors MagicCSACoreTests.MakeMinimalFE8URomWithCsa
// (engine signature @0x95d780 + spell-table signature @0x100000 + CSA pointer
// @0x100010 -> CSA table @0x200000 + magic-effect pointer table @0x300000) so
// MagicCSACore.GetCSASpellTablePointer resolves a real pointer. A large 0xFF
// free region is planted at 0x400000 so ExpandTableTo relocates predictably.
//
// [Collection("SharedState")] + save/restore CoreState.ROM/Undo because
// ExpandTableTo repoints CoreState.CommentCache/LintCache and the helper reads
// CoreState through DataExpansionCore.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MagicListExpandCoreTests : IDisposable
    {
        // Synthetic-ROM layout (all offsets; GBA pointers add 0x08000000).
        const uint EngineSigAddr = 0x95d780u;
        const uint TableSigAddr = 0x100000u;
        const uint CsaPointerSlot = 0x100010u;   // GetCSASpellTablePointer() result
        const uint CsaTableBase = 0x200000u;     // table-2 base before expand
        const uint MagicEffectBase = 0x300000u;  // table-1 base before expand

        // Two SEPARATE 0xFF free regions so the two ExpandTableTo relocations
        // (both scan from FindFreeSpace's default 0x100000) land in distinct
        // areas without overlapping. Region A is sized to fit ONLY table-1
        // (magic-effect: 254*4+4 = 1020 B) so table-2 (254*20+4 = 5084 B) can't
        // fit there and falls through to the larger Region B.
        const uint FreeRegionA = 0x400000u;      // table-1 lands here
        const int FreeRegionASize = 0x500;       // 1280 B — fits table-1 only
        const uint FreeRegionB = 0x500000u;      // table-2 lands here
        const int FreeRegionBSize = 0x4000;      // 16 KiB — fits table-2

        // Secondary references to table-1 (magic-effect) base.
        const uint MeRawSlot = 0x004000u;
        const uint MeLdrInstr = 0x005000u;       // ARM Thumb LDR r0,[pc,#0] (0x4800)
        const uint MeLdrSlot = MeLdrInstr + 4;

        // Secondary references to table-2 (CSA) base.
        const uint CsaRawSlot = 0x006000u;
        const uint CsaLdrInstr = 0x007000u;
        const uint CsaLdrSlot = CsaLdrInstr + 4;

        readonly ROM? _savedRom;
        readonly Undo? _savedUndo;

        public MagicListExpandCoreTests()
        {
            _savedRom = CoreState.ROM;
            _savedUndo = CoreState.Undo;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Undo = _savedUndo;
        }

        // ==================================================================
        // All-reference expand: both tables grow + repoint canonical/raw/LDR.
        // ==================================================================

        [Fact]
        public void ExpandMagicLists_GrowsBothTables_WritesTerminators_CopiesRows_RepointsAllRefs()
        {
            ROM rom = MakeRomWithCsa();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // table-1 current count (magic-effect) — we plant a few valid rows.
            const uint meCurrent = 4;
            PlantMagicEffectRows(rom, meCurrent);
            // table-2 current count (CSA) — plant a few valid 20-byte rows.
            const uint csaCurrent = 3;
            PlantCsaRows(rom, csaCurrent);

            PlantSecondaryRefs(rom, MeRawSlot, MeLdrInstr, MagicEffectBase);
            PlantSecondaryRefs(rom, CsaRawSlot, CsaLdrInstr, CsaTableBase);

            // Sanity: every reference resolves to its base before expand.
            Assert.Equal(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
            Assert.Equal(MagicEffectBase, rom.p32(MeRawSlot));
            Assert.Equal(MagicEffectBase, rom.p32(MeLdrSlot));
            Assert.Equal(CsaTableBase, rom.p32(CsaPointerSlot));
            Assert.Equal(CsaTableBase, rom.p32(CsaRawSlot));
            Assert.Equal(CsaTableBase, rom.p32(CsaLdrSlot));

            var result = Expand(rom, meCurrent, csaCurrent);
            Assert.True(result.Success, result.Error);
            Assert.Equal(MagicListExpandCore.NewCount, result.ResultCount);

            uint meNewBase = result.MagicEffectNewBase;
            uint csaNewBase = result.CsaNewBase;
            Assert.NotEqual(MagicEffectBase, meNewBase);
            Assert.NotEqual(CsaTableBase, csaNewBase);

            // 0xFFFFFFFF terminators at newBase + 254*entrySize.
            Assert.Equal(0xFFFFFFFFu,
                rom.u32(meNewBase + MagicListExpandCore.NewCount * MagicListExpandCore.MagicEffectEntrySize));
            Assert.Equal(0xFFFFFFFFu,
                rom.u32(csaNewBase + MagicListExpandCore.NewCount * MagicListExpandCore.CsaEntrySize));

            // table-1 existing rows copied verbatim (distinct per-row marker).
            for (uint i = 0; i < meCurrent; i++)
            {
                uint a = meNewBase + i * MagicListExpandCore.MagicEffectEntrySize;
                Assert.Equal(U.toPointer(0x95d7edu + i), rom.u32(a));
            }
            // table-1 new rows zero-filled.
            for (uint i = meCurrent; i < MagicListExpandCore.NewCount; i++)
            {
                uint a = meNewBase + i * MagicListExpandCore.MagicEffectEntrySize;
                Assert.Equal(0u, rom.u32(a));
            }

            // table-2 existing rows copied verbatim (the +0 field is the marker).
            for (uint i = 0; i < csaCurrent; i++)
            {
                uint a = csaNewBase + i * MagicListExpandCore.CsaEntrySize;
                Assert.Equal(CsaRowMarker(i), rom.u32(a));
            }
            // table-2 new rows zero-filled.
            for (uint i = csaCurrent; i < MagicListExpandCore.NewCount; i++)
            {
                uint a = csaNewBase + i * MagicListExpandCore.CsaEntrySize;
                Assert.Equal(0u, rom.u32(a));
            }

            // ALL references for BOTH tables now point at their new bases.
            Assert.Equal(meNewBase, rom.p32(rom.RomInfo.magic_effect_pointer)); // canonical
            Assert.Equal(meNewBase, rom.p32(MeRawSlot));                        // raw
            Assert.Equal(meNewBase, rom.p32(MeLdrSlot));                        // LDR literal
            Assert.Equal(csaNewBase, rom.p32(CsaPointerSlot));                  // canonical
            Assert.Equal(csaNewBase, rom.p32(CsaRawSlot));                      // raw
            Assert.Equal(csaNewBase, rom.p32(CsaLdrSlot));                      // LDR literal
        }

        [Fact]
        public void ExpandMagicLists_Rollback_RestoresAllRefs_AndOldRegions()
        {
            ROM rom = MakeRomWithCsa();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            const uint meCurrent = 4;
            const uint csaCurrent = 3;
            PlantMagicEffectRows(rom, meCurrent);
            PlantCsaRows(rom, csaCurrent);
            PlantSecondaryRefs(rom, MeRawSlot, MeLdrInstr, MagicEffectBase);
            PlantSecondaryRefs(rom, CsaRawSlot, CsaLdrInstr, CsaTableBase);

            byte[] meOldRegion = rom.getBinaryData(MagicEffectBase, meCurrent * MagicListExpandCore.MagicEffectEntrySize);
            byte[] csaOldRegion = rom.getBinaryData(CsaTableBase, csaCurrent * MagicListExpandCore.CsaEntrySize);

            var result = Expand(rom, meCurrent, csaCurrent);
            Assert.True(result.Success, result.Error);
            Assert.NotEqual(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
            Assert.NotEqual(CsaTableBase, rom.p32(CsaPointerSlot));

            CoreState.Undo.RunUndo();

            // Every reference restored to its old base.
            Assert.Equal(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));
            Assert.Equal(MagicEffectBase, rom.p32(MeRawSlot));
            Assert.Equal(MagicEffectBase, rom.p32(MeLdrSlot));
            Assert.Equal(CsaTableBase, rom.p32(CsaPointerSlot));
            Assert.Equal(CsaTableBase, rom.p32(CsaRawSlot));
            Assert.Equal(CsaTableBase, rom.p32(CsaLdrSlot));
            // Old region bytes restored verbatim (ExpandTableTo had wiped them to 0).
            Assert.Equal(meOldRegion, rom.getBinaryData(MagicEffectBase, meCurrent * MagicListExpandCore.MagicEffectEntrySize));
            Assert.Equal(csaOldRegion, rom.getBinaryData(CsaTableBase, csaCurrent * MagicListExpandCore.CsaEntrySize));
        }

        // ==================================================================
        // CSA NOT_FOUND -> clean abort BEFORE table-1 expand (load-bearing).
        // ==================================================================

        [Fact]
        public void ExpandMagicLists_CsaNotFound_AbortsWithZeroMutation_NoUndoRecord()
        {
            // Build a ROM WITHOUT the CSA spell-table signature so
            // GetCSASpellTablePointer returns NOT_FOUND. We still plant a
            // magic-effect table so a (buggy) early table-1 expand would be
            // detectable as a byte change.
            ROM rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // No CSA signature -> NOT_FOUND.
            Assert.Equal(U.NOT_FOUND, MagicCSACore.GetCSASpellTablePointer(rom));

            // Plant a magic-effect pointer table + rows + free region so a leaked
            // table-1 expand WOULD move the canonical pointer / wipe the region.
            WriteU32(rom, rom.RomInfo.magic_effect_pointer, U.toPointer(MagicEffectBase));
            const uint meCurrent = 4;
            PlantMagicEffectRows(rom, meCurrent);
            PlantFreeRegion(rom, FreeRegionA, FreeRegionASize);

            // Snapshot the ENTIRE ROM before the call.
            byte[] before = (byte[])rom.Data.Clone();
            int undoBufferBefore = CoreState.Undo.UndoBuffer.Count;

            var ud = CoreState.Undo.NewUndoData("MagicListExpand NOT_FOUND test");
            MagicListExpandCore.Result result;
            using (ROM.BeginUndoScope(ud))
            {
                result = MagicListExpandCore.ExpandMagicLists(rom, meCurrent, /*csaCurrent*/0u, ud);
            }

            // Aborted with an error, NO ROM bytes changed (INCLUDING table-1).
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Error));
            Assert.Equal(before, rom.Data);
            // The canonical magic-effect pointer is untouched (no leaked expand).
            Assert.Equal(MagicEffectBase, rom.p32(rom.RomInfo.magic_effect_pointer));

            // The in-scope undo transaction recorded ZERO write positions — the
            // helper performed no ROM writes before aborting (proves the
            // discovery-before-first-expand ordering early-out).
            Assert.True(ud.list == null || ud.list.Count == 0,
                "CSA NOT_FOUND must abort before any ROM write (no undo positions recorded).");
            // And we never pushed the (empty) record onto the undo buffer.
            Assert.Equal(undoBufferBefore, CoreState.Undo.UndoBuffer.Count);
        }

        // ==================================================================
        // newCount(254) <= currentCount guard.
        // ==================================================================

        [Fact]
        public void ExpandMagicLists_NewCountNotGreaterThanCurrent_IsRejected()
        {
            ROM rom = MakeRomWithCsa();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            PlantMagicEffectRows(rom, 4);
            PlantCsaRows(rom, 3);

            // magicEffectCurrentCount == NewCount(254) -> guard rejects.
            byte[] before = (byte[])rom.Data.Clone();
            var result = Expand(rom, MagicListExpandCore.NewCount, 3);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Error));
            // No mutation occurred (guard runs after CSA discovery, before any expand).
            Assert.Equal(before, rom.Data);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>Run the expand inside an ambient undo scope (mirrors the
        /// View's UndoService.Begin/Commit), pushing the transaction on success
        /// so CoreState.Undo.RunUndo() can roll it back.</summary>
        static MagicListExpandCore.Result Expand(ROM rom, uint meCurrent, uint csaCurrent)
        {
            var ud = CoreState.Undo.NewUndoData("MagicListExpand test");
            MagicListExpandCore.Result result;
            using (ROM.BeginUndoScope(ud))
            {
                result = MagicListExpandCore.ExpandMagicLists(rom, meCurrent, csaCurrent, ud);
            }
            if (result.Success)
                CoreState.Undo.Push(ud);
            return result;
        }

        /// <summary>Synthetic FE8U ROM with a detectable CSA magic system, a
        /// magic-effect pointer table @0x300000, a CSA table @0x200000, and a
        /// 0xFF free region @0x400000. Mirrors MagicCSACoreTests recipe.</summary>
        static ROM MakeRomWithCsa()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            // 1) Engine signature (SCA_Creator FE8U variant).
            byte[] engineSig = { 0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0xD9,0xD8,0x95,0x08 };
            Buffer.BlockCopy(engineSig, 0, rom.Data, (int)EngineSigAddr, engineSig.Length);

            // 2) Spell-table signature + 3) CSA pointer right after it.
            byte[] tableSig = { 0x1C,0x58,0x05,0x08,0x00,0x01,0x00,0x80,0xED,0xD7,0x95,0x08,0x99,0xD8,0x95,0x08 };
            Buffer.BlockCopy(tableSig, 0, rom.Data, (int)TableSigAddr, tableSig.Length);
            WriteU32(rom, CsaPointerSlot, U.toPointer(CsaTableBase));

            // 4) Magic-effect pointer table base.
            WriteU32(rom, rom.RomInfo.magic_effect_pointer, U.toPointer(MagicEffectBase));

            // 5) Two 0xFF free regions so the two ExpandTableTo relocations land
            // in distinct areas (Region A fits table-1 only; table-2 -> Region B).
            PlantFreeRegion(rom, FreeRegionA, FreeRegionASize);
            PlantFreeRegion(rom, FreeRegionB, FreeRegionBSize);

            return rom;
        }

        /// <summary>Plant <paramref name="count"/> valid pointer rows in the
        /// magic-effect table (each a single 4-byte pointer marker) + a stop
        /// word after.</summary>
        static void PlantMagicEffectRows(ROM rom, uint count)
        {
            for (uint i = 0; i < count; i++)
                WriteU32(rom, MagicEffectBase + i * MagicListExpandCore.MagicEffectEntrySize,
                    U.toPointer(0x95d7edu + i));
            // Stop word so a read scan stops at `count` (not load-bearing for the
            // helper which takes the count explicitly, but keeps the ROM sane).
            WriteU32(rom, MagicEffectBase + count * MagicListExpandCore.MagicEffectEntrySize, 0xFFFFFFFFu);
        }

        /// <summary>Per-row marker pointer for CSA row <paramref name="i"/>.
        /// Deliberately in a 0x9xxxxx range so it can NEVER collide with either
        /// table base (CsaTableBase 0x200000 / MagicEffectBase 0x300000) — a
        /// self-referential marker would be (correctly) repointed by
        /// RepointAllReferences and break the verbatim-copy assertion.</summary>
        static uint CsaRowMarker(uint i) => U.toPointer(0x900000u + i * 0x100u);

        /// <summary>Plant <paramref name="count"/> valid 20-byte CSA rows (the
        /// +0 pointer field is the per-row marker).</summary>
        static void PlantCsaRows(ROM rom, uint count)
        {
            for (uint i = 0; i < count; i++)
            {
                uint row = CsaTableBase + i * MagicListExpandCore.CsaEntrySize;
                WriteU32(rom, row + 0, CsaRowMarker(i));
                WriteU32(rom, row + 4, 0u);
                WriteU32(rom, row + 8, 0u);
                WriteU32(rom, row + 12, 0u);
                WriteU32(rom, row + 16, 0u);
            }
        }

        /// <summary>Plant a SECOND raw 32-bit pointer + an ARM Thumb LDR
        /// literal-pool load, both referencing <paramref name="baseAddr"/>.</summary>
        static void PlantSecondaryRefs(ROM rom, uint rawSlot, uint ldrInstr, uint baseAddr)
        {
            WriteU32(rom, rawSlot, U.toPointer(baseAddr));   // raw 32-bit pointer
            int ldrIdx = (int)ldrInstr;
            rom.Data[ldrIdx + 0] = 0x00;                     // ldr r0,[pc,#0]
            rom.Data[ldrIdx + 1] = 0x48;                     // = 0x4800
            WriteU32(rom, ldrInstr + 4, U.toPointer(baseAddr)); // literal-pool slot
        }

        static void PlantFreeRegion(ROM rom, uint start, int length)
        {
            int baseIdx = (int)start;
            for (int i = 0; i < length; i++)
                rom.Data[baseIdx + i] = 0xFF;
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
