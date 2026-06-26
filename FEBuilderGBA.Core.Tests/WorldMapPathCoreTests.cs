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

        [Fact]
        public void LoadPath_CorruptCount_ReturnsEmpty_NotPartial()
        {
            // A header claiming count==250 (>= MAX_CHIPS_PER_ROW) is corrupt:
            // LoadPath must return EMPTY (not the chips decoded before it), so a
            // truncated/corrupt road can't reach the editor + be written back
            // truncated (Copilot PR #1228 review #1).
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                var data = new List<byte>
                {
                    // valid first row: 1 chip
                    0, 0, 1, 1,  0, 0,
                    // corrupt second row: count 250
                    1, 0, 250, 1,
                };
                PlantPathData(rom, 0, data.ToArray());
                Assert.Empty(WorldMapPathCore.LoadPath(rom, 0));
            });
        }

        // =================================================================
        // PackPath — the contiguous-run fix (Copilot plan review #1)
        // =================================================================

        [Fact]
        public void PackPath_LongContiguousRun_SplitsAndRoundTrips()
        {
            // A 250-chip contiguous run exceeds the per-header count cap
            // (MAX_CHIPS_PER_ROW-1 == 199). PackPath must SPLIT it into multiple
            // headers so every count round-trips through LoadPath (which rejects
            // count>=200) — all 250 chips reload at their exact positions
            // (Copilot PR #1228 review #2).
            var chips = new List<PathChip>();
            for (int i = 0; i < 250; i++)
                chips.Add(new PathChip(i * 8, 0, 0, 0));

            byte[] packed = WorldMapPathCore.PackPath(chips, out string err);
            Assert.Equal("", err);

            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, packed);
                var reloaded = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Equal(250, reloaded.Count);
                for (int i = 0; i < 250; i++)
                    Assert.Equal(i * 8, reloaded[i].WorldX);
            });
        }

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

        [Fact]
        public void PackPath_StartXTile255_ReturnsError()
        {
            // X/8 == 0xFF writes a header byte LoadPath reads as the TERMINATOR
            // (silently dropping that chip + everything after). PackPath must
            // reject it (Copilot PR #1228 re-review). 0xFE (254*8) is allowed.
            var bad = new List<PathChip> { new PathChip(255 * 8, 0, 0, 0) };
            Assert.Null(WorldMapPathCore.PackPath(bad, out string err));
            Assert.False(string.IsNullOrEmpty(err));

            var ok = new List<PathChip> { new PathChip(254 * 8, 0, 0, 0) };
            Assert.NotNull(WorldMapPathCore.PackPath(ok, out string okErr));
            Assert.Equal("", okErr);
        }

        [Fact]
        public void PackPath_StartXTile255_DoesNotRoundTripAsTerminator()
        {
            // Belt-and-suspenders: prove the rejected stream would otherwise have
            // collapsed at the 0xFF terminator. A chip at X-tile 254 (the max
            // allowed) DOES round-trip; X-tile 255 is rejected outright above.
            var ok = new List<PathChip> { new PathChip(254 * 8, 16, 0, 0) };
            byte[] packed = WorldMapPathCore.PackPath(ok, out string err);
            Assert.Equal("", err);
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                PlantPathData(rom, 0, packed);
                var reloaded = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Single(reloaded);
                Assert.Equal(254 * 8, reloaded[0].WorldX);
            });
        }

        // =================================================================
        // ExportPathBinFromRom / DecodePathBin — *.road.bin file I/O (#1458)
        // =================================================================

        [Fact]
        public void ExportPathBinFromRom_ReturnsRawStream_ThroughTerminator()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                // Two contiguous chips on row y=2 + one on y=5, then terminator.
                byte[] packed = BuildPacked(
                    new[] {
                        (x8:3, y8:2, chips: new[]{ (tile:1, flag:0), (tile:2, flag:4) }),
                        (x8:7, y8:5, chips: new[]{ (tile:3, flag:8) }),
                    });
                PlantPathData(rom, 0, packed);

                WorldMapPathCore.GetPathDataOffset(rom, 0, out uint off);
                byte[] exported = WorldMapPathCore.ExportPathBinFromRom(rom, off, out string err);
                Assert.Equal("", err);
                // Byte-for-byte equal to the planted stream (incl. the 4-byte terminator).
                Assert.Equal(packed, exported);
            });
        }

        [Fact]
        public void ExportPathBinFromRom_PreservesNonCanonicalStream()
        {
            // PARITY ANCHOR (Copilot review #4): WF SaveAS exports raw ROM bytes,
            // so a non-canonical-but-loadable stream must round out byte-for-byte:
            //   * header byte 3 is 0xAB (canonical data writes 1) — ignored on read
            //   * a chip with an unknown flag 0x07 (canonical flags are 0/4/8/0xC,
            //     decoded as variant 0) — its raw byte must be preserved verbatim
            // PackPath would canonicalize both (byte 3 -> 1, flag -> 0); the raw
            // ExportPathBinFromRom must NOT.
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                byte[] noncanon = new byte[]
                {
                    0x00, 0x00, 0x02, 0xAB, // header: x8=0,y8=0,count=2, byte3=0xAB (not 1)
                    0x01, 0x00,             // chip 0: tile=1, flag=0
                    0x02, 0x07,             // chip 1: tile=2, flag=7 (unknown flag -> variant 0)
                    0xFF, 0x00, 0x00, 0x00, // terminator
                };
                PlantPathData(rom, 0, noncanon);

                WorldMapPathCore.GetPathDataOffset(rom, 0, out uint off);
                byte[] exported = WorldMapPathCore.ExportPathBinFromRom(rom, off, out string err);
                Assert.Equal("", err);
                Assert.Equal(noncanon, exported); // exact bytes, NOT canonicalized
            });
        }

        [Fact]
        public void ExportPathBinFromRom_NullRom_ReturnsError()
        {
            byte[] r = WorldMapPathCore.ExportPathBinFromRom(null, 0x2000, out string err);
            Assert.Null(r);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ExportPathBinFromRom_NoDataAtOffset_ReturnsError()
        {
            // An immediate 0xFF terminator yields a 4-byte stream — that's still a
            // valid (empty-road) export. A genuinely empty offset (length 0 only
            // happens on an unsafe/out-of-range addr) returns an error.
            WithRom((rom) =>
            {
                byte[] r = WorldMapPathCore.ExportPathBinFromRom(rom, 0x0, out string err);
                Assert.Null(r); // 0x0 is in the danger zone -> unsafe -> error
                Assert.False(string.IsNullOrEmpty(err));
            });
        }

        [Fact]
        public void ExportPathBinFromRom_ZeroStream_ReturnsError_NoHugeWalk()
        {
            // "No road data" guard (Copilot PR #1564 review): a resolvable offset
            // whose first u32 is 0 is null road data. Without the guard,
            // CalcPathDataLength would walk 4 bytes at a time (count==0) to EOF and
            // Save would export a huge file. The guard rejects it with an error.
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                // PATH_DATA_OFFSET region is all-zero by default.
                byte[] r = WorldMapPathCore.ExportPathBinFromRom(rom, PATH_DATA_OFFSET, out string err);
                Assert.Null(r);
                Assert.False(string.IsNullOrEmpty(err));
            });
        }

        [Fact]
        public void ExportPathBinFromRom_NonFE8_ReturnsError()
        {
            WithRomVersion(MakeFE7Rom, (rom) =>
            {
                byte[] r = WorldMapPathCore.ExportPathBinFromRom(rom, 0x2000, out string err);
                Assert.Null(r);
                Assert.False(string.IsNullOrEmpty(err));
            });
        }

        [Fact]
        public void DecodePathBin_RoundTripsWith_ExportPathBinFromRom()
        {
            WithRom((rom) =>
            {
                PlantRoadTable(rom);
                byte[] packed = BuildPacked(
                    new[] {
                        (x8:3, y8:2, chips: new[]{ (tile:1, flag:0), (tile:2, flag:4) }),
                        (x8:7, y8:5, chips: new[]{ (tile:3, flag:8) }),
                    });
                PlantPathData(rom, 0, packed);

                WorldMapPathCore.GetPathDataOffset(rom, 0, out uint off);
                byte[] exported = WorldMapPathCore.ExportPathBinFromRom(rom, off, out _);

                var chips = WorldMapPathCore.DecodePathBin(exported, out string err);
                Assert.Equal("", err);
                Assert.NotNull(chips);
                // Same decode result as LoadPath from ROM.
                var fromRom = WorldMapPathCore.LoadPath(rom, 0);
                Assert.Equal(fromRom.Count, chips.Count);
                for (int i = 0; i < chips.Count; i++)
                {
                    Assert.Equal(fromRom[i].WorldX, chips[i].WorldX);
                    Assert.Equal(fromRom[i].WorldY, chips[i].WorldY);
                    Assert.Equal(fromRom[i].PathX, chips[i].PathX);
                    Assert.Equal(fromRom[i].PathY, chips[i].PathY);
                }
            });
        }

        [Fact]
        public void DecodePathBin_PackPath_RoundTrips_ByteIdentical()
        {
            // PackPath produces canonical bytes; DecodePathBin then re-PackPath
            // must reproduce the SAME bytes (self-consistent canonical format).
            var chips = new List<PathChip>
            {
                new PathChip(0, 0, 0, 0),
                new PathChip(8, 0, 8, 8),
                new PathChip(0, 16, 16, 16),
            };
            byte[] packed = WorldMapPathCore.PackPath(chips, out string perr);
            Assert.Equal("", perr);

            var decoded = WorldMapPathCore.DecodePathBin(packed, out string derr);
            Assert.Equal("", derr);
            Assert.NotNull(decoded);

            byte[] repacked = WorldMapPathCore.PackPath(decoded, out string rerr);
            Assert.Equal("", rerr);
            Assert.Equal(packed, repacked);
        }

        [Fact]
        public void DecodePathBin_EmptyTerminatorOnly_ReturnsEmptyList()
        {
            var chips = WorldMapPathCore.DecodePathBin(new byte[] { 0xFF, 0, 0, 0 }, out string err);
            Assert.Equal("", err);
            Assert.NotNull(chips);
            Assert.Empty(chips);
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x01, 0x00, 0x00 })]            // no terminator
        [InlineData(new byte[] { 0x00, 0x00, 0xFA, 0x01 })]                        // count 250 (>=200) corrupt
        [InlineData(new byte[] { 0x00, 0x00, 0x02, 0x01, 0x00, 0x00 })]            // truncated mid-pairs
        public void DecodePathBin_Corrupt_ReturnsNullAndError(byte[] bin)
        {
            var chips = WorldMapPathCore.DecodePathBin(bin, out string err);
            Assert.Null(chips);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void DecodePathBin_NullOrTooShort_ReturnsNullAndError()
        {
            Assert.Null(WorldMapPathCore.DecodePathBin(null, out string e1));
            Assert.False(string.IsNullOrEmpty(e1));
            Assert.Null(WorldMapPathCore.DecodePathBin(new byte[] { 0xFF, 0 }, out string e2));
            Assert.False(string.IsNullOrEmpty(e2));
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
        public void TryRenderChipPalette_NonFE8_ReturnsNull()
        {
            // FE8-only gate (Copilot PR #1228 re-review): TryRenderRoad is
            // version-agnostic, so the palette must still be null on FE7 even if
            // the road pointer resolves.
            WithRomVersion(MakeFE7Rom, (rom) =>
            {
                PlantRoadStrip(rom);
                Assert.Null(WorldMapPathCore.TryRenderChipPalette(rom, out int cols));
                Assert.Equal(0, cols);
            });
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
            // 0x400 per id leaves room for a max-length (250-chip) packed path
            // without colliding with the next id's region or the point table.
            uint dataOff = PATH_DATA_OFFSET + (uint)id * 0x400;
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
