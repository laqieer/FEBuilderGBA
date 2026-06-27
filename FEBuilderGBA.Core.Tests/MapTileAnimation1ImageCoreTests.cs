// SPDX-License-Identifier: GPL-3.0-or-later
// #1602 Core tests for MapTileAnimation1ImageCore — the Map Tile Animation
// Type 1 image preview + single-PNG Import/Export + batch (.mapanime1.txt) seam.
//
// The anime1 graphics block is RAW UNCOMPRESSED 4bpp at the entry's +4 pointer,
// sized by the entry's +2 u16 length (the INVERSE of anime2). These tests use a
// synthetic ROM + a stub image service (for GBAColorToRGBA) and validate:
//   * CalcEntryHeight matches the WF CalcHeight(256, len) formula
//   * single import keeps the +2 length authoritative (UNCHANGED) and repoints +4
//   * a wrong-size image mutates ZERO bytes (byte-identical ROM + error)
//   * the written RAW bytes round-trip byte-identically to the encoded tiles
//   * a fault (free-space exhaustion) restores byte-identical
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapTileAnimation1ImageCoreTests
    {
        // Entry near the ROM start; palette just after it; free 0xFF space after
        // the midpoint for WriteRawToROM to land in.
        const uint ENTRY_ADDR = 0x400;
        const uint PAL_ADDR = 0x500;
        const uint ROM_LEN = 0x20000; // 128 KiB

        // A 256x8 sheet = 1024 bytes (one tile row). CalcHeight(256, 1024) == 8.
        const int SHEET_W = 256;
        const int SHEET_H = 8;
        const uint SHEET_LEN = (uint)(SHEET_W * SHEET_H / 2); // 1024

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF); // free-space marker
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00; // deterministic header/entry region

            // Entry: wait=0x10@+0, length=SHEET_LEN@+2, pointer=0x08000800@+4.
            data[ENTRY_ADDR + 0] = 0x10;
            data[ENTRY_ADDR + 1] = 0x00;
            data[ENTRY_ADDR + 2] = (byte)(SHEET_LEN & 0xFF);
            data[ENTRY_ADDR + 3] = (byte)((SHEET_LEN >> 8) & 0xFF);
            // +4 points at 0x800 (safe, inside the zeroed region).
            data[ENTRY_ADDR + 4] = 0x00;
            data[ENTRY_ADDR + 5] = 0x08;
            data[ENTRY_ADDR + 6] = 0x00;
            data[ENTRY_ADDR + 7] = 0x08;

            // A 16-color palette at PAL_ADDR: distinct GBA colors so remap is
            // unambiguous. color i = i*0x0421 (spreads R/G/B bits).
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(i | (i << 5) | (i << 10));
                data[PAL_ADDR + i * 2] = (byte)(c & 0xFF);
                data[PAL_ADDR + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null) CoreState.ImageService = new StubImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        // Build RGBA pixels whose colors match the synthetic palette (entry i ->
        // palette[i%16]) so RemapToExistingPalette picks index i%16 exactly.
        static byte[] MakeRgbaFromPalette(int w, int h)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                int idx = i % 16;
                ushort c = (ushort)(idx | (idx << 5) | (idx << 10));
                byte r = (byte)((c & 0x1F) << 3);
                byte g = (byte)(((c >> 5) & 0x1F) << 3);
                byte b = (byte)(((c >> 10) & 0x1F) << 3);
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                // index 0 must be alpha<128 (transparent) so it remaps to 0.
                rgba[i * 4 + 3] = (idx == 0) ? (byte)0 : (byte)255;
            }
            return rgba;
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1024, 8)]   // 8 rows exact
        [InlineData(128, 8)]    // exactly one row
        [InlineData(129, 8)]    // WF rounds up 1->2 then aligns 2->8 then /8*8 = 8
        [InlineData(1025, 16)]  // 9 rows -> align to 16
        [InlineData(4096, 32)]  // 32 rows exact
        public void CalcEntryHeight_MatchesWFFormula(int length, int expected)
        {
            Assert.Equal(expected, MapTileAnimation1ImageCore.CalcEntryHeight(length));
        }

        [Fact]
        public void ImportEntryImage_KeepsLengthAuthoritative_RepointsP4()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint lenBefore = rom.u16(ENTRY_ADDR + 2);
                uint p4Before = rom.u32(ENTRY_ADDR + 4);
                byte[] rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H);

                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, SHEET_H, PAL_ADDR, 0);

                Assert.Equal("", err);
                // +2 length is UNCHANGED (single-import parity).
                Assert.Equal(lenBefore, rom.u16(ENTRY_ADDR + 2));
                // +4 repointed to a fresh pointer.
                uint p4After = rom.u32(ENTRY_ADDR + 4);
                Assert.True(U.isPointer(p4After), $"P4 not a pointer: 0x{p4After:X08}");
                Assert.NotEqual(p4Before, p4After);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_RoundTrips_RawBytesByteIdentical()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, SHEET_H, PAL_ADDR, 0);
                Assert.Equal("", err);

                // The RAW bytes written at the new +4 must equal the bytes we'd
                // encode independently (decode -> encode -> ROM round-trip).
                byte[] palette = ImageUtilCore.GetPalette(rom, PAL_ADDR, 16);
                byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, SHEET_W, SHEET_H, palette, 16);
                byte[] expected = ImageImportCore.EncodeDirectTiles4bpp(indexed, SHEET_W, SHEET_H);

                uint newP4 = U.toOffset(rom.u32(ENTRY_ADDR + 4));
                byte[] actual = rom.getBinaryData(newP4, expected.Length);
                Assert.Equal(expected, actual);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_WrongSize_NoMutation()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // Height 16 does NOT match CalcHeight(256, SHEET_LEN)=8 -> reject.
                byte[] rgba = MakeRgbaFromPalette(SHEET_W, 16);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, 16, PAL_ADDR, 0);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_WrongWidth_NoMutation()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // Width must be exactly 256.
                byte[] rgba = MakeRgbaFromPalette(128, SHEET_H);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, 128, SHEET_H, PAL_ADDR, 0);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_NoFreeSpace_RestoresByteIdentical()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                // A ROM at the 32MB cap fully packed with a NON-free byte (0x01)
                // forces FindFreeSpace AND AppendToRomEnd (already at the cap) to
                // fail -> snapshot restore. (FindFreeSpace treats 0x00 and 0xFF
                // runs as free, so the filler must be neither.)
                var rom = new ROM();
                byte[] data = new byte[0x2000000]; // 32 MiB at the cap
                Array.Fill(data, (byte)0x01);
                // entry + palette at the start so the import gets past validation.
                data[ENTRY_ADDR + 2] = (byte)(SHEET_LEN & 0xFF);
                data[ENTRY_ADDR + 3] = (byte)((SHEET_LEN >> 8) & 0xFF);
                data[ENTRY_ADDR + 4] = 0x00; data[ENTRY_ADDR + 5] = 0x08;
                data[ENTRY_ADDR + 6] = 0x00; data[ENTRY_ADDR + 7] = 0x08;
                for (int i = 0; i < 16; i++)
                {
                    ushort c = (ushort)(i | (i << 5) | (i << 10));
                    data[PAL_ADDR + i * 2] = (byte)(c & 0xFF);
                    data[PAL_ADDR + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
                }
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                byte[] rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, SHEET_H, PAL_ADDR, 0);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical restore
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderEntryImage_ValidEntry_ReturnsImage()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                uint p4 = rom.u32(ENTRY_ADDR + 4);
                var img = MapTileAnimation1ImageCore.RenderEntryImage(rom, p4, SHEET_LEN, PAL_ADDR, 0);
                Assert.NotNull(img);
                Assert.Equal(256, img.Width);
                Assert.Equal(8, img.Height);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderEntryImage_BadPointer_ReturnsNull()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // 0 pointer / out-of-range -> null, never throws.
                Assert.Null(MapTileAnimation1ImageCore.RenderEntryImage(rom, 0, SHEET_LEN, PAL_ADDR, 0));
                Assert.Null(MapTileAnimation1ImageCore.RenderEntryImage(rom, 0xFFFFFFFF, SHEET_LEN, PAL_ADDR, 0));
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
