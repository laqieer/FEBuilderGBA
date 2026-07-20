// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// The four boundary rows/columns of raw (palette-independent) 4bpp color indices for one
    /// 16x16 metatile (chipset), as actually rendered after TSA hFlip/vFlip is applied. Used
    /// to decide whether two chipsets are structurally compatible along a shared edge when
    /// the strict, directly-observed adjacency model (<see cref="BuiltInRandomMapAdjacencyModel.Strict"/>)
    /// has too little corpus evidence.
    ///
    /// <para>Each array holds exactly 16 raw color indices (0..15), ordered by increasing
    /// screen coordinate along that edge (top/bottom: increasing x; left/right: increasing
    /// y). Values are the same nibble <see cref="MapEditorTilesetCore.RenderTile4bpp"/> would
    /// look up in the palette — this signature intentionally stops one step earlier (no
    /// palette indirection) so two chipsets that share art but use different palette slots
    /// still compare as compatible, matching how the game visually tiles them.</para>
    /// </summary>
    public sealed class MetatileEdgeSignature
    {
        internal MetatileEdgeSignature(int[] top, int[] bottom, int[] left, int[] right)
        {
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
        }

        /// <summary>Raw color indices along the top edge (py=0), left-to-right, 16 entries.</summary>
        public int[] Top { get; }

        /// <summary>Raw color indices along the bottom edge (py=15), left-to-right, 16 entries.</summary>
        public int[] Bottom { get; }

        /// <summary>Raw color indices along the left edge (px=0), top-to-bottom, 16 entries.</summary>
        public int[] Left { get; }

        /// <summary>Raw color indices along the right edge (px=15), top-to-bottom, 16 entries.</summary>
        public int[] Right { get; }
    }

    /// <summary>
    /// Computes <see cref="MetatileEdgeSignature"/>s directly from decompressed OBJ/CFG
    /// tileset bytes (no ROM access, no palette lookup) and defines the exact edge-matching
    /// semantics used by the relaxed adjacency model. See <c>docs/CORE-SEAMS.md</c>
    /// ("Built-in Random Map Generator") for the documented signature contract.
    /// </summary>
    public static class BuiltInRandomMapEdgeSignatureCore
    {
        // Sub-tile layout inside one 16x16 chipset (matches MapEditorTilesetCore.RenderChipsetIntoBuffer):
        //   sub 0 = top-left 8x8, sub 1 = top-right, sub 2 = bottom-left, sub 3 = bottom-right.
        const int SubTopLeft = 0;
        const int SubTopRight = 1;
        const int SubBottomLeft = 2;
        const int SubBottomRight = 3;

        /// <summary>
        /// Decode the boundary signature for the chipset addressed by <paramref name="marValue"/>.
        /// Returns false when the TSA block is out of range for <paramref name="configData"/>
        /// (mirrors <see cref="BuiltInRandomMapTilesetCore.IsMarRenderable"/>'s bounds check).
        /// </summary>
        public static bool TryComputeEdgeSignature(
            ushort marValue,
            byte[] configData,
            byte[] objData,
            out MetatileEdgeSignature signature)
        {
            signature = null;
            if (configData == null) return false;

            int tsaBase = marValue << 1;
            if (tsaBase + 7 >= configData.Length) return false;

            ReadSubTile(configData, tsaBase, 0, out int tile0, out bool hf0, out bool vf0);
            ReadSubTile(configData, tsaBase, 1, out int tile1, out bool hf1, out bool vf1);
            ReadSubTile(configData, tsaBase, 2, out int tile2, out bool hf2, out bool vf2);
            ReadSubTile(configData, tsaBase, 3, out int tile3, out bool hf3, out bool vf3);

            int[] top = new int[16];
            int[] bottom = new int[16];
            int[] left = new int[16];
            int[] right = new int[16];

            for (int x = 0; x < 8; x++)
            {
                top[x] = RawIndex(objData, tile0, hf0, vf0, x, 0);
                top[x + 8] = RawIndex(objData, tile1, hf1, vf1, x, 0);
                bottom[x] = RawIndex(objData, tile2, hf2, vf2, x, 7);
                bottom[x + 8] = RawIndex(objData, tile3, hf3, vf3, x, 7);
            }
            for (int y = 0; y < 8; y++)
            {
                left[y] = RawIndex(objData, tile0, hf0, vf0, 0, y);
                left[y + 8] = RawIndex(objData, tile2, hf2, vf2, 0, y);
                right[y] = RawIndex(objData, tile1, hf1, vf1, 7, y);
                right[y + 8] = RawIndex(objData, tile3, hf3, vf3, 7, y);
            }

            signature = new MetatileEdgeSignature(top, bottom, left, right);
            return true;
        }

        /// <summary>
        /// Whether a chipset placed at <paramref name="west"/> may be immediately west of a
        /// chipset placed at <paramref name="east"/>: the touching column of raw indices must
        /// match exactly, index-for-index, top-to-bottom.
        /// </summary>
        public static bool HorizontallyCompatible(MetatileEdgeSignature west, MetatileEdgeSignature east)
        {
            if (west == null || east == null) return false;
            return SequenceEqual(west.Right, east.Left);
        }

        /// <summary>
        /// Whether a chipset placed at <paramref name="north"/> may be immediately north of a
        /// chipset placed at <paramref name="south"/>: the touching row of raw indices must
        /// match exactly, index-for-index, left-to-right.
        /// </summary>
        public static bool VerticallyCompatible(MetatileEdgeSignature north, MetatileEdgeSignature south)
        {
            if (north == null || south == null) return false;
            return SequenceEqual(north.Bottom, south.Top);
        }

        static bool SequenceEqual(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static void ReadSubTile(byte[] configData, int tsaBase, int sub, out int tileIndex, out bool hFlip, out bool vFlip)
        {
            int tsaOff = tsaBase + sub * 2;
            ushort tsa = (ushort)(configData[tsaOff] | (configData[tsaOff + 1] << 8));
            tileIndex = tsa & 0x3FF;
            hFlip = (tsa & 0x400) != 0;
            vFlip = (tsa & 0x800) != 0;
        }

        /// <summary>
        /// Raw (unpaletted) 4bpp color index at local (px,py) within one 8x8 OBJ tile, after
        /// applying hFlip/vFlip exactly as <see cref="MapEditorTilesetCore.RenderTile4bpp"/>
        /// does. Returns 0 (transparent/background index) for missing tile data instead of
        /// throwing, matching that method's "leave blank" behavior for out-of-range tiles.
        /// </summary>
        static int RawIndex(byte[] objData, int tileIndex, bool hFlip, bool vFlip, int px, int py)
        {
            const int bytesPerTile = 32;
            if (objData == null) return 0;
            int tileOffset = tileIndex * bytesPerTile;
            if (tileOffset + bytesPerTile > objData.Length || tileOffset < 0) return 0;

            int srcY = vFlip ? (7 - py) : py;
            int srcX = hFlip ? (7 - px) : px;
            int bytePos = tileOffset + srcY * 4 + srcX / 2;
            if (bytePos < 0 || bytePos >= objData.Length) return 0;

            byte b = objData[bytePos];
            return (srcX % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);
        }
    }
}
