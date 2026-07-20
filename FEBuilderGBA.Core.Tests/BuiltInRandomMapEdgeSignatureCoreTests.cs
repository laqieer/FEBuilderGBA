// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class BuiltInRandomMapEdgeSignatureCoreTests
    {
        const int BytesPerTile = 32;

        [Fact]
        public void TryComputeEdgeSignature_OutOfRangeTsa_ReturnsFalse()
        {
            byte[] configData = new byte[4]; // too small for any TSA block
            bool ok = BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(0, configData, new byte[32], out var sig);
            Assert.False(ok);
            Assert.Null(sig);
        }

        [Fact]
        public void TryComputeEdgeSignature_NullConfigData_ReturnsFalse()
        {
            bool ok = BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(0, null, new byte[32], out var sig);
            Assert.False(ok);
        }

        [Fact]
        public void TryComputeEdgeSignature_DecodesBoundaryPixelsPerSubtileLayout()
        {
            // Four distinct 8x8 tiles (indices 0..3), each filled with a deterministic
            // formula so the expected boundary arrays can be recomputed independently of
            // BuiltInRandomMapEdgeSignatureCore's own RawIndex implementation.
            byte[] objData = new byte[BytesPerTile * 4];
            for (int t = 0; t < 4; t++)
                FillTile(objData, t, (px, py) => (t * 4 + px + py) % 16);

            byte[] configData = new byte[16];
            SetTsa(configData, 0, sub: 0, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, 0, sub: 1, tileIndex: 1, hFlip: false, vFlip: false);
            SetTsa(configData, 0, sub: 2, tileIndex: 2, hFlip: false, vFlip: false);
            SetTsa(configData, 0, sub: 3, tileIndex: 3, hFlip: false, vFlip: false);

            bool ok = BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(0, configData, objData, out var sig);
            Assert.True(ok);

            int[] ExpectedTop() => Concat(Row(0, 0), Row(1, 0));
            int[] ExpectedBottom() => Concat(Row(2, 7), Row(3, 7));
            int[] ExpectedLeft() => Concat(Col(0, 0), Col(2, 0));
            int[] ExpectedRight() => Concat(Col(1, 7), Col(3, 7));

            Assert.Equal(ExpectedTop(), sig.Top);
            Assert.Equal(ExpectedBottom(), sig.Bottom);
            Assert.Equal(ExpectedLeft(), sig.Left);
            Assert.Equal(ExpectedRight(), sig.Right);

            static int[] Row(int tile, int py)
            {
                int[] r = new int[8];
                for (int px = 0; px < 8; px++) r[px] = (tile * 4 + px + py) % 16;
                return r;
            }
            static int[] Col(int tile, int px)
            {
                int[] r = new int[8];
                for (int py = 0; py < 8; py++) r[py] = (tile * 4 + px + py) % 16;
                return r;
            }
            static int[] Concat(int[] a, int[] b)
            {
                int[] r = new int[a.Length + b.Length];
                a.CopyTo(r, 0);
                b.CopyTo(r, a.Length);
                return r;
            }
        }

        [Fact]
        public void TryComputeEdgeSignature_HFlipAndVFlipMirrorPixels()
        {
            // Tile 0: color index equals px (independent of py) so horizontal flip is
            // trivially observable; tile 1 is identical content used with vFlip so the
            // top/bottom rows swap meaning within one 8x8 tile is irrelevant here since
            // vFlip only affects py, and this tile's content ignores py, so instead we
            // encode py directly for the vFlip check.
            byte[] objData = new byte[BytesPerTile * 2];
            FillTile(objData, 0, (px, py) => px); // varies only with px
            FillTile(objData, 1, (px, py) => py); // varies only with py

            byte[] configData = new byte[16];
            // All four sub-tiles use tile 0 with hFlip on subs 1/3 (east column) so the
            // Right edge should be the mirror of an unflipped tile's right column, and the
            // Left edge (subs 0/2, no flip) should be tile 0's true left column (all zeros,
            // since px=0 there).
            SetTsa(configData, 0, sub: 0, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, 0, sub: 1, tileIndex: 0, hFlip: true, vFlip: false);
            SetTsa(configData, 0, sub: 2, tileIndex: 1, hFlip: false, vFlip: false);
            SetTsa(configData, 0, sub: 3, tileIndex: 1, hFlip: false, vFlip: true);

            bool ok = BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(0, configData, objData, out var sig);
            Assert.True(ok);

            // Left edge = sub0 col0 (tile0, no flip) then sub2 col0 (tile1, no flip).
            // sub0's content is px (=0 at col0) for all 8 rows => all zeros.
            for (int y = 0; y < 8; y++)
                Assert.Equal(0, sig.Left[y]);
            // sub2's content is py (independent of column) => 0..7 down the column.
            for (int y = 0; y < 8; y++)
                Assert.Equal(y, sig.Left[8 + y]);

            // Right edge = sub1 col7 (tile0, hFlip): reading local px=7 with hFlip means
            // srcX = 7-7 = 0, so value should also be 0 for the top half (sub1 covers y 0..7).
            for (int y = 0; y < 8; y++)
                Assert.Equal(0, sig.Right[y]);

            // Bottom edge = sub2 row7 (tile1, no flip): value = py = srcY = 7 for every px.
            for (int px = 0; px < 8; px++)
                Assert.Equal(7, sig.Bottom[px]);

            // Bottom edge second half = sub3 row7 (tile1, vFlip): srcY = 7-7 = 0.
            for (int px = 0; px < 8; px++)
                Assert.Equal(0, sig.Bottom[8 + px]);
        }

        [Fact]
        public void HorizontallyCompatible_MatchesOnlyWhenTouchingColumnsAreIdentical()
        {
            byte[] objData = new byte[BytesPerTile * 2];
            FillTile(objData, 0, (px, py) => px % 16);
            FillTile(objData, 1, (px, py) => (px + 1) % 16);

            byte[] configData = new byte[16];
            for (int sub = 0; sub < 4; sub++)
                SetTsa(configData, 0, sub, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, 8, sub: 0, tileIndex: 0, hFlip: false, vFlip: false); // MAR=4 -> tsaBase=8
            SetTsa(configData, 8, sub: 1, tileIndex: 1, hFlip: false, vFlip: false);
            SetTsa(configData, 8, sub: 2, tileIndex: 0, hFlip: false, vFlip: false);
            SetTsa(configData, 8, sub: 3, tileIndex: 1, hFlip: false, vFlip: false);

            Assert.True(BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(0, configData, objData, out var chipA));
            Assert.True(BuiltInRandomMapEdgeSignatureCore.TryComputeEdgeSignature(4, configData, objData, out var chipB));

            // chipA (all tile 0) vs itself: chipA.Right (tile0/tile0 px=7 col) must equal chipA.Left (tile0/tile0 px=0 col).
            // tile0 content = px, so col7 = [7,7,...] and col0 = [0,0,...] -> NOT compatible with itself.
            Assert.False(BuiltInRandomMapEdgeSignatureCore.HorizontallyCompatible(chipA, chipA));

            // chipB's Left column (subs 0/2 = tile0, px=0) is all zeros; chipA's Right column
            // (subs 1/3 = tile0, px=7) is all sevens -> still incompatible.
            Assert.False(BuiltInRandomMapEdgeSignatureCore.HorizontallyCompatible(chipA, chipB));

            // Null-safety: neither compatibility check should throw on a null signature.
            Assert.False(BuiltInRandomMapEdgeSignatureCore.HorizontallyCompatible(null, chipB));
            Assert.False(BuiltInRandomMapEdgeSignatureCore.VerticallyCompatible(chipA, null));
        }

        static void FillTile(byte[] objData, int tileIndex, System.Func<int, int, int> colorAt)
        {
            int baseOff = tileIndex * BytesPerTile;
            for (int py = 0; py < 8; py++)
            {
                for (int px = 0; px < 8; px += 2)
                {
                    int lo = colorAt(px, py) & 0xF;
                    int hi = colorAt(px + 1, py) & 0xF;
                    objData[baseOff + py * 4 + px / 2] = (byte)(lo | (hi << 4));
                }
            }
        }

        static void SetTsa(byte[] configData, int tsaBase, int sub, int tileIndex, bool hFlip, bool vFlip)
        {
            ushort tsa = (ushort)(tileIndex & 0x3FF);
            if (hFlip) tsa |= 0x400;
            if (vFlip) tsa |= 0x800;
            int off = tsaBase + sub * 2;
            configData[off] = (byte)(tsa & 0xFF);
            configData[off + 1] = (byte)((tsa >> 8) & 0xFF);
        }
    }
}
