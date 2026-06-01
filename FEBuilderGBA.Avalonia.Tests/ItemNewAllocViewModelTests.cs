// SPDX-License-Identifier: GPL-3.0-or-later
// VM-level integration tests for the #831 item new-alloc methods on
// ItemFE6ViewModel / ItemEditorViewModel. The exact-byte template + toPointer +
// undo-rollback assertions live in FEBuilderGBA.Core.Tests/ItemAllocCoreTests
// (cross-platform). These tests verify the ViewModel layer wires through to
// ItemAllocCore correctly: the no-clobber + index gate, the field refresh, and
// undo rollback restoring the slot to 0.
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ItemNewAllocViewModelTests
    {
        const uint ItemAddr = 0x00400000u;

        /// <summary>Synthetic FE8U ROM with a single item record whose P12/P16
        /// pointer slots are 0 and an upper-half free-space region (0xFF).</summary>
        static ROM MakeRom()
        {
            var bytes = new byte[0x1100000];
            for (int i = bytes.Length / 2; i < bytes.Length; i++) bytes[i] = 0xFF;
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
            bytes[(int)ItemAddr + 6] = 0x01; // item number (record is plausible)
            rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
            return rom;
        }

        // =============================================================
        // ItemFE6ViewModel — the FE6 gate uses the public SelectedListIndex,
        // so we can drive the full method without LoadItem.
        // =============================================================

        [Fact]
        public void FE6_AllocStatBonuses_SetsPointer_UnderUndoScope()
        {
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                var vm = new ItemFE6ViewModel
                {
                    CurrentAddr = ItemAddr,
                    StatBonusesPtr = 0,
                    SelectedListIndex = 1, // real item row (> 0)
                };

                var undo = CoreState.Undo.NewUndoData("FE6 statbooster alloc");
                bool ok;
                using (ROM.BeginUndoScope(undo))
                {
                    ok = vm.AllocStatBonuses(undo);
                }
                Assert.True(ok);
                // VM field + the ROM record both hold the new GBA pointer.
                Assert.True(U.isPointer(vm.StatBonusesPtr));
                Assert.Equal(vm.StatBonusesPtr, rom.u32(ItemAddr + 12));

                // Rollback restores the slot to 0.
                CoreState.Undo.Push(undo);
                CoreState.Undo.RunUndo();
                Assert.Equal(0u, rom.u32(ItemAddr + 12));
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        [Fact]
        public void FE6_AllocEffectiveness_SetsPointer()
        {
            var prevRom = CoreState.ROM;
            var prevUndo = CoreState.Undo;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();

                var vm = new ItemFE6ViewModel
                {
                    CurrentAddr = ItemAddr,
                    EffectivenessPtr = 0,
                    SelectedListIndex = 1,
                };

                var undo = CoreState.Undo.NewUndoData("FE6 effectiveness alloc");
                bool ok;
                using (ROM.BeginUndoScope(undo))
                {
                    ok = vm.AllocEffectiveness(skillSystemsRework: false, undo);
                }
                Assert.True(ok);
                Assert.True(U.isPointer(vm.EffectivenessPtr));
                Assert.Equal(vm.EffectivenessPtr, rom.u32(ItemAddr + 16));
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Undo = prevUndo;
            }
        }

        [Fact]
        public void FE6_AllocStatBonuses_AlreadyAllocated_NoOp()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                rom.write_u32(ItemAddr + 12, 0x08123456u);

                var vm = new ItemFE6ViewModel
                {
                    CurrentAddr = ItemAddr,
                    StatBonusesPtr = 0x08123456u,
                    SelectedListIndex = 1,
                };
                bool ok = vm.AllocStatBonuses(undoData: null);
                Assert.False(ok); // refused — non-zero pointer
                Assert.Equal(0x08123456u, rom.u32(ItemAddr + 12));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void FE6_AllocStatBonuses_IndexZero_NoOp()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                var vm = new ItemFE6ViewModel
                {
                    CurrentAddr = ItemAddr,
                    StatBonusesPtr = 0,
                    SelectedListIndex = 0, // dummy row 0 — gate closed
                };
                bool ok = vm.AllocStatBonuses(undoData: null);
                Assert.False(ok);
                Assert.Equal(0u, rom.u32(ItemAddr + 12));
                Assert.False(vm.ShowAllocStatBonuses); // button hidden
            }
            finally { CoreState.ROM = prevRom; }
        }

        // =============================================================
        // ItemEditorViewModel — guard checks that do not require the private
        // _currentItemIndex (null-ROM / no-CurrentAddr early returns).
        // =============================================================

        [Fact]
        public void ItemEditor_AllocStatBonuses_NoCurrentAddr_NoOp()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeRom();
                var vm = new ItemEditorViewModel(); // CurrentAddr == 0
                Assert.False(vm.AllocStatBonuses(undoData: null));
                Assert.False(vm.AllocEffectiveness(skillSystemsRework: false, undoData: null));
            }
            finally { CoreState.ROM = prevRom; }
        }
    }
}
