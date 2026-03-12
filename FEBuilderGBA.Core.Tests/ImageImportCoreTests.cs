using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Minimal IImageService mock that only implements GBAColorToRGBA (for RemapToExistingPalette tests).
    /// </summary>
    internal class MinimalImageService : IImageService
    {
        public IImage CreateImage(int w, int h) => null;
        public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => null;
        public IImage LoadImage(string f) => null;
        public IImage LoadImageFromBytes(byte[] d) => null;
        public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
        {
            r = (byte)((gbaColor & 0x1F) << 3);
            g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
            b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
        }
        public ushort RGBAToGBAColor(byte r, byte g, byte b) => 0;
        public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p) => null;
        public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p) => null;
        public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => null;
        public byte[] Encode4bppTiles(IImage i) => null;
        public byte[] Encode8bppTiles(IImage i) => null;
        public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
        public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
    }

    [Collection("SharedState")]
    public class ImageImportCoreTests
    {
        /// <summary>
        /// Helper to create a ROM with free space for testing write operations.
        /// </summary>
        private static ROM CreateTestRom(int size = 0x200000)
        {
            var rom = new ROM();
            var data = new byte[size];
            // Fill second half with 0xFF for free space
            for (int i = size / 2; i < size; i++)
                data[i] = 0xFF;
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        /// <summary>
        /// Helper to create a simple 8x8 indexed pixel tile with a known pattern.
        /// Each pixel is a palette index (0-15).
        /// </summary>
        private static byte[] MakeSolidTile(byte colorIndex, int width = 8, int height = 8)
        {
            var pixels = new byte[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)(colorIndex & 0x0F);
            return pixels;
        }

        // ---- EncodeTSA roundtrip ----

        [Fact]
        public void EncodeTSA_SingleTile_ProducesTileDataAndTSA()
        {
            // 8x8 solid color-1 tile
            byte[] pixels = MakeSolidTile(1);
            var result = ImageImportCore.EncodeTSA(pixels, 8, 8);

            Assert.NotNull(result);
            Assert.Equal(1, result.UniqueTileCount);
            Assert.Equal(32, result.TileData.Length); // 1 tile * 32 bytes
            Assert.Equal(2, result.TSAData.Length);   // 1 TSA entry * 2 bytes

            // TSA entry should point to tile 0 with no flip, palette 0
            ushort tsaEntry = (ushort)(result.TSAData[0] | (result.TSAData[1] << 8));
            Assert.Equal(0, tsaEntry);
        }

        [Fact]
        public void EncodeTSA_16x8_TwoDistinctTiles()
        {
            // 16x8 image: two different 8x8 tiles side by side
            byte[] pixels = new byte[16 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                    pixels[y * 16 + x] = 1; // tile 0: color 1
                for (int x = 8; x < 16; x++)
                    pixels[y * 16 + x] = 2; // tile 1: color 2
            }

            var result = ImageImportCore.EncodeTSA(pixels, 16, 8);

            Assert.NotNull(result);
            Assert.Equal(2, result.UniqueTileCount);
            Assert.Equal(64, result.TileData.Length); // 2 tiles * 32 bytes
            Assert.Equal(4, result.TSAData.Length);   // 2 TSA entries * 2 bytes
        }

        [Fact]
        public void EncodeTSA_NullInput_ReturnsNull()
        {
            Assert.Null(ImageImportCore.EncodeTSA(null, 8, 8));
        }

        [Fact]
        public void EncodeTSA_NonMultipleOf8_ReturnsNull()
        {
            Assert.Null(ImageImportCore.EncodeTSA(new byte[10 * 10], 10, 10));
            Assert.Null(ImageImportCore.EncodeTSA(new byte[8 * 7], 8, 7));
        }

        [Fact]
        public void EncodeTSA_PaletteIndex_EncodedInTSA()
        {
            byte[] pixels = MakeSolidTile(1);
            var result = ImageImportCore.EncodeTSA(pixels, 8, 8, paletteIndex: 5);

            Assert.NotNull(result);
            ushort tsaEntry = (ushort)(result.TSAData[0] | (result.TSAData[1] << 8));
            int palBits = (tsaEntry >> 12) & 0xF;
            Assert.Equal(5, palBits);
        }

        // ---- EncodeTSA deduplication ----

        [Fact]
        public void EncodeTSA_IdenticalTiles_Deduplicated()
        {
            // 16x8 image: two identical 8x8 tiles
            byte[] pixels = new byte[16 * 8];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 3;

            var result = ImageImportCore.EncodeTSA(pixels, 16, 8);

            Assert.NotNull(result);
            Assert.Equal(1, result.UniqueTileCount); // deduped to 1
            Assert.Equal(32, result.TileData.Length);
            Assert.Equal(4, result.TSAData.Length);   // 2 TSA entries

            // Both TSA entries should reference tile 0
            ushort tsa0 = (ushort)(result.TSAData[0] | (result.TSAData[1] << 8));
            ushort tsa1 = (ushort)(result.TSAData[2] | (result.TSAData[3] << 8));
            Assert.Equal(0, tsa0 & 0x03FF);
            Assert.Equal(0, tsa1 & 0x03FF);
        }

        [Fact]
        public void EncodeTSA_ManyIdenticalTiles_AllDeduplicated()
        {
            // 32x32 = 16 tiles, all identical
            int w = 32, h = 32;
            byte[] pixels = new byte[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 7;

            var result = ImageImportCore.EncodeTSA(pixels, w, h);

            Assert.NotNull(result);
            Assert.Equal(1, result.UniqueTileCount);
        }

        // ---- EncodeTSA flip detection ----

        [Fact]
        public void EncodeTSA_HFlippedTile_DetectedWithFlipFlag()
        {
            // Create a 16x8 image: tile 0 has a gradient left-to-right,
            // tile 1 is the horizontal mirror (gradient right-to-left)
            byte[] pixels = new byte[16 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    byte val = (byte)(x & 0x0F);
                    pixels[y * 16 + x] = val;       // tile 0: 0,1,2,3,4,5,6,7
                    pixels[y * 16 + 8 + (7 - x)] = val; // tile 1: mirrored = 7,6,5,4,3,2,1,0
                }
            }

            var result = ImageImportCore.EncodeTSA(pixels, 16, 8);

            Assert.NotNull(result);
            Assert.Equal(1, result.UniqueTileCount); // flipped tile deduped

            // Second TSA entry should have H-flip flag (bit 10)
            ushort tsa1 = (ushort)(result.TSAData[2] | (result.TSAData[3] << 8));
            Assert.True((tsa1 & 0x0400) != 0, "H-flip bit should be set");
        }

        [Fact]
        public void EncodeTSA_VFlippedTile_DetectedWithFlipFlag()
        {
            // Create a 16x8 image: tile 0 has rows with different values,
            // tile 1 is the vertical mirror
            byte[] pixels = new byte[16 * 8];
            for (int y = 0; y < 8; y++)
            {
                byte val = (byte)(y & 0x0F);
                for (int x = 0; x < 8; x++)
                {
                    pixels[y * 16 + x] = val;                   // tile 0
                    pixels[(7 - y) * 16 + 8 + x] = val;         // tile 1: v-flipped
                }
            }

            var result = ImageImportCore.EncodeTSA(pixels, 16, 8);

            Assert.NotNull(result);
            Assert.Equal(1, result.UniqueTileCount); // flipped tile deduped

            // Second TSA entry should have V-flip flag (bit 11)
            ushort tsa1 = (ushort)(result.TSAData[2] | (result.TSAData[3] << 8));
            Assert.True((tsa1 & 0x0800) != 0, "V-flip bit should be set");
        }

        // ---- EncodeDirectTiles4bpp ----

        [Fact]
        public void EncodeDirectTiles4bpp_SingleTile_MatchesExpected4bpp()
        {
            // 8x8 tile, all pixels = 5 (palette index 5)
            byte[] pixels = MakeSolidTile(5);
            byte[] result = ImageImportCore.EncodeDirectTiles4bpp(pixels, 8, 8);

            Assert.NotNull(result);
            Assert.Equal(32, result.Length);

            // In 4bpp: two pixels per byte, lo nibble = pixel[x], hi nibble = pixel[x+1]
            // All pixels = 5, so each byte = 0x55
            for (int i = 0; i < 32; i++)
                Assert.Equal(0x55, result[i]);
        }

        [Fact]
        public void EncodeDirectTiles4bpp_NullInput_ReturnsNull()
        {
            Assert.Null(ImageImportCore.EncodeDirectTiles4bpp(null, 8, 8));
        }

        [Fact]
        public void EncodeDirectTiles4bpp_NonMultipleOf8_ReturnsNull()
        {
            Assert.Null(ImageImportCore.EncodeDirectTiles4bpp(new byte[10 * 8], 10, 8));
        }

        [Fact]
        public void EncodeDirectTiles4bpp_16x16_ProducesFourTiles()
        {
            byte[] pixels = new byte[16 * 16];
            byte[] result = ImageImportCore.EncodeDirectTiles4bpp(pixels, 16, 16);

            Assert.NotNull(result);
            Assert.Equal(4 * 32, result.Length); // 4 tiles * 32 bytes
        }

        [Fact]
        public void EncodeDirectTiles4bpp_AlternatingPixels_CorrectNibblePacking()
        {
            // 8x8 tile: even pixels = 1, odd pixels = 2
            byte[] pixels = new byte[64];
            for (int i = 0; i < 64; i++)
                pixels[i] = (byte)((i % 2 == 0) ? 1 : 2);

            byte[] result = ImageImportCore.EncodeDirectTiles4bpp(pixels, 8, 8);

            Assert.NotNull(result);
            // Each byte: lo nibble = even pixel (1), hi nibble = odd pixel (2) => 0x21
            for (int i = 0; i < 32; i++)
                Assert.Equal(0x21, result[i]);
        }

        // ---- ExtractTile4bpp ----

        [Fact]
        public void ExtractTile4bpp_SimpleTile_Correct4bppEncoding()
        {
            // 8x8 pixel data, all color 3
            byte[] pixels = MakeSolidTile(3);
            byte[] tile = ImageImportCore.ExtractTile4bpp(pixels, 8, 0, 0);

            Assert.Equal(32, tile.Length);
            // color 3: each byte = (3 | (3 << 4)) = 0x33
            for (int i = 0; i < 32; i++)
                Assert.Equal(0x33, tile[i]);
        }

        [Fact]
        public void ExtractTile4bpp_FirstPixelOnly_LoNibbleSet()
        {
            // All zeros except pixel (0,0) = 5
            byte[] pixels = new byte[64];
            pixels[0] = 5;
            byte[] tile = ImageImportCore.ExtractTile4bpp(pixels, 8, 0, 0);

            // First byte: lo nibble = 5, hi nibble = 0 (pixel 1 is 0)
            Assert.Equal(0x05, tile[0]);
            // Rest should be 0
            for (int i = 1; i < 32; i++)
                Assert.Equal(0x00, tile[i]);
        }

        [Fact]
        public void ExtractTile4bpp_SecondPixelOnly_HiNibbleSet()
        {
            // All zeros except pixel (1,0) = 0xA (masked to 0x0A)
            byte[] pixels = new byte[64];
            pixels[1] = 0x0A;
            byte[] tile = ImageImportCore.ExtractTile4bpp(pixels, 8, 0, 0);

            // First byte: lo nibble = 0 (pixel 0), hi nibble = 0xA (pixel 1) => 0xA0
            Assert.Equal(0xA0, tile[0]);
        }

        [Fact]
        public void ExtractTile4bpp_OffsetTile_ExtractsCorrectRegion()
        {
            // 16x16 image, extract tile at (8, 0)
            byte[] pixels = new byte[16 * 16];
            // Fill second tile column with color 7
            for (int y = 0; y < 8; y++)
                for (int x = 8; x < 16; x++)
                    pixels[y * 16 + x] = 7;

            byte[] tile = ImageImportCore.ExtractTile4bpp(pixels, 16, 8, 0);

            // All 0x77
            for (int i = 0; i < 32; i++)
                Assert.Equal(0x77, tile[i]);
        }

        // ---- FlipTileH4bpp ----

        [Fact]
        public void FlipTileH4bpp_SolidTile_Unchanged()
        {
            // A solid tile is unchanged by any flip
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++)
                tile[i] = 0x55;

            byte[] flipped = ImageImportCore.FlipTileH4bpp(tile);
            Assert.Equal(tile, flipped);
        }

        [Fact]
        public void FlipTileH4bpp_DoubleFlip_ReturnsOriginal()
        {
            // Flipping twice should return the original
            byte[] tile = new byte[32];
            var rng = new Random(42);
            rng.NextBytes(tile);

            byte[] flippedOnce = ImageImportCore.FlipTileH4bpp(tile);
            byte[] flippedTwice = ImageImportCore.FlipTileH4bpp(flippedOnce);
            Assert.Equal(tile, flippedTwice);
        }

        [Fact]
        public void FlipTileH4bpp_KnownPattern_CorrectResult()
        {
            // Row of 8 pixels: [1,2,3,4,5,6,7,8]
            // 4bpp bytes: (1|(2<<4)), (3|(4<<4)), (5|(6<<4)), (7|(8<<4))
            //           = 0x21,       0x43,       0x65,       0x87
            // After H-flip: [8,7,6,5,4,3,2,1]
            // 4bpp bytes: (8|(7<<4)), (6|(5<<4)), (4|(3<<4)), (2|(1<<4))
            //           = 0x78,       0x56,       0x34,       0x12
            byte[] tile = new byte[32];
            // Set first row only
            tile[0] = 0x21;
            tile[1] = 0x43;
            tile[2] = 0x65;
            tile[3] = 0x87;

            byte[] flipped = ImageImportCore.FlipTileH4bpp(tile);

            Assert.Equal(0x78, flipped[0]);
            Assert.Equal(0x56, flipped[1]);
            Assert.Equal(0x34, flipped[2]);
            Assert.Equal(0x12, flipped[3]);
        }

        // ---- FlipTileV4bpp ----

        [Fact]
        public void FlipTileV4bpp_SolidTile_Unchanged()
        {
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++)
                tile[i] = 0xAA;

            byte[] flipped = ImageImportCore.FlipTileV4bpp(tile);
            Assert.Equal(tile, flipped);
        }

        [Fact]
        public void FlipTileV4bpp_DoubleFlip_ReturnsOriginal()
        {
            byte[] tile = new byte[32];
            var rng = new Random(99);
            rng.NextBytes(tile);

            byte[] flippedOnce = ImageImportCore.FlipTileV4bpp(tile);
            byte[] flippedTwice = ImageImportCore.FlipTileV4bpp(flippedOnce);
            Assert.Equal(tile, flippedTwice);
        }

        [Fact]
        public void FlipTileV4bpp_KnownPattern_RowsReversed()
        {
            // Fill each row with its row number
            byte[] tile = new byte[32];
            for (int y = 0; y < 8; y++)
                for (int b = 0; b < 4; b++)
                    tile[y * 4 + b] = (byte)y;

            byte[] flipped = ImageImportCore.FlipTileV4bpp(tile);

            // Row 0 of flipped should be row 7 of original
            for (int y = 0; y < 8; y++)
                for (int b = 0; b < 4; b++)
                    Assert.Equal((byte)(7 - y), flipped[y * 4 + b]);
        }

        // ---- FindAndWriteData ----

        [Fact]
        public void FindAndWriteData_WritesToFreeSpace()
        {
            var rom = CreateTestRom();
            byte[] data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            uint addr = ImageImportCore.FindAndWriteData(rom, data);

            Assert.NotEqual(U.NOT_FOUND, addr);
            // Verify data was written
            Assert.Equal(0xDE, rom.Data[addr]);
            Assert.Equal(0xAD, rom.Data[addr + 1]);
            Assert.Equal(0xBE, rom.Data[addr + 2]);
            Assert.Equal(0xEF, rom.Data[addr + 3]);
        }

        [Fact]
        public void FindAndWriteData_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, ImageImportCore.FindAndWriteData(null, new byte[] { 1 }));
        }

        [Fact]
        public void FindAndWriteData_NullData_ReturnsNotFound()
        {
            var rom = CreateTestRom();
            Assert.Equal(U.NOT_FOUND, ImageImportCore.FindAndWriteData(rom, null));
        }

        [Fact]
        public void FindAndWriteData_EmptyData_ReturnsNotFound()
        {
            var rom = CreateTestRom();
            Assert.Equal(U.NOT_FOUND, ImageImportCore.FindAndWriteData(rom, Array.Empty<byte>()));
        }

        [Fact]
        public void FindAndWriteData_AddressIsAligned()
        {
            var rom = CreateTestRom();
            byte[] data = new byte[] { 0x01, 0x02 };

            uint addr = ImageImportCore.FindAndWriteData(rom, data);

            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.Equal(0u, addr % 4); // 4-byte aligned
        }

        // ---- WriteBytes ----

        [Fact]
        public void WriteBytes_WritesCorrectly()
        {
            var rom = CreateTestRom();
            byte[] data = new byte[] { 0x11, 0x22, 0x33 };

            ImageImportCore.WriteBytes(rom, 0x100, data);

            Assert.Equal(0x11, rom.Data[0x100]);
            Assert.Equal(0x22, rom.Data[0x101]);
            Assert.Equal(0x33, rom.Data[0x102]);
        }

        [Fact]
        public void WriteBytes_NullRom_DoesNotThrow()
        {
            // Should silently return
            ImageImportCore.WriteBytes(null, 0, new byte[] { 1 });
        }

        [Fact]
        public void WriteBytes_BeyondRomSize_DoesNotWrite()
        {
            var rom = CreateTestRom(0x100);
            byte[] data = new byte[] { 0xFF };

            // Address beyond ROM size - should not write
            ImageImportCore.WriteBytes(rom, 0x100, data);
            // No exception thrown
        }

        // ---- ImportFixedIcon ----

        [Fact]
        public void ImportFixedIcon_16x16_WritesTileData()
        {
            var rom = CreateTestRom();
            byte[] pixels = new byte[16 * 16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 2;

            uint destAddr = 0x1000;
            bool ok = ImageImportCore.ImportFixedIcon(rom, pixels, 16, 16, destAddr);

            Assert.True(ok);
            // 16x16 = 4 tiles * 32 bytes = 128 bytes of 4bpp data
            // All pixels = 2, so each byte = 0x22
            Assert.Equal(0x22, rom.Data[destAddr]);
            Assert.Equal(0x22, rom.Data[destAddr + 127]);
        }

        [Fact]
        public void ImportFixedIcon_NullPixels_ReturnsFalse()
        {
            var rom = CreateTestRom();
            Assert.False(ImageImportCore.ImportFixedIcon(rom, null, 16, 16, 0x1000));
        }

        [Fact]
        public void ImportFixedIcon_BeyondRomEnd_ReturnsFalse()
        {
            var rom = CreateTestRom(0x1000);
            byte[] pixels = new byte[16 * 16];
            // dest + 128 bytes exceeds ROM size
            Assert.False(ImageImportCore.ImportFixedIcon(rom, pixels, 16, 16, 0xFFF));
        }

        [Fact]
        public void ImportFixedIcon_8x8_WritesOneTile()
        {
            var rom = CreateTestRom();
            byte[] pixels = MakeSolidTile(0x0F);
            uint destAddr = 0x2000;

            bool ok = ImageImportCore.ImportFixedIcon(rom, pixels, 8, 8, destAddr);

            Assert.True(ok);
            // color 0xF: each byte = 0xFF
            for (int i = 0; i < 32; i++)
                Assert.Equal(0xFF, rom.Data[destAddr + i]);
        }

        // ---- Import3Pointer ----

        [Fact]
        public void Import3Pointer_ValidInput_SucceedsAndUpdatesPointers()
        {
            var rom = CreateTestRom();
            int w = 8, h = 8;
            byte[] pixels = MakeSolidTile(1, w, h);
            byte[] palette = new byte[32]; // 16 colors * 2 bytes

            uint imgPtrAddr = 0x100;
            uint tsaPtrAddr = 0x104;
            uint palPtrAddr = 0x108;

            var result = ImageImportCore.Import3Pointer(rom, pixels, palette, w, h,
                imgPtrAddr, tsaPtrAddr, palPtrAddr);

            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.NotEqual(U.NOT_FOUND, result.TileDataOffset);
            Assert.NotEqual(U.NOT_FOUND, result.TSADataOffset);
            Assert.NotEqual(U.NOT_FOUND, result.PaletteOffset);

            // Verify pointers were written (GBA pointer = offset + 0x08000000)
            uint imgPtr = (uint)(rom.Data[imgPtrAddr] | (rom.Data[imgPtrAddr + 1] << 8)
                | (rom.Data[imgPtrAddr + 2] << 16) | (rom.Data[imgPtrAddr + 3] << 24));
            Assert.Equal(result.TileDataOffset + 0x08000000, imgPtr);
        }

        [Fact]
        public void Import3Pointer_CompressPalette_WritesLZ77CompressedPalette()
        {
            var rom = CreateTestRom();
            int w = 8, h = 8;
            byte[] pixels = MakeSolidTile(1, w, h);
            byte[] palette = new byte[32]; // 16 colors * 2 bytes
            palette[2] = 0x1F; // color 1 = red

            uint imgPtrAddr = 0x100;
            uint tsaPtrAddr = 0x104;
            uint palPtrAddr = 0x108;

            var result = ImageImportCore.Import3Pointer(rom, pixels, palette, w, h,
                imgPtrAddr, tsaPtrAddr, palPtrAddr, compressPalette: true);

            Assert.True(result.Success);
            Assert.NotEqual(U.NOT_FOUND, result.PaletteOffset);

            // Verify palette is LZ77 compressed (starts with 0x10)
            Assert.Equal(0x10, rom.Data[result.PaletteOffset]);

            // Verify we can decompress it back
            byte[] decompressed = LZ77.decompress(rom.Data, result.PaletteOffset);
            Assert.NotNull(decompressed);
            Assert.Equal(palette.Length, decompressed.Length);
            Assert.Equal(palette[2], decompressed[2]); // red color preserved
        }

        [Fact]
        public void Import3Pointer_DefaultPalette_WritesRawPalette()
        {
            var rom = CreateTestRom();
            int w = 8, h = 8;
            byte[] pixels = MakeSolidTile(1, w, h);
            byte[] palette = new byte[32];
            palette[2] = 0x1F;

            uint imgPtrAddr = 0x100;
            uint tsaPtrAddr = 0x104;
            uint palPtrAddr = 0x108;

            var result = ImageImportCore.Import3Pointer(rom, pixels, palette, w, h,
                imgPtrAddr, tsaPtrAddr, palPtrAddr);

            Assert.True(result.Success);

            // Default (compressPalette=false): palette written raw, NOT LZ77 compressed
            // Raw palette should NOT start with 0x10 (unless coincidence, so check content matches)
            Assert.Equal(palette[0], rom.Data[result.PaletteOffset]);
            Assert.Equal(palette[1], rom.Data[result.PaletteOffset + 1]);
            Assert.Equal(palette[2], rom.Data[result.PaletteOffset + 2]);
        }

        [Fact]
        public void Import3Pointer_NullPixels_ReturnsError()
        {
            var rom = CreateTestRom();
            var result = ImageImportCore.Import3Pointer(rom, null, new byte[32], 8, 8, 0, 4, 8);

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void Import3Pointer_InvalidDimensions_ReturnsError()
        {
            var rom = CreateTestRom();
            var result = ImageImportCore.Import3Pointer(rom, new byte[10 * 10], new byte[32],
                10, 10, 0, 4, 8);

            Assert.False(result.Success);
        }

        // ---- Import2Pointer ----

        [Fact]
        public void Import2Pointer_ValidInput_SucceedsAndUpdatesPointers()
        {
            var rom = CreateTestRom();
            int w = 8, h = 8;
            byte[] pixels = MakeSolidTile(2, w, h);
            byte[] palette = new byte[32];

            uint imgPtrAddr = 0x200;
            uint palPtrAddr = 0x204;

            var result = ImageImportCore.Import2Pointer(rom, pixels, palette, w, h,
                imgPtrAddr, palPtrAddr);

            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.NotEqual(U.NOT_FOUND, result.TileDataOffset);
            Assert.NotEqual(U.NOT_FOUND, result.PaletteOffset);
            Assert.Equal(0u, result.TSADataOffset); // Not used in 2-pointer mode

            // Verify image pointer was written
            uint imgPtr = (uint)(rom.Data[imgPtrAddr] | (rom.Data[imgPtrAddr + 1] << 8)
                | (rom.Data[imgPtrAddr + 2] << 16) | (rom.Data[imgPtrAddr + 3] << 24));
            Assert.Equal(result.TileDataOffset + 0x08000000, imgPtr);

            // Verify palette pointer was written
            uint palPtr = (uint)(rom.Data[palPtrAddr] | (rom.Data[palPtrAddr + 1] << 8)
                | (rom.Data[palPtrAddr + 2] << 16) | (rom.Data[palPtrAddr + 3] << 24));
            Assert.Equal(result.PaletteOffset + 0x08000000, palPtr);
        }

        [Fact]
        public void Import2Pointer_NullPixels_ReturnsError()
        {
            var rom = CreateTestRom();
            var result = ImageImportCore.Import2Pointer(rom, null, new byte[32], 8, 8, 0, 4);

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void Import2Pointer_InvalidDimensions_ReturnsError()
        {
            var rom = CreateTestRom();
            var result = ImageImportCore.Import2Pointer(rom, new byte[7 * 8], new byte[32],
                7, 8, 0, 4);

            Assert.False(result.Success);
        }

        [Fact]
        public void Import2Pointer_LargerImage_Succeeds()
        {
            var rom = CreateTestRom();
            int w = 32, h = 32;
            byte[] pixels = new byte[w * h];
            byte[] palette = new byte[32];

            var result = ImageImportCore.Import2Pointer(rom, pixels, palette, w, h, 0x300, 0x304);

            Assert.True(result.Success);
        }

        // ---- WriteCompressedToROM ----

        [Fact]
        public void WriteCompressedToROM_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, ImageImportCore.WriteCompressedToROM(null, new byte[4], 0));
        }

        [Fact]
        public void WriteCompressedToROM_NullData_ReturnsNotFound()
        {
            var rom = CreateTestRom();
            Assert.Equal(U.NOT_FOUND, ImageImportCore.WriteCompressedToROM(rom, null, 0));
        }

        [Fact]
        public void WriteCompressedToROM_ValidData_WritesAndUpdatesPointer()
        {
            var rom = CreateTestRom();
            byte[] rawData = new byte[64];
            for (int i = 0; i < 64; i++)
                rawData[i] = (byte)(i & 0xFF);

            uint ptrAddr = 0x80;
            uint addr = ImageImportCore.WriteCompressedToROM(rom, rawData, ptrAddr);

            Assert.NotEqual(U.NOT_FOUND, addr);

            // Pointer should be updated
            uint ptr = (uint)(rom.Data[ptrAddr] | (rom.Data[ptrAddr + 1] << 8)
                | (rom.Data[ptrAddr + 2] << 16) | (rom.Data[ptrAddr + 3] << 24));
            Assert.Equal(addr + 0x08000000, ptr);

            // Data at addr should start with LZ77 header (0x10)
            Assert.Equal(0x10, rom.Data[addr]);
        }

        // ---- WriteRawToROM ----

        [Fact]
        public void WriteRawToROM_ValidData_WritesUncompressed()
        {
            var rom = CreateTestRom();
            byte[] data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            uint ptrAddr = 0x90;

            uint addr = ImageImportCore.WriteRawToROM(rom, data, ptrAddr);

            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.Equal(0xAA, rom.Data[addr]);
            Assert.Equal(0xBB, rom.Data[addr + 1]);
            Assert.Equal(0xCC, rom.Data[addr + 2]);
            Assert.Equal(0xDD, rom.Data[addr + 3]);
        }

        // ---- RemapToExistingPalette ----

        [Fact]
        public void RemapToExistingPalette_MapsPixelsToClosestColor()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                // Build a 2-color GBA palette: index 0 = black (transparent), index 1 = bright red
                // GBA format: R=31,G=0,B=0 → 0x001F
                byte[] gbaPal = new byte[4];
                gbaPal[0] = 0x00; gbaPal[1] = 0x00; // color 0: black
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00; // color 1: red (R=31)

                // 8x8 RGBA image: all pixels are red (255, 0, 0, 255)
                byte[] rgba = new byte[8 * 8 * 4];
                for (int i = 0; i < 8 * 8; i++)
                {
                    rgba[i * 4 + 0] = 255; // R
                    rgba[i * 4 + 1] = 0;   // G
                    rgba[i * 4 + 2] = 0;   // B
                    rgba[i * 4 + 3] = 255; // A
                }

                byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, 8, 8, gbaPal, 2);

                Assert.NotNull(indexed);
                Assert.Equal(64, indexed.Length);
                // All pixels should map to index 1 (red)
                for (int i = 0; i < 64; i++)
                    Assert.Equal(1, indexed[i]);
            }
            finally
            {
                CoreState.ImageService = prevService;
            }
        }

        [Fact]
        public void RemapToExistingPalette_TransparentPixelsMapToIndex0()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                byte[] gbaPal = new byte[4];
                gbaPal[0] = 0x00; gbaPal[1] = 0x00;
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00;

                // All transparent
                byte[] rgba = new byte[8 * 8 * 4];
                for (int i = 0; i < 8 * 8; i++)
                {
                    rgba[i * 4 + 0] = 255;
                    rgba[i * 4 + 1] = 0;
                    rgba[i * 4 + 2] = 0;
                    rgba[i * 4 + 3] = 0; // transparent
                }

                byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, 8, 8, gbaPal, 2);

                Assert.NotNull(indexed);
                for (int i = 0; i < 64; i++)
                    Assert.Equal(0, indexed[i]);
            }
            finally
            {
                CoreState.ImageService = prevService;
            }
        }

        [Fact]
        public void RemapToExistingPalette_PicksClosestColor()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                // 3-color palette: 0=black, 1=red, 2=blue
                byte[] gbaPal = new byte[6];
                gbaPal[0] = 0x00; gbaPal[1] = 0x00; // black
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00; // red (R=31<<3=248)
                gbaPal[4] = 0x00; gbaPal[5] = 0x7C; // blue (B=31<<3=248) → 0x7C00

                // One pixel: pure blue (0, 0, 255)
                byte[] rgba = new byte[8 * 8 * 4];
                for (int i = 0; i < 64; i++)
                {
                    rgba[i * 4 + 0] = 0;   // R
                    rgba[i * 4 + 1] = 0;   // G
                    rgba[i * 4 + 2] = 255; // B
                    rgba[i * 4 + 3] = 255; // A
                }

                byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, 8, 8, gbaPal, 3);

                Assert.NotNull(indexed);
                // Should map to index 2 (blue)
                for (int i = 0; i < 64; i++)
                    Assert.Equal(2, indexed[i]);
            }
            finally
            {
                CoreState.ImageService = prevService;
            }
        }

        // ---- RemapToMultiPalette ----

        [Fact]
        public void RemapToMultiPalette_SingleSubPalette_MapsCorrectly()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                // 1 sub-palette: 0=black, 1=red
                byte[] gbaPal = new byte[32]; // 16 colors * 2 bytes
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00; // color 1 = red

                // 8x8 RGBA: all red
                byte[] rgba = new byte[8 * 8 * 4];
                for (int i = 0; i < 64; i++)
                {
                    rgba[i * 4 + 0] = 248; // R (31<<3)
                    rgba[i * 4 + 1] = 0;
                    rgba[i * 4 + 2] = 0;
                    rgba[i * 4 + 3] = 255;
                }

                var result = ImageImportCore.RemapToMultiPalette(rgba, 8, 8, gbaPal, 1);

                Assert.NotNull(result);
                Assert.Equal(8, result.Width);
                Assert.Equal(8, result.Height);
                Assert.Single(result.TilePaletteIndices);
                Assert.Equal(0, result.TilePaletteIndices[0]); // only sub-palette 0
                // All pixels should map to index 1 (red)
                for (int i = 0; i < 64; i++)
                    Assert.Equal(1, result.IndexedPixels[i]);
            }
            finally { CoreState.ImageService = prevService; }
        }

        [Fact]
        public void RemapToMultiPalette_TwoSubPalettes_PicksBestPerTile()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                // Sub-palette 0: 0=black, 1=red
                // Sub-palette 1: 0=black, 1=blue
                byte[] gbaPal = new byte[64]; // 2 * 16 * 2
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00; // pal0 color1 = red
                gbaPal[32 + 2] = 0x00; gbaPal[32 + 3] = 0x7C; // pal1 color1 = blue

                // 16x8 (2 tiles): tile0=red, tile1=blue
                byte[] rgba = new byte[16 * 8 * 4];
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        int i = y * 16 + x;
                        rgba[i * 4 + 0] = 248; rgba[i * 4 + 3] = 255; // red
                    }
                    for (int x = 8; x < 16; x++)
                    {
                        int i = y * 16 + x;
                        rgba[i * 4 + 2] = 248; rgba[i * 4 + 3] = 255; // blue
                    }
                }

                var result = ImageImportCore.RemapToMultiPalette(rgba, 16, 8, gbaPal, 2);

                Assert.NotNull(result);
                Assert.Equal(2, result.TilePaletteIndices.Length);
                Assert.Equal(0, result.TilePaletteIndices[0]); // red tile → sub-palette 0
                Assert.Equal(1, result.TilePaletteIndices[1]); // blue tile → sub-palette 1
            }
            finally { CoreState.ImageService = prevService; }
        }

        [Fact]
        public void RemapToMultiPalette_TransparentPixels_MappedToIndex0()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                byte[] gbaPal = new byte[32];
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00;

                // All transparent
                byte[] rgba = new byte[8 * 8 * 4]; // alpha = 0 by default

                var result = ImageImportCore.RemapToMultiPalette(rgba, 8, 8, gbaPal, 1);

                Assert.NotNull(result);
                for (int i = 0; i < 64; i++)
                    Assert.Equal(0, result.IndexedPixels[i]);
            }
            finally { CoreState.ImageService = prevService; }
        }

        [Fact]
        public void RemapToMultiPalette_NullInput_ReturnsNull()
        {
            Assert.Null(ImageImportCore.RemapToMultiPalette(null, 8, 8, new byte[32], 1));
            Assert.Null(ImageImportCore.RemapToMultiPalette(new byte[256], 8, 8, null, 1));
        }

        [Fact]
        public void RemapToMultiPalette_NonMultipleOf8_ReturnsNull()
        {
            Assert.Null(ImageImportCore.RemapToMultiPalette(new byte[10 * 10 * 4], 10, 10, new byte[32], 1));
        }

        // ---- EncodeTSAMultiPalette ----

        [Fact]
        public void EncodeTSAMultiPalette_PerTilePaletteInTSA()
        {
            // 16x8 = 2 tiles: tile 0 → palette 2, tile 1 → palette 5
            byte[] pixels = new byte[16 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                    pixels[y * 16 + x] = 1;
                for (int x = 8; x < 16; x++)
                    pixels[y * 16 + x] = 2;
            }
            int[] palIndices = new int[] { 2, 5 };

            var result = ImageImportCore.EncodeTSAMultiPalette(pixels, 16, 8, palIndices);

            Assert.NotNull(result);
            ushort tsa0 = (ushort)(result.TSAData[0] | (result.TSAData[1] << 8));
            ushort tsa1 = (ushort)(result.TSAData[2] | (result.TSAData[3] << 8));
            Assert.Equal(2, (tsa0 >> 12) & 0xF);
            Assert.Equal(5, (tsa1 >> 12) & 0xF);
        }

        [Fact]
        public void EncodeTSAMultiPalette_SamePaletteTilesDedup()
        {
            // 16x8 = 2 identical tiles, same palette → should dedup
            byte[] pixels = new byte[16 * 8];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 3;
            int[] palIndices = new int[] { 1, 1 };

            var result = ImageImportCore.EncodeTSAMultiPalette(pixels, 16, 8, palIndices);

            Assert.NotNull(result);
            Assert.Equal(1, result.UniqueTileCount); // deduped
        }

        [Fact]
        public void EncodeTSAMultiPalette_DiffPaletteTilesNoDedup()
        {
            // 16x8 = 2 identical tiles but different palettes → should NOT dedup
            byte[] pixels = new byte[16 * 8];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 3;
            int[] palIndices = new int[] { 1, 2 };

            var result = ImageImportCore.EncodeTSAMultiPalette(pixels, 16, 8, palIndices);

            Assert.NotNull(result);
            Assert.Equal(2, result.UniqueTileCount); // not deduped because different palette
        }

        [Fact]
        public void EncodeTSAMultiPalette_NullInput_ReturnsNull()
        {
            Assert.Null(ImageImportCore.EncodeTSAMultiPalette(null, 8, 8, new int[] { 0 }));
            Assert.Null(ImageImportCore.EncodeTSAMultiPalette(new byte[64], 8, 8, null));
        }

        // ---- Import3PointerMultiPalette ----

        [Fact]
        public void Import3PointerMultiPalette_ValidInput_Succeeds()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();
                var rom = CreateTestRom();

                // 1 sub-palette, 8x8 red image
                byte[] gbaPal = new byte[32];
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00;

                byte[] rgba = new byte[8 * 8 * 4];
                for (int i = 0; i < 64; i++)
                {
                    rgba[i * 4 + 0] = 248;
                    rgba[i * 4 + 3] = 255;
                }

                var result = ImageImportCore.Import3PointerMultiPalette(rom, rgba, gbaPal,
                    8, 8, 0x100, 0x104, 0x108, subPaletteCount: 1);

                Assert.True(result.Success);
                Assert.NotEqual(U.NOT_FOUND, result.TileDataOffset);
                Assert.NotEqual(U.NOT_FOUND, result.TSADataOffset);
                Assert.NotEqual(U.NOT_FOUND, result.PaletteOffset);
            }
            finally { CoreState.ImageService = prevService; }
        }

        [Fact]
        public void Import3PointerMultiPalette_CompressPalette_WritesLZ77()
        {
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();
                var rom = CreateTestRom();

                byte[] gbaPal = new byte[32];
                gbaPal[2] = 0x1F;

                byte[] rgba = new byte[8 * 8 * 4];
                for (int i = 0; i < 64; i++)
                {
                    rgba[i * 4 + 0] = 248;
                    rgba[i * 4 + 3] = 255;
                }

                var result = ImageImportCore.Import3PointerMultiPalette(rom, rgba, gbaPal,
                    8, 8, 0x100, 0x104, 0x108, subPaletteCount: 1, compressPalette: true);

                Assert.True(result.Success);
                Assert.Equal(0x10, rom.Data[result.PaletteOffset]); // LZ77 header
            }
            finally { CoreState.ImageService = prevService; }
        }

        [Fact]
        public void RemapToExistingPalette_NullInputReturnsNull()
        {
            Assert.Null(ImageImportCore.RemapToExistingPalette(null, 8, 8, new byte[4], 2));
            Assert.Null(ImageImportCore.RemapToExistingPalette(new byte[256], 8, 8, null, 2));
        }

        [Fact]
        public void RemapToExistingPalette_ExportImportRoundtrip()
        {
            // Simulates: export PNG (palette indices → RGBA) → import PNG (RGBA → remap to same palette)
            // Should produce identical palette indices
            var prevService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                // 4-color palette typical of a GBA icon
                byte[] gbaPal = new byte[8];
                gbaPal[0] = 0x00; gbaPal[1] = 0x00; // 0: black (transparent)
                gbaPal[2] = 0x1F; gbaPal[3] = 0x00; // 1: red
                gbaPal[4] = 0xE0; gbaPal[5] = 0x03; // 2: green
                gbaPal[6] = 0x00; gbaPal[7] = 0x7C; // 3: blue

                // Original indexed pixels: pattern of 0,1,2,3
                byte[] origIndexed = new byte[8 * 8];
                for (int i = 0; i < 64; i++)
                    origIndexed[i] = (byte)(i % 4);

                // Simulate export: convert indexed → RGBA using palette
                byte[] rgba = new byte[64 * 4];
                for (int i = 0; i < 64; i++)
                {
                    int idx = origIndexed[i];
                    ushort gbaColor = (ushort)(gbaPal[idx * 2] | (gbaPal[idx * 2 + 1] << 8));
                    CoreState.ImageService.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                    rgba[i * 4 + 0] = r;
                    rgba[i * 4 + 1] = g;
                    rgba[i * 4 + 2] = b;
                    rgba[i * 4 + 3] = (byte)(idx == 0 ? 0 : 255); // index 0 = transparent
                }

                // Simulate import: remap RGBA back to palette indices
                byte[] remapped = ImageImportCore.RemapToExistingPalette(rgba, 8, 8, gbaPal, 4);

                Assert.NotNull(remapped);
                Assert.Equal(64, remapped.Length);
                // Every pixel should match the original index
                for (int i = 0; i < 64; i++)
                    Assert.Equal(origIndexed[i], remapped[i]);
            }
            finally
            {
                CoreState.ImageService = prevService;
            }
        }

        // ---- Auto-expand ROM tests ----

        [Fact]
        public void FindAndWriteData_NoFreeSpace_AppendsToRomEnd()
        {
            // Create a ROM filled with non-free data (0x01) so FindFreeSpace returns NOT_FOUND
            var rom = new ROM();
            int size = 0x1000;
            var data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = 0x01;
            rom.SwapNewROMDataDirect(data);

            byte[] toWrite = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            uint addr = ImageImportCore.FindAndWriteData(rom, toWrite);

            Assert.NotEqual(U.NOT_FOUND, addr);
            // Should be appended at the old ROM end (aligned)
            Assert.True(addr >= (uint)size);
            // Verify data was written
            Assert.Equal((uint)0xAA, rom.u8(addr));
            Assert.Equal((uint)0xBB, rom.u8(addr + 1));
            Assert.Equal((uint)0xCC, rom.u8(addr + 2));
            Assert.Equal((uint)0xDD, rom.u8(addr + 3));
        }

        [Fact]
        public void FindAndWriteData_AppendExpands_RomGrows()
        {
            var rom = new ROM();
            int originalSize = 0x800;
            var data = new byte[originalSize];
            for (int i = 0; i < originalSize; i++) data[i] = 0x01;
            rom.SwapNewROMDataDirect(data);

            byte[] toWrite = new byte[0x100];
            for (int i = 0; i < toWrite.Length; i++) toWrite[i] = 0x42;

            uint addr = ImageImportCore.FindAndWriteData(rom, toWrite);

            Assert.NotEqual(U.NOT_FOUND, addr);
            // ROM should have grown
            Assert.True(rom.Data.Length > originalSize);
            // Verify written data
            for (int i = 0; i < toWrite.Length; i++)
                Assert.Equal((uint)0x42, rom.u8(addr + (uint)i));
        }

        [Fact]
        public void FindAndWriteData_MaxRomSize_ReturnsNotFound()
        {
            // Create a small ROM, then request a size that would exceed 32MB
            var rom = new ROM();
            var data = new byte[0x100];
            rom.SwapNewROMDataDirect(data);

            // Request more space than would fit in 32MB
            uint tooLarge = 0x02000000; // requesting exactly 32MB on top of existing data
            uint result = ImageImportCore.AppendToRomEnd(rom, tooLarge);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void AppendToRomEnd_ExpandsRom()
        {
            var rom = new ROM();
            int originalSize = 0x400;
            var data = new byte[originalSize];
            rom.SwapNewROMDataDirect(data);

            uint needSize = 0x100;
            uint addr = ImageImportCore.AppendToRomEnd(rom, needSize);

            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.Equal((uint)originalSize, addr); // appended at old end
            Assert.Equal((int)(originalSize + needSize), rom.Data.Length);
        }

        [Fact]
        public void FindAndWriteData_NullOrEmpty_ReturnsNotFound()
        {
            var rom = CreateTestRom();
            Assert.Equal(U.NOT_FOUND, ImageImportCore.FindAndWriteData(null, new byte[1]));
            Assert.Equal(U.NOT_FOUND, ImageImportCore.FindAndWriteData(rom, null));
            Assert.Equal(U.NOT_FOUND, ImageImportCore.FindAndWriteData(rom, new byte[0]));
        }

        // ---- GrepPointerAll tests ----

        [Fact]
        public void GrepPointerAll_FindsMultipleReferences()
        {
            byte[] data = new byte[0x200];
            uint target = 0x1000; // offset in ROM
            uint pointer = U.toPointer(target); // 0x08001000
            // Write pointer at two locations
            U.write_u32(data, 0x100, pointer);
            U.write_u32(data, 0x120, pointer);

            var refs = U.GrepPointerAll(data, target);
            Assert.Equal(2, refs.Count);
            Assert.Contains((uint)0x100, refs);
            Assert.Contains((uint)0x120, refs);
        }

        [Fact]
        public void GrepPointerAll_SingleReference()
        {
            byte[] data = new byte[0x200];
            uint target = 0x2000;
            U.write_u32(data, 0x100, U.toPointer(target));

            var refs = U.GrepPointerAll(data, target);
            Assert.Single(refs);
            Assert.Equal((uint)0x100, refs[0]);
        }

        [Fact]
        public void GrepPointerAll_NoReferences()
        {
            byte[] data = new byte[0x200];
            var refs = U.GrepPointerAll(data, 0x1000);
            Assert.Empty(refs);
        }

        [Fact]
        public void GrepPointerAll_ZeroAddress_ReturnsEmpty()
        {
            byte[] data = new byte[0x200];
            var refs = U.GrepPointerAll(data, 0);
            Assert.Empty(refs);
        }

        [Fact]
        public void GrepPointerAll_NotFoundAddress_ReturnsEmpty()
        {
            byte[] data = new byte[0x200];
            var refs = U.GrepPointerAll(data, U.NOT_FOUND);
            Assert.Empty(refs);
        }

        // ---- IsPointerShared tests ----

        [Fact]
        public void IsPointerShared_SharedPointer_ReturnsTrue()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000];
            // Place palette data at 0x1000
            data[0x1000] = 0x01;
            // Two pointers pointing to 0x1000
            uint ptr = U.toPointer(0x1000);
            U.write_u32(data, 0x100, ptr);
            U.write_u32(data, 0x200, ptr);
            rom.SwapNewROMDataDirect(data);

            Assert.True(ImageImportCore.IsPointerShared(rom, 0x100));
        }

        [Fact]
        public void IsPointerShared_UniquePointer_ReturnsFalse()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000];
            uint ptr = U.toPointer(0x1000);
            U.write_u32(data, 0x100, ptr);
            rom.SwapNewROMDataDirect(data);

            Assert.False(ImageImportCore.IsPointerShared(rom, 0x100));
        }

        [Fact]
        public void IsPointerShared_NullRom_ReturnsFalse()
        {
            Assert.False(ImageImportCore.IsPointerShared(null, 0x100));
        }

        // ---- ReadPaletteFromROM tests ----

        [Fact]
        public void ReadPaletteFromROM_ReadsRawPalette()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000];
            // Write 16 colors at 0x1000 (32 bytes)
            for (int i = 0; i < 32; i++)
                data[0x1000 + i] = (byte)(i + 1);
            U.write_u32(data, 0x100, U.toPointer(0x1000));
            rom.SwapNewROMDataDirect(data);

            byte[] pal = ImageImportCore.ReadPaletteFromROM(rom, 0x100, 16, false);
            Assert.NotNull(pal);
            Assert.Equal(32, pal.Length);
            for (int i = 0; i < 32; i++)
                Assert.Equal((byte)(i + 1), pal[i]);
        }

        [Fact]
        public void ReadPaletteFromROM_NullRom_ReturnsNull()
        {
            Assert.Null(ImageImportCore.ReadPaletteFromROM(null, 0x100));
        }

        // ---- RemapPaletteIndices tests ----

        [Fact]
        public void RemapPaletteIndices_IdenticalPalettes_NoChange()
        {
            byte[] palette = new byte[32]; // 16 colors
            for (int i = 0; i < 16; i++)
            {
                ushort color = (ushort)(i * 2); // arbitrary colors
                palette[i * 2] = (byte)(color & 0xFF);
                palette[i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }

            byte[] pixels = new byte[] { 0, 1, 2, 3, 4, 5 };
            byte[] result = ImageImportCore.RemapPaletteIndices(pixels, palette, palette);

            Assert.NotNull(result);
            Assert.Equal(pixels.Length, result.Length);
            for (int i = 0; i < pixels.Length; i++)
                Assert.Equal(pixels[i], result[i]);
        }

        [Fact]
        public void RemapPaletteIndices_TransparentIndex0AlwaysMaps0()
        {
            byte[] fromPal = new byte[32];
            byte[] toPal = new byte[32];
            // Make palettes different
            fromPal[2] = 0x1F; // color 1 = red
            toPal[4] = 0x1F;   // color 2 = red in target

            byte[] pixels = new byte[] { 0, 1 };
            byte[] result = ImageImportCore.RemapPaletteIndices(pixels, fromPal, toPal);

            Assert.NotNull(result);
            Assert.Equal(0, result[0]); // index 0 always stays 0
        }

        [Fact]
        public void RemapPaletteIndices_RemapsToClosestColor()
        {
            byte[] fromPal = new byte[32];
            byte[] toPal = new byte[32];

            // fromPal color 1 = pure red (R=31, G=0, B=0) = 0x001F
            fromPal[2] = 0x1F; fromPal[3] = 0x00;
            // toPal color 1 = pure green
            toPal[2] = 0xE0; toPal[3] = 0x03; // (0, 31, 0)
            // toPal color 2 = pure red (same as fromPal color 1)
            toPal[4] = 0x1F; toPal[5] = 0x00;

            byte[] pixels = new byte[] { 1 }; // uses color 1 from fromPal (red)
            byte[] result = ImageImportCore.RemapPaletteIndices(pixels, fromPal, toPal);

            Assert.NotNull(result);
            Assert.Equal(2, result[0]); // should map to toPal color 2 (red)
        }

        [Fact]
        public void RemapPaletteIndices_NullInputs_ReturnsNull()
        {
            byte[] pal = new byte[32];
            byte[] pix = new byte[] { 1 };
            Assert.Null(ImageImportCore.RemapPaletteIndices(null, pal, pal));
            Assert.Null(ImageImportCore.RemapPaletteIndices(pix, null, pal));
            Assert.Null(ImageImportCore.RemapPaletteIndices(pix, pal, null));
        }

        [Fact]
        public void RemapPaletteIndices_ShortPalette_ReturnsNull()
        {
            byte[] pix = new byte[] { 1 };
            byte[] shortPal = new byte[10]; // too short for 16 colors
            byte[] fullPal = new byte[32];
            Assert.Null(ImageImportCore.RemapPaletteIndices(pix, shortPal, fullPal));
            Assert.Null(ImageImportCore.RemapPaletteIndices(pix, fullPal, shortPal));
        }

        // ---- Import3Pointer shared palette tests ----

        [Fact]
        public void Import3Pointer_SharedPalette_PreservesExistingPalette()
        {
            var rom = new ROM();
            byte[] data = new byte[0x200000];
            // Fill second half with 0xFF (free space)
            for (int i = 0x100000; i < data.Length; i++) data[i] = 0xFF;

            // Place a palette at 0x1000 (16 colors = 32 bytes)
            byte[] existingPalette = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort color = (ushort)(i * 2);
                existingPalette[i * 2] = (byte)(color & 0xFF);
                existingPalette[i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }
            Array.Copy(existingPalette, 0, data, 0x1000, 32);

            // Two entries sharing the same palette pointer
            uint palPtr = U.toPointer(0x1000);
            U.write_u32(data, 0x100, palPtr); // entry 1 palette pointer
            U.write_u32(data, 0x200, palPtr); // entry 2 palette pointer (shared)

            // Image and TSA pointers for entry 1
            U.write_u32(data, 0x104, U.toPointer(0x2000));
            U.write_u32(data, 0x108, U.toPointer(0x3000));

            rom.SwapNewROMDataDirect(data);

            // Create an 8x8 image with indexed pixels
            byte[] indexedPixels = new byte[64];
            for (int i = 0; i < 64; i++) indexedPixels[i] = (byte)(i % 15 + 1);

            var result = ImageImportCore.Import3Pointer(rom, indexedPixels, existingPalette,
                8, 8, 0x104, 0x108, 0x100);

            Assert.True(result.Success);
            Assert.True(result.PaletteWasShared);
            // The palette pointer at 0x200 should still be valid (unchanged)
            Assert.Equal(palPtr, U.u32(rom.Data, 0x200));
        }

        [Fact]
        public void Import3Pointer_UniquePalette_WritesNewPalette()
        {
            var rom = new ROM();
            byte[] data = new byte[0x200000];
            for (int i = 0x100000; i < data.Length; i++) data[i] = 0xFF;

            // Palette at 0x1000 with only ONE reference
            byte[] existingPalette = new byte[32];
            existingPalette[2] = 0x1F; // color 1 = red
            Array.Copy(existingPalette, 0, data, 0x1000, 32);
            U.write_u32(data, 0x100, U.toPointer(0x1000)); // only reference
            U.write_u32(data, 0x104, U.toPointer(0x2000));
            U.write_u32(data, 0x108, U.toPointer(0x3000));

            rom.SwapNewROMDataDirect(data);

            byte[] newPalette = new byte[32];
            newPalette[2] = 0x00; newPalette[3] = 0x7C; // color 1 = blue
            byte[] indexedPixels = new byte[64];

            var result = ImageImportCore.Import3Pointer(rom, indexedPixels, newPalette,
                8, 8, 0x104, 0x108, 0x100);

            Assert.True(result.Success);
            Assert.False(result.PaletteWasShared);
        }

        // ---- Import2Pointer shared palette test ----

        [Fact]
        public void Import2Pointer_SharedPalette_PreservesExistingPalette()
        {
            var rom = new ROM();
            byte[] data = new byte[0x200000];
            for (int i = 0x100000; i < data.Length; i++) data[i] = 0xFF;

            byte[] existingPalette = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                existingPalette[i * 2] = (byte)(i & 0xFF);
            }
            Array.Copy(existingPalette, 0, data, 0x1000, 32);

            uint palPtr = U.toPointer(0x1000);
            U.write_u32(data, 0x100, palPtr); // entry 1 palette pointer
            U.write_u32(data, 0x200, palPtr); // entry 2 palette pointer (shared)
            U.write_u32(data, 0x104, U.toPointer(0x2000)); // image pointer

            rom.SwapNewROMDataDirect(data);

            byte[] indexedPixels = new byte[64];
            var result = ImageImportCore.Import2Pointer(rom, indexedPixels, existingPalette,
                8, 8, 0x104, 0x100);

            Assert.True(result.Success);
            Assert.True(result.PaletteWasShared);
            Assert.Equal(palPtr, U.u32(rom.Data, 0x200));
        }

        // ---- Import3PointerMultiPalette shared palette test ----

        [Fact]
        public void Import3PointerMultiPalette_SharedPalette_UsesExisting()
        {
            var oldImageService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new MinimalImageService();

                var rom = new ROM();
                byte[] data = new byte[0x200000];
                for (int i = 0x100000; i < data.Length; i++) data[i] = 0xFF;

                // Create multi-palette (2 sub-palettes, 16 colors each = 64 bytes)
                byte[] existingPalette = new byte[64];
                for (int i = 0; i < 32; i++)
                {
                    existingPalette[i * 2] = (byte)(i & 0xFF);
                }
                Array.Copy(existingPalette, 0, data, 0x1000, 64);

                uint palPtr = U.toPointer(0x1000);
                U.write_u32(data, 0x100, palPtr); // entry 1
                U.write_u32(data, 0x200, palPtr); // entry 2 (shared)
                U.write_u32(data, 0x104, U.toPointer(0x2000)); // img
                U.write_u32(data, 0x108, U.toPointer(0x3000)); // tsa

                rom.SwapNewROMDataDirect(data);

                // 8x8 RGBA image (all red pixels)
                byte[] rgbaPixels = new byte[64 * 4];
                for (int i = 0; i < 64; i++)
                {
                    rgbaPixels[i * 4 + 0] = 248; // R
                    rgbaPixels[i * 4 + 3] = 255; // A
                }

                var result = ImageImportCore.Import3PointerMultiPalette(rom, rgbaPixels, existingPalette,
                    8, 8, 0x104, 0x108, 0x100, subPaletteCount: 2);

                Assert.True(result.Success);
                Assert.True(result.PaletteWasShared);
            }
            finally
            {
                CoreState.ImageService = oldImageService;
            }
        }

        // ======================== Palette roundtrip tests ========================

        [Fact]
        public void WritePaletteToROM_ThenReadBack_RoundtripsExactly()
        {
            var rom = CreateTestRom();
            // Create a 16-color GBA palette (32 bytes)
            byte[] palette = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort color = (ushort)(i | (i << 5) | (i << 10)); // BGR555
                palette[i * 2] = (byte)(color & 0xFF);
                palette[i * 2 + 1] = (byte)(color >> 8);
            }

            // Write palette to ROM at pointer address 0x100
            // First, set up a dummy pointer at 0x100
            uint pointerAddr = 0x100;
            uint writeAddr = ImageImportCore.WritePaletteToROM(rom, palette, pointerAddr);
            Assert.NotEqual(U.NOT_FOUND, writeAddr);

            // Read it back
            byte[] readBack = ImageImportCore.ReadPaletteFromROM(rom, pointerAddr, 16, compressed: false);
            Assert.NotNull(readBack);
            Assert.Equal(palette.Length, readBack.Length);
            for (int i = 0; i < palette.Length; i++)
                Assert.Equal(palette[i], readBack[i]);
        }

        [Fact]
        public void WriteCompressedPalette_ThenReadBack_RoundtripsExactly()
        {
            var rom = CreateTestRom();
            // Create a 16-color GBA palette (32 bytes)
            byte[] palette = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort color = (ushort)((i * 2) | ((i * 2) << 5) | ((i * 2) << 10));
                palette[i * 2] = (byte)(color & 0xFF);
                palette[i * 2 + 1] = (byte)(color >> 8);
            }

            // Write compressed palette
            uint pointerAddr = 0x100;
            uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, palette, pointerAddr);
            Assert.NotEqual(U.NOT_FOUND, writeAddr);

            // Read it back (compressed)
            byte[] readBack = ImageImportCore.ReadPaletteFromROM(rom, pointerAddr, 16, compressed: true);
            Assert.NotNull(readBack);
            Assert.Equal(palette.Length, readBack.Length);
            for (int i = 0; i < palette.Length; i++)
                Assert.Equal(palette[i], readBack[i]);
        }

        [Fact]
        public void WriteMultiPalette_ThenReadBack_Roundtrips()
        {
            var rom = CreateTestRom();
            // Create multi-palette: 4 sub-palettes = 128 bytes
            byte[] palette = new byte[128];
            for (int sub = 0; sub < 4; sub++)
            {
                for (int i = 0; i < 16; i++)
                {
                    ushort color = (ushort)((sub * 4 + i) & 0x7FFF);
                    int off = (sub * 16 + i) * 2;
                    palette[off] = (byte)(color & 0xFF);
                    palette[off + 1] = (byte)(color >> 8);
                }
            }

            // Write compressed multi-palette
            uint pointerAddr = 0x100;
            uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, palette, pointerAddr);
            Assert.NotEqual(U.NOT_FOUND, writeAddr);

            // Read back full palette (64 colors = 4 sub-palettes)
            byte[] readBack = ImageImportCore.ReadPaletteFromROM(rom, pointerAddr, 64, compressed: true);
            Assert.NotNull(readBack);
            Assert.Equal(palette.Length, readBack.Length);
            for (int i = 0; i < palette.Length; i++)
                Assert.Equal(palette[i], readBack[i]);
        }

        [Fact]
        public void ReadPaletteFromROM_InvalidPointer_ReturnsNull()
        {
            var rom = CreateTestRom();
            // Pointer at 0x100 is 0 (no valid pointer)
            byte[] result = ImageImportCore.ReadPaletteFromROM(rom, 0x100, 16, compressed: false);
            Assert.Null(result);
        }

        [Fact]
        public void WritePaletteToROM_NullRom_ReturnsNotFound()
        {
            uint result = ImageImportCore.WritePaletteToROM(null, new byte[32], 0x100);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void WritePaletteToROM_NullPalette_ReturnsNotFound()
        {
            var rom = CreateTestRom();
            uint result = ImageImportCore.WritePaletteToROM(rom, null, 0x100);
            Assert.Equal(U.NOT_FOUND, result);
        }

        // ---- Import3Pointer parameter order regression test ----

        [Fact]
        public void Import3Pointer_ParameterOrder_ImgTsaPal()
        {
            // Regression test: Import3Pointer signature is (rom, pixels, palette,
            //   width, height, imgPointerAddr, tsaPointerAddr, palPointerAddr).
            // WinForms ROM layout for CG/BG/TSAAnime:
            //   addr+0 = image pointer, addr+4 = TSA pointer, addr+8 = palette pointer
            // Callers must pass (addr+0, addr+4, addr+8) NOT (addr+0, addr+8, addr+4).
            var method = typeof(ImageImportCore).GetMethod("Import3Pointer",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            var parameters = method.GetParameters();
            // Verify parameter names match expected order
            Assert.Equal("imgPointerAddr", parameters[5].Name);
            Assert.Equal("tsaPointerAddr", parameters[6].Name);
            Assert.Equal("palPointerAddr", parameters[7].Name);
        }
    }
}
