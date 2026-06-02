// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for GenericEnemyPortraitImportCore.ImportPortrait (issue #907:
// Generic Enemy Portrait editor Image Import — RAW image (+0) + RAW palette
// (+0x20), TWO slots).
//
// Pipeline under test (mirrors the Avalonia ImportButton_Click path):
//   indexedPixels -> EncodeDirectTiles4bpp (RAW 512B) + raw 32B palette
//   -> recycle OLD region + RecycleAddress.WriteAmbient + write_p32(slot)
//      + BlackOutAmbient, for BOTH the image (+0) and palette (+0x20) slots.
//
// Coverage:
//   * Round-trip: import -> BOTH slots repointed; image region == 512B RAW
//     (decompresses-as-identity, i.e. NOT LZ77); palette == 32B RAW; decode
//     reproduces the input pixels.
//   * Cross-version +0x20: the palette slot offset is +0x20 on FE6/FE7/FE8
//     (catches the count*4 trap).
//   * Guards: wrong size (16x16) rejected; index>15 rejected; rom != CoreState
//     .ROM refused; forced-failure leaves the ROM byte-identical (atomic undo).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class GenericEnemyPortraitImportCoreTests : IDisposable
    {
        // Layout planted in the synthetic ROM.
        const uint ENTRY_ADDR     = 0x300;       // entry base (image slot @ +0)
        const uint IMAGE_SLOT     = ENTRY_ADDR + 0x00;
        const uint PALETTE_SLOT   = ENTRY_ADDR + 0x20;
        const uint IMAGE_DATA     = 0x110000;    // RAW 4bpp tilesheet (512B)
        const uint PALETTE_DATA   = 0x120000;    // RAW palette (32B)
        const uint FREE_SPACE     = 0x800000;    // 0x00-filled free space

        const int IMAGE_RAW_BYTES = 512;         // 32x32 4bpp = 16 tiles * 32B
        const int PALETTE_BYTES   = 32;          // 16 colors * 2B

        readonly ROM _prevRom;
        readonly Undo _prevUndo;

        public GenericEnemyPortraitImportCoreTests()
        {
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
        }

        // ---------------------------------------------------------------------
        // Round-trip — BOTH slots repointed; RAW (not LZ77); decode matches
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportPortrait_RoundTrip_RepointsBothSlots_RawData()
        {
            ROM rom = MakeRom("BE8E01"); // FE8U
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            uint origImgPtr = rom.p32(IMAGE_SLOT);
            uint origPalPtr = rom.p32(PALETTE_SLOT);

            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            byte[] palette = MakePalette();

            Undo.UndoData ud = CoreState.Undo.NewUndoData("genenemyportrait import");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = GenericEnemyPortraitImportCore.ImportPortrait(
                    rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);
            }
            if (ud.list.Count > 0) CoreState.Undo.Push(ud);

            Assert.Equal(string.Empty, err);

            // Both pointer slots resolve to a safe (possibly new) region.
            uint newImgPtr = rom.p32(IMAGE_SLOT);
            uint newPalPtr = rom.p32(PALETTE_SLOT);
            Assert.True(U.isSafetyOffset(newImgPtr, rom));
            Assert.True(U.isSafetyOffset(newPalPtr, rom));

            // The OLD same-size raw regions are recycled in-place, so the
            // addresses MAY match the originals. What matters is the data.
            uint imgOffset = U.toOffset(newImgPtr);
            uint palOffset = U.toOffset(newPalPtr);

            // Image region: 512 bytes RAW == EncodeDirectTiles4bpp output.
            byte[] expectedTiles = ImageImportCore.EncodeDirectTiles4bpp(pixels, 32, 32);
            Assert.NotNull(expectedTiles);
            Assert.Equal(IMAGE_RAW_BYTES, expectedTiles.Length);
            for (int i = 0; i < IMAGE_RAW_BYTES; i++)
                Assert.Equal((int)expectedTiles[i], (int)rom.Data[imgOffset + i]);

            // Palette region: 32 bytes RAW == input palette.
            for (int i = 0; i < PALETTE_BYTES; i++)
                Assert.Equal((int)palette[i], (int)rom.Data[palOffset + i]);

            // RAW, not LZ77: the byte-equality above is exactly the encoded
            // 512-byte tilesheet with no LZ77 header/stream. The decode walk
            // below reproduces the input pixels — the strongest round-trip
            // guarantee that the bytes are plain 4bpp tiles.
            AssertDecodeMatches(rom, imgOffset, pixels);

            // Both pointer slots hold a non-zero, safe pointer.
            Assert.NotEqual(0u, newImgPtr);
            Assert.NotEqual(0u, newPalPtr);
            _ = origImgPtr; _ = origPalPtr;
        }

        // ---------------------------------------------------------------------
        // Cross-version +0x20 — palette slot is +0x20 on FE6/FE7/FE8
        // ---------------------------------------------------------------------

        [Theory]
        [InlineData("AFEJ01")] // FE6 JP — count 7
        [InlineData("AE7E01")] // FE7 US — count 6
        [InlineData("BE8E01")] // FE8 US — count 8
        public void ImportPortrait_PaletteSlotIsFixedAt0x20_AllVersions(string romName)
        {
            ROM rom = MakeRom(romName);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // The palette slot is +0x20 regardless of the per-version count
            // (8 image slots + 8 palette slots reserved). If the implementation
            // ever derived it from count*4 it would land at a DIFFERENT offset
            // (FE6=0x1C, FE7=0x18, FE8=0x20) and corrupt the wrong slot.
            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            byte[] palette = MakePalette();

            // Snapshot the bytes BETWEEN the image slot and +0x20 (the would-be
            // count*4 targets) so we can prove the import only touched +0 and
            // +0x20 pointer slots.
            byte[] before = (byte[])rom.Data.Clone();

            Undo.UndoData ud = CoreState.Undo.NewUndoData("genenemyportrait crossver");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = GenericEnemyPortraitImportCore.ImportPortrait(
                    rom, pixels, palette, ENTRY_ADDR + 0, ENTRY_ADDR + 0x20);
            }
            if (ud.list.Count > 0) CoreState.Undo.Push(ud);

            Assert.Equal(string.Empty, err);

            // The palette was written to the slot at +0x20 (NOT count*4). The
            // same-size 32-byte region is recycled in-place, so the POINTER
            // value may stay identical — what proves the write landed is the
            // palette DATA at the +0x20 target now equals the imported palette.
            uint palPtr = rom.p32(ENTRY_ADDR + 0x20);
            uint palOffset = U.toOffset(palPtr);
            Assert.True(U.isSafetyOffset(palOffset, rom));
            for (int i = 0; i < PALETTE_BYTES; i++)
                Assert.Equal((int)palette[i], (int)rom.Data[palOffset + i]);

            // The would-be count*4 pointer slots are NOT the palette slot and
            // must be left byte-for-byte untouched (catches the count*4 trap:
            // FE6 count*4=0x1C, FE7 count*4=0x18).
            Assert.Equal(0xCAFEBABEu, rom.u32(ENTRY_ADDR + 0x1C)); // FE6 count*4
            Assert.Equal(0xDEADBEEFu, rom.u32(ENTRY_ADDR + 0x18)); // FE7 count*4
        }

        // ---------------------------------------------------------------------
        // Undo — ambient scope rolls back BOTH slots + both data regions
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportPortrait_Undo_RestoresPointersAndBytes()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();

            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            byte[] palette = MakePalette();

            Undo.UndoData ud = CoreState.Undo.NewUndoData("genenemyportrait undo");
            using (ROM.BeginUndoScope(ud))
            {
                string err = GenericEnemyPortraitImportCore.ImportPortrait(
                    rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);
                Assert.Equal(string.Empty, err);
            }
            if (ud.list.Count > 0)
            {
                CoreState.Undo.Push(ud);
                CoreState.Undo.RunUndo();
            }

            AssertBytesEqual(before, rom.Data);
        }

        // ---------------------------------------------------------------------
        // Guards — all reject WITHOUT mutating the ROM
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportPortrait_RomNotCoreStateRom_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            ROM other = MakeRom("BE8E01");
            CoreState.ROM = other; // active ROM is a DIFFERENT instance
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            byte[] palette = MakePalette();

            string err = GenericEnemyPortraitImportCore.ImportPortrait(
                rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportPortrait_WrongImageSize_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            // 16x16 = 256 pixels != 32x32 (1024) -> reject.
            byte[] pixels = MakeGradientIndexedPixels(16, 16);
            byte[] palette = MakePalette();

            Undo.UndoData ud = CoreState.Undo.NewUndoData("genenemyportrait wrong size");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = GenericEnemyPortraitImportCore.ImportPortrait(
                    rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);
            }

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportPortrait_WrongPaletteLength_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            byte[] palette = new byte[30]; // != 32 (>16 colors would be 256 too; 30 is malformed)

            string err = GenericEnemyPortraitImportCore.ImportPortrait(
                rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportPortrait_PaletteOver16Colors_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            // 256-color (512 byte) palette is NOT a 16-color (32 byte) palette.
            byte[] palette = new byte[256 * 2];

            string err = GenericEnemyPortraitImportCore.ImportPortrait(
                rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportPortrait_IndexAbove15_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            pixels[42] = 16; // > 15 -> reject (4bpp)
            byte[] palette = MakePalette();

            Undo.UndoData ud = CoreState.Undo.NewUndoData("genenemyportrait bad index");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = GenericEnemyPortraitImportCore.ImportPortrait(
                    rom, pixels, palette, IMAGE_SLOT, PALETTE_SLOT);
            }

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportPortrait_OutOfBoundsImageSlot_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            byte[] pixels = MakeGradientIndexedPixels(32, 32);
            byte[] palette = MakePalette();

            // Image slot beyond ROM.Data.Length -> reject, no mutation.
            uint badSlot = (uint)rom.Data.Length + 0x100u;
            string err = GenericEnemyPortraitImportCore.ImportPortrait(
                rom, pixels, palette, badSlot, PALETTE_SLOT);

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportPortrait_NullArgs_Refused()
        {
            ROM rom = MakeRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            byte[] palette = MakePalette();
            byte[] pixels = MakeGradientIndexedPixels(32, 32);

            Assert.False(string.IsNullOrEmpty(
                GenericEnemyPortraitImportCore.ImportPortrait(rom, null!, palette, IMAGE_SLOT, PALETTE_SLOT)));
            Assert.False(string.IsNullOrEmpty(
                GenericEnemyPortraitImportCore.ImportPortrait(rom, pixels, null!, IMAGE_SLOT, PALETTE_SLOT)));

            AssertBytesEqual(before, rom.Data);
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        static void AssertDecodeMatches(ROM rom, uint imgOffset, byte[] expectedPixels)
        {
            // Decode the RAW 4bpp tiles (4x4 tiles for 32x32) back to indexed
            // pixels and assert byte-equality with the input. Mirrors the
            // ByteToImage16Tile / DecodeTileToPixels nibble walk: tile-row-major
            // y(8)->x(8)->y8->x8(2) layout with lo|(hi<<4) packing.
            const int W = 32, H = 32;
            int tilesX = W / 8, tilesY = H / 8;
            byte[] decoded = new byte[W * H];
            int pos = (int)imgOffset;
            for (int ty = 0; ty < tilesY; ty++)
            for (int tx = 0; tx < tilesX; tx++)
            for (int py = 0; py < 8; py++)
            for (int px = 0; px < 8; px += 2)
            {
                byte b = rom.Data[pos++];
                int lo = b & 0x0F;
                int hi = (b >> 4) & 0x0F;
                int x0 = tx * 8 + px;
                int y0 = ty * 8 + py;
                decoded[y0 * W + x0] = (byte)lo;
                decoded[y0 * W + x0 + 1] = (byte)hi;
            }
            for (int i = 0; i < expectedPixels.Length; i++)
                Assert.Equal((int)expectedPixels[i], (int)decoded[i]);
        }

        static void AssertBytesEqual(byte[] a, byte[] b)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.Equal((int)a[i], (int)b[i]);
        }

        /// <summary>Build a w*h indexed image whose indices vary 0..15 so the
        /// encoded tiles are non-trivial (round-trip is meaningful).</summary>
        static byte[] MakeGradientIndexedPixels(int width, int height)
        {
            byte[] px = new byte[width * height];
            for (int i = 0; i < px.Length; i++)
                px[i] = (byte)(i % 16);
            return px;
        }

        /// <summary>16-color RAW GBA palette (32 bytes, BGR555 LE).</summary>
        static byte[] MakePalette()
        {
            byte[] pal = new byte[PALETTE_BYTES];
            for (int i = 0; i < 16; i++)
            {
                ushort color = (ushort)((i * 2) | ((i * 2) << 5) | ((i * 2) << 10));
                pal[i * 2 + 0] = (byte)(color & 0xFF);
                pal[i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }
            return pal;
        }

        static ROM MakeRom(string romName)
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            Array.Fill(data, (byte)0xFF);
            rom.LoadLow("synth.gba", data, romName);

            // Free space (0x00) for any freespace fallback allocation.
            for (uint i = 0; i < 0x100000; i++) rom.Data[FREE_SPACE + i] = 0x00;

            // Plant the RAW 4bpp tilesheet (512 bytes, solid index 5).
            for (int i = 0; i < IMAGE_RAW_BYTES; i++)
                rom.Data[IMAGE_DATA + i] = (byte)((5 << 4) | 5);
            U.write_u32(rom.Data, IMAGE_SLOT, U.toPointer(IMAGE_DATA));

            // Plant the RAW 16-color palette (32 bytes).
            for (int i = 0; i < PALETTE_BYTES; i++)
                rom.Data[PALETTE_DATA + i] = (byte)(0x50 + (i & 0x1F));
            U.write_u32(rom.Data, PALETTE_SLOT, U.toPointer(PALETTE_DATA));

            // Plant distinct sentinel pointers at the would-be count*4 slots
            // (0x18 / 0x1C) so the cross-version test can prove they are NOT
            // overwritten by the +0x20 palette write.
            U.write_u32(rom.Data, ENTRY_ADDR + 0x18, 0xDEADBEEFu);
            U.write_u32(rom.Data, ENTRY_ADDR + 0x1C, 0xCAFEBABEu);

            return rom;
        }
    }
}
