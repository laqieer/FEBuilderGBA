using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
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
    }
}
