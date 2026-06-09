// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageWorldMapCore.ImportIconStrip + TryGetStripPalette (#1000) —
// the World Map Image editor's four single-LZ77-stream strip image imports
// (Mini / Point1 / Point2 / Road).
//
// Each strip is a single LZ77 image pointer + a 16-color palette. The import is
// IMAGE-ONLY (the View nearest-color-remaps onto the existing palette first, so
// the shared palette is NOT written): 4bpp-encode → LZ77-compress → free-space
// append + repoint the single image pointer. A FAILED import mutates ZERO bytes
// (defensive byte-identical fault restore, #885/#923).
//
// Reuses the synthetic-ROM harness shape from ImageWorldMapCoreTests
// (rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01") + CoreState.ROM).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageWorldMapStripImportTests
    {
        // Distinct data offsets for each planted strip image / palette, well
        // clear of the 0x0..0x200 danger zone and of each other. Each strip is
        // planted with a small LZ77 region + free space follows in the (mostly
        // zero-filled) 16 MB ROM, so WriteCompressedToROM finds free space.
        const uint MINI_IMAGE_OFFSET   = 0x001000;
        const uint MINI_PALETTE_OFFSET = 0x002000;
        const uint ICON1_OFFSET        = 0x003000;
        const uint ICON2_OFFSET        = 0x004000;
        const uint ROAD_OFFSET         = 0x005000;
        const uint ICON_PALETTE_OFFSET = 0x006000;

        // GBA 5-5-5 colors for the planted palettes.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;
        const ushort WHITE = 0x7FFF;

        // =================================================================
        // Success round-trip — image pointer repointed to fresh in-free-space
        // data that LZ77.decompress re-expands to the EncodeDirectTiles4bpp out.
        // =================================================================

        [Theory]
        [InlineData(64, 64)]    // Mini   (8x8 tiles)
        [InlineData(256, 64)]   // Point1 (32x8 tiles)
        [InlineData(96, 32)]    // Point2 (12x4 tiles)
        [InlineData(8, 120)]    // Road   (1x15 tiles)
        public void ImportIconStrip_Success_RoundTrips(int w, int h)
        {
            WithRom((rom) =>
            {
                uint imgPtr = rom.RomInfo.worldmap_mini_image_pointer;
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, imgPtr, 8 * 8);

                byte[] pixels = MakeIndexed(w, h);
                byte[] expectedTiles = ImageImportCore.EncodeDirectTiles4bpp(pixels, w, h);

                var result = ImageWorldMapCore.ImportIconStrip(rom, imgPtr, pixels, w, h);
                Assert.True(result.Success, result.Error);

                // The image pointer slot now points to fresh in-free-space data.
                uint newDataOffset = U.toOffset(rom.u32(imgPtr));
                Assert.True(U.isSafetyOffset(newDataOffset, rom));
                byte[] decompressed = LZ77.decompress(rom.Data, newDataOffset);
                Assert.NotNull(decompressed);
                Assert.True(decompressed.Length >= expectedTiles.Length);
                for (int i = 0; i < expectedTiles.Length; i++)
                    Assert.Equal(expectedTiles[i], decompressed[i]);
            });
        }

        // =================================================================
        // Shared-palette no-change — importing a strip that uses the shared
        // icon palette must NOT touch the icon-palette bytes (image-only).
        // =================================================================

        [Fact]
        public void ImportIconStrip_Point1_DoesNotTouchSharedIconPalette()
        {
            WithRom((rom) =>
            {
                uint imgPtr = rom.RomInfo.worldmap_icon1_pointer;
                PlantIconStrip(rom, ICON1_OFFSET, imgPtr, 32 * 8);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                // Snapshot the 32-byte (16-color) shared icon palette block.
                byte[] before = ReadBytes(rom, ICON_PALETTE_OFFSET, 16 * 2);

                var result = ImageWorldMapCore.ImportIconStrip(
                    rom, imgPtr, MakeIndexed(256, 64), 256, 64);
                Assert.True(result.Success, result.Error);

                // The shared palette bytes are byte-identical after the import.
                byte[] after = ReadBytes(rom, ICON_PALETTE_OFFSET, 16 * 2);
                Assert.Equal(before, after);
            });
        }

        // =================================================================
        // Mini reads its OWN palette — TryGetStripPalette returns the mini
        // palette, and importing Mini doesn't touch the icon palette.
        // =================================================================

        [Fact]
        public void TryGetStripPalette_Mini_ReadsOwnPalette()
        {
            WithRom((rom) =>
            {
                // Mini palette: idx1=BLUE idx2=WHITE (distinct from the icon
                // palette below so we can prove it reads its OWN slot).
                PlantPalette(rom, MINI_PALETTE_OFFSET, rom.RomInfo.worldmap_mini_palette_pointer, BLUE, WHITE);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                bool ok = ImageWorldMapCore.TryGetStripPalette(
                    rom, rom.RomInfo.worldmap_mini_palette_pointer, out byte[] palette);
                Assert.True(ok);
                Assert.NotNull(palette);
                Assert.Equal(16 * 2, palette.Length);
                // idx1 = BLUE, idx2 = WHITE (the mini palette, NOT the icon one).
                Assert.Equal(BLUE, (ushort)(palette[1 * 2] | (palette[1 * 2 + 1] << 8)));
                Assert.Equal(WHITE, (ushort)(palette[2 * 2] | (palette[2 * 2 + 1] << 8)));
            });
        }

        [Fact]
        public void ImportIconStrip_Mini_DoesNotTouchIconPalette()
        {
            WithRom((rom) =>
            {
                uint imgPtr = rom.RomInfo.worldmap_mini_image_pointer;
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, imgPtr, 8 * 8);
                PlantPalette(rom, MINI_PALETTE_OFFSET, rom.RomInfo.worldmap_mini_palette_pointer, BLUE, WHITE);
                PlantIconPalette(rom, ICON_PALETTE_OFFSET, rom.RomInfo.worldmap_icon_palette_pointer);

                byte[] iconBefore = ReadBytes(rom, ICON_PALETTE_OFFSET, 16 * 2);
                byte[] miniBefore = ReadBytes(rom, MINI_PALETTE_OFFSET, 16 * 2);

                var result = ImageWorldMapCore.ImportIconStrip(rom, imgPtr, MakeIndexed(64, 64), 64, 64);
                Assert.True(result.Success, result.Error);

                // Neither the icon palette NOR the mini palette is written (image-only).
                Assert.Equal(iconBefore, ReadBytes(rom, ICON_PALETTE_OFFSET, 16 * 2));
                Assert.Equal(miniBefore, ReadBytes(rom, MINI_PALETTE_OFFSET, 16 * 2));
            });
        }

        // =================================================================
        // Strict failure cases — each mutates ZERO bytes (whole-ROM snapshot
        // byte-identical after).
        // =================================================================

        [Fact]
        public void ImportIconStrip_WrongSizeBuffer_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                uint imgPtr = rom.RomInfo.worldmap_mini_image_pointer;
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, imgPtr, 8 * 8);
                byte[] before = (byte[])rom.Data.Clone();

                // buffer length < w*h.
                byte[] tooShort = new byte[64 * 64 - 1];
                var result = ImageWorldMapCore.ImportIconStrip(rom, imgPtr, tooShort, 64, 64);

                Assert.False(result.Success);
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportIconStrip_NonMultipleOf8Dims_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                uint imgPtr = rom.RomInfo.worldmap_mini_image_pointer;
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, imgPtr, 8 * 8);
                byte[] before = (byte[])rom.Data.Clone();

                var result = ImageWorldMapCore.ImportIconStrip(rom, imgPtr, MakeIndexed(60, 64), 60, 64);

                Assert.False(result.Success);
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportIconStrip_NearEofImagePointerSlot_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                byte[] before = (byte[])rom.Data.Clone();

                // A pointer slot whose +4 read overruns the ROM.
                uint nearEofSlot = (uint)rom.Data.Length - 2;
                var result = ImageWorldMapCore.ImportIconStrip(rom, nearEofSlot, MakeIndexed(64, 64), 64, 64);

                Assert.False(result.Success);
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportIconStrip_NullPixels_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                uint imgPtr = rom.RomInfo.worldmap_mini_image_pointer;
                PlantIconStrip(rom, MINI_IMAGE_OFFSET, imgPtr, 8 * 8);
                byte[] before = (byte[])rom.Data.Clone();

                var result = ImageWorldMapCore.ImportIconStrip(rom, imgPtr, null, 64, 64);

                Assert.False(result.Success);
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportIconStrip_NullRom_Fails()
        {
            var result = ImageWorldMapCore.ImportIconStrip(null, 0x1000u, MakeIndexed(64, 64), 64, 64);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        [Fact]
        public void ImportIconStrip_ForcedNoFreeSpace_MutatesZeroBytes()
        {
            var savedRom = CoreState.ROM;
            try
            {
                // Force WriteCompressedToROM -> U.NOT_FOUND: a ROM already at the
                // 32 MB max filled with 0x01 (neither 0x00 nor 0xFF, so
                // FindFreeSpace finds no free run) and AppendToRomEnd refuses
                // (newEnd > 0x02000000). The import must restore byte-identical
                // (no length change, no repoint) and return a non-empty error.
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01); // no 0x00/0xFF free runs anywhere
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;

                uint imgPtr = rom.RomInfo.worldmap_mini_image_pointer;
                byte[] before = (byte[])rom.Data.Clone();

                var result = ImageWorldMapCore.ImportIconStrip(rom, imgPtr, MakeIndexed(64, 64), 64, 64);

                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(result.Error));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // =================================================================
        // TryGetStripPalette null-safety.
        // =================================================================

        [Fact]
        public void TryGetStripPalette_NullRom_ReturnsFalse()
        {
            Assert.False(ImageWorldMapCore.TryGetStripPalette(null, 0x1000u, out byte[] palette));
            Assert.Null(palette);
        }

        [Fact]
        public void TryGetStripPalette_ZeroSlot_ReturnsFalse()
        {
            WithRom((rom) =>
            {
                Assert.False(ImageWorldMapCore.TryGetStripPalette(rom, 0u, out byte[] palette));
                Assert.Null(palette);
            });
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithRom(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                body(rom);
            }
            finally { CoreState.ROM = savedRom; }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>An indexed buffer (1 byte/pixel) with a simple non-zero
        /// pattern so the round-trip has content (values stay in 0..15).</summary>
        static byte[] MakeIndexed(int w, int h)
        {
            byte[] px = new byte[w * h];
            for (int i = 0; i < px.Length; i++)
                px[i] = (byte)((i % 15) + 1);
            return px;
        }

        /// <summary>Plant an LZ77 image of <paramref name="tileCount"/> dummy
        /// tiles at <paramref name="imageOffset"/> + wire its canonical pointer.</summary>
        static void PlantIconStrip(ROM rom, uint imageOffset, uint pointerSlot, int tileCount)
        {
            if (tileCount < 1) tileCount = 1;
            byte[] tiles = new byte[tileCount * 32];
            tiles[0] = 0x12; // a non-zero tile byte
            PlantBytes(rom, imageOffset, LZ77.compress(tiles));
            SetPtr(rom, pointerSlot, imageOffset);
        }

        /// <summary>16-color icon palette: idx1=RED idx2=GREEN. Wires its pointer.</summary>
        static void PlantIconPalette(ROM rom, uint paletteOffset, uint pointerSlot)
            => PlantPalette(rom, paletteOffset, pointerSlot, RED, GREEN);

        /// <summary>16-color palette with idx1=<paramref name="c1"/> idx2=<paramref name="c2"/>.</summary>
        static void PlantPalette(ROM rom, uint paletteOffset, uint pointerSlot, ushort c1, ushort c2)
        {
            byte[] pal = new byte[16 * 2];
            pal[1 * 2] = (byte)(c1 & 0xFF); pal[1 * 2 + 1] = (byte)(c1 >> 8);
            pal[2 * 2] = (byte)(c2 & 0xFF); pal[2 * 2 + 1] = (byte)(c2 >> 8);
            PlantBytes(rom, paletteOffset, pal);
            SetPtr(rom, pointerSlot, paletteOffset);
        }

        static byte[] ReadBytes(ROM rom, uint offset, int len)
        {
            byte[] buf = new byte[len];
            Array.Copy(rom.Data, offset, buf, 0, len);
            return buf;
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        static void SetPtr(ROM rom, uint pointerSlot, uint dataOffset)
            => rom.write_u32(pointerSlot, U.toPointer(dataOffset));
    }
}
