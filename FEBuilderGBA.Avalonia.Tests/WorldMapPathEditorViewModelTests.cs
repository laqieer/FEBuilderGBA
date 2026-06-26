// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for WorldMapPathEditorViewModel (#1185) — the FE8 World Map Road
// (Path) editor view-model. Covers the FE8 CanEdit gate, the paint/erase/
// eyedropper chip-buffer edits + dirty marking, the render delegates, and the
// no-ROM guard. Synthetic FE8/FE7 ROMs (no real ROM file needed).
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class WorldMapPathEditorViewModelTests
    {
        const uint ROAD_TABLE_OFFSET   = 0x001000;
        const uint PATH_DATA_OFFSET    = 0x002000;
        const uint POINT_TABLE_OFFSET  = 0x003000;
        const uint ROAD_TILE_OFFSET    = 0x008000;
        const uint ICON_PALETTE_OFFSET = 0x009000;
        const uint MAIN_IMAGE_OFFSET      = 0x010000;
        const uint MAIN_PALETTE_OFFSET    = 0x030000;
        const uint MAIN_PALETTEMAP_OFFSET = 0x031000;

        // =================================================================
        // CanEdit gate
        // =================================================================

        [Fact]
        public void CanEdit_True_OnFE8()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                Assert.True(vm.CanEdit);
            });
        }

        [Fact]
        public void CanEdit_False_OnFE7()
        {
            WithRom("AE7E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                Assert.False(vm.CanEdit);
            });
        }

        [Fact]
        public void CanEdit_False_NoRom()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new WorldMapPathEditorViewModel();
                Assert.False(vm.CanEdit);
            }
            finally { CoreState.ROM = saved; }
        }

        // =================================================================
        // PutPathChip — add / update / erase + dirty marking
        // =================================================================

        [Fact]
        public void PutPathChip_AddsChip_AndMarksDirty()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                vm.MarkClean();
                vm.SelectedChipCol = 1; // variant 1
                vm.SelectedChipRow = 2; // row 2

                bool changed = vm.PutPathChip(16, 24);

                Assert.True(changed);
                Assert.Single(vm.Chips);
                Assert.Equal(16, vm.Chips[0].WorldX);
                Assert.Equal(24, vm.Chips[0].WorldY);
                Assert.Equal(8, vm.Chips[0].PathX);   // col 1 * 8
                Assert.Equal(16, vm.Chips[0].PathY);  // row 2 * 8
                Assert.True(vm.IsDirty);
            });
        }

        [Fact]
        public void PutPathChip_UpdatesExistingChip()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                vm.SelectedChipCol = 0; vm.SelectedChipRow = 0;
                vm.PutPathChip(8, 8);
                Assert.Single(vm.Chips);

                vm.SelectedChipCol = 2; vm.SelectedChipRow = 3;
                bool changed = vm.PutPathChip(8, 8);

                Assert.True(changed);
                Assert.Single(vm.Chips); // still one — updated in place
                Assert.Equal(16, vm.Chips[0].PathX); // col 2 * 8
                Assert.Equal(24, vm.Chips[0].PathY); // row 3 * 8
            });
        }

        [Fact]
        public void PutPathChip_EraseColumn_RemovesChip()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                vm.SelectedChipCol = 0; vm.SelectedChipRow = 0;
                vm.PutPathChip(0, 0);
                Assert.Single(vm.Chips);

                vm.SelectedChipCol = 4; // erase column
                Assert.True(vm.IsEraseSelected);
                bool changed = vm.PutPathChip(0, 0);

                Assert.True(changed);
                Assert.Empty(vm.Chips);
            });
        }

        [Fact]
        public void PutPathChip_EraseEmpty_NoChange()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                vm.SelectedChipCol = 4;
                Assert.False(vm.PutPathChip(0, 0));
                Assert.Empty(vm.Chips);
            });
        }

        [Fact]
        public void PickChipAt_SetsPaletteSelection()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                vm.SelectedChipCol = 3; vm.SelectedChipRow = 5;
                vm.PutPathChip(40, 40);

                // Move selection away, then eyedrop the placed chip.
                vm.SelectedChipCol = 0; vm.SelectedChipRow = 0;
                Assert.True(vm.PickChipAt(40, 40));
                Assert.Equal(3, vm.SelectedChipCol);
                Assert.Equal(5, vm.SelectedChipRow);
            });
        }

        // =================================================================
        // Render delegates
        // =================================================================

        [Fact]
        public void RenderComposite_NonNull_OnFE8WithGraphics()
        {
            WithRom("BE8E01", 0x1000000, (rom) =>
            {
                EnsureSkiaService();
                PlantMainFieldGraphic(rom);
                PlantRoadStrip(rom);

                var vm = new WorldMapPathEditorViewModel();
                vm.PutPathChip(0, 0);
                using IImage img = vm.RenderComposite();
                Assert.NotNull(img);
                Assert.Equal(480, img.Width);
                Assert.Equal(320, img.Height);
            });
        }

        [Fact]
        public void RenderChipPalette_NonNull_OnFE8WithRoad()
        {
            WithRom("BE8E01", 0x1000000, (rom) =>
            {
                EnsureSkiaService();
                PlantRoadStrip(rom);

                var vm = new WorldMapPathEditorViewModel();
                using IImage img = vm.RenderChipPalette(out int cols);
                Assert.NotNull(img);
                Assert.Equal(5, cols);
            });
        }

        // =================================================================
        // WritePath gate
        // =================================================================

        [Fact]
        public void WritePath_NoSelection_ReturnsError()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                // CurrentPathId defaults to -1 (no selection).
                string err = vm.WritePath();
                Assert.False(string.IsNullOrEmpty(err));
            });
        }

        // =================================================================
        // *.road.bin file Export / Import (#1458 — WF SaveAS/Load parity)
        // =================================================================

        [Fact]
        public void ExportPathBin_RoundTrips_ThroughImport_AndMarksDirty()
        {
            WithRom("BE8E01", 0x1000000, (rom) =>
            {
                PlantRoadTableAndPath(rom);

                var vm = new WorldMapPathEditorViewModel();
                vm.LoadEntry(0);
                int origCount = vm.Chips.Count;
                Assert.True(origCount >= 1); // planted path has chips

                byte[] bin = vm.ExportPathBin(out string exErr);
                Assert.Equal("", exErr);
                Assert.NotNull(bin);

                // Clear the buffer, then import the exported bytes back.
                var vm2 = new WorldMapPathEditorViewModel();
                vm2.LoadEntry(0);
                vm2.MarkClean();
                string imErr = vm2.ImportPathBin(bin);
                Assert.Equal("", imErr);
                Assert.Equal(origCount, vm2.Chips.Count);
                Assert.True(vm2.IsDirty); // import dirties the editor (WF parity)

                for (int i = 0; i < origCount; i++)
                {
                    Assert.Equal(vm.Chips[i].WorldX, vm2.Chips[i].WorldX);
                    Assert.Equal(vm.Chips[i].WorldY, vm2.Chips[i].WorldY);
                    Assert.Equal(vm.Chips[i].PathX, vm2.Chips[i].PathX);
                    Assert.Equal(vm.Chips[i].PathY, vm2.Chips[i].PathY);
                }
            });
        }

        [Fact]
        public void ExportPathBin_NoSelection_ReturnsError()
        {
            WithRom("BE8E01", 0x1000000, (_) =>
            {
                var vm = new WorldMapPathEditorViewModel();
                // CurrentPathId defaults to -1.
                byte[] bin = vm.ExportPathBin(out string err);
                Assert.Null(bin);
                Assert.False(string.IsNullOrEmpty(err));
            });
        }

        [Fact]
        public void ImportPathBin_Corrupt_LeavesBufferUnchanged()
        {
            WithRom("BE8E01", 0x1000000, (rom) =>
            {
                PlantRoadTableAndPath(rom);

                var vm = new WorldMapPathEditorViewModel();
                vm.LoadEntry(0);
                int before = vm.Chips.Count;
                Assert.True(before >= 1);
                var snapshot = new List<PathChip>(vm.Chips);
                vm.MarkClean();

                // count=250 (>=200) is corrupt — DecodePathBin rejects it.
                string err = vm.ImportPathBin(new byte[] { 0x00, 0x00, 0xFA, 0x01 });
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, vm.Chips.Count);     // unchanged
                Assert.False(vm.IsDirty);                  // not dirtied
                for (int i = 0; i < before; i++)
                    Assert.Equal(snapshot[i].WorldX, vm.Chips[i].WorldX);
            });
        }

        [Fact]
        public void ImportPathBin_LeavesRomAndIdsUntouched()
        {
            WithRom("BE8E01", 0x1000000, (rom) =>
            {
                PlantRoadTableAndPath(rom);

                var vm = new WorldMapPathEditorViewModel();
                vm.LoadEntry(0);
                byte[] romBefore = (byte[])rom.Data.Clone();
                uint addrBefore = vm.CurrentAddr;
                int idBefore = vm.CurrentPathId;

                // A valid 2-chip stream.
                byte[] bin = new byte[]
                {
                    0x00, 0x00, 0x02, 0x01,
                    0x00, 0x00,
                    0x01, 0x04,
                    0xFF, 0x00, 0x00, 0x00,
                };
                string err = vm.ImportPathBin(bin);
                Assert.Equal("", err);

                // Load is a buffer replace — ROM + current path ids untouched.
                Assert.Equal(romBefore, rom.Data);
                Assert.Equal(addrBefore, vm.CurrentAddr);
                Assert.Equal(idBefore, vm.CurrentPathId);
            });
        }

        // =================================================================
        // Palette click mapping (Stretch=Fill fix — Copilot PR #1228 review)
        // =================================================================

        [Theory]
        // posX,posY are in the PaletteScale(4)-scaled, Stretch=Fill DIPs:
        // native px = pos/4, cell = nativePx/8. So one cell = 32 scaled DIPs.
        [InlineData(0.0, 0.0, 0, 0)]        // col 0, row 0
        [InlineData(32.0, 0.0, 1, 0)]       // col 1 (H-flip)
        [InlineData(96.0, 0.0, 3, 0)]       // col 3 (HV-flip)
        [InlineData(128.0, 0.0, 4, 0)]      // col 4 (erase)
        [InlineData(0.0, 64.0, 0, 2)]       // row 2
        public void TryPaletteCell_MapsScaledClickToCell(double x, double y, int expCol, int expRow)
        {
            bool ok = global::FEBuilderGBA.Avalonia.Views.WorldMapPathEditorView
                .TryPaletteCell(x, y, out int col, out int row);
            Assert.True(ok);
            Assert.Equal(expCol, col);
            Assert.Equal(expRow, row);
        }

        [Theory]
        [InlineData(-1.0, 0.0)]             // negative
        [InlineData(160.0, 0.0)]           // col 5 (past the 5-col grid)
        [InlineData(0.0, 480.0)]           // row 15 (past the 15-row grid)
        public void TryPaletteCell_OutsideGrid_ReturnsFalse(double x, double y)
        {
            Assert.False(global::FEBuilderGBA.Avalonia.Views.WorldMapPathEditorView
                .TryPaletteCell(x, y, out _, out _));
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithRom(string sig, int size, Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("synth.gba", new byte[size], sig);
                CoreState.ROM = rom;
                body(rom);
            }
            finally { CoreState.ROM = savedRom; }
        }

        static void EnsureSkiaService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }

        // Plant the 12-byte road table + a packed path stream for entry 0 (so
        // LoadEntry(0) has real chips + a resolvable CurrentAddr).
        static void PlantRoadTableAndPath(ROM rom)
        {
            SetPtr(rom, rom.RomInfo.worldmap_road_pointer, ROAD_TABLE_OFFSET);
            // Minimal point table so the list labels resolve.
            SetPtr(rom, rom.RomInfo.worldmap_point_pointer, POINT_TABLE_OFFSET);
            rom.write_u16(POINT_TABLE_OFFSET + 24, 10);
            rom.write_u16(POINT_TABLE_OFFSET + 26, 20);

            // Packed path: row y=0 with 2 chips, then terminator.
            byte[] packed = new byte[]
            {
                0x00, 0x00, 0x02, 0x01,
                0x01, 0x00, // tile 1, flag 0
                0x02, 0x04, // tile 2, flag 4
                0xFF, 0x00, 0x00, 0x00,
            };
            Array.Copy(packed, 0, rom.Data, PATH_DATA_OFFSET, packed.Length);
            SetPtr(rom, ROAD_TABLE_OFFSET + 0, PATH_DATA_OFFSET); // entry 0 +0 path ptr
        }

        static void PlantRoadStrip(ROM rom)
        {
            byte[] tiles = new byte[15 * 32];
            tiles[0] = 0x12;
            Plant(rom, ROAD_TILE_OFFSET, LZ77.compress(tiles));
            SetPtr(rom, rom.RomInfo.worldmap_road_tile_pointer, ROAD_TILE_OFFSET);

            byte[] pal = new byte[16 * 2];
            pal[2] = 0x1F; // idx1 = red
            Plant(rom, ICON_PALETTE_OFFSET, pal);
            SetPtr(rom, rom.RomInfo.worldmap_icon_palette_pointer, ICON_PALETTE_OFFSET);
        }

        static void PlantMainFieldGraphic(ROM rom)
        {
            // Point table for the marker overlay.
            SetPtr(rom, rom.RomInfo.worldmap_point_pointer, POINT_TABLE_OFFSET);
            rom.write_u16(POINT_TABLE_OFFSET + 24, 10);
            rom.write_u16(POINT_TABLE_OFFSET + 26, 20);

            byte[] image = new byte[(480 * 320) / 2];
            image[0] = 0x01;
            Plant(rom, MAIN_IMAGE_OFFSET, image);
            SetPtr(rom, rom.RomInfo.worldmap_big_image_pointer, MAIN_IMAGE_OFFSET);

            byte[] pal = new byte[256 * 2];
            pal[2] = 0x1F;
            Plant(rom, MAIN_PALETTE_OFFSET, pal);
            SetPtr(rom, rom.RomInfo.worldmap_big_palette_pointer, MAIN_PALETTE_OFFSET);

            byte[] pm = new byte[1280];
            Plant(rom, MAIN_PALETTEMAP_OFFSET, LZ77.compress(pm));
            SetPtr(rom, rom.RomInfo.worldmap_big_palettemap_pointer, MAIN_PALETTEMAP_OFFSET);
        }

        static void Plant(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        static void SetPtr(ROM rom, uint slot, uint dataOffset)
            => rom.write_u32(slot, U.toPointer(dataOffset));
    }
}
