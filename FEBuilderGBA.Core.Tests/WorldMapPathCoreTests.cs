// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for WorldMapPathCore (#1185) — the FE8 World Map Road (Path) editor's
// data decode (LoadPath), pure packer (PackPath, with the contiguous-run fix),
// ROM-mutating write (WritePath: validate-before-mutate + byte-identical fault
// restore + ambient undo), and the composite + chip-palette renders.
//
// Reuses the synthetic-ROM harness shape from ImageWorldMapCoreTests
// (rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01") + the shared
// StubImageService for renders).
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class WorldMapPathCoreTests
    {
        // Distinct offsets clear of the 0x0..0x200 danger zone and each other.
        const uint ROAD_TABLE_OFFSET   = 0x001000; // 12-byte entries
        const uint PATH_DATA_OFFSET    = 0x002000; // packed path bytes for entry 0
        const uint POINT_TABLE_OFFSET  = 0x003000; // world-map point entries (32 B)
        const uint ROAD_TILE_OFFSET    = 0x008000; // LZ77 road strip (1x15)
        const uint ICON_PALETTE_OFFSET = 0x009000; // 16-color icon palette

        // Main-field-map graphic (so TryRenderMainFieldMap is non-null).
        const uint MAIN_IMAGE_OFFSET      = 0x010000; // 76,800 B
        const uint MAIN_PALETTE_OFFSET    = 0x030000; // 512 B
        const uint MAIN_PALETTEMAP_OFFSET = 0x031000; // LZ77 stream
        const int  MAIN_IMAGE_BYTES   = (480 * 320) / 2; // 76,800
        const int  MAIN_PALETTE_BYTES = 256 * 2;         // 512

        const ushort RED = 0x001F;
        const ushort GREEN = 0x03E0;

        // =================================================================
        // LoadPath
        // =================================================================

        [Fact]
        public void LoadPath_DecodesPlantedChips()
        {
            WithRom((rom) =>
            {
                // Two contiguous chips on row y=2 (x=3,4) + one chip on row y=5.
                PlantRoadTable(rom);
                byte[] packed = BuildPacked(
                    new[] {
                        (x8:3, y8:2, chips: new[]{ (tile:1, flag:0), (tile:2, flag:4) }),
                        (x8:7, y8:5, chips: new[]{ (tile:3, flag:8) }),
                    });
                PlantPathData(rom, 0, packed);

                var list = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Equal(3, list.Count);

                Assert.Equal(3 * 8, list[0].WorldX);
                Assert.Equal(2 * 8, list[0].WorldY);
                Assert.Equal(1 * 8, list[0].PathY);
                Assert.Equal(0 * 8, list[0].PathX);   // flag 0 -> variant 0

                Assert.Equal(4 * 8, list[1].WorldX);  // x8*8 + ix*8
                Assert.Equal(2 * 8, list[1].WorldY);
                Assert.Equal(2 * 8, list[1].PathY);
                Assert.Equal(1 * 8, list[1].PathX);   // flag 4 -> variant 1

                Assert.Equal(7 * 8, list[2].WorldX);
                Assert.Equal(5 * 8, list[2].WorldY);
                Assert.Equal(3 * 8, list[2].PathY);
                Assert.Equal(2 * 8, list[2].PathX);   // flag 8 -> variant 2
            });
        }

        [Fact]
        public void LoadPath_NonFE8_ReturnsEmpty()
        {
            WithRomVersion(MakeFE7Rom, (rom) =>
            {
                Assert.Empty(WorldMapPathCore.LoadPath(rom, 0));
            });
        }

        [Fact]
        public void LoadPath_NullRom_ReturnsEmpty()
        {
            Assert.Empty(WorldMapPathCore.LoadPath(null, 0));
        }

        [Fact]
        public void LoadPath_NullPathPointer_ReturnsEmpty()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                // Entry 0's +0 path pointer is 0 (planted table leaves it null).
                Assert.Empty(WorldMapPathCore.LoadPath(rom, 0));
            });
        }

        // =================================================================
        // PackPath — the contiguous-run fix (Copilot plan review #1)
        // =================================================================

        [Fact]
        public void PackPath_NonContiguousRow_RoundTripsExactly()
        {
            // A row with a GAP: x=0 and x=16 (NOT x=8). The packer must split
            // this into TWO headers so the reload reproduces x=0 and x=16, not
            // x=0 and x=8 (the latent WF bug this Core fixes).
            var chips = new List<PathChip>
            {
                new PathChip(0,  16, 0, 8),   // (x=0, y=16)
                new PathChip(16, 16, 0, 8),   // (x=16, y=16) — gap of 16
            };
            byte[] packed = WorldMapPathCore.PackPath(chips, out string err);
            Assert.Equal("", err);
            Assert.NotNull(packed);

            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, packed);
                var reloaded = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Equal(2, reloaded.Count);
                Assert.Equal(0,  reloaded[0].WorldX);
                Assert.Equal(16, reloaded[0].WorldY);
                Assert.Equal(16, reloaded[1].WorldX); // NOT 8
                Assert.Equal(16, reloaded[1].WorldY);
            });
        }

        [Fact]
        public void PackPath_ContiguousRow_RoundTrips()
        {
            var chips = new List<PathChip>
            {
                new PathChip(0, 0, 0, 0),
                new PathChip(8, 0, 8, 8),    // variant 1, row 1
                new PathChip(16, 0, 16, 16), // variant 2, row 2
            };
            byte[] packed = WorldMapPathCore.PackPath(chips, out string err);
            Assert.Equal("", err);

            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, packed);
                var reloaded = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Equal(3, reloaded.Count);
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(chips[i].WorldX, reloaded[i].WorldX);
                    Assert.Equal(chips[i].WorldY, reloaded[i].WorldY);
                    Assert.Equal(chips[i].PathX, reloaded[i].PathX);
                    Assert.Equal(chips[i].PathY, reloaded[i].PathY);
                }
            });
        }

        [Fact]
        public void PackPath_Empty_ProducesTerminatorOnly()
        {
            byte[] packed = WorldMapPathCore.PackPath(new List<PathChip>(), out string err);
            Assert.Equal("", err);
            Assert.Equal(new byte[] { 0xFF, 0, 0, 0 }, packed);
        }

        [Theory]
        [InlineData(4 * 8, 0)]      // PathX/8 == 4 (the erase column) — rejected
        [InlineData(0, 15 * 8)]     // PathY/8 == 15 (out of the 0..14 strip)
        [InlineData(-8, 0)]         // negative coordinate
        public void PackPath_InvalidChip_ReturnsError(int pathX, int pathY)
        {
            var chips = new List<PathChip> { new PathChip(0, 0, pathX, pathY) };
            byte[] packed = WorldMapPathCore.PackPath(chips, out string err);
            Assert.Null(packed);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void PackPath_WorldCoordOver255Tiles_ReturnsError()
        {
            var chips = new List<PathChip> { new PathChip(256 * 8, 0, 0, 0) };
            byte[] packed = WorldMapPathCore.PackPath(chips, out string err);
            Assert.Null(packed);
            Assert.False(string.IsNullOrEmpty(err));
        }

        // =================================================================
        // WritePath — round-trip + ambient undo + zero-mutation on failure
        // =================================================================

        [Fact]
        public void WritePath_RoundTrips_AndRepoints()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                // Seed an initial (different) path so the entry +0 is a pointer.
                PlantPathData(rom, 0, new byte[] { 0xFF, 0, 0, 0 });

                var chips = new List<PathChip>
                {
                    new PathChip(0, 0, 0, 0),
                    new PathChip(8, 0, 8, 8),
                };
                string err = WorldMapPathCore.WritePath(rom, 0, chips);
                Assert.Equal("", err);

                var reloaded = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Equal(2, reloaded.Count);
                Assert.Equal(0, reloaded[0].WorldX);
                Assert.Equal(8, reloaded[1].WorldX);
            });
        }

        [Fact]
        public void WritePath_AmbientUndo_RestoresByteIdentical()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, new byte[] { 0xFF, 0, 0, 0 });

                byte[] before = (byte[])rom.Data.Clone();
                var chips = new List<PathChip> { new PathChip(0, 0, 0, 0) };

                var ud = new Undo.UndoData
                {
                    time = DateTime.Now,
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };
                using (ROM.BeginUndoScope(ud))
                {
                    string err = WorldMapPathCore.WritePath(rom, 0, chips);
                    Assert.Equal("", err);
                }
                // The scope captured the writes — roll the whole thing back.
                var undo = new Undo();
                undo.Push(ud);
                undo.RunUndo();

                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void WritePath_NonFE8_ReturnsErrorAndZeroMutation()
        {
            WithRomVersion(MakeFE7Rom, (rom) =>
            {
                byte[] before = (byte[])rom.Data.Clone();
                string err = WorldMapPathCore.WritePath(rom, 0, new List<PathChip>());
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void WritePath_InvalidChip_ReturnsErrorAndZeroMutation()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, new byte[] { 0xFF, 0, 0, 0 });
                byte[] before = (byte[])rom.Data.Clone();

                // PathX/8 == 4 is the erase sentinel — must NOT be stored.
                var chips = new List<PathChip> { new PathChip(0, 0, 4 * 8, 0) };
                string err = WorldMapPathCore.WritePath(rom, 0, chips);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void WritePath_NullRom_ReturnsError()
        {
            string err = WorldMapPathCore.WritePath(null, 0, new List<PathChip>());
            Assert.False(string.IsNullOrEmpty(err));
        }

        // =================================================================
        // MakePathList
        // =================================================================

        [Fact]
        public void MakePathList_ListsEntriesWithPointers()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, new byte[] { 0xFF, 0, 0, 0 });
                PlantPathData(rom, 1, new byte[] { 0xFF, 0, 0, 0 });

                var list = WorldMapPathCore.MakePathList(rom);
                Assert.Equal(2, list.Count);
                Assert.Equal(0u, list[0].tag); // path id carried in tag
                Assert.Equal(1u, list[1].tag);
            });
        }

        [Fact]
        public void MakePathList_NonFE8_ReturnsEmpty()
        {
            WithRomVersion(MakeFE7Rom, (rom) =>
                Assert.Empty(WorldMapPathCore.MakePathList(rom)));
        }

        // =================================================================
        // Renders
        // =================================================================

        [Fact]
        public void TryRenderPathComposite_FE8_NonNull()
        {
            WithRom((rom) =>
            {
                PlantMainFieldGraphic(rom);
                PlantRoadStrip(rom);

                var chips = new List<PathChip> { new PathChip(0, 0, 0, 0) };
                IImage img = WorldMapPathCore.TryRenderPathComposite(rom, chips);
                Assert.NotNull(img);
                Assert.Equal(480, img.Width);
                Assert.Equal(320, img.Height);
            });
        }

        [Fact]
        public void TryRenderPathComposite_NonFE8_ReturnsNull()
        {
            WithRomVersion(MakeFE7Rom, (rom) =>
                Assert.Null(WorldMapPathCore.TryRenderPathComposite(rom, new List<PathChip>())));
        }

        [Fact]
        public void TryRenderPathComposite_NullRom_ReturnsNull()
        {
            Assert.Null(WorldMapPathCore.TryRenderPathComposite(null, new List<PathChip>()));
        }

        [Fact]
        public void TryRenderChipPalette_FE8_NonNull_FiveColumns()
        {
            WithRom((rom) =>
            {
                PlantRoadStrip(rom);
                IImage img = WorldMapPathCore.TryRenderChipPalette(rom, out int cols);
                Assert.NotNull(img);
                Assert.Equal(5, cols);
                Assert.Equal(8 * 5, img.Width);  // 40
                Assert.Equal(120, img.Height);
            });
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithRom(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
            }
        }

        static void WithRomVersion(Func<ROM> make, Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = make();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
            }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static ROM MakeFE7Rom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("synth_fe7.gba", data, "AE7E01");
            return rom;
        }

        // Plant the 12-byte road table with the base pointer wired. Entries are
        // left with a null +0 path pointer until PlantPathData wires them.
        static void PlantRoadTable(ROM rom)
        {
            SetPtr(rom, rom.RomInfo.worldmap_road_pointer, ROAD_TABLE_OFFSET);
            // Plant a minimal point table so the list labels resolve (and the
            // marker overlay has points). Entry 0: name text id 0 -> "0".
            SetPtr(rom, rom.RomInfo.worldmap_point_pointer, POINT_TABLE_OFFSET);
            // A single point entry with pointer-or-null shop slots and (x,y).
            // +12/+16/+20 left 0 (null) -> the scan terminates after entry 0.
            rom.write_u16(POINT_TABLE_OFFSET + 24, 10); // x
            rom.write_u16(POINT_TABLE_OFFSET + 26, 20); // y
        }

        // Wire path-data bytes for entry `id` at a per-id offset and repoint +0.
        static void PlantPathData(ROM rom, int id, byte[] packed)
        {
            uint dataOff = PATH_DATA_OFFSET + (uint)id * 0x100;
            Array.Copy(packed, 0, rom.Data, dataOff, packed.Length);
            uint entry = ROAD_TABLE_OFFSET + (uint)id * 12;
            SetPtr(rom, entry + 0, dataOff);
        }

        // Build packed bytes from a row spec for LoadPath tests (NOT through
        // PackPath, so the decode is tested independently of the packer).
        static byte[] BuildPacked(
            (int x8, int y8, (int tile, int flag)[] chips)[] rows)
        {
            var data = new List<byte>();
            foreach (var row in rows)
            {
                data.Add((byte)row.x8);
                data.Add((byte)row.y8);
                data.Add((byte)row.chips.Length);
                data.Add(1);
                foreach (var c in row.chips)
                {
                    data.Add((byte)c.tile);
                    data.Add((byte)c.flag);
                }
            }
            data.Add(0xFF);
            data.Add(0);
            data.Add(0);
            data.Add(0);
            return data.ToArray();
        }

        // Plant the LZ77 road strip (1x15 tiles = 8x120) + icon palette.
        static void PlantRoadStrip(ROM rom)
        {
            byte[] tiles = new byte[15 * 32];
            tiles[0] = 0x12; // non-zero content
            PlantBytes(rom, ROAD_TILE_OFFSET, LZ77.compress(tiles));
            SetPtr(rom, rom.RomInfo.worldmap_road_tile_pointer, ROAD_TILE_OFFSET);

            byte[] pal = new byte[16 * 2];
            pal[1 * 2] = (byte)(RED & 0xFF); pal[1 * 2 + 1] = (byte)(RED >> 8);
            pal[2 * 2] = (byte)(GREEN & 0xFF); pal[2 * 2 + 1] = (byte)(GREEN >> 8);
            PlantBytes(rom, ICON_PALETTE_OFFSET, pal);
            SetPtr(rom, rom.RomInfo.worldmap_icon_palette_pointer, ICON_PALETTE_OFFSET);
        }

        // Plant the FE8 main-field-map graphic (so the composite background is
        // non-null) + a point table for the marker overlay.
        static void PlantMainFieldGraphic(ROM rom)
        {
            PlantRoadTable(rom); // wires the point table for the marker overlay

            byte[] image = new byte[MAIN_IMAGE_BYTES];
            image[0] = 0x01;
            PlantBytes(rom, MAIN_IMAGE_OFFSET, image);
            SetPtr(rom, rom.RomInfo.worldmap_big_image_pointer, MAIN_IMAGE_OFFSET);

            byte[] pal = new byte[MAIN_PALETTE_BYTES];
            pal[1 * 2] = (byte)(RED & 0xFF); pal[1 * 2 + 1] = (byte)(RED >> 8);
            PlantBytes(rom, MAIN_PALETTE_OFFSET, pal);
            SetPtr(rom, rom.RomInfo.worldmap_big_palette_pointer, MAIN_PALETTE_OFFSET);

            byte[] pm = new byte[1280];
            PlantBytes(rom, MAIN_PALETTEMAP_OFFSET, LZ77.compress(pm));
            SetPtr(rom, rom.RomInfo.worldmap_big_palettemap_pointer, MAIN_PALETTEMAP_OFFSET);
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        static void SetPtr(ROM rom, uint pointerSlot, uint dataOffset)
            => rom.write_u32(pointerSlot, U.toPointer(dataOffset));
    }
}
