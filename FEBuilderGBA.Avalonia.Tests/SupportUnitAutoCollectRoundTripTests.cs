// SPDX-License-Identifier: GPL-3.0-or-later
// Round-trip tests for the Avalonia Support Unit Editor AutoCollect path (#1455):
// WriteSupportUnit() must mirror per-partner init/growth into the partner's
// reciprocal support slot under the View's ambient undo scope, recompute B21,
// and undo-rollback must restore BOTH rows byte-identically. When AutoCollect is
// off it must write ONLY the 24 selected struct bytes (no reciprocal mutation).
//
// Uses a synthetic FE8U ROM (no real ROM file required) so the mirroring is
// asserted deterministically. CoreState.ROM is mutated -> [Collection("SharedState")].
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SupportUnitAutoCollectRoundTripTests
    {
        const uint SUPPORT_LIMIT = SupportUnitAutoCollectCore.SUPPORT_LIMIT; // 7

        static void WriteU32(byte[] data, uint addr, uint value)
        {
            data[addr + 0] = (byte)(value & 0xFF);
            data[addr + 1] = (byte)((value >> 8) & 0xFF);
            data[addr + 2] = (byte)((value >> 16) & 0xFF);
            data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01"); // FE8U
            Assert.NotNull(rom.RomInfo);
            return rom;
        }

        static void PlantUnitSupportPtr(ROM rom, uint unitTableBase, uint unitId1Based, uint supportFileOffset)
        {
            uint unitPtr = rom.RomInfo.unit_pointer;
            uint dataSize = rom.RomInfo.unit_datasize;
            WriteU32(rom.Data, unitPtr, unitTableBase | 0x08000000);
            uint structAddr = unitTableBase + (unitId1Based - 1) * dataSize;
            WriteU32(rom.Data, structAddr + SupportUnitNavigation.SUPPORT_POINTER_OFFSET_IN_UNIT_STRUCT,
                supportFileOffset | 0x08000000);
        }

        // Build a 2-unit support pair: unit1@rowA, unit2@rowB. Row A lists unit2
        // in slot 0; Row B lists unit1 in slot 3 (reciprocal). Returns rowA/rowB.
        static (uint rowA, uint rowB) PlantPair(ROM rom)
        {
            uint unitTableBase = 0x200000;
            uint rowA = 0x500000;
            uint rowB = 0x500100;
            PlantUnitSupportPtr(rom, unitTableBase, 1, rowA);
            PlantUnitSupportPtr(rom, unitTableBase, 2, rowB);
            rom.Data[rowA + 0] = 2; // row A slot 0 -> unit 2
            rom.Data[rowB + 3] = 1; // row B slot 3 -> unit 1 (reciprocal)
            return (rowA, rowB);
        }

        // ----------------------------------------------------------------
        // AutoCollect ON: WriteSupportUnit mirrors init/growth into the
        // partner's reciprocal slot and recomputes B21.
        // ----------------------------------------------------------------
        [Fact]
        public void Write_AutoCollectOn_MirrorsReciprocalAndRecomputesCount()
        {
            ROM? prev = CoreState.ROM;
            Undo? prevUndo = CoreState.Undo;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                var (rowA, rowB) = PlantPair(rom);

                var vm = new SupportUnitEditorViewModel();
                vm.LoadSupportUnit(rowA);
                Assert.Equal(2u, vm.Partner1);

                // Edit partner1's init/growth.
                vm.InitialValue1 = 0x11;
                vm.GrowthRate1 = 0x22;
                vm.AutoCollect = true;

                var ud = CoreState.Undo!.NewUndoData("support");
                using (ROM.BeginUndoScope(ud))
                {
                    vm.WriteSupportUnit();
                }

                // Row A struct written.
                Assert.Equal(0x11u, rom.u8(rowA + 7));   // init slot 0
                Assert.Equal(0x22u, rom.u8(rowA + 14));  // growth slot 0
                // B21 recomputed = 1 (only slot 0 non-zero).
                Assert.Equal(1u, rom.u8(rowA + 21));
                Assert.Equal(1u, vm.PartnerCount);

                // Reciprocal mirror into row B slot 3.
                Assert.Equal(0x11u, rom.u8(rowB + 3 + SUPPORT_LIMIT));
                Assert.Equal(0x22u, rom.u8(rowB + 3 + SUPPORT_LIMIT + SUPPORT_LIMIT));
            }
            finally
            {
                CoreState.ROM = prev;
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // Undo rollback restores BOTH rows byte-identically.
        // ----------------------------------------------------------------
        [Fact]
        public void Write_AutoCollectOn_UndoRollbackRestoresBothRows()
        {
            ROM? prev = CoreState.ROM;
            Undo? prevUndo = CoreState.Undo;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                var (rowA, rowB) = PlantPair(rom);

                // Snapshot both rows before any edit.
                byte[] beforeA = rom.getBinaryData(rowA, 24);
                byte[] beforeB = rom.getBinaryData(rowB, 24);

                var vm = new SupportUnitEditorViewModel();
                vm.LoadSupportUnit(rowA);
                vm.InitialValue1 = 0x33;
                vm.GrowthRate1 = 0x44;
                vm.AutoCollect = true;

                var ud = CoreState.Undo!.NewUndoData("support");
                using (ROM.BeginUndoScope(ud))
                {
                    vm.WriteSupportUnit();
                }

                // Confirm something changed first.
                Assert.NotEqual(0x33u, beforeA[7]);
                Assert.Equal(0x33u, rom.u8(rowA + 7));
                Assert.Equal(0x33u, rom.u8(rowB + 3 + SUPPORT_LIMIT));

                // Roll back the undo record: each UndoPostion snapshotted the
                // ORIGINAL bytes at construction (write_u8 -> new UndoPostion),
                // so replaying them in reverse restores the pre-write state.
                for (int i = ud.list.Count - 1; i >= 0; i--)
                {
                    rom.write_range(ud.list[i].addr, ud.list[i].data);
                }

                byte[] afterA = rom.getBinaryData(rowA, 24);
                byte[] afterB = rom.getBinaryData(rowB, 24);
                Assert.Equal(beforeA, afterA);
                Assert.Equal(beforeB, afterB);
            }
            finally
            {
                CoreState.ROM = prev;
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // AutoCollect OFF: only the 24 selected struct bytes are written;
        // the partner's reciprocal slot is untouched.
        // ----------------------------------------------------------------
        [Fact]
        public void Write_AutoCollectOff_DoesNotTouchReciprocalRow()
        {
            ROM? prev = CoreState.ROM;
            Undo? prevUndo = CoreState.Undo;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                var (rowA, rowB) = PlantPair(rom);

                byte[] beforeB = rom.getBinaryData(rowB, 24);

                var vm = new SupportUnitEditorViewModel();
                vm.LoadSupportUnit(rowA);
                vm.InitialValue1 = 0x55;
                vm.GrowthRate1 = 0x66;
                vm.PartnerCount = 0; // user value preserved when AutoCollect off
                vm.AutoCollect = false;

                var ud = CoreState.Undo!.NewUndoData("support");
                using (ROM.BeginUndoScope(ud))
                {
                    vm.WriteSupportUnit();
                }

                // Row A struct written.
                Assert.Equal(0x55u, rom.u8(rowA + 7));
                Assert.Equal(0x66u, rom.u8(rowA + 14));
                // B21 NOT recomputed (left at the user-set 0).
                Assert.Equal(0u, rom.u8(rowA + 21));

                // Row B completely untouched.
                byte[] afterB = rom.getBinaryData(rowB, 24);
                Assert.Equal(beforeB, afterB);
            }
            finally
            {
                CoreState.ROM = prev;
                CoreState.Undo = prevUndo;
            }
        }
    }
}
