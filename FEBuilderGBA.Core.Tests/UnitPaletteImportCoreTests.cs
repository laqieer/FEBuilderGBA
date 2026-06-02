// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for UnitPaletteImportCore (#904).
//
// Validates the ≤16-color guard (CORRECTION 3a) and the INDEX-ORDER palette
// extraction (CORRECTION 3b) used by the Avalonia Unit Palette Editor's
// "Import Image" button.
//
// Cross-platform: no WinForms, no Avalonia, no System.Drawing.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class UnitPaletteImportCoreTests
    {
        // Build a flat RGBA pixel buffer from an ordered list of (r8,g8,b8) colors,
        // one pixel per color, in scan order.
        static byte[] RgbaPixels(params (byte r, byte g, byte b)[] colors)
        {
            var buf = new byte[colors.Length * 4];
            for (int i = 0; i < colors.Length; i++)
            {
                buf[i * 4 + 0] = colors[i].r;
                buf[i * 4 + 1] = colors[i].g;
                buf[i * 4 + 2] = colors[i].b;
                buf[i * 4 + 3] = 255;
            }
            return buf;
        }

        // Expected RGB555 channel for an 8-bit value: top 5 bits.
        static uint Ch5(byte v) => (uint)((v >> 3) & 0x1F);

        // ----- CORRECTION 3b: ordered extraction from RGBA pixels -----

        [Fact]
        public void ExtractFromRgba_PreservesIndexOrder()
        {
            // 4 distinct colors in a deliberate, non-sorted scan order.
            byte[] px = RgbaPixels(
                (0xF8, 0x00, 0x00),   // index 0 = red (backdrop)
                (0x00, 0xF8, 0x00),   // index 1 = green
                (0x00, 0x00, 0xF8),   // index 2 = blue
                (0xF8, 0xF8, 0x00));  // index 3 = yellow

            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                System.Array.Empty<byte>(), px, out uint[] r, out uint[] g, out uint[] b);

            Assert.True(ok);
            // Ordered equality (NOT set equality) — guards CORRECTION 3b.
            Assert.Equal(Ch5(0xF8), r[0]); Assert.Equal(0u, g[0]);        Assert.Equal(0u, b[0]);
            Assert.Equal(0u, r[1]);        Assert.Equal(Ch5(0xF8), g[1]); Assert.Equal(0u, b[1]);
            Assert.Equal(0u, r[2]);        Assert.Equal(0u, g[2]);        Assert.Equal(Ch5(0xF8), b[2]);
            Assert.Equal(Ch5(0xF8), r[3]); Assert.Equal(Ch5(0xF8), g[3]); Assert.Equal(0u, b[3]);
            // Unused slots zero-filled.
            for (int i = 4; i < 16; i++) { Assert.Equal(0u, r[i]); Assert.Equal(0u, g[i]); Assert.Equal(0u, b[i]); }
        }

        [Fact]
        public void ExtractFromRgba_DeduplicatesButKeepsFirstAppearanceOrder()
        {
            // Repeated colors must collapse to first-appearance order, not be re-counted.
            byte[] px = RgbaPixels(
                (0x10, 0x10, 0x10),   // index 0
                (0x10, 0x10, 0x10),   // dup of 0
                (0xF8, 0x00, 0x00),   // index 1
                (0x10, 0x10, 0x10));  // dup of 0 again

            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                System.Array.Empty<byte>(), px, out uint[] r, out uint[] g, out uint[] b);

            Assert.True(ok);
            Assert.Equal(Ch5(0x10), r[0]);
            Assert.Equal(Ch5(0xF8), r[1]); Assert.Equal(0u, g[1]); Assert.Equal(0u, b[1]);
            Assert.Equal(0u, r[2]); // nothing past the 2 distinct colors
        }

        [Fact]
        public void ExtractFromRgba_Exactly16Colors_Accepted()
        {
            var colors = new (byte, byte, byte)[16];
            for (int i = 0; i < 16; i++) colors[i] = ((byte)(i * 8), (byte)0, (byte)0);
            byte[] px = RgbaPixels(colors);

            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                System.Array.Empty<byte>(), px, out uint[] r, out _, out _);
            Assert.True(ok);
            Assert.Equal(Ch5(0), r[0]);
            Assert.Equal(Ch5(15 * 8), r[15]);
        }

        // ----- CORRECTION 3a: >16-color rejection -----

        [Fact]
        public void ExtractFromRgba_SeventeenColors_Rejected_NoChange()
        {
            var colors = new (byte, byte, byte)[17];
            for (int i = 0; i < 17; i++) colors[i] = ((byte)(i * 8), (byte)0, (byte)0);
            byte[] px = RgbaPixels(colors);

            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                System.Array.Empty<byte>(), px, out uint[] r, out uint[] g, out uint[] b);

            Assert.False(ok); // CORRECTION 3a — refuse, no extraction
            // All channels left zeroed (caller must make no change).
            for (int i = 0; i < 16; i++) { Assert.Equal(0u, r[i]); Assert.Equal(0u, g[i]); Assert.Equal(0u, b[i]); }
        }

        [Fact]
        public void ExtractFromRgba_EmptyPixels_Rejected()
        {
            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                System.Array.Empty<byte>(), System.Array.Empty<byte>(),
                out _, out _, out _);
            Assert.False(ok);
        }

        // ----- Path 1: loader-preserved indexed GBA palette -----

        [Fact]
        public void ExtractFromGbaPalette_DecodesInIndexOrder()
        {
            // GBA RGB555 bytes for 3 colors, little-endian: red, green, blue.
            ushort red   = (ushort)((0x1F) | (0 << 5) | (0 << 10));
            ushort green = (ushort)((0) | (0x1F << 5) | (0 << 10));
            ushort blue  = (ushort)((0) | (0 << 5) | (0x1F << 10));
            byte[] gba = new byte[6];
            gba[0] = (byte)red;   gba[1] = (byte)(red >> 8);
            gba[2] = (byte)green; gba[3] = (byte)(green >> 8);
            gba[4] = (byte)blue;  gba[5] = (byte)(blue >> 8);

            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                gba, System.Array.Empty<byte>(), out uint[] r, out uint[] g, out uint[] b);

            Assert.True(ok);
            Assert.Equal(0x1Fu, r[0]); Assert.Equal(0u, g[0]); Assert.Equal(0u, b[0]);
            Assert.Equal(0u, r[1]); Assert.Equal(0x1Fu, g[1]); Assert.Equal(0u, b[1]);
            Assert.Equal(0u, r[2]); Assert.Equal(0u, g[2]); Assert.Equal(0x1Fu, b[2]);
        }

        [Fact]
        public void ExtractFromGbaPalette_SeventeenColors_Rejected()
        {
            byte[] gba = new byte[17 * 2]; // 17 colors -> reject
            bool ok = UnitPaletteImportCore.TryExtractIndexOrdered(
                gba, System.Array.Empty<byte>(), out _, out _, out _);
            Assert.False(ok);
        }
    }
}
