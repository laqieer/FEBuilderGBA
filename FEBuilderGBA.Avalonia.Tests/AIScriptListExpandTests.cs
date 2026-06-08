// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptViewModel.ExpandList tests (#1020).
//
// Proves the Avalonia AI Script editor's "List Expand" actually grows the
// active AI pointer table (ai1 when FilterIndex==0, ai2 when 1) instead of
// surfacing a no-op info dialog. Mirrors WF
// AIScriptForm.AddressListExpandsEventNoCopyPointer:
//   - DataExpansionCore.ExpandTableTo relocates the table to free space,
//     copies the old N slots verbatim, zero-fills the K new slots, writes a
//     0xFFFFFFFF terminator, wipes the old region, and repoints the canonical
//     base slot ai*[0];
//   - ExpandList then repoints the two additional consecutive base-pointer
//     slots ai*[1]/ai*[2] (isPointer-guarded) so all THREE point at the new
//     base (the AI table base lives in 3 consecutive pointer slots in WF);
//   - newCount < ReadCount is rejected with an error and NO relocation;
//   - FilterIndex==1 targets ai2_pointer, not ai1_pointer.
//
// Marked [Collection("SharedState")] because the suite mutates CoreState.ROM.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptListExpandTests
    {
        // A free in-ROM table base, clear of the FE8U ai{1,2}_pointer slots
        // (0x5A91D8 / 0x5A91E4) and any script-body offsets the other AI
        // tests use (0x100000 / 0x200000).
        const uint TableBase = 0x500000;

        /// <summary>
        /// Build a synthetic zero-filled FE8U ROM, plant an N-entry AI pointer
        /// table at <see cref="TableBase"/> (N distinct GBA pointers + a
        /// 0xFFFFFFFF terminator), and set the 3 consecutive base-pointer slots
        /// at <paramref name="basePointer"/> + 0/4/8 to toPointer(TableBase).
        /// </summary>
        static ROM BuildRomWithAiTable(uint basePointer, int n, out uint[] originalPtrs)
        {
            var rom = new ROM();
            rom.LoadLow("x.gba", new byte[0x1000000], "BE8E01"); // FE8U

            originalPtrs = new uint[n];
            for (int i = 0; i < n; i++)
            {
                // Distinct, plainly-valid GBA pointers (well inside the ROM).
                uint gbaPtr = U.toPointer(0x300000u + (uint)i * 0x100u);
                originalPtrs[i] = gbaPtr;
                U.write_u32(rom.Data, TableBase + (uint)i * 4, gbaPtr);
            }
            // 0xFFFFFFFF terminator at base + N*4.
            U.write_u32(rom.Data, TableBase + (uint)n * 4, 0xFFFFFFFF);

            // The 3 consecutive base-pointer slots ai*[0..2] all point at TableBase.
            uint baseGba = U.toPointer(TableBase);
            for (uint s = 0; s < 3; s++)
                U.write_u32(rom.Data, basePointer + s * 4, baseGba);

            return rom;
        }

        // ----------------------------------------------------------------
        // 1. Synthetic round-trip: ai1 table grows, relocates, copies the
        //    originals, zero-fills the new slots, terminates, and repoints
        //    all THREE base-pointer slots.
        // ----------------------------------------------------------------

        [Fact]
        public void ExpandList_Ai1_RelocatesCopiesAndRepointsAllThreeSlots()
        {
            const int N = 3;
            const uint K = 2;

            ROM? prevRom = CoreState.ROM;
            try
            {
                var rom = BuildRomWithAiTable(0x5A91E4 /*ai1_pointer FE8U*/, N, out uint[] origPtrs);
                CoreState.ROM = rom;

                uint oldBase = TableBase;
                uint ai1 = rom.RomInfo.ai1_pointer;
                Assert.Equal(oldBase, U.toOffset(rom.u32(ai1))); // sanity: slot[0] -> old base

                var vm = new AIScriptViewModel
                {
                    FilterIndex = 0,
                    ReadCount = (uint)N,
                    IsLoaded = true,
                };

                var ud = new Undo().NewUndoData("expand");
                string err;
                using (ROM.BeginUndoScope(ud))
                {
                    err = vm.ExpandList((uint)N + K, ROM.GetAmbientUndoData());
                }

                Assert.Equal("", err);
                Assert.Equal((uint)N + K, vm.ReadCount);

                // The table relocated to a NEW base.
                uint newBase = U.toOffset(rom.u32(ai1));
                Assert.NotEqual(oldBase, newBase);
                Assert.True(U.isSafetyOffset(newBase));

                // The original N pointers were copied verbatim at the new base.
                for (int i = 0; i < N; i++)
                    Assert.Equal(origPtrs[i], rom.u32(newBase + (uint)i * 4));

                // The K new slots are zero-filled.
                for (uint i = 0; i < K; i++)
                    Assert.Equal(0u, rom.u32(newBase + ((uint)N + i) * 4));

                // 0xFFFFFFFF terminator at newBase + (N+K)*4.
                Assert.Equal(0xFFFFFFFFu, rom.u32(newBase + ((uint)N + K) * 4));

                // ALL THREE consecutive base-pointer slots now hold toPointer(newBase).
                uint expected = U.toPointer(newBase);
                for (uint s = 0; s < 3; s++)
                    Assert.Equal(expected, rom.u32(ai1 + s * 4));
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        // ----------------------------------------------------------------
        // 2. Guard: newCount < ReadCount returns an error and does NOT relocate.
        // ----------------------------------------------------------------

        [Fact]
        public void ExpandList_NewCountLessThanReadCount_RejectsAndDoesNotRelocate()
        {
            const int N = 3;

            ROM? prevRom = CoreState.ROM;
            try
            {
                var rom = BuildRomWithAiTable(0x5A91E4 /*ai1_pointer FE8U*/, N, out _);
                CoreState.ROM = rom;

                uint ai1 = rom.RomInfo.ai1_pointer;
                uint slotBefore0 = rom.u32(ai1 + 0);
                uint slotBefore1 = rom.u32(ai1 + 4);
                uint slotBefore2 = rom.u32(ai1 + 8);

                var vm = new AIScriptViewModel
                {
                    FilterIndex = 0,
                    ReadCount = (uint)N,
                    IsLoaded = true,
                };

                var ud = new Undo().NewUndoData("expand");
                string err;
                using (ROM.BeginUndoScope(ud))
                {
                    err = vm.ExpandList((uint)N - 1, ROM.GetAmbientUndoData());
                }

                Assert.False(string.IsNullOrEmpty(err)); // non-empty error
                Assert.Equal((uint)N, vm.ReadCount);     // unchanged

                // The table did NOT relocate — all three slots still point at TableBase.
                Assert.Equal(slotBefore0, rom.u32(ai1 + 0));
                Assert.Equal(slotBefore1, rom.u32(ai1 + 4));
                Assert.Equal(slotBefore2, rom.u32(ai1 + 8));
                Assert.Equal(TableBase, U.toOffset(rom.u32(ai1)));
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        // ----------------------------------------------------------------
        // 3. FilterIndex==1 targets ai2_pointer (not ai1_pointer).
        // ----------------------------------------------------------------

        [Fact]
        public void ExpandList_FilterIndex1_TargetsAi2PointerTable()
        {
            const int N = 3;
            const uint K = 1;

            ROM? prevRom = CoreState.ROM;
            try
            {
                var rom = BuildRomWithAiTable(0x5A91D8 /*ai2_pointer FE8U*/, N, out uint[] origPtrs);
                CoreState.ROM = rom;

                uint ai1 = rom.RomInfo.ai1_pointer;
                uint ai2 = rom.RomInfo.ai2_pointer;
                uint oldBase = TableBase;
                // ai1 is untouched (zero) so we can prove ExpandList ignored it.
                uint ai1Before = rom.u32(ai1);

                var vm = new AIScriptViewModel
                {
                    FilterIndex = 1,
                    ReadCount = (uint)N,
                    IsLoaded = true,
                };

                var ud = new Undo().NewUndoData("expand");
                string err;
                using (ROM.BeginUndoScope(ud))
                {
                    err = vm.ExpandList((uint)N + K, ROM.GetAmbientUndoData());
                }

                Assert.Equal("", err);
                Assert.Equal((uint)N + K, vm.ReadCount);

                // ai2 was repointed; the table relocated off TableBase.
                uint newBase = U.toOffset(rom.u32(ai2));
                Assert.NotEqual(oldBase, newBase);
                Assert.True(U.isSafetyOffset(newBase));

                // ai1 was NOT touched (still its original value).
                Assert.Equal(ai1Before, rom.u32(ai1));

                // Originals copied; new slot zero-filled; terminator present.
                for (int i = 0; i < N; i++)
                    Assert.Equal(origPtrs[i], rom.u32(newBase + (uint)i * 4));
                Assert.Equal(0u, rom.u32(newBase + (uint)N * 4));
                Assert.Equal(0xFFFFFFFFu, rom.u32(newBase + ((uint)N + K) * 4));

                // All three ai2 base-pointer slots repointed.
                uint expected = U.toPointer(newBase);
                for (uint s = 0; s < 3; s++)
                    Assert.Equal(expected, rom.u32(ai2 + s * 4));
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }
    }
}
