// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for PaletteCore (#400) - cross-platform palette read/write helpers
// for the Avalonia ImagePalletView (and any future WinForms reuse).
//
// Verifies:
//   * Pointer (0x08xxxxxx) and raw-offset (0x00xxxxxx) both work via
//     U.toOffset normalization (Copilot CLI plan review #1).
//   * Bit-math delegates to PaletteFormatConverter (no parallel copy)
//     (Copilot CLI plan review #3).
//   * Palette-index offset arithmetic (paletteIndex * 32).
//   * Overflow guards (zero tuples when data too small).
using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PaletteCoreTests
    {
        // --- helpers ---

        static byte[] Make16MBuffer() => new byte[0x1_000_000];

        static byte[] PaletteBytesFromColors(params ushort[] colors)
        {
            byte[] buf = new byte[colors.Length * 2];
            for (int i = 0; i < colors.Length; i++)
            {
                buf[i * 2] = (byte)(colors[i] & 0xFF);
                buf[i * 2 + 1] = (byte)(colors[i] >> 8);
            }
            return buf;
        }

        // --- constants ---

        [Fact]
        public void Constants_PaletteBlockSize_Is32()
        {
            Assert.Equal(32, PaletteCore.PALETTE_BLOCK_SIZE);
        }

        [Fact]
        public void Constants_MaxPaletteCount_Is16()
        {
            Assert.Equal(16, PaletteCore.MAX_PALETTE_COUNT);
        }

        // --- ReadPalette ---

        [Fact]
        public void ReadPalette_RawOffset_ReadsCorrectBytes()
        {
            byte[] data = Make16MBuffer();
            // Plant 16 BGR15 colors at offset 0x100000.
            // Black, White, Red, Green, Blue, then 11 zero entries.
            ushort[] colors = new ushort[16];
            colors[0] = 0x0000;
            colors[1] = 0x7FFF;
            colors[2] = 0x001F;
            colors[3] = 0x03E0;
            colors[4] = 0x7C00;
            byte[] palBytes = PaletteBytesFromColors(colors);
            Buffer.BlockCopy(palBytes, 0, data, 0x100000, palBytes.Length);

            var rgb = PaletteCore.ReadPalette(data, 0x100000u, 0);

            Assert.Equal(16, rgb.Length);
            Assert.Equal((0, 0, 0), rgb[0]);                  // Black
            Assert.Equal((248, 248, 248), rgb[1]);            // White (5-bit max -> 248)
            Assert.Equal((248, 0, 0), rgb[2]);                // Red
            Assert.Equal((0, 248, 0), rgb[3]);                // Green
            Assert.Equal((0, 0, 248), rgb[4]);                // Blue
        }

        [Fact]
        public void ReadPalette_GbaPointer_NormalizedViaToOffset_ReadsSameBytes()
        {
            byte[] data = Make16MBuffer();
            // Plant a marker color at offset 0x100000.
            ushort[] colors = new ushort[16];
            colors[0] = 0x7FFF;
            byte[] palBytes = PaletteBytesFromColors(colors);
            Buffer.BlockCopy(palBytes, 0, data, 0x100000, palBytes.Length);

            // Passing GBA pointer (0x08100000) must hit the same offset.
            var rgbPtr = PaletteCore.ReadPalette(data, 0x08100000u, 0);
            // Passing raw offset (0x100000) - control case.
            var rgbOff = PaletteCore.ReadPalette(data, 0x00100000u, 0);

            Assert.Equal(rgbOff[0], rgbPtr[0]);
            Assert.Equal((248, 248, 248), rgbPtr[0]);
        }

        [Fact]
        public void ReadPalette_NonZeroIndex_ReadsFromIndexTimes32Offset()
        {
            byte[] data = Make16MBuffer();
            // Index 0: 16 black colors at offset 0x100000.
            // Index 1: starts at offset 0x100020. Plant white in slot 0 there.
            ushort[] idx1 = new ushort[16];
            idx1[0] = 0x7FFF;
            byte[] idx1Bytes = PaletteBytesFromColors(idx1);
            Buffer.BlockCopy(idx1Bytes, 0, data, 0x100020, idx1Bytes.Length);

            var rgb0 = PaletteCore.ReadPalette(data, 0x100000u, 0);
            var rgb1 = PaletteCore.ReadPalette(data, 0x100000u, 1);

            Assert.Equal((0, 0, 0), rgb0[0]);            // black (default zeros)
            Assert.Equal((248, 248, 248), rgb1[0]);      // white from index-1 slot
        }

        [Fact]
        public void ReadPalette_PastEnd_ReturnsZeros()
        {
            byte[] data = new byte[16]; // too small for a full palette.
            // Use a non-zero but past-end address so the sentinel guards
            // (0 / U.NOT_FOUND - Copilot bot inline review #2) don't
            // short-circuit before the out-of-bounds check runs.
            var rgb = PaletteCore.ReadPalette(data, 0x100u, 0);
            Assert.Equal(16, rgb.Length);
            foreach (var c in rgb)
                Assert.Equal((0, 0, 0), c);
        }

        // Regression: U.NOT_FOUND (0xFFFFFFFF) and uint.MaxValue must NOT
        // crash ReadPalette/WritePalette via 32-bit wraparound. The
        // bounds check uses ulong math (Copilot CLI round-1 review on
        // PR #586).
        [Theory]
        [InlineData(uint.MaxValue)]
        [InlineData(0xFFFFFFFFu)]            // alias for U.NOT_FOUND
        [InlineData(0xFFFFFFE0u)]            // near MaxValue, edge case
        public void ReadPalette_InvalidAddress_ReturnsZerosWithoutCrash(uint addr)
        {
            byte[] data = Make16MBuffer();
            var rgb = PaletteCore.ReadPalette(data, addr, 0);
            Assert.Equal(16, rgb.Length);
            foreach (var c in rgb)
                Assert.Equal((0, 0, 0), c);
        }

        [Theory]
        [InlineData(uint.MaxValue)]
        [InlineData(0xFFFFFFFFu)]
        [InlineData(0xFFFFFFE0u)]
        public void WritePalette_InvalidAddress_NoOpWithoutCrash(uint addr)
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", Make16MBuffer(), "AE7E01");
            // Capture a baseline byte at offset 0 so we can assert no
            // accidental write happens.
            rom.Data[0] = 0xAB;
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xF8, 0, 0);

            PaletteCore.WritePalette(rom, addr, 0, colors);

            Assert.Equal(0xAB, rom.Data[0]);
        }

        [Fact]
        public void ReadPalette_BareZeroAddress_ReturnsZerosWithoutReading()
        {
            byte[] data = Make16MBuffer();
            // Plant non-zero bytes at offset 0 so we can prove the
            // sentinel guard short-circuited (without it, the read
            // would unpack those bytes as BGR15).
            data[0] = 0xFF;
            data[1] = 0x7F;
            var rgb = PaletteCore.ReadPalette(data, 0u, 0);
            Assert.Equal((0, 0, 0), rgb[0]);
        }

        [Fact]
        public void ReadPalette_NegativeIndex_ClampsToZero()
        {
            byte[] data = Make16MBuffer();
            ushort[] colors = new ushort[16];
            colors[0] = 0x7FFF;
            byte[] palBytes = PaletteBytesFromColors(colors);
            Buffer.BlockCopy(palBytes, 0, data, 0x100000, palBytes.Length);

            var rgb = PaletteCore.ReadPalette(data, 0x100000u, -5);

            Assert.Equal((248, 248, 248), rgb[0]); // read from index 0 (clamped).
        }

        // --- PackToBytes ---

        [Fact]
        public void PackToBytes_ProducesThirtyTwoBytes()
        {
            var colors = new (byte, byte, byte)[16];
            byte[] bytes = PaletteCore.PackToBytes(colors);
            Assert.Equal(32, bytes.Length);
        }

        [Fact]
        public void PackToBytes_Black_AllZero()
        {
            var colors = new (byte, byte, byte)[16];
            for (int i = 0; i < 16; i++)
                colors[i] = (0, 0, 0);
            byte[] bytes = PaletteCore.PackToBytes(colors);
            foreach (byte b in bytes)
                Assert.Equal(0, b);
        }

        [Fact]
        public void PackToBytes_RedOnly_ProducesBGR15Red_0x001F()
        {
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xF8, 0, 0); // 5-bit aligned red
            byte[] bytes = PaletteCore.PackToBytes(colors);
            ushort packed = (ushort)(bytes[0] | (bytes[1] << 8));
            Assert.Equal(0x001F, packed);
        }

        [Fact]
        public void PackToBytes_GreenOnly_ProducesBGR15Green_0x03E0()
        {
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0, 0xF8, 0);
            byte[] bytes = PaletteCore.PackToBytes(colors);
            ushort packed = (ushort)(bytes[0] | (bytes[1] << 8));
            Assert.Equal(0x03E0, packed);
        }

        [Fact]
        public void PackToBytes_BlueOnly_ProducesBGR15Blue_0x7C00()
        {
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0, 0, 0xF8);
            byte[] bytes = PaletteCore.PackToBytes(colors);
            ushort packed = (ushort)(bytes[0] | (bytes[1] << 8));
            Assert.Equal(0x7C00, packed);
        }

        [Fact]
        public void PackToBytes_White_ProducesBGR15White_0x7FFF()
        {
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xF8, 0xF8, 0xF8);
            byte[] bytes = PaletteCore.PackToBytes(colors);
            ushort packed = (ushort)(bytes[0] | (bytes[1] << 8));
            Assert.Equal(0x7FFF, packed);
        }

        // --- WritePalette ---

        [Fact]
        public void WritePalette_RawOffset_WritesCorrectBytes()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", Make16MBuffer(), "AE7E01"); // FE7U test ROM tag.

            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xF8, 0, 0);
            colors[1] = (0, 0xF8, 0);
            colors[2] = (0, 0, 0xF8);

            PaletteCore.WritePalette(rom, 0x100000u, 0, colors);

            // Read back via ReadPalette to confirm round-trip.
            var rgb = PaletteCore.ReadPalette(rom.Data, 0x100000u, 0);
            Assert.Equal((248, 0, 0), rgb[0]);
            Assert.Equal((0, 248, 0), rgb[1]);
            Assert.Equal((0, 0, 248), rgb[2]);
        }

        [Fact]
        public void WritePalette_GbaPointer_NormalizedViaToOffset_WritesSameBytes()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", Make16MBuffer(), "AE7E01");

            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xF8, 0xF8, 0xF8);

            // Write via GBA pointer.
            PaletteCore.WritePalette(rom, 0x08100000u, 0, colors);

            // The raw bytes at offset 0x100000 must reflect white (0x7FFF).
            ushort packed = (ushort)(rom.Data[0x100000] | (rom.Data[0x100001] << 8));
            Assert.Equal(0x7FFF, packed);
        }

        [Fact]
        public void WritePalette_NonZeroIndex_LeavesIndex0Untouched()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", Make16MBuffer(), "AE7E01");

            // Plant a marker at index 0 (offset 0x100000).
            ushort[] marker = new ushort[16];
            marker[0] = 0x1234; // distinctive
            byte[] markerBytes = PaletteBytesFromColors(marker);
            Buffer.BlockCopy(markerBytes, 0, rom.Data, 0x100000, markerBytes.Length);

            // Write at index 1 (offset 0x100020).
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xF8, 0xF8, 0xF8);
            PaletteCore.WritePalette(rom, 0x100000u, 1, colors);

            // Index 0 bytes must remain 0x1234.
            ushort idx0Word = (ushort)(rom.Data[0x100000] | (rom.Data[0x100001] << 8));
            Assert.Equal(0x1234, idx0Word);
            // Index 1 must reflect the write.
            ushort idx1Word = (ushort)(rom.Data[0x100020] | (rom.Data[0x100021] << 8));
            Assert.Equal(0x7FFF, idx1Word);
        }

        // --- delegation to PaletteFormatConverter ---

        [Fact]
        public void PackToBytes_BitPack_RoundTripsVia_PaletteFormatConverter_JascPal()
        {
            // Build a 16-color palette via PaletteCore.PackToBytes, then
            // round-trip through PaletteFormatConverter via the JascPal
            // export+import path (which DOES exercise the bit-math
            // helpers, unlike GbaRaw which is a clone). If
            // PaletteCore.PackToBytes were to use different bit math
            // than PaletteFormatConverter, the round-trip would
            // diverge and this test would fail (Copilot bot inline
            // review #5 on PR #586).
            var colors = new (byte, byte, byte)[16];
            for (int i = 0; i < 16; i++)
                colors[i] = ((byte)((i * 8) & 0xF8), (byte)((i * 16) & 0xF8), (byte)((i * 24) & 0xF8));
            byte[] packedByCore = PaletteCore.PackToBytes(colors);

            // Round-trip: GbaRaw -> JascPal -> GbaRaw. The intermediate
            // JascPal export reads the GBA bytes via PaletteFormatConverter
            // .GbaToRgb (the same helper PaletteCore uses), and the import
            // re-packs via .RgbToGba. Result must be identical bytes.
            byte[] jasc = PaletteFormatConverter.ExportToFormat(packedByCore, PaletteFormat.JascPal);
            byte[] roundTripped = PaletteFormatConverter.ImportFromFormat(jasc, PaletteFormat.JascPal);

            Assert.Equal(packedByCore.Length, roundTripped.Length);
            for (int i = 0; i < packedByCore.Length; i++)
                Assert.Equal(packedByCore[i], roundTripped[i]);
        }

        // --- 5-bit lossy quantization documentation ---

        [Fact]
        public void Pack_LosesBottom3Bits_OfInputByte()
        {
            // Input 0xFF (255) should pack as if it were 0xF8 (248) — bottom 3 bits dropped.
            var colors = new (byte, byte, byte)[16];
            colors[0] = (0xFF, 0xFF, 0xFF);
            byte[] bytes = PaletteCore.PackToBytes(colors);
            ushort packed = (ushort)(bytes[0] | (bytes[1] << 8));
            Assert.Equal(0x7FFF, packed); // same as if input had been (0xF8, 0xF8, 0xF8).
        }
    }
}
