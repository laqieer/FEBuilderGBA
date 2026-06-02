// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for TSAImageImportCore.ImportTSAImage (issue #901: TSA editor
// "Main Image" import — tilesheet-only).
//
// Pipeline under test (mirrors the Avalonia MainImageImport_Click path):
//   indexedPixels -> EncodeDirectTiles4bpp -> LZ77.compress
//   -> RecycleAddress.WriteAmbient + write_p32(ZImg) + BlackOutAmbient
//
// Coverage:
//   * Round-trip: import -> LZ77.decompress at the new ZImg addr ->
//     ByteToImage16Tile-equivalent raw tiles reproduce the input.
//   * PARITY GUARD: TSA region + palette region are BYTE-IDENTICAL after.
//   * Undo: ambient scope rolls back the tile region AND the ZImg pointer.
//   * Guards: rom != CoreState.ROM, wrong-size, index>15 — all rejected with
//     NO ROM mutation.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class TSAImageImportCoreTests : IDisposable
    {
        // Layout planted in the synthetic ROM.
        const uint ZIMG_POINTER   = 0x300;      // pointer SLOT for the tilesheet (>= 0x200)
        const uint TSA_POINTER     = 0x304;      // pointer SLOT for TSA (parity guard)
        const uint PALETTE_POINTER = 0x308;      // pointer SLOT for palette (parity guard)
        const uint ZIMG_DATA       = 0x110000;   // LZ77 tilesheet data
        const uint TSA_DATA        = 0x120000;   // TSA data region (never written)
        const uint PALETTE_DATA    = 0x130000;   // palette data region (never written)
        const uint FREE_SPACE      = 0x800000;   // 0x00-filled free space

        readonly ROM _prevRom;
        readonly Undo _prevUndo;

        public TSAImageImportCoreTests()
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
        // Round-trip
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportTSAImage_RoundTrip_DecompressedTilesMatch()
        {
            ROM rom = MakeRom(tileCount: 4); // 4 tiles -> (32, 8)
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            int w = 32, h = 8;
            byte[] pixels = MakeGradientIndexedPixels(w, h);

            Undo.UndoData ud = CoreState.Undo.NewUndoData("tsa import roundtrip");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);
            }
            if (ud.list.Count > 0) CoreState.Undo.Push(ud);

            Assert.Equal(string.Empty, err);

            // The ZImg pointer resolves to the freshly-written tilesheet. The
            // old region is recycled (WF WriteImageData in-place recycle), so
            // the address MAY equal the original — what matters is that the
            // decompressed bytes round-trip to the encoded input.
            uint newAddr = rom.p32(ZIMG_POINTER);
            Assert.True(U.isSafetyOffset(newAddr, rom));

            byte[] decompressed = LZ77.decompress(rom.Data, newAddr);
            Assert.NotNull(decompressed);

            byte[] expected = ImageImportCore.EncodeDirectTiles4bpp(pixels, w, h);
            Assert.NotNull(expected);
            Assert.Equal(expected.Length, decompressed.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal((int)expected[i], (int)decompressed[i]);
        }

        // ---------------------------------------------------------------------
        // PARITY GUARD — TSA + palette regions byte-identical after import
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportTSAImage_DoesNotTouchTSAOrPalette()
        {
            ROM rom = MakeRom(tileCount: 4);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            // Snapshot the TSA + palette pointer SLOTS and their DATA regions.
            uint tsaPtrBefore = rom.u32(TSA_POINTER);
            uint palPtrBefore = rom.u32(PALETTE_POINTER);
            byte[] tsaBefore = Snapshot(rom, TSA_DATA, 0x40);
            byte[] palBefore = Snapshot(rom, PALETTE_DATA, 0x20);

            int w = 32, h = 8;
            byte[] pixels = MakeGradientIndexedPixels(w, h);

            Undo.UndoData ud = CoreState.Undo.NewUndoData("tsa parity guard");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);
            }
            if (ud.list.Count > 0) CoreState.Undo.Push(ud);

            Assert.Equal(string.Empty, err);

            // Pointer slots unchanged.
            Assert.Equal(tsaPtrBefore, rom.u32(TSA_POINTER));
            Assert.Equal(palPtrBefore, rom.u32(PALETTE_POINTER));

            // Data regions byte-identical.
            AssertBytesEqual(tsaBefore, Snapshot(rom, TSA_DATA, 0x40));
            AssertBytesEqual(palBefore, Snapshot(rom, PALETTE_DATA, 0x20));
        }

        // ---------------------------------------------------------------------
        // Undo — ambient scope rolls back tile region + ZImg pointer
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportTSAImage_Undo_RestoresPointerAndBytes()
        {
            ROM rom = MakeRom(tileCount: 4);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            uint origPtr = rom.p32(ZIMG_POINTER);
            byte[] origTiles = Snapshot(rom, ZIMG_DATA, 0x40);

            int w = 32, h = 8;
            byte[] pixels = MakeGradientIndexedPixels(w, h);

            Undo.UndoData ud = CoreState.Undo.NewUndoData("tsa import undo");
            using (ROM.BeginUndoScope(ud))
            {
                string err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);
                Assert.Equal(string.Empty, err);
            }
            if (ud.list.Count > 0)
            {
                CoreState.Undo.Push(ud);
                CoreState.Undo.RunUndo();
            }

            // Pointer restored.
            Assert.Equal(origPtr, rom.p32(ZIMG_POINTER));
            // Original tilesheet bytes restored.
            AssertBytesEqual(origTiles, Snapshot(rom, ZIMG_DATA, 0x40));
        }

        // ---------------------------------------------------------------------
        // Guards — all reject WITHOUT mutating the ROM
        // ---------------------------------------------------------------------

        [Fact]
        public void ImportTSAImage_RomNotCoreStateRom_Refused()
        {
            ROM rom = MakeRom(tileCount: 4);
            ROM other = MakeRom(tileCount: 4);
            CoreState.ROM = other;      // active ROM is a DIFFERENT instance
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            int w = 32, h = 8;
            byte[] pixels = MakeGradientIndexedPixels(w, h);

            string err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportTSAImage_WrongSize_Refused()
        {
            ROM rom = MakeRom(tileCount: 4); // existing tilesheet is (32, 8)
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            // 8x8 != existing (32, 8) -> reject.
            int w = 8, h = 8;
            byte[] pixels = MakeGradientIndexedPixels(w, h);

            Undo.UndoData ud = CoreState.Undo.NewUndoData("tsa wrong size");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);
            }

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportTSAImage_IndexAbove15_Refused()
        {
            ROM rom = MakeRom(tileCount: 4);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            int w = 32, h = 8;
            byte[] pixels = MakeGradientIndexedPixels(w, h);
            pixels[10] = 16; // > 15 -> reject

            Undo.UndoData ud = CoreState.Undo.NewUndoData("tsa bad index");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);
            }

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        [Fact]
        public void ImportTSAImage_PixelLengthMismatch_Refused()
        {
            ROM rom = MakeRom(tileCount: 4);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] before = (byte[])rom.Data.Clone();
            int w = 32, h = 8;
            byte[] pixels = new byte[w * h - 1]; // wrong length

            string err = TSAImageImportCore.ImportTSAImage(rom, pixels, w, h, ZIMG_POINTER);

            Assert.False(string.IsNullOrEmpty(err));
            AssertBytesEqual(before, rom.Data);
        }

        // ---------------------------------------------------------------------
        // TryCalcTilesheetSize — derives the natural (w, h) from the ZImg slot
        // ---------------------------------------------------------------------

        [Fact]
        public void TryCalcTilesheetSize_FourTiles_Returns32x8()
        {
            ROM rom = MakeRom(tileCount: 4);
            CoreState.ROM = rom;
            bool ok = TSAImageImportCore.TryCalcTilesheetSize(rom, ZIMG_POINTER, out int w, out int h);
            Assert.True(ok);
            Assert.Equal(32, w);
            Assert.Equal(8, h);
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        static byte[] Snapshot(ROM rom, uint addr, int len)
        {
            byte[] b = new byte[len];
            Array.Copy(rom.Data, (int)addr, b, 0, len);
            return b;
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

        static ROM MakeRom(int tileCount)
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            Array.Fill(data, (byte)0xFF);
            rom.LoadLow("synth.gba", data, "BE8E01");

            // Free space (0x00) for the new compressed tilesheet.
            for (uint i = 0; i < 0x100000; i++) rom.Data[FREE_SPACE + i] = 0x00;

            // Plant the LZ77 tilesheet: `tileCount` solid tiles (index 5).
            byte[] raw = new byte[tileCount * 32];
            for (int t = 0; t < tileCount; t++)
            {
                byte packed = (byte)((5 << 4) | 5);
                for (int i = 0; i < 32; i++) raw[t * 32 + i] = packed;
            }
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, ZIMG_DATA, comp.Length);
            U.write_u32(rom.Data, ZIMG_POINTER, U.toPointer(ZIMG_DATA));

            // Plant a TSA data region + pointer (must stay untouched).
            for (uint i = 0; i < 0x40; i++) rom.Data[TSA_DATA + i] = (byte)(0xA0 + (i & 0x0F));
            U.write_u32(rom.Data, TSA_POINTER, U.toPointer(TSA_DATA));

            // Plant a 16-color palette + pointer (must stay untouched).
            for (uint i = 0; i < 0x20; i++) rom.Data[PALETTE_DATA + i] = (byte)(0x50 + (i & 0x1F));
            U.write_u32(rom.Data, PALETTE_POINTER, U.toPointer(PALETTE_DATA));

            return rom;
        }
    }
}
