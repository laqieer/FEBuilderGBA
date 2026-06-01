// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageWorldMapCore (#843, NV5a) — the World Map Image editor's
// reuse-based live previews (event / mini / point1 / point2 / road).
//
// Verifies, per preview, on a synthetic ROM with the planted graphic at the
// canonical RomInfo pointer(s):
//   * a non-null IImage of the EXACT dims from the #843 review table,
//   * null-safety: a bad / truncated / missing image pointer -> null no-crash.
// The EVENT test additionally asserts the 4-sub-palette read (CAUTION 2): a
// pixel whose TSA cell selects sub-palette 1 renders sub-palette 1's color, NOT
// sub-palette 0's — proving the 64-color (128-byte) read, not a 16-color read.
//
// Reuses the synthetic-ROM harness shape from ImageTSAEditorCoreTests
// (StubImageService captures RGBA; LZ77.compress plants the image / TSA;
// raw palette planted with explicit sub-palette banks). [Collection("SharedState")]
// + CoreState.ROM/ImageService save+restore because TryRenderIconStrip reads
// CoreState.ROM (via LoadROMTiles4bpp).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageWorldMapCoreTests
    {
        // Distinct data offsets for each planted stream (well clear of the
        // 0x0..0x200 danger zone and of each other).
        const uint EVENT_IMAGE_OFFSET   = 0x001000;
        const uint EVENT_TSA_OFFSET     = 0x002000;
        const uint EVENT_PALETTE_OFFSET = 0x003000;
        const uint MINI_IMAGE_OFFSET    = 0x004000;
        const uint MINI_PALETTE_OFFSET  = 0x005000;
        const uint ICON1_OFFSET         = 0x006000;
        const uint ICON2_OFFSET         = 0x007000;
        const uint ROAD_OFFSET          = 0x008000;
        const uint ICON_PALETTE_OFFSET  = 0x009000;

        // GBA 5-5-5 colors.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;
        const ushort WHITE = 0x7FFF;

        // =================================================================
        // Event — dims + the two-LZ77-streams + 64-color 4-sub-palette read
        // =================================================================

        [Fact]
        public void Event_RendersExpectedDims_256x160()
        {
            WithRom((rom) =>
            {
                // 32x20 tiles of image (one marker tile reused), header-TSA {0,0}
                // + one cell pointing at tile 1 (bank 0). Both image AND TSA LZ77.
                PlantEventGraphic(rom, Cell(1, false, false, 0));

                IImage img = ImageWorldMapCore.TryRenderEvent(rom);

                Assert.NotNull(img);
                Assert.Equal(256, img.Width);  // 32 tiles * 8
                Assert.Equal(160, img.Height); // 20 tiles * 8
            });
        }

        [Fact]
        public void Event_RendersMarkerTile_SubPalette0()
        {
            WithRom((rom) =>
            {
                PlantEventGraphic(rom, Cell(1, false, false, 0));

                IImage img = ImageWorldMapCore.TryRenderEvent(rom);

                Assert.NotNull(img);
                // tile 1 marker (0,0) = color index 2 = GREEN in bank 0;
                // neighbour (1,0) = index 1 = RED.
                AssertPixel(img, 0, 0, 0, 248, 0, 255);
                AssertPixel(img, 1, 0, 248, 0, 0, 255);
            });
        }

        [Fact]
        public void Event_UsesFourSubPalettes_NotJustSixteenColors()
        {
            // CAUTION 2: the event palette is 4 sub-palettes (64 colors / 128
            // bytes); DecodeHeaderTSA selects the bank via TSA bits 12-15. Plant
            // the TSA cell with bank=1, where idx1=BLUE and idx2=WHITE. If only
            // 16 colors (bank 0) were read, the palByteOffset for bank 1 (>= 32
            // bytes) would be out of the 32-byte buffer -> the pixel would be
            // bounds-skipped (alpha 0 / unchanged) instead of WHITE. The marker
            // rendering WHITE proves the 64-color read.
            WithRom((rom) =>
            {
                PlantEventGraphic(rom, Cell(1, false, false, 1));

                IImage img = ImageWorldMapCore.TryRenderEvent(rom);

                Assert.NotNull(img);
                // marker (0,0) = bank1 idx2 = WHITE; neighbour (1,0) = bank1 idx1 = BLUE.
                AssertPixel(img, 0, 0, 248, 248, 248, 255);
                AssertPixel(img, 1, 0, 0, 0, 248, 255);
            });
        }

        [Fact]
        public void Event_CorruptImagePointer_ReturnsNull()
        {
            WithRom((rom) =>
            {
                // Plant TSA + palette but NOT the image (image pointer -> zero-filled
                // region = not a valid LZ77 stream).
                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, EVENT_IMAGE_OFFSET);
                PlantBytes(rom, EVENT_TSA_OFFSET, LZ77.compress(HeaderTsaBytes(Cell(1, false, false, 0))));
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, EVENT_TSA_OFFSET);
                PlantEventPalette(rom);
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, EVENT_PALETTE_OFFSET);

                Assert.Null(ImageWorldMapCore.TryRenderEvent(rom));
            });
        }

        [Fact]
        public void Event_TruncatedTsaStream_ReturnsNull()
        {
            // CAUTION 1: the TSA is the SECOND LZ77 stream and must also pass the
            // 4-byte-header + truncation guard. Plant a valid image but a TSA
            // header claiming more bytes than remain -> getCompressedSize == 0.
            WithRom((rom) =>
            {
                PlantBytes(rom, EVENT_IMAGE_OFFSET, LZ77.compress(MarkerTiles()));
                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, EVENT_IMAGE_OFFSET);
                PlantEventPalette(rom);
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, EVENT_PALETTE_OFFSET);

                uint tsaAddr = (uint)rom.Data.Length - 4;
                rom.Data[tsaAddr + 0] = 0x10;
                rom.Data[tsaAddr + 1] = 0x00;
                rom.Data[tsaAddr + 2] = 0x01;
                rom.Data[tsaAddr + 3] = 0x00;
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, tsaAddr);

                Assert.Null(ImageWorldMapCore.TryRenderEvent(rom));
            });
        }

        [Fact]
        public void Event_NullRom_ReturnsNull()
        {
            WithRom((_) => Assert.Null(ImageWorldMapCore.TryRenderEvent(null)));
        }

        // =================================================================
        // Mini — 8x8 tiles = 64x64
        // =================================================================

        [Fact]
        public void Mini_RendersExpectedDims_64x64()
        {
            WithRom((rom) =>
            {
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, rom.RomInfo.worldmap_mini_image_pointer, 8 * 8);
                PlantIconPalette(rom, MINI_PALETTE_OFFSET, rom.RomInfo.worldmap_mini_palette_pointer);

                IImage img = ImageWorldMapCore.TryRenderMini(rom);

                Assert.NotNull(img);
                Assert.Equal(64, img.Width);   // 8 tiles * 8
                Assert.Equal(64, img.Height);  // 8 tiles * 8
            });
        }

        [Fact]
        public void Mini_CorruptImagePointer_ReturnsNull()
        {
            WithRom((rom) =>
            {
                // Image pointer -> zero-filled region (no valid LZ77 stream).
                SetPtr(rom, rom.RomInfo.worldmap_mini_image_pointer, MINI_IMAGE_OFFSET);
                PlantIconPalette(rom, MINI_PALETTE_OFFSET, rom.RomInfo.worldmap_mini_palette_pointer);

                Assert.Null(ImageWorldMapCore.TryRenderMini(rom));
            });
        }

        // =================================================================
        // Point1 — 32x8 tiles = 256x64
        // =================================================================

        [Fact]
        public void Point1_RendersExpectedDims_256x64()
        {
            WithRom((rom) =>
            {
                PlantIconStrip(rom, ICON1_OFFSET, rom.RomInfo.worldmap_icon1_pointer, 32 * 8);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                IImage img = ImageWorldMapCore.TryRenderPoint1(rom);

                Assert.NotNull(img);
                Assert.Equal(256, img.Width);  // 32 tiles * 8
                Assert.Equal(64, img.Height);  // 8 tiles * 8
            });
        }

        [Fact]
        public void Point1_TruncatedLz77Image_ReturnsNull()
        {
            WithRom((rom) =>
            {
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);
                uint addr = (uint)rom.Data.Length - 4;
                rom.Data[addr + 0] = 0x10;
                rom.Data[addr + 1] = 0x00;
                rom.Data[addr + 2] = 0x01;
                rom.Data[addr + 3] = 0x00;
                SetPtr(rom, rom.RomInfo.worldmap_icon1_pointer, addr);

                Assert.Null(ImageWorldMapCore.TryRenderPoint1(rom));
            });
        }

        // =================================================================
        // Point2 — 12x4 tiles = 96x32
        // =================================================================

        [Fact]
        public void Point2_RendersExpectedDims_96x32()
        {
            WithRom((rom) =>
            {
                PlantIconStrip(rom, ICON2_OFFSET, rom.RomInfo.worldmap_icon2_pointer, 12 * 4);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                IImage img = ImageWorldMapCore.TryRenderPoint2(rom);

                Assert.NotNull(img);
                Assert.Equal(96, img.Width);   // 12 tiles * 8
                Assert.Equal(32, img.Height);  // 4 tiles * 8
            });
        }

        [Fact]
        public void Point2_CorruptPalettePointer_ReturnsNull()
        {
            WithRom((rom) =>
            {
                PlantIconStrip(rom, ICON2_OFFSET, rom.RomInfo.worldmap_icon2_pointer, 12 * 4);
                // Palette pointer at the header (0) -> isSafetyOffset false.
                rom.write_u32(rom.RomInfo.worldmap_icon_palette_pointer, U.toPointer(0));

                Assert.Null(ImageWorldMapCore.TryRenderPoint2(rom));
            });
        }

        // =================================================================
        // Road — 1x15 tiles = 8x120
        // =================================================================

        [Fact]
        public void Road_RendersExpectedDims_8x120()
        {
            WithRom((rom) =>
            {
                PlantIconStrip(rom, ROAD_OFFSET, rom.RomInfo.worldmap_road_tile_pointer, 1 * 15);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                IImage img = ImageWorldMapCore.TryRenderRoad(rom);

                Assert.NotNull(img);
                Assert.Equal(8, img.Width);    // 1 tile * 8
                Assert.Equal(120, img.Height); // 15 tiles * 8
            });
        }

        [Fact]
        public void Road_CoreStateRomMismatch_ReturnsNull()
        {
            // The icon-strip path delegates the tile decode to LoadROMTiles4bpp,
            // which reads CoreState.ROM. ImageWorldMapCore guards against a
            // passed-ROM != CoreState.ROM mismatch (the resolved offset/palette
            // would otherwise be read from the wrong ROM). Confirm the guard.
            WithRom((rom) =>
            {
                PlantIconStrip(rom, ROAD_OFFSET, rom.RomInfo.worldmap_road_tile_pointer, 1 * 15);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                // Point CoreState.ROM at a DIFFERENT instance -> guard returns null.
                var other = MakeRom();
                var savedRom = CoreState.ROM;
                try
                {
                    CoreState.ROM = other;
                    Assert.Null(ImageWorldMapCore.TryRenderRoad(rom));
                }
                finally { CoreState.ROM = savedRom; }
            });
        }

        [Fact]
        public void Road_NullRom_ReturnsNull()
        {
            WithRom((_) => Assert.Null(ImageWorldMapCore.TryRenderRoad(null)));
        }

        // =================================================================
        // No-ImageService safety (shared)
        // =================================================================

        [Fact]
        public void NoImageService_AllReturnNull()
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.ImageService = null;
                PlantEventGraphic(rom, Cell(1, false, false, 0));
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, rom.RomInfo.worldmap_mini_image_pointer, 64);
                PlantIconPalette(rom, MINI_PALETTE_OFFSET, rom.RomInfo.worldmap_mini_palette_pointer);

                Assert.Null(ImageWorldMapCore.TryRenderEvent(rom));
                Assert.Null(ImageWorldMapCore.TryRenderMini(rom));
                Assert.Null(ImageWorldMapCore.TryRenderPoint1(rom));
                Assert.Null(ImageWorldMapCore.TryRenderPoint2(rom));
                Assert.Null(ImageWorldMapCore.TryRenderRoad(rom));
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
            }
        }

        // =================================================================
        // EOF guards (#845 review threads 1 + 2)
        // =================================================================

        [Fact]
        public void Event_PaletteNearRomEnd_InsufficientBytes_ReturnsNull()
        {
            // Thread 2: the event palette is a FIXED 128 bytes (64 colors). A
            // palette pointer with FEWER than 128 bytes to EOF is a truncated /
            // corrupt pointer, NOT a smaller-but-valid palette -> the resolver must
            // return null (so the preview is null and Export PNG stays disabled),
            // NOT a partial-read non-null image. Plant a valid image + TSA so the
            // ONLY failure point is the palette.
            WithRom((rom) =>
            {
                PlantBytes(rom, EVENT_IMAGE_OFFSET, LZ77.compress(MarkerTiles()));
                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, EVENT_IMAGE_OFFSET);
                PlantBytes(rom, EVENT_TSA_OFFSET, LZ77.compress(HeaderTsaBytes(Cell(1, false, false, 0))));
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, EVENT_TSA_OFFSET);

                // 100 bytes to EOF < the required 128.
                uint palNearEnd = (uint)rom.Data.Length - 100;
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, palNearEnd);

                Assert.Null(ImageWorldMapCore.TryRenderEvent(rom));
            });
        }

        [Fact]
        public void Mini_PaletteNearRomEnd_InsufficientBytes_ReturnsNull()
        {
            // Thread 2, icon side: the icon palette is a FIXED 32 bytes (16 colors).
            // A palette pointer with fewer than 32 bytes to EOF -> null (not a
            // partial read). Plant a valid image so the palette is the only failure.
            WithRom((rom) =>
            {
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, rom.RomInfo.worldmap_mini_image_pointer, 8 * 8);

                // 20 bytes to EOF < the required 32.
                uint palNearEnd = (uint)rom.Data.Length - 20;
                SetPtr(rom, rom.RomInfo.worldmap_mini_palette_pointer, palNearEnd);

                Assert.Null(ImageWorldMapCore.TryRenderMini(rom));
            });
        }

        // Thread 1 note: TryResolveDataOffset now reads rom.u32(pointerSlot) behind
        // a 4-byte EOF guard (IsRegionSafe(rom, slot, 4)) — the SAME guard the
        // palette path uses — so a pointer slot in the last 1-3 bytes of the ROM
        // returns null instead of throwing IndexOutOfRangeException. This cannot be
        // exercised behaviorally here: a validly-detected FE8U ROM is a mandatory
        // 16 MB (ROM.LoadLow requires Length >= 0x1000000) and the worldmap RomInfo
        // pointer slots are at fixed LOW offsets (e.g. worldmap_road_tile_pointer =
        // 0xB8F98), so a slot can never sit near EOF, and ROM.Data is protected-set
        // (cannot be shrunk from a test). The guard's logic is covered by the
        // palette-near-EOF tests above (same IsRegionSafe helper).

        // =================================================================
        // Harness
        // =================================================================

        /// <summary>Set CoreState.ROM (a fresh synthetic ROM) + a StubImageService,
        /// run the body, restore both. The ROM is passed to the body so it can be
        /// the same instance ImageWorldMapCore reads via CoreState.ROM.</summary>
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

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        // ---- Event planting ----

        /// <summary>Plant the full event graphic: LZ77 image (marker tiles) +
        /// LZ77 header-TSA (one cell) + 64-color/4-sub-palette block, and wire the
        /// three event pointers.</summary>
        static void PlantEventGraphic(ROM rom, ushort cell)
        {
            PlantBytes(rom, EVENT_IMAGE_OFFSET, LZ77.compress(MarkerTiles()));
            SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, EVENT_IMAGE_OFFSET);

            PlantBytes(rom, EVENT_TSA_OFFSET, LZ77.compress(HeaderTsaBytes(cell)));
            SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, EVENT_TSA_OFFSET);

            PlantEventPalette(rom);
            SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, EVENT_PALETTE_OFFSET);
        }

        /// <summary>Header-TSA bytes: header {x=0,y=0} + one extra cell so the
        /// uncompressed length is >= 3 (getCompressedSize rejects payloads &lt; 3
        /// bytes). The header path places the FIRST data cell at tile[0] -> (0,0).</summary>
        static byte[] HeaderTsaBytes(ushort cell)
        {
            return new byte[]
            {
                0x00, 0x00,                                  // header x=0,y=0
                (byte)(cell & 0xFF), (byte)(cell >> 8),      // cell @ tile[0]
                0x00, 0x00,                                   // padding cell (>=3 bytes)
            };
        }

        /// <summary>64-color / 128-byte event palette: bank0 idx1=RED idx2=GREEN;
        /// bank1 idx1=BLUE idx2=WHITE (proves the 4-sub-palette read).</summary>
        static void PlantEventPalette(ROM rom)
        {
            byte[] pal = new byte[64 * 2];
            SetColor(pal, 0, 1, RED);
            SetColor(pal, 0, 2, GREEN);
            SetColor(pal, 1, 1, BLUE);
            SetColor(pal, 1, 2, WHITE);
            PlantBytes(rom, EVENT_PALETTE_OFFSET, pal);
        }

        // ---- Icon-strip planting (mini / point / road) ----

        /// <summary>Plant an LZ77 image of <paramref name="tileCount"/> marker
        /// tiles at <paramref name="imageOffset"/> and wire its canonical pointer.</summary>
        static void PlantIconStrip(ROM rom, uint imageOffset, uint pointerSlot, int tileCount)
        {
            byte[] tiles = MakeTiles(tileCount);
            PlantBytes(rom, imageOffset, LZ77.compress(tiles));
            SetPtr(rom, pointerSlot, imageOffset);
        }

        /// <summary>16-color icon palette: idx1=RED idx2=GREEN. Wires its pointer.</summary>
        static void PlantIconPalette(ROM rom, uint paletteOffset, uint pointerSlot)
        {
            byte[] pal = new byte[16 * 2];
            SetColor(pal, 0, 1, RED);
            SetColor(pal, 0, 2, GREEN);
            PlantBytes(rom, paletteOffset, pal);
            SetPtr(rom, pointerSlot, paletteOffset);
        }

        // ---- tile builders ----

        /// <summary>2 tiles: tile 0 all index 0, tile 1 index-1 fill with an
        /// index-2 marker at (0,0). (Same shape as ImageTSAEditorCoreTests.)</summary>
        static byte[] MarkerTiles() => MakeTiles(2);

        /// <summary><paramref name="tileCount"/> tiles where tile 0 is the marker
        /// (index-1 fill + index-2 at (0,0)) and the rest are index-1 fills. At
        /// least 1 tile. For event MarkerTiles the marker is on tile 1, so a
        /// 2-tile build keeps tile 0 = all-index-0; for the icon strips we want
        /// tile 0 to carry the marker so a 1x15 road shows it at (0,0).</summary>
        static byte[] MakeTiles(int tileCount)
        {
            if (tileCount < 1) tileCount = 1;
            byte[] tiles = new byte[tileCount * 32];
            // tile 0: marker (index-2 at (0,0)) over an index-1 fill — visible at
            // pixel (0,0) for the icon strips and the road.
            FillTile(tiles, 0, 1);
            SetPixel(tiles, 0, 0, 0, 2);
            if (tileCount >= 2)
            {
                // tile 1: the event marker tile (index-1 fill + index-2 at (0,0)),
                // so the event header-TSA cell -> tile[0]=tile1 renders the marker.
                FillTile(tiles, 1, 1);
                SetPixel(tiles, 1, 0, 0, 2);
            }
            return tiles;
        }

        // ---- low-level helpers (mirror ImageTSAEditorCoreTests) ----

        static void SetColor(byte[] pal, int bank, int index, ushort c)
        {
            int off = bank * 32 + index * 2;
            pal[off] = (byte)(c & 0xFF);
            pal[off + 1] = (byte)(c >> 8);
        }

        static void FillTile(byte[] tiles, int tile, int colorIndex)
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    SetPixel(tiles, tile, x, y, colorIndex);
        }

        static void SetPixel(byte[] tiles, int tile, int x, int y, int colorIndex)
        {
            int pos = tile * 32 + y * 4 + x / 2;
            byte b = tiles[pos];
            if (x % 2 == 0) b = (byte)((b & 0xF0) | (colorIndex & 0x0F));
            else b = (byte)((b & 0x0F) | ((colorIndex & 0x0F) << 4));
            tiles[pos] = b;
        }

        static ushort Cell(int tileIndex, bool h, bool v, int bank)
            => (ushort)((tileIndex & 0x3FF) | (h ? 0x400 : 0) | (v ? 0x800 : 0) | ((bank & 0xF) << 12));

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        /// <summary>Write the encoded GBA pointer of <paramref name="dataOffset"/>
        /// into the canonical pointer slot (pointer-to-pointer wiring).</summary>
        static void SetPtr(ROM rom, uint pointerSlot, uint dataOffset)
            => rom.write_u32(pointerSlot, U.toPointer(dataOffset));

        static void AssertPixel(IImage img, int x, int y, byte r, byte g, byte b, byte a)
        {
            byte[] px = img.GetPixelData();
            int idx = (y * img.Width + x) * 4;
            Assert.Equal(r, px[idx + 0]);
            Assert.Equal(g, px[idx + 1]);
            Assert.Equal(b, px[idx + 2]);
            Assert.Equal(a, px[idx + 3]);
        }
    }
}
