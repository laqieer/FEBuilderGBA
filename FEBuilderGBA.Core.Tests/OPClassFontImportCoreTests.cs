// SPDX-License-Identifier: GPL-3.0-or-later
// #999 Core tests for OPClassFontImportCore — the static PNG/BMP OP class font
// glyph import seam (validate dims -> encode 4bpp + LZ77 write-back + repoint the
// D0 glyph pointer, with a defensive length-aware snapshot restore on fault).
//
// Mirrors WaitIconImportCoreTests. Uses a synthetic FE8U ROM. The seam only
// touches the D0 glyph pointer slot + ROM free space, so the round-trip plants a
// glyph entry slot directly and decodes the written tiles. Validates:
//   * 32x32 import repoints D0 to fresh in-free-space LZ77 data + round-trips
//     back to the same 1024 4bpp-packed indices
//   * a size-mismatched buffer mutates ZERO bytes (byte-identical ROM + error)
//   * a near-EOF glyph slot is rejected with NO mutation
//   * null indexedPixels / null ROM are rejected with NO throw
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class OPClassFontImportCoreTests
    {
        // A glyph entry near the ROM START, with plenty of free 0xFF space after
        // the midpoint for WriteCompressedToROM to land in.
        const uint GLYPH_ADDR = 0x400;
        const uint ROM_LEN = 0x20000; // 128 KiB

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            // Fill with 0xFF (free-space marker) so FindFreeSpace can place data.
            Array.Fill(data, (byte)0xFF);
            // Zero the entry region + header so reads are deterministic.
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        // 32x32 indexed glyph: one byte per pixel, indices 0-15.
        static byte[] MakeIndexed(int w, int h)
        {
            byte[] px = new byte[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)(i % 16);
            return px;
        }

        [Fact]
        public void Import_32x32_RepointsD0_AndRoundTrips()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] indexed = MakeIndexed(32, 32);
                uint d0Before = rom.u32(GLYPH_ADDR);

                // No ambient undo scope (matches WaitIconImportCoreTests): the
                // seam's writes short-circuit the null _ambientUndoData and still
                // mutate the ROM; success-path coverage doesn't need an UNDO record.
                string err = OPClassFontImportCore.Import(rom, GLYPH_ADDR, indexed, 32, 32);
                Assert.Equal("", err);

                // D0 repointed to a real GBA pointer (changed from the old value).
                uint d0After = rom.u32(GLYPH_ADDR);
                Assert.True(U.isPointer(d0After), $"D0 not a pointer: 0x{d0After:X08}");
                Assert.NotEqual(d0Before, d0After);

                // ROUND-TRIP: decode the written LZ77 tiles and confirm they
                // re-expand to the same 4bpp-packed bytes the encoder produced.
                uint imgOff = U.toOffset(d0After);
                byte[] decoded = LZ77.decompress(rom.Data, imgOff);
                byte[] expectedTiles = ImageImportCore.EncodeDirectTiles4bpp(indexed, 32, 32);
                Assert.NotNull(decoded);
                Assert.Equal(expectedTiles.Length, decoded.Length);   // 4x4 tiles * 32 = 512 bytes
                Assert.Equal(expectedTiles, decoded);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_SizeMismatch_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // A 16x16 buffer (256 bytes) declared as 32x32 (1024 expected) —
                // indexedPixels.Length < width*height => rejected with ZERO mutation.
                string err = OPClassFontImportCore.Import(rom, GLYPH_ADDR, MakeIndexed(16, 16), 32, 32);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_NonMultipleOf8_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // 12x12 is positive but not a multiple of 8 — rejected, no mutation.
                string err = OPClassFontImportCore.Import(rom, GLYPH_ADDR, MakeIndexed(12, 12), 12, 12);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_GlyphAddrNearEof_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // glyphPtrAddr + 4 > Data.Length => out-of-range, rejected, no mutation.
                uint nearEof = (uint)rom.Data.Length - 2;
                string err = OPClassFontImportCore.Import(rom, nearEof, MakeIndexed(32, 32), 32, 32);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_NullIndexedPixels_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                string err = OPClassFontImportCore.Import(rom, GLYPH_ADDR, null, 32, 32);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_NullRom_ReturnsError_NoThrow()
        {
            string err = OPClassFontImportCore.Import(null, GLYPH_ADDR, MakeIndexed(32, 32), 32, 32);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void Import_ForcedFault_RestoresByteIdentical()
        {
            var savedRom = CoreState.ROM;
            try
            {
                // Force WriteCompressedToROM -> U.NOT_FOUND: a ROM already at the
                // 32MB max with NO free space at all. FindFreeSpace treats both
                // 0x00 AND 0xFF runs as free, so the ROM is filled with 0x01
                // (a non-free byte) to break every run; AppendToRomEnd then
                // refuses (newEnd > 0x02000000). The import must restore
                // byte-identical and return a non-empty error.
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01);    // no 0x00/0xFF free runs anywhere
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;

                byte[] before = (byte[])rom.Data.Clone();
                string err = OPClassFontImportCore.Import(rom, GLYPH_ADDR, MakeIndexed(32, 32), 32, 32);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical, ZERO mutation
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
