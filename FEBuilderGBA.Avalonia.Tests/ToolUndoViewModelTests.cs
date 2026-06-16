// SPDX-License-Identifier: GPL-3.0-or-later
// #1190 — Undo history tool (Avalonia port of WinForms ToolUndoForm).
// Unit-tests the ToolUndoViewModel logic: position<->addr encoding, the
// newest-first history list (HEAD "最新版" down to pos 0), the rollback-enable
// gating (can't roll back to the CURRENT position), and the rollback guards.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolUndoViewModelTests
    {
        // Seed CoreState.ROM (NewUndoData reads ROM.Data.Length) + a fresh undo
        // buffer with `count` pushed snapshots (Postion ends at count == HEAD).
        static (Undo undo, ROM rom) Seed(int count)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x200]);
            CoreState.ROM = rom;

            var undo = new Undo();
            for (int i = 0; i < count; i++)
                undo.Push(undo.NewUndoData("Edit#" + i));
            CoreState.Undo = undo;
            return (undo, rom);
        }

        [Fact]
        public void AddrPosEncoding_RoundTrips_AndAvoidsZero()
        {
            Assert.Equal(1u, ToolUndoViewModel.AddrFromPos(0));   // pos 0 maps to a NON-zero addr
            for (int pos = 0; pos < 10; pos++)
                Assert.Equal(pos, ToolUndoViewModel.PosFromAddr(ToolUndoViewModel.AddrFromPos(pos)));
        }

        [Fact]
        public void LoadList_ListsHeadDownToZero_NewestFirst_AllSelectable()
        {
            var prevUndo = CoreState.Undo; var prevRom = CoreState.ROM;
            try
            {
                var (_, _) = Seed(3);
                var vm = new ToolUndoViewModel();
                var list = vm.LoadList();

                Assert.Equal(4, list.Count);                                   // pos 3 (HEAD) .. 0
                Assert.Equal(ToolUndoViewModel.AddrFromPos(3), list[0].addr);  // newest first
                Assert.Equal(ToolUndoViewModel.AddrFromPos(0), list[3].addr);
                Assert.Contains("最新版", list[0].name);                        // HEAD label
                foreach (var r in list) Assert.False(r.isNULL());             // addr != 0 => selectable
            }
            finally { CoreState.Undo = prevUndo; CoreState.ROM = prevRom; }
        }

        [Fact]
        public void LoadEntry_CurrentPositionCannotRollback_OlderCan()
        {
            var prevUndo = CoreState.Undo; var prevRom = CoreState.ROM;
            try
            {
                var (undo, _) = Seed(3);   // Postion == 3 == HEAD
                var vm = new ToolUndoViewModel();

                vm.LoadEntry(ToolUndoViewModel.AddrFromPos(undo.Postion));     // HEAD
                Assert.Equal(undo.Postion, vm.SelectedPos);
                Assert.False(vm.CanRollback);                                  // already here
                Assert.True(vm.CanTestPlay);
                Assert.NotEqual("", vm.SelectedInfo);

                vm.LoadEntry(ToolUndoViewModel.AddrFromPos(1));                // older snapshot
                Assert.Equal(1, vm.SelectedPos);
                Assert.True(vm.CanRollback);
            }
            finally { CoreState.Undo = prevUndo; CoreState.ROM = prevRom; }
        }

        [Fact]
        public void Rollback_ToCurrentPosition_IsNoOp_ReturnsFalse()
        {
            var prevUndo = CoreState.Undo; var prevRom = CoreState.ROM;
            try
            {
                var (undo, _) = Seed(2);
                var vm = new ToolUndoViewModel();
                Assert.False(vm.Rollback(undo.Postion));   // current -> no-op
                Assert.Equal(2, undo.Postion);             // unchanged
            }
            finally { CoreState.Undo = prevUndo; CoreState.ROM = prevRom; }
        }

        [Fact]
        public void Rollback_ToOlderPosition_AppliesAndMovesPosition()
        {
            var prevUndo = CoreState.Undo; var prevRom = CoreState.ROM;
            try
            {
                var (undo, _) = Seed(3);
                var vm = new ToolUndoViewModel();
                Assert.True(vm.Rollback(1));        // roll back to pos 1
                Assert.Equal(1, undo.Postion);
            }
            finally { CoreState.Undo = prevUndo; CoreState.ROM = prevRom; }
        }

        [Fact]
        public void NullUndo_LoadListEmpty_RollbackFalse()
        {
            var prevUndo = CoreState.Undo;
            try
            {
                CoreState.Undo = null;
                var vm = new ToolUndoViewModel();
                Assert.Empty(vm.LoadList());
                Assert.False(vm.Rollback(0));
            }
            finally { CoreState.Undo = prevUndo; }
        }
    }
}
