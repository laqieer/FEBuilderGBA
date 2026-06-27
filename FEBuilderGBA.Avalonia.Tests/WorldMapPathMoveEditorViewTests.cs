// SPDX-License-Identifier: GPL-3.0-or-later
// #1598 — World Map Path Movement editor corruption fix (Avalonia layer).
// Proves the VM/View behaviour the Core tests underpin:
//   * with NO path selected, LoadList() is empty and CanWrite is false (the old
//     code fell back to the record-table base and walked garbage);
//   * after SelectPath(0) on a planted FE8 ROM, the list populates from
//     p32(record+8) — the movement sub-table, NOT the record table;
//   * a Write to the record-table base / terminator is refused (zero mutation);
//   * the View constructs with the PathType combo present (FindControl).
//
// [Collection("SharedState")] — the tests mutate CoreState.ROM.
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class WorldMapPathMoveEditorViewTests
    {
        const uint ROAD_TABLE_OFFSET = 0x001000;
        const uint MOVE_OFFSET       = 0x004000;
        const uint MOVE2_OFFSET      = 0x005000; // record 1's movement list (immediate terminator -> empty)
        const uint POINT_TABLE_OFFSET = 0x003000;
        const int ROM_SIZE = 0x1000000;

        static void WithRom(System.Action<ROM> body)
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("synth.gba", new byte[ROM_SIZE], "BE8E01");
                CoreState.ROM = rom;
                PlantRecord(rom);
                body(rom);
            }
            finally { CoreState.ROM = saved; }
        }

        static void PlantRecord(ROM rom)
        {
            rom.write_u32(rom.RomInfo.worldmap_road_pointer, U.toPointer(ROAD_TABLE_OFFSET));
            rom.write_u32(rom.RomInfo.worldmap_point_pointer, U.toPointer(POINT_TABLE_OFFSET));
            rom.write_u32(ROAD_TABLE_OFFSET + 0, U.toPointer(0x002000)); // path-data ptr
            rom.write_u32(ROAD_TABLE_OFFSET + 8, U.toPointer(MOVE_OFFSET)); // movement ptr
            // 2 movement nodes (ElapsedTime u16 0x0547 proves 16-bit) + terminator.
            rom.write_u16(MOVE_OFFSET + 0 * 8 + 0, 0x0547);
            rom.write_u16(MOVE_OFFSET + 0 * 8 + 4, 11);
            rom.write_u16(MOVE_OFFSET + 0 * 8 + 6, 22);
            rom.write_u16(MOVE_OFFSET + 1 * 8 + 0, 0x0800);
            rom.write_u16(MOVE_OFFSET + 1 * 8 + 4, 33);
            rom.write_u16(MOVE_OFFSET + 1 * 8 + 6, 44);
            rom.write_u32(MOVE_OFFSET + 2 * 8, 0xFFFFFFFF);

            // Record 1: a second selectable path whose movement list is EMPTY
            // (immediate 0xFFFFFFFF terminator). +0 must be a pointer so it is listed.
            rom.write_u32(ROAD_TABLE_OFFSET + 12 + 0, U.toPointer(0x002800)); // path-data ptr
            rom.write_u32(ROAD_TABLE_OFFSET + 12 + 8, U.toPointer(MOVE2_OFFSET)); // movement ptr
            rom.write_u32(MOVE2_OFFSET + 0, 0xFFFFFFFF); // empty movement list
        }

        [Fact]
        public void Vm_EmptyUntilSelected_PopulatedAfterSelect()
        {
            WithRom((rom) =>
            {
                var vm = new WorldMapPathMoveEditorViewModel();

                // Before SelectPath: no movement base, empty list, no write.
                Assert.False(vm.HasPath);
                Assert.Empty(vm.LoadList());
                Assert.False(vm.CanWrite);

                // The path selector lists the record(s).
                var paths = vm.LoadPathList();
                Assert.True(paths.Count >= 1);

                // After selecting path 0: list populates from p32(record+8).
                Assert.True(vm.SelectPath(0));
                Assert.True(vm.HasPath);
                Assert.Equal(MOVE_OFFSET, vm.MovementBase);

                var nodes = vm.LoadList();
                Assert.Equal(2, nodes.Count); // 2 real nodes, terminator excluded
                Assert.Equal(MOVE_OFFSET + 0 * 8, nodes[0].addr);
            });
        }

        [Fact]
        public void Vm_LoadEntry_DecodesU16ElapsedTime()
        {
            WithRom((rom) =>
            {
                var vm = new WorldMapPathMoveEditorViewModel();
                vm.LoadPathList();
                vm.SelectPath(0);
                vm.LoadEntry(MOVE_OFFSET + 0 * 8);

                Assert.True(vm.IsLoaded);
                Assert.Equal(0x0547u, vm.ElapsedTime); // u16, not a 4-byte DWord
                Assert.Equal(11u, vm.CoordinateX);
                Assert.Equal(22u, vm.CoordinateY);
                Assert.True(vm.CanWrite);
            });
        }

        // Copilot PR #1618 review #1: selecting a node on path A then switching to a
        // path B whose movement list is empty must DROP the loaded node, so a later
        // Write can never silently edit path A's node while path B is shown.
        [Fact]
        public void Vm_SwitchToEmptyPath_DropsStaleNode_NoWrite()
        {
            WithRom((rom) =>
            {
                var vm = new WorldMapPathMoveEditorViewModel();
                vm.LoadPathList();

                // Path 0: select + load a node -> writable.
                vm.SelectPath(0);
                vm.LoadEntry(MOVE_OFFSET + 0 * 8);
                Assert.True(vm.CanWrite);
                Assert.Equal(MOVE_OFFSET + 0 * 8, vm.CurrentAddr);

                // Switch to path 1 (empty movement list). SelectPath must clear the
                // stale node so CanWrite is false and CurrentAddr is reset.
                vm.SelectPath(1);
                Assert.True(vm.HasPath);          // a (empty) movement base still resolved
                Assert.Empty(vm.LoadList());      // no nodes
                Assert.False(vm.CanWrite);        // not writable
                Assert.Equal(0u, vm.CurrentAddr); // stale node dropped

                // A Write now refuses (CurrentAddr == 0) with zero mutation.
                byte[] before = (byte[])rom.Data.Clone();
                string err = vm.Write();
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void Vm_Write_OnRecordBase_RefusedZeroMutation()
        {
            WithRom((rom) =>
            {
                var vm = new WorldMapPathMoveEditorViewModel();
                vm.LoadPathList();
                vm.SelectPath(0);
                // Point CurrentAddr at the record-table base (NOT a movement node).
                vm.LoadEntry(MOVE_OFFSET + 0 * 8);
                vm.CurrentAddr = ROAD_TABLE_OFFSET;

                byte[] before = (byte[])rom.Data.Clone();
                string err = vm.Write();
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void Vm_Write_OnRealNode_Persists()
        {
            WithRom((rom) =>
            {
                var vm = new WorldMapPathMoveEditorViewModel();
                vm.LoadPathList();
                vm.SelectPath(0);
                vm.LoadEntry(MOVE_OFFSET + 0 * 8);
                vm.ElapsedTime = 0x0123;
                vm.CoordinateX = 0x0044;
                vm.CoordinateY = 0x0055;

                string err = vm.Write();
                Assert.Equal("", err);
                Assert.Equal(0x0123u, rom.u16(MOVE_OFFSET + 0));
                Assert.Equal(0x0044u, rom.u16(MOVE_OFFSET + 4));
                Assert.Equal(0x0055u, rom.u16(MOVE_OFFSET + 6));
            });
        }

        [AvaloniaFact]
        public void View_Constructs_WithPathTypeCombo()
        {
            WithRom((rom) =>
            {
                var view = new WorldMapPathMoveEditorView();
                view.Show();
                try
                {
                    var combo = view.FindControl<ComboBox>("PathTypeCombo");
                    Assert.NotNull(combo);
                    var writeBtn = view.FindControl<Button>("WriteButton");
                    Assert.NotNull(writeBtn);
                }
                finally
                {
                    view.Close();
                }
            });
        }
    }
}
