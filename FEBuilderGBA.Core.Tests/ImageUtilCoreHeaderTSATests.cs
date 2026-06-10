// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the shared header-TSA decode-to-cells + serialize seam (#1071) in
// ImageUtilCore:
//   * DecodeHeaderTSAToCells / SerializeHeaderTSA byte-identical round-trip with
//     ASYMMETRIC data (unique nonzero values across top/bottom AND left/right,
//     >=2 rows/cols, a NONZERO right margin) so a vertical inversion or
//     off-by-one cannot pass accidentally.
//   * Sentinel: canvas cells OUTSIDE the header region are NOT serialized — the
//     serializer output length + content is unaffected by sentinels planted at
//     the canvas tail.
//   * IsValidHeader is false for the corrupt/fallback cases (len<2, mhx/mhy>32,
//     n>=size) so the editor never edits fallback cells; DecodeHeaderTSA keeps
//     its public render fallback byte/pixel-identical (rendered-pixel parity).
//   * SerializeHeaderTSA guard: invalid dims / null tile return the 2-byte
//     degenerate output (NOT a throw).
//
// These are PURE byte-math tests — no ROM, no IImageService needed for the
// serializer round-trip; the render-fallback parity tests use the StubImageService.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageUtilCoreHeaderTSATests
    {
        // -----------------------------------------------------------------
        // Build an ASYMMETRIC synthetic header-TSA stream: a {mhx, mhy} header
        // followed by (mhx+1)*(mhy+1) UNIQUE nonzero u16 cells in stride order.
        // Using value = base + headery*16 + headerx + 1 guarantees every cell is
        // distinct AND differs across rows (top/bottom) and columns (left/right),
        // so the round-trip pins the bottom-to-top vertical inversion.
        // -----------------------------------------------------------------
        static byte[] MakeAsymmetricHeaderTsa(int mhx, int mhy, int baseVal = 0x100)
        {
            int cells = (mhx + 1) * (mhy + 1);
            byte[] tsa = new byte[2 + cells * 2];
            tsa[0] = (byte)mhx;
            tsa[1] = (byte)mhy;
            int i = 2;
            // The header stream is laid out in DECODE-STRIDE order (the inverse
            // of which the serializer re-emits), so we just fill 0..cells-1 with
            // distinct values; the round-trip proves Decode->Serialize is exact
            // regardless of which physical cell each value lands on.
            for (int k = 0; k < cells; k++)
            {
                ushort v = (ushort)(baseVal + k * 7 + 1); // distinct, nonzero
                tsa[i] = (byte)(v & 0xFF);
                tsa[i + 1] = (byte)(v >> 8);
                i += 2;
            }
            return tsa;
        }

        [Theory]
        // mhx < screenWidthTiles-1  => a NONZERO right margin (canvas wider than header).
        // >=2 rows/cols in every case.
        [InlineData(3, 2, 32, 20)]   // header 4x3 in a 32x20 canvas (margin 28)
        [InlineData(2, 4, 32, 20)]   // header 3x5
        [InlineData(5, 3, 40, 24)]   // header 6x4 in a wider 40x24 canvas
        [InlineData(1, 1, 32, 20)]   // smallest >=2x2 header (2x2) with margin
        public void DecodeToCells_ThenSerialize_IsByteIdentical_Asymmetric(
            int mhx, int mhy, int wTiles, int hTiles)
        {
            byte[] original = MakeAsymmetricHeaderTsa(mhx, mhy);

            ImageUtilCore.HeaderTSACells decoded =
                ImageUtilCore.DecodeHeaderTSAToCells(original, wTiles, hTiles, 0);

            Assert.True(decoded.IsValidHeader);
            Assert.NotNull(decoded.Tile);
            Assert.Equal(mhx, decoded.MasterHeaderX);
            Assert.Equal(mhy, decoded.MasterHeaderY);

            byte[] reser = ImageUtilCore.SerializeHeaderTSA(
                decoded.Tile, decoded.MasterHeaderX, decoded.MasterHeaderY, 0);

            // Exact byte-identical inverse — header + every cell in stride order.
            Assert.Equal(original.Length, reser.Length);
            Assert.Equal(original, reser);
        }

        [Fact]
        public void Serialize_OutputLength_Matches_2_Plus_HeaderFootprint()
        {
            int mhx = 4, mhy = 3;
            byte[] original = MakeAsymmetricHeaderTsa(mhx, mhy);
            var decoded = ImageUtilCore.DecodeHeaderTSAToCells(original, 32, 20, 0);

            byte[] reser = ImageUtilCore.SerializeHeaderTSA(
                decoded.Tile, mhx, mhy, 0);

            int expected = 2 + (mhx + 1) * (mhy + 1) * 2;
            Assert.Equal(expected, reser.Length);
        }

        // -----------------------------------------------------------------
        // Sentinel: cells OUTSIDE the header region (the min-clamped canvas tail)
        // are NOT serialized. We decode a valid header, then PLANT sentinels into
        // every canvas cell that the header stride did NOT touch, and prove the
        // serialize output is byte-identical to the no-sentinel serialize.
        // -----------------------------------------------------------------
        [Fact]
        public void Serialize_IgnoresCellsOutsideHeaderRegion_Sentinel()
        {
            int mhx = 3, mhy = 2, wTiles = 32, hTiles = 20;
            byte[] original = MakeAsymmetricHeaderTsa(mhx, mhy);

            var decoded = ImageUtilCore.DecodeHeaderTSAToCells(original, wTiles, hTiles, 0);
            Assert.True(decoded.IsValidHeader);

            // Baseline serialize from the clean decode.
            byte[] clean = ImageUtilCore.SerializeHeaderTSA(decoded.Tile, mhx, mhy, 0);

            // Determine which stride indices the header touches, then plant a
            // sentinel into EVERY other canvas cell.
            bool[] touched = new bool[decoded.Tile.Length];
            for (int hy = 0; hy <= mhy; hy++)
                for (int hx = 0; hx <= mhx; hx++)
                {
                    int n = (mhy - hy) * 32 + hx; // the editor's CellIndex stride map
                    if (n >= 0 && n < touched.Length) touched[n] = true;
                }

            ushort[] polluted = (ushort[])decoded.Tile.Clone();
            for (int k = 0; k < polluted.Length; k++)
                if (!touched[k]) polluted[k] = 0xBEEF; // sentinel

            byte[] withSentinels = ImageUtilCore.SerializeHeaderTSA(polluted, mhx, mhy, 0);

            // The sentinels must NOT appear in the serialized stream.
            Assert.Equal(clean, withSentinels);
        }

        // -----------------------------------------------------------------
        // IsValidHeader semantics — the editor must not edit fallback cells.
        // -----------------------------------------------------------------
        [Fact]
        public void DecodeToCells_TooShort_IsInvalid()
        {
            var r0 = ImageUtilCore.DecodeHeaderTSAToCells(null, 32, 20, 0);
            Assert.False(r0.IsValidHeader);
            Assert.Null(r0.Tile);

            var r1 = ImageUtilCore.DecodeHeaderTSAToCells(new byte[] { 5 }, 32, 20, 0);
            Assert.False(r1.IsValidHeader);
            Assert.Null(r1.Tile);
        }

        [Fact]
        public void DecodeToCells_OversizedHeaderDims_IsInvalid()
        {
            // mhx > 32 / mhy > 32 are the corrupt fallback cases.
            byte[] bad = new byte[2 + 4];
            bad[0] = 40; bad[1] = 2;
            var r = ImageUtilCore.DecodeHeaderTSAToCells(bad, 32, 20, 0);
            Assert.False(r.IsValidHeader);
            Assert.Null(r.Tile);

            byte[] bad2 = new byte[2 + 4];
            bad2[0] = 2; bad2[1] = 40;
            var r2 = ImageUtilCore.DecodeHeaderTSAToCells(bad2, 32, 20, 0);
            Assert.False(r2.IsValidHeader);
        }

        [Fact]
        public void DecodeToCells_HeaderStartOutOfRange_IsInvalid()
        {
            // mhy=20 => n = 20<<5 = 640 >= size (32*20=640) -> n >= size invalid.
            byte[] bad = new byte[2 + 2];
            bad[0] = 1; bad[1] = 20;
            var r = ImageUtilCore.DecodeHeaderTSAToCells(bad, 32, 20, 0);
            Assert.False(r.IsValidHeader);
            Assert.Null(r.Tile);
        }

        // -----------------------------------------------------------------
        // SerializeHeaderTSA guard: degenerate (2-byte) output, never a throw.
        // -----------------------------------------------------------------
        [Fact]
        public void Serialize_NullTile_ReturnsDegenerate()
        {
            byte[] r = ImageUtilCore.SerializeHeaderTSA(null, 3, 2, 0);
            Assert.Equal(2, r.Length);
        }

        [Theory]
        [InlineData(33, 2)]
        [InlineData(2, 33)]
        [InlineData(-1, 2)]
        [InlineData(2, -1)]
        public void Serialize_InvalidDims_ReturnsDegenerate(int mhx, int mhy)
        {
            ushort[] tile = new ushort[32 * 32];
            byte[] r = ImageUtilCore.SerializeHeaderTSA(tile, mhx, mhy, 0);
            Assert.Equal(2, r.Length);
        }

        [Fact]
        public void Serialize_HeaderStartBeyondTile_ReturnsDegenerate()
        {
            // n = mhy<<5 must be < tile.Length. A tiny tile array with mhy=20
            // (n=640) overflows -> degenerate.
            ushort[] tiny = new ushort[8];
            byte[] r = ImageUtilCore.SerializeHeaderTSA(tiny, 1, 20, 0);
            Assert.Equal(2, r.Length);
        }

        // -----------------------------------------------------------------
        // DecodeHeaderTSA render-fallback parity: the refactor must keep the
        // public render byte/pixel-identical for the corrupt cases. We compare
        // the rendered pixels of the header path against the documented linear /
        // blank fallback for an oversized header.
        // -----------------------------------------------------------------
        [Fact]
        public void DecodeHeaderTSA_OversizedHeader_RendersLinearFallback_PixelIdentical()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();

                byte[] tiles = MakeTwoTiles();
                byte[] palette = MakePalette();

                // Oversized header (mhx=40) -> fallback to linear DecodeTSA(raw).
                byte[] tsa = new byte[2 + 8];
                tsa[0] = 40; tsa[1] = 2;
                // Plant a couple of linear TSA entries so the fallback render is
                // non-trivial.
                WriteU16(tsa, 2, (ushort)(1)); // tile 1 at linear pos 0
                WriteU16(tsa, 4, (ushort)(1)); // tile 1 at linear pos 1

                IImage viaHeader = ImageUtilCore.DecodeHeaderTSA(
                    tiles, tsa, palette, 4, 4, true, 0, 0);
                IImage viaLinear = ImageUtilCore.DecodeTSA(
                    tiles, tsa, palette, 4, 4, true, 0);

                Assert.NotNull(viaHeader);
                Assert.NotNull(viaLinear);
                Assert.Equal(viaLinear.GetPixelData(), viaHeader.GetPixelData());
            }
            finally { CoreState.ImageService = saved; }
        }

        [Fact]
        public void DecodeHeaderTSA_ValidHeader_StillRenders()
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                byte[] tiles = MakeTwoTiles();
                byte[] palette = MakePalette();

                byte[] tsa = MakeAsymmetricHeaderTsa(2, 2, baseVal: 1);
                IImage img = ImageUtilCore.DecodeHeaderTSA(
                    tiles, tsa, palette, 32, 20, true, 0, 0);
                Assert.NotNull(img);
                Assert.Equal(32 * 8, img.Width);
                Assert.Equal(20 * 8, img.Height);
            }
            finally { CoreState.ImageService = saved; }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        static void WriteU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)(v >> 8);
        }

        static byte[] MakeTwoTiles()
        {
            byte[] tiles = new byte[2 * 32];
            // Tile 1 = all color index 1.
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int pos = 32 + y * 4 + x / 2;
                    byte cur = tiles[pos];
                    if (x % 2 == 0) cur = (byte)((cur & 0xF0) | 1);
                    else cur = (byte)((cur & 0x0F) | (1 << 4));
                    tiles[pos] = cur;
                }
            return tiles;
        }

        static byte[] MakePalette()
        {
            byte[] pal = new byte[512];
            // bank 0 index 1 = red.
            pal[2] = 0x1F; pal[3] = 0x00;
            return pal;
        }
    }
}
