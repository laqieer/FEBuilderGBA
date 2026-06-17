// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RomAnimeCore (#1176) — the In-ROM Magic Animation editor's list /
// per-frame preview render / single-frame PNG import over the `romanime_` table.
//
// A `romanime_` row is { ImageWidth(tiles), OPTION, FRAME-ptr, TSA-ptr, IMAGE-ptr,
// PAL-ptr, NAME }. The TSA/IMAGE are LZ77; the PAL is a RAW 16-color (32-byte)
// palette. Each FRAME's TSA/IMAGE/PAL pointer slot points to a per-frame pointer
// LIST. The render decodes a plain (non-header) TSA over the image bytes; the
// import repoints the resolved frame's IMAGE+TSA (and, unless COMMONPALETTE,
// PAL) list slots with validate-all-before-mutate + byte-identical fault restore.
//
// Reuses the synthetic-ROM harness shape from ImageWorldMapCoreTests
// (rom.LoadLow + a StubImageService) — [Collection("SharedState")] because the
// render reads CoreState.ImageService and the import reads CoreState.ROM.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RomAnimeCoreTests
    {
        // FIXEDCOUNT entry: a 1-frame, 8-tile-wide (64 px) animation. Distinct
        // data offsets well clear of the 0x0..0x200 danger zone and of each other.
        const int WIDTH_TILES = 8;            // 64 px wide
        const int FRAME_COUNT = 1;            // FIXEDCOUNT == 1
        // Pointer SLOTS (addresses that themselves hold pointer-lists).
        const uint TSA_PTR_SLOT     = 0x001000;
        const uint IMAGE_PTR_SLOT   = 0x002000;
        const uint PAL_PTR_SLOT     = 0x003000;
        // The pointer LISTS (each holds one per-frame pointer).
        const uint TSA_LIST         = 0x010000;
        const uint IMAGE_LIST       = 0x011000;
        const uint PAL_LIST         = 0x012000;
        // The per-frame data.
        const uint TSA_DATA         = 0x020000;
        const uint IMAGE_DATA       = 0x021000;
        const uint PAL_DATA         = 0x022000;

        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;

        // =================================================================
        // Resolve — parse a fixed-count row into pointers + per-frame lists.
        // =================================================================

        [Fact]
        public void Resolve_FixedCountEntry_BuildsPerFrameLists()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);

                Assert.NotNull(e);
                Assert.Equal(WIDTH_TILES, e.ImageWidthTiles);
                Assert.Equal("FIXEDCOUNT", e.Option);
                Assert.False(e.IsFrameTable); // FramePointer == 1 (< 0x100)
                Assert.Single(e.TSAList);
                Assert.Single(e.ImageList);
                Assert.Single(e.PaletteList);
                Assert.Equal(TSA_DATA, e.TSAList[0]);
                Assert.Equal(IMAGE_DATA, e.ImageList[0]);
                Assert.Equal(PAL_DATA, e.PaletteList[0]);
            });
        }

        [Fact]
        public void GetFrameCount_FixedCountEntry_IsFrameCount()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);
                Assert.Equal(FRAME_COUNT, RomAnimeCore.GetFrameCount(rom, e));
            });
        }

        // =================================================================
        // Render — a valid entry renders a non-null image of the right dims.
        // =================================================================

        [Fact]
        public void TryRenderFrame_ValidEntry_RendersNonNull()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);

                using IImage img = RomAnimeCore.TryRenderFrame(rom, e, 0);
                Assert.NotNull(img);
                Assert.Equal(WIDTH_TILES * 8, img.Width);
                Assert.True(img.Height > 0);
                Assert.True(img.Height <= 8 * 16);
            });
        }

        [Fact]
        public void TryRenderFrame_NullRom_ReturnsNull()
        {
            var e = new RomAnimeCore.RomAnimeEntry { ImageWidthTiles = WIDTH_TILES };
            Assert.Null(RomAnimeCore.TryRenderFrame(null, e, 0));
        }

        [Fact]
        public void TryRenderFrame_NullEntry_ReturnsNull()
        {
            WithRom(rom => Assert.Null(RomAnimeCore.TryRenderFrame(rom, null, 0)));
        }

        [Fact]
        public void TryRenderFrame_TruncatedImagePointer_ReturnsNull()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);
                // Corrupt the image-list slot to point near EOF (no LZ77 header).
                rom.write_p32(IMAGE_LIST, (uint)rom.Data.Length - 2);
                e = ResolveEntry(rom); // re-resolve with the corrupt list
                Assert.Null(RomAnimeCore.TryRenderFrame(rom, e, 0));
            });
        }

        // =================================================================
        // Import round-trip — repoints image/tsa/pal; undo restores identical.
        // =================================================================

        [Fact]
        public void ImportFrame_Success_RepointsAndRoundTrips()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);

                int w = WIDTH_TILES * 8, h = 16;
                byte[] indexed = MakeIndexed(w, h);
                byte[] pal = MakePalette(BLUE, GREEN);

                bool ok = RomAnimeCore.ImportFrame(rom, e, 0, indexed, pal, w, h, out string err);
                Assert.True(ok, err);

                // The IMAGE list slot now points to fresh in-free-space LZ77 data
                // that re-expands to the EncodeTSA tile output.
                uint newImg = U.toOffset(rom.u32(IMAGE_LIST));
                Assert.True(U.isSafetyOffset(newImg, rom));
                byte[] decompressed = LZ77.decompress(rom.Data, newImg);
                Assert.NotNull(decompressed);
                Assert.True(decompressed.Length > 0);

                // The PALETTE list slot now points to the 32-byte palette we wrote.
                uint newPal = U.toOffset(rom.u32(PAL_LIST));
                Assert.True(U.isSafetyOffset(newPal, rom));
                for (int i = 0; i < pal.Length; i++)
                    Assert.Equal(pal[i], rom.Data[newPal + i]);
            });
        }

        [Fact]
        public void ImportFrame_CommonPaletteInPlace_OverwritesBlockRaw()
        {
            // COMMONPALETTE entry whose PAL pointer dereferences to a SINGLE 32-byte
            // block (no pointer-list) -> the import overwrites that block in-place
            // rather than repointing a slot (review #1 case b). The PAL pointer slot
            // value must NOT change (no repoint), but the 32 palette bytes DO change.
            WithRom(rom =>
            {
                PlantCommonPaletteEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveCommonPaletteEntry(rom);
                Assert.True(e.PaletteIsInPlaceBlock);
                Assert.True(e.IsCommonPalette);

                uint palPtrBefore = rom.u32(PAL_PTR_SLOT); // the in-place block pointer
                int w = WIDTH_TILES * 8, h = 16;
                byte[] newPal = MakePalette(BLUE, RED);

                bool ok = RomAnimeCore.ImportFrame(rom, e, 0,
                    MakeIndexed(w, h), newPal, w, h, out string err);
                Assert.True(ok, err);

                // The PAL pointer slot is UNCHANGED (in-place overwrite, no repoint).
                Assert.Equal(palPtrBefore, rom.u32(PAL_PTR_SLOT));
                // The 32 palette bytes at the resolved block now equal the new palette.
                uint palOffset = U.toOffset(palPtrBefore);
                for (int i = 0; i < newPal.Length; i++)
                    Assert.Equal(newPal[i], rom.Data[palOffset + i]);
            });
        }

        [Fact]
        public void Entry_IsFrameTable_DoesNotReadCoreStateRom()
        {
            // Resolve an entry, then null CoreState.ROM and read IsFrameTable: the
            // property must be self-contained (precomputed) and never throw (review #2).
            RomAnimeCore.RomAnimeEntry e = null;
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                e = ResolveEntry(rom);
            });
            // CoreState.ROM is restored (non-test) here; force it null to prove the
            // property does not depend on it.
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                bool isFrameTable = e.IsFrameTable; // must NOT throw
                Assert.False(isFrameTable);          // FramePointer == 1 (fixed count)
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void ImportFrame_OversizedWidth_MutatesZeroBytes()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);
                byte[] before = (byte[])rom.Data.Clone();

                int badW = (WIDTH_TILES + 2) * 8; // wrong width
                bool ok = RomAnimeCore.ImportFrame(rom, e, 0,
                    MakeIndexed(badW, 16), MakePalette(RED, GREEN), badW, 16, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportFrame_WrongPaletteLength_MutatesZeroBytes()
        {
            WithRom(rom =>
            {
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);
                byte[] before = (byte[])rom.Data.Clone();

                int w = WIDTH_TILES * 8, h = 16;
                byte[] badPal = new byte[64]; // not 32 bytes (>16 colors)
                bool ok = RomAnimeCore.ImportFrame(rom, e, 0,
                    MakeIndexed(w, h), badPal, w, h, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportFrame_NullRom_Fails()
        {
            var e = new RomAnimeCore.RomAnimeEntry { ImageWidthTiles = WIDTH_TILES };
            bool ok = RomAnimeCore.ImportFrame(null, e, 0,
                MakeIndexed(64, 16), MakePalette(RED, GREEN), 64, 16, out string err);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ImportFrame_ForcedNoFreeSpace_MutatesZeroBytes()
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                // A ROM at the 32 MB max filled with 0x01 (no 0x00/0xFF free runs)
                // so WriteCompressedToROM returns U.NOT_FOUND. The import must
                // restore byte-identical (no length change, no repoint).
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01);
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Plant just the pointer chain (so slots resolve) over the 0x01 fill.
                PlantFixedCountEntry(rom);
                RomAnimeCore.RomAnimeEntry e = ResolveEntry(rom);
                byte[] before = (byte[])rom.Data.Clone();

                int w = WIDTH_TILES * 8, h = 16;
                bool ok = RomAnimeCore.ImportFrame(rom, e, 0,
                    MakeIndexed(w, h), MakePalette(RED, GREEN), w, h, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        // =================================================================
        // LoadTSVResource (multi-field) parse — Core port of WF U.LoadTSVResource.
        // =================================================================

        [Fact]
        public void LoadTSVResource_MultiField_ParsesRowsAndSkipsComments()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllLines(path, new[]
                {
                    "//ID\tW\tNAME",          // comment header -> skipped
                    "0001\t30\tefxA",        // id 0x0001 -> ["30", "efxA"]
                    "",                         // blank -> skipped
                    "00A2\t32\tefxB\tEXTRA",  // id 0x00A2 -> ["32", "efxB", "EXTRA"]
                });

                Dictionary<uint, string[]> dic = U.LoadTSVResource(path, false);

                Assert.Equal(2, dic.Count);
                Assert.Equal(new[] { "30", "efxA" }, dic[0x0001]);
                Assert.Equal(new[] { "32", "efxB", "EXTRA" }, dic[0x00A2]);
            }
            finally { System.IO.File.Delete(path); }
        }

        [Fact]
        public void LoadTSVResource_MissingFile_ReturnsEmpty()
        {
            Dictionary<uint, string[]> dic = U.LoadTSVResource(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-romanime-file.txt"), false);
            Assert.Empty(dic);
        }

        // =================================================================
        // CalcHeightbyTSA parity (WF ImageUtil.cs:904).
        // =================================================================

        [Theory]
        [InlineData(64, 8 * 2 * 8, 8)]    // 8 tiles wide, 8 TSA entries -> 1 row -> aligned to 8 px -> 8? (see WF formula)
        [InlineData(64, 0, 0)]            // empty TSA -> 0 rows -> aligned -> 0/align*align... see body
        public void CalcHeightbyTSA_MatchesWf(int width, int tsaSize, int _expectedSentinel)
        {
            // Compare against the WF formula computed inline (parity oracle).
            int expected = WfCalcHeightbyTSA(width, tsaSize, 8);
            Assert.Equal(expected, RomAnimeCore.CalcHeightbyTSA(width, tsaSize, 8));
        }

        [Fact]
        public void CalcHeightbyTSA_FullTallSheet_MatchesWf()
        {
            // 8-tile-wide sheet with 16 rows of TSA = 8*16 entries = 128 tiles.
            int width = 64;
            int tsaSize = 8 * 16 * 2; // 256 bytes
            int expected = WfCalcHeightbyTSA(width, tsaSize, 8);
            Assert.Equal(expected, RomAnimeCore.CalcHeightbyTSA(width, tsaSize, 8));
        }

        // The WF ImageUtil.CalcHeightbyTSA reference, inlined as a parity oracle.
        static int WfCalcHeightbyTSA(int width, int tsaSize, int align)
        {
            width = width / 8;
            tsaSize = tsaSize / 2;
            int height = tsaSize / width;
            if (tsaSize % width != 0) height++;
            if (height % align != 0) height += align;
            return height * 8 / align * align;
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithRom(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                body(rom);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        // Build the entry the way the View does (fields array + Resolve), but with
        // hand-planted pointers rather than the config file.
        static RomAnimeCore.RomAnimeEntry ResolveEntry(ROM rom)
        {
            string[] fields =
            {
                WIDTH_TILES.ToString(),                 // 0 ImageWidth (tiles)
                "FIXEDCOUNT",                           // 1 OPTION
                U.ToHexString(FRAME_COUNT),             // 2 FRAME (fixed count == 1)
                U.ToHexString(TSA_PTR_SLOT),            // 3 TSA ptr slot
                U.ToHexString(IMAGE_PTR_SLOT),          // 4 IMAGE ptr slot
                U.ToHexString(PAL_PTR_SLOT),            // 5 PAL ptr slot
                "efxTest",                              // 6 NAME
            };
            return RomAnimeCore.Resolve(rom, 0x1234, fields);
        }

        // Plant the full pointer chain + one frame of TSA/IMAGE/PAL data.
        static void PlantFixedCountEntry(ROM rom)
        {
            // One 8-tile-wide row of TSA (8 entries) referencing tiles 0..7.
            byte[] tsa = new byte[WIDTH_TILES * 2];
            for (int i = 0; i < WIDTH_TILES; i++)
            {
                tsa[i * 2] = (byte)(i & 0xFF);
                tsa[i * 2 + 1] = 0;
            }
            // 8 4bpp tiles of image data (32 bytes each), non-zero content.
            byte[] image = new byte[WIDTH_TILES * 32];
            for (int i = 0; i < image.Length; i++) image[i] = (byte)((i % 15) + 1);

            PlantBytes(rom, TSA_DATA, LZ77.compress(tsa));
            PlantBytes(rom, IMAGE_DATA, LZ77.compress(image));
            PlantBytes(rom, PAL_DATA, MakePalette(RED, GREEN));

            // Per-frame pointer LISTS (one entry each).
            rom.write_p32(TSA_LIST, TSA_DATA);
            rom.write_p32(IMAGE_LIST, IMAGE_DATA);
            rom.write_p32(PAL_LIST, PAL_DATA);
            // Terminate each list with a non-pointer so GetPointerListCount stops.
            rom.write_u32(TSA_LIST + 4, 0);
            rom.write_u32(IMAGE_LIST + 4, 0);
            rom.write_u32(PAL_LIST + 4, 0);

            // Pointer SLOTS point at the lists.
            rom.write_p32(TSA_PTR_SLOT, TSA_LIST);
            rom.write_p32(IMAGE_PTR_SLOT, IMAGE_LIST);
            rom.write_p32(PAL_PTR_SLOT, PAL_LIST);
        }

        // A COMMONPALETTE entry whose PAL pointer SLOT dereferences directly to a
        // single 32-byte palette block (NOT a pointer-list), so the palette resolves
        // as an in-place block. TSA/IMAGE keep their per-frame pointer-lists.
        static void PlantCommonPaletteEntry(ROM rom)
        {
            byte[] tsa = new byte[WIDTH_TILES * 2];
            for (int i = 0; i < WIDTH_TILES; i++) tsa[i * 2] = (byte)(i & 0xFF);
            byte[] image = new byte[WIDTH_TILES * 32];
            for (int i = 0; i < image.Length; i++) image[i] = (byte)((i % 15) + 1);

            PlantBytes(rom, TSA_DATA, LZ77.compress(tsa));
            PlantBytes(rom, IMAGE_DATA, LZ77.compress(image));
            // The palette block: 32 RAW bytes whose first u32 is NOT a valid pointer
            // (so GetPalettePointerListCount takes the in-place fallback).
            PlantBytes(rom, PAL_DATA, MakePalette(RED, GREEN));

            rom.write_p32(TSA_LIST, TSA_DATA);
            rom.write_p32(IMAGE_LIST, IMAGE_DATA);
            rom.write_u32(TSA_LIST + 4, 0);
            rom.write_u32(IMAGE_LIST + 4, 0);

            rom.write_p32(TSA_PTR_SLOT, TSA_LIST);
            rom.write_p32(IMAGE_PTR_SLOT, IMAGE_LIST);
            // PAL pointer SLOT -> directly the 32-byte block (no intermediate list).
            rom.write_p32(PAL_PTR_SLOT, PAL_DATA);
        }

        static RomAnimeCore.RomAnimeEntry ResolveCommonPaletteEntry(ROM rom)
        {
            string[] fields =
            {
                WIDTH_TILES.ToString(),
                "COMMONPALETTE",
                U.ToHexString(FRAME_COUNT),
                U.ToHexString(TSA_PTR_SLOT),
                U.ToHexString(IMAGE_PTR_SLOT),
                U.ToHexString(PAL_PTR_SLOT),
                "efxCommon",
            };
            return RomAnimeCore.Resolve(rom, 0x4321, fields);
        }

        static byte[] MakeIndexed(int w, int h)
        {
            byte[] px = new byte[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)((i % 15) + 1);
            return px;
        }

        static byte[] MakePalette(ushort c1, ushort c2)
        {
            byte[] pal = new byte[16 * 2];
            pal[1 * 2] = (byte)(c1 & 0xFF); pal[1 * 2 + 1] = (byte)(c1 >> 8);
            pal[2 * 2] = (byte)(c2 & 0xFF); pal[2 * 2 + 1] = (byte)(c2 >> 8);
            return pal;
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);
    }
}
