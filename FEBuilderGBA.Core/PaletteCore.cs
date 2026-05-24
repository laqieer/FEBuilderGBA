// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform palette helpers for FEBuilderGBA. Extracted from the
// WinForms PaletteFormRef so the Avalonia ImagePalletView can share the
// ROM read/write + palette-index offset arithmetic without a WinForms
// dependency. See issue #400.
//
// Design notes (from the accepted plan + Copilot CLI plan review):
//
//   * GBA palette format is BGR15: 16-bit little-endian with 5-bit
//     R/G/B channels packed as R | G<<5 | B<<10. One 16-color palette
//     is 32 bytes (16 colors x 2 bytes).
//
//   * Multiple palette tables can live contiguously in ROM: palette
//     N starts at addr + N * 32. The original WF MakePaletteROMToUI
//     uses the same arithmetic. PaletteCore.ReadPalette/WritePalette
//     take a paletteIndex argument that adds N*32 to the base address.
//
//   * Address inputs are GBA pointers (high bit 0x08xxxxxx may be set).
//     PaletteCore normalizes every address via U.toOffset(addr) before
//     touching ROM bytes, so callers can pass either form (this was
//     Copilot CLI plan-review finding #1).
//
//   * BGR15 bit-pack/unpack delegates to PaletteFormatConverter's
//     internal helpers (Copilot CLI plan-review finding #3). PaletteCore
//     does NOT carry a parallel copy of the bit-shift math; the
//     converter is the single source of truth.
//
//   * The 5-bit channel format is lossy: input R=0xFF round-trips to
//     R=0xF8 (loses bottom 3 bits). The Avalonia view enforces this
//     via NumericUpDown.Increment=8.

using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for reading and writing 16-color GBA
    /// palettes (BGR15) to/from raw ROM data. See file-level comment
    /// for the bit-format spec and design notes.
    /// </summary>
    public static class PaletteCore
    {
        /// <summary>One 16-color palette is 32 bytes (16 colors x 2 bytes BGR15).</summary>
        public const int PALETTE_BLOCK_SIZE = 32;

        /// <summary>The maximum number of palettes WF expects in one editor session.</summary>
        public const int MAX_PALETTE_COUNT = 16;

        /// <summary>
        /// Read a 16-color palette from <paramref name="data"/> at
        /// <paramref name="paletteAddress"/> (GBA pointer or raw offset)
        /// and the <paramref name="paletteIndex"/>-th palette block
        /// (paletteIndex * 32 byte offset).
        ///
        /// Returns a 16-tuple array of (R, G, B) byte triples. On
        /// overflow (data too small) or unreachable offset, returns
        /// 16 black entries.
        /// </summary>
        public static (byte r, byte g, byte b)[] ReadPalette(byte[] data, uint paletteAddress, int paletteIndex)
        {
            var result = new (byte, byte, byte)[16];
            if (data == null) return result;
            if (paletteIndex < 0) paletteIndex = 0;

            uint offset = U.toOffset(paletteAddress);
            uint start = offset + (uint)(paletteIndex * PALETTE_BLOCK_SIZE);
            // Guard against overflow / out-of-bounds reads.
            if (start + PALETTE_BLOCK_SIZE > (uint)data.Length)
                return result;

            for (int i = 0; i < 16; i++)
            {
                uint cur = start + (uint)(i * 2);
                ushort gba = (ushort)(data[cur] | (data[cur + 1] << 8));
                PaletteFormatConverter.GbaToRgb(gba, out byte r, out byte g, out byte b);
                result[i] = (r, g, b);
            }
            return result;
        }

        /// <summary>
        /// Pack 16 (R, G, B) tuples into a 32-byte BGR15 blob. Lossy
        /// quantization: each 8-bit channel is reduced to 5-bit (loses
        /// bottom 3 bits). The packer uses
        /// <see cref="PaletteFormatConverter"/>'s shared bit-math so any
        /// future converter improvement automatically applies here.
        /// </summary>
        public static byte[] PackToBytes((byte r, byte g, byte b)[] colors)
        {
            byte[] bytes = new byte[PALETTE_BLOCK_SIZE];
            if (colors == null) return bytes;
            int count = Math.Min(colors.Length, 16);
            for (int i = 0; i < count; i++)
            {
                ushort gba = PaletteFormatConverter.RgbToGba(colors[i].r, colors[i].g, colors[i].b);
                bytes[i * 2] = (byte)(gba & 0xFF);
                bytes[i * 2 + 1] = (byte)(gba >> 8);
            }
            return bytes;
        }

        /// <summary>
        /// Write 16 (R, G, B) tuples to <paramref name="rom"/> at
        /// <paramref name="paletteAddress"/> (GBA pointer or raw offset)
        /// + <paramref name="paletteIndex"/> * 32. No-op when
        /// <paramref name="rom"/> is null or the destination would
        /// overflow ROM data. The write is undo-tracked when the ROM
        /// has an ambient undo scope active (see ROM.BeginUndoScope).
        /// </summary>
        public static void WritePalette(ROM rom, uint paletteAddress, int paletteIndex, (byte r, byte g, byte b)[] colors)
        {
            if (rom == null) return;
            if (paletteIndex < 0) paletteIndex = 0;

            uint offset = U.toOffset(paletteAddress);
            uint start = offset + (uint)(paletteIndex * PALETTE_BLOCK_SIZE);
            if (start + PALETTE_BLOCK_SIZE > (uint)rom.Data.Length)
                return;

            byte[] bytes = PackToBytes(colors);
            rom.write_range(start, bytes);
        }
    }
}
