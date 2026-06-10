// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the per-cell TSA editing seam (#1005) in ImageTSAEditorCore:
//   * SerializeCell bit-pack <-> DecodeTSA unpack round-trips (#1005).
//   * DecodeTsaCells (RAW + LZ77) returns the row-major cell grid.
//   * WriteTsaCells RAW in-place: edit a cell, write, re-read; the TSA byte
//     COUNT is unchanged and the pointer slot is NOT repointed.
//   * WriteTsaCells LZ77 repoint: the pointer slot now points at fresh
//     free-space data that decompresses to the edited cells.
//   * RenderMainImageFromCells differs in pixels from the unedited render.
//   * Fault paths (out-of-bounds raw region, etc.) mutate ZERO bytes.
//
// Reuses the StubImageService/StubImage (defined in BattleAnimeDetailTests.cs)
// and the synthetic-ROM harness shape from ImageTSAEditorCoreTests.cs.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageTSAEditorCellTests
    {
        const uint IMAGE_OFFSET   = 0x1000;
        const uint TSA_RAW_OFFSET = 0x4000;
        const uint TSA_LZ_OFFSET  = 0x6000;
        const uint PALETTE_OFFSET = 0x8000;

        // Pointer SLOTS that hold the resolved TSA data addresses. These mirror
        // the View's _tsaPointer (slot -> rom.p32 -> data addr) contract.
        const uint TSA_RAW_SLOT = 0x2000;
        const uint TSA_LZ_SLOT  = 0x2010;

        // GBA 5-5-5 colors used in the planted palette.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;
        const ushort WHITE = 0x7FFF;

        // -----------------------------------------------------------------
        // SerializeCell <-> DecodeTSA bit-pack round-trip (#1005).
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(0, false, false, 0)]
        [InlineData(1, false, false, 0)]
        [InlineData(0x3FF, true, true, 0xF)]
        [InlineData(0x123, true, false, 5)]
        [InlineData(0x2AB, false, true, 0xA)]
        public void SerializeCell_IsExactInverseOfDecodeTSAUnpack(
            int tileId, bool h, bool v, int bank)
        {
            ushort entry = ImageTSAEditorCore.SerializeCell(tileId, h, v, bank);

            // Re-unpack with the SAME bit layout DecodeTSA uses.
            int decTile = entry & 0x3FF;
            bool decH = (entry & 0x400) != 0;
            bool decV = (entry & 0x800) != 0;
            int decBank = (entry >> 12) & 0xF;

            Assert.Equal(tileId, decTile);
            Assert.Equal(h, decH);
            Assert.Equal(v, decV);
            Assert.Equal(bank, decBank);
        }

        [Fact]
        public void SerializeCell_MatchesTheTestHarnessCellHelper()
        {
            // Pin SerializeCell against the independent Cell() helper used by the
            // render tests (which encodes the on-ROM truth).
            for (int t = 0; t <= 0x3FF; t += 0x55)
                for (int b = 0; b <= 0xF; b += 5)
                {
                    Assert.Equal(Cell(t, true, false, b),
                        ImageTSAEditorCore.SerializeCell(t, true, false, b));
                    Assert.Equal(Cell(t, false, true, b),
                        ImageTSAEditorCore.SerializeCell(t, false, true, b));
                }
        }

        // -----------------------------------------------------------------
        // DecodeTsaCells — RAW + LZ77 row-major grid.
        // -----------------------------------------------------------------

        [Fact]
        public void DecodeTsaCells_Raw_ReturnsRowMajorGrid()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                ushort[] planted =
                {
                    Cell(1, false, false, 0), Cell(2, true, false, 1),
                    Cell(3, false, true, 2),  Cell(0, true, true, 3),
                };
                PlantRawCells(rom, TSA_RAW_OFFSET, planted);

                ushort[] cells = ImageTSAEditorCore.DecodeTsaCells(
                    rom, 2, 2, false, TSA_RAW_OFFSET);

                Assert.NotNull(cells);
                Assert.Equal(4, cells.Length);
                Assert.Equal(planted, cells);
            });
        }

        [Fact]
        public void DecodeTsaCells_Lz77_ReturnsRowMajorGrid()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                ushort[] planted =
                {
                    Cell(1, false, false, 0), Cell(2, true, false, 1),
                };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(planted)));

                ushort[] cells = ImageTSAEditorCore.DecodeTsaCells(
                    rom, 2, 1, true, TSA_LZ_OFFSET);

                Assert.NotNull(cells);
                Assert.Equal(2, cells.Length);
                Assert.Equal(planted, cells);
            });
        }

        [Fact]
        public void DecodeTsaCells_ZeroDimensions_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                Assert.Null(ImageTSAEditorCore.DecodeTsaCells(rom, 0, 2, false, TSA_RAW_OFFSET));
                Assert.Null(ImageTSAEditorCore.DecodeTsaCells(rom, 2, 0, false, TSA_RAW_OFFSET));
            });
        }

        [Fact]
        public void DecodeTsaCells_NullRom_ReturnsNull()
        {
            Assert.Null(ImageTSAEditorCore.DecodeTsaCells(null, 2, 2, false, TSA_RAW_OFFSET));
        }

        [Fact]
        public void DecodeTsaCells_CorruptTsaPointer_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                // tsaAddr at header (0) -> isSafetyOffset false.
                Assert.Null(ImageTSAEditorCore.DecodeTsaCells(rom, 2, 2, false, 0));
            });
        }

        // -----------------------------------------------------------------
        // WriteTsaCells — RAW in-place (same-size, no repoint).
        // -----------------------------------------------------------------

        [Fact]
        public void WriteTsaCells_Raw_InPlace_RoundTripsAndKeepsSlotAndCount()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                ushort[] planted =
                {
                    Cell(1, false, false, 0), Cell(1, false, false, 0),
                    Cell(1, false, false, 0), Cell(1, false, false, 0),
                };
                PlantRawCells(rom, TSA_RAW_OFFSET, planted);
                // Point the slot at the raw data so a (hypothetical) repoint
                // would be observable.
                rom.write_p32(TSA_RAW_SLOT, TSA_RAW_OFFSET);
                uint slotBefore = rom.u32(TSA_RAW_SLOT);

                // Edit cell (1,0) -> tile 2, H-flip, bank 3.
                ushort[] edited = (ushort[])planted.Clone();
                edited[1] = ImageTSAEditorCore.SerializeCell(2, true, false, 3);

                string err = ImageTSAEditorCore.WriteTsaCells(
                    rom, 2, 2, false, TSA_RAW_SLOT, TSA_RAW_OFFSET, edited);
                Assert.Equal("", err);

                // Re-read: the edited grid round-trips.
                ushort[] readBack = ImageTSAEditorCore.DecodeTsaCells(
                    rom, 2, 2, false, TSA_RAW_OFFSET);
                Assert.NotNull(readBack);
                Assert.Equal(edited, readBack);

                // Byte COUNT unchanged: the data still occupies exactly 8 bytes
                // at TSA_RAW_OFFSET (no append). The slot must be UNCHANGED
                // (raw in-place never repoints).
                Assert.Equal(slotBefore, rom.u32(TSA_RAW_SLOT));
            });
        }

        [Fact]
        public void WriteTsaCells_Raw_DoesNotGrowRom()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int lenBefore = rom.Data.Length;
                ushort[] planted = { Cell(1, false, false, 0), Cell(0, false, false, 0) };
                PlantRawCells(rom, TSA_RAW_OFFSET, planted);

                ushort[] edited = { Cell(3, true, true, 7), Cell(2, false, false, 1) };
                string err = ImageTSAEditorCore.WriteTsaCells(
                    rom, 2, 1, false, TSA_RAW_SLOT, TSA_RAW_OFFSET, edited);

                Assert.Equal("", err);
                Assert.Equal(lenBefore, rom.Data.Length); // raw in-place never resizes
            });
        }

        // -----------------------------------------------------------------
        // WriteTsaCells — LZ77 repoint.
        // -----------------------------------------------------------------

        [Fact]
        public void WriteTsaCells_Lz77_RepointsSlotToFreshData()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                ushort[] planted = { Cell(1, false, false, 0), Cell(0, false, false, 0) };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(planted)));
                rom.write_p32(TSA_LZ_SLOT, TSA_LZ_OFFSET);
                uint slotBefore = rom.u32(TSA_LZ_SLOT);

                ushort[] edited = { Cell(2, true, false, 5), Cell(3, false, true, 2) };
                string err = ImageTSAEditorCore.WriteTsaCells(
                    rom, 2, 1, true, TSA_LZ_SLOT, /*tsaDataAddr*/ TSA_LZ_OFFSET, edited);
                Assert.Equal("", err);

                // The slot must now point at FRESH free-space data (repointed).
                uint slotAfter = rom.u32(TSA_LZ_SLOT);
                Assert.NotEqual(slotBefore, slotAfter);

                // Decoding via the slot's NEW target yields the edited cells.
                uint newDataAddr = rom.p32(TSA_LZ_SLOT);
                ushort[] readBack = ImageTSAEditorCore.DecodeTsaCells(
                    rom, 2, 1, true, newDataAddr);
                Assert.NotNull(readBack);
                Assert.Equal(edited, readBack);
            });
        }

        // -----------------------------------------------------------------
        // Outer-transaction rollback: TSA write SUCCEEDS, then the palette half
        // FAILS -> the View's one undo scope rolls back to zero net mutation.
        // This is the riskiest write path (LZ77 append + repoint) — Copilot CLI
        // review on PR #1072 required an EXECUTABLE test, not just static parity.
        // -----------------------------------------------------------------

        [Fact]
        public void WriteTsaCells_Lz77_OuterScopeRollback_RestoresByteIdentical()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                ushort[] planted = { Cell(1, false, false, 0), Cell(0, false, false, 0) };
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(CellsToBytes(planted)));
                rom.write_p32(TSA_LZ_SLOT, TSA_LZ_OFFSET);

                ROM prevRom = CoreState.ROM;
                Undo prevUndo = CoreState.Undo;
                try
                {
                    var undo = new Undo();
                    CoreState.ROM = rom;
                    CoreState.Undo = undo;

                    byte[] preData = (byte[])rom.Data.Clone();
                    uint preSlot = rom.u32(TSA_LZ_SLOT);

                    var ud = new Undo.UndoData
                    {
                        time = DateTime.Now,
                        name = "WriteTSA",
                        list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                        filesize = (uint)rom.Data.Length,
                    };

                    // TSA half: LZ77 append + repoint, captured by the outer scope.
                    ushort[] edited = { Cell(2, true, false, 5), Cell(3, false, true, 2) };
                    using (ROM.BeginUndoScope(ud))
                    {
                        string err = ImageTSAEditorCore.WriteTsaCells(
                            rom, 2, 1, true, TSA_LZ_SLOT, TSA_LZ_OFFSET, edited);
                        Assert.Equal("", err);
                    }
                    Assert.NotEqual(preSlot, rom.u32(TSA_LZ_SLOT)); // repointed
                    Assert.True(rom.Data.Length >= preData.Length); // appended

                    // Palette half "fails" -> the View rolls the one scope back.
                    undo.Rollback(ud);

                    // Byte-identical restore: length, pointer slot, every byte.
                    Assert.Equal(preData.Length, rom.Data.Length);
                    Assert.Equal(preSlot, rom.u32(TSA_LZ_SLOT));
                    Assert.True(preData.AsSpan().SequenceEqual(rom.Data));
                }
                finally
                {
                    CoreState.ROM = prevRom;
                    CoreState.Undo = prevUndo;
                }
            });
        }

        // -----------------------------------------------------------------
        // RenderMainImageFromCells — an edited cell changes pixels.
        // -----------------------------------------------------------------

        [Fact]
        public void RenderMainImageFromCells_EditedCell_DiffersFromUnedited()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());          // tile 0 blank, tile 1 marker
                PlantPalette(rom, StandardPalette());

                // Original cell renders tile 0 (all index 0 -> transparent).
                ushort[] original = { Cell(0, false, false, 0) };
                IImage imgA = ImageTSAEditorCore.RenderMainImageFromCells(
                    rom, 1, 1, IMAGE_OFFSET, original, PALETTE_OFFSET);
                Assert.NotNull(imgA);

                // Edited cell renders tile 1 (green marker at (0,0)).
                ushort[] edited = { ImageTSAEditorCore.SerializeCell(1, false, false, 0) };
                IImage imgB = ImageTSAEditorCore.RenderMainImageFromCells(
                    rom, 1, 1, IMAGE_OFFSET, edited, PALETTE_OFFSET);
                Assert.NotNull(imgB);

                byte[] pxA = imgA.GetPixelData();
                byte[] pxB = imgB.GetPixelData();
                Assert.NotEqual(pxA, pxB);
                // Concretely: edited (0,0) is green-marker, original is transparent.
                AssertPixel(imgB, 0, 0, 0, 248, 0, 255);
                Assert.Equal(0, pxA[3]); // original (0,0) alpha = 0 (transparent)
            });
        }

        [Fact]
        public void RenderMainImageFromCells_NullCells_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());
                Assert.Null(ImageTSAEditorCore.RenderMainImageFromCells(
                    rom, 1, 1, IMAGE_OFFSET, null, PALETTE_OFFSET));
            });
        }

        // -----------------------------------------------------------------
        // Fault paths mutate ZERO bytes (byte-identical restore).
        // -----------------------------------------------------------------

        [Fact]
        public void WriteTsaCells_Raw_OutOfBoundsRegion_MutatesZeroBytes()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                byte[] snap = (byte[])rom.Data.Clone();
                int lenBefore = rom.Data.Length;

                // tsaDataAddr past the ROM end -> bounds check fails, NO mutation.
                uint badAddr = (uint)rom.Data.Length - 2; // only 2 bytes left, need 4
                ushort[] cells = { Cell(1, false, false, 0), Cell(2, false, false, 0) };

                string err = ImageTSAEditorCore.WriteTsaCells(
                    rom, 2, 1, false, TSA_RAW_SLOT, badAddr, cells);

                Assert.False(string.IsNullOrEmpty(err)); // returns an error
                Assert.Equal(lenBefore, rom.Data.Length);
                Assert.Equal(snap, rom.Data); // byte-identical: zero mutation
            });
        }

        [Fact]
        public void WriteTsaCells_CellCountMismatch_MutatesZeroBytes()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantRawCells(rom, TSA_RAW_OFFSET, new ushort[] { Cell(1, false, false, 0) });
                byte[] snap = (byte[])rom.Data.Clone();

                // cells.Length (3) != width8*height8 (2*1 = 2) -> rejected.
                ushort[] wrong = { Cell(1, false, false, 0), Cell(2, false, false, 0), Cell(3, false, false, 0) };
                string err = ImageTSAEditorCore.WriteTsaCells(
                    rom, 2, 1, false, TSA_RAW_SLOT, TSA_RAW_OFFSET, wrong);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(snap, rom.Data); // zero mutation
            });
        }

        [Fact]
        public void WriteTsaCells_NullCells_ReturnsErrorNoMutation()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                byte[] snap = (byte[])rom.Data.Clone();
                string err = ImageTSAEditorCore.WriteTsaCells(
                    rom, 2, 1, false, TSA_RAW_SLOT, TSA_RAW_OFFSET, null);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(snap, rom.Data);
            });
        }

        // -----------------------------------------------------------------
        // GetTilesheetTileCount — tile-id clamp source.
        // -----------------------------------------------------------------

        [Fact]
        public void GetTilesheetTileCount_ReturnsDecodedTileCount()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());  // 2 tiles
                Assert.Equal(2, ImageTSAEditorCore.GetTilesheetTileCount(rom, IMAGE_OFFSET));
            });
        }

        [Fact]
        public void GetTilesheetTileCount_CorruptPointer_ReturnsZero()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                Assert.Equal(0, ImageTSAEditorCore.GetTilesheetTileCount(rom, 0));
                Assert.Equal(0, ImageTSAEditorCore.GetTilesheetTileCount(null, IMAGE_OFFSET));
            });
        }

        // =================================================================
        // HEADER-TSA per-cell editing (#1071).
        // =================================================================

        // Build an ASYMMETRIC header-TSA stream: {mhx, mhy} + (mhx+1)*(mhy+1)
        // distinct nonzero u16 cells in stride order.
        static byte[] MakeHeaderTsa(int mhx, int mhy, int baseVal = 0x100)
        {
            int cells = (mhx + 1) * (mhy + 1);
            byte[] tsa = new byte[2 + cells * 2];
            tsa[0] = (byte)mhx;
            tsa[1] = (byte)mhy;
            for (int k = 0; k < cells; k++)
            {
                ushort v = (ushort)(baseVal + k * 7 + 1);
                tsa[2 + k * 2] = (byte)(v & 0xFF);
                tsa[2 + k * 2 + 1] = (byte)(v >> 8);
            }
            return tsa;
        }

        // The editor's stride index map for a header cell (headerx, headery).
        static int HeaderCellIndex(int mhy, int x, int y) => (mhy - y) * 32 + x;

        // -----------------------------------------------------------------
        // DecodeHeaderTsaCells — RAW + LZ77 returns the stride array + dims.
        // -----------------------------------------------------------------

        [Fact]
        public void DecodeHeaderTsaCells_Raw_ReturnsTileAndDims()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int mhx = 3, mhy = 2;
                byte[] tsa = MakeHeaderTsa(mhx, mhy);
                PlantBytes(rom, TSA_RAW_OFFSET, tsa);

                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, false, TSA_RAW_OFFSET, out int dmhx, out int dmhy);

                Assert.NotNull(tile);
                Assert.Equal(mhx, dmhx);
                Assert.Equal(mhy, dmhy);
                // The decoded tile array is canvas-sized (32*20).
                Assert.Equal(32 * 20, tile.Length);
            });
        }

        [Fact]
        public void DecodeHeaderTsaCells_Lz77_ReturnsTileAndDims()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int mhx = 2, mhy = 3;
                byte[] tsa = MakeHeaderTsa(mhx, mhy);
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(tsa));

                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, true, TSA_LZ_OFFSET, out int dmhx, out int dmhy);

                Assert.NotNull(tile);
                Assert.Equal(mhx, dmhx);
                Assert.Equal(mhy, dmhy);
            });
        }

        [Fact]
        public void DecodeHeaderTsaCells_CorruptHeader_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                // Oversized mhx => DecodeHeaderTSAToCells.IsValidHeader false.
                byte[] bad = new byte[2 + 4];
                bad[0] = 40; bad[1] = 2;
                PlantBytes(rom, TSA_RAW_OFFSET, bad);

                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, false, TSA_RAW_OFFSET, out int dmhx, out int dmhy);

                Assert.Null(tile);
            });
        }

        [Fact]
        public void DecodeHeaderTsaCells_TruncatedLz77Stream_ReturnsNull()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                // A header that claims mhx=3, mhy=2 (footprint = 2 + 4*3*2 = 26
                // bytes) but whose decompressed stream is TRUNCATED to only the
                // header + a couple of cells. DecodeHeaderTSAToCells would still
                // return a (partially-filled) valid tile, but the editor must
                // REFUSE so a later write can't synthesize bytes the source
                // never had (Copilot re-review on #1100).
                int mhx = 3, mhy = 2;
                int fullLen = 2 + (mhx + 1) * (mhy + 1) * 2; // 26
                byte[] full = MakeHeaderTsa(mhx, mhy);
                Assert.Equal(fullLen, full.Length);

                // Decompress-able stream that is SHORTER than the full footprint.
                byte[] truncated = new byte[fullLen - 8]; // header + 5 cells only
                Array.Copy(full, truncated, truncated.Length);
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(truncated));

                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, true, TSA_LZ_OFFSET, out int dmhx, out int dmhy);

                Assert.Null(tile);
            });
        }

        // -----------------------------------------------------------------
        // WriteHeaderTsaCells — RAW in-place (same-size, no growth, edit lands).
        // -----------------------------------------------------------------

        [Fact]
        public void WriteHeaderTsaCells_Raw_EditLands_NoGrowth()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int mhx = 3, mhy = 2;
                byte[] tsa = MakeHeaderTsa(mhx, mhy);
                PlantBytes(rom, TSA_RAW_OFFSET, tsa);
                rom.write_p32(TSA_RAW_SLOT, TSA_RAW_OFFSET);
                uint slotBefore = rom.u32(TSA_RAW_SLOT);
                int lenBefore = rom.Data.Length;

                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, false, TSA_RAW_OFFSET, out int dmhx, out int dmhy);
                Assert.NotNull(tile);

                // Edit header cell (1,0) -> a new screen entry.
                ushort newEntry = ImageTSAEditorCore.SerializeCell(7, true, false, 3);
                tile[HeaderCellIndex(dmhy, 1, 0)] = newEntry;

                string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                    rom, tile, dmhx, dmhy, false, TSA_RAW_SLOT, TSA_RAW_OFFSET);
                Assert.Equal("", err);

                // No growth, no repoint on the raw path.
                Assert.Equal(lenBefore, rom.Data.Length);
                Assert.Equal(slotBefore, rom.u32(TSA_RAW_SLOT));

                // Re-decode: the edit landed at cell (1,0).
                ushort[] readBack = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, false, TSA_RAW_OFFSET, out _, out _);
                Assert.NotNull(readBack);
                Assert.Equal(newEntry, readBack[HeaderCellIndex(dmhy, 1, 0)]);
            });
        }

        // -----------------------------------------------------------------
        // WriteHeaderTsaCells — LZ77 repoint to fresh data.
        // -----------------------------------------------------------------

        [Fact]
        public void WriteHeaderTsaCells_Lz77_RepointsSlot_EditLands()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int mhx = 2, mhy = 2;
                byte[] tsa = MakeHeaderTsa(mhx, mhy);
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(tsa));
                rom.write_p32(TSA_LZ_SLOT, TSA_LZ_OFFSET);
                uint slotBefore = rom.u32(TSA_LZ_SLOT);

                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, true, TSA_LZ_OFFSET, out int dmhx, out int dmhy);
                Assert.NotNull(tile);

                ushort newEntry = ImageTSAEditorCore.SerializeCell(5, false, true, 2);
                tile[HeaderCellIndex(dmhy, 2, 1)] = newEntry;

                string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                    rom, tile, dmhx, dmhy, true, TSA_LZ_SLOT, TSA_LZ_OFFSET);
                Assert.Equal("", err);

                // The slot was repointed to fresh free-space data.
                uint slotAfter = rom.u32(TSA_LZ_SLOT);
                Assert.NotEqual(slotBefore, slotAfter);

                // Decompress + re-decode via the NEW target -> the edit landed.
                uint newDataAddr = rom.p32(TSA_LZ_SLOT);
                ushort[] readBack = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, true, newDataAddr, out _, out _);
                Assert.NotNull(readBack);
                Assert.Equal(newEntry, readBack[HeaderCellIndex(dmhy, 2, 1)]);
            });
        }

        // -----------------------------------------------------------------
        // Failure / guard paths mutate ZERO bytes (byte-identical incl. length).
        // -----------------------------------------------------------------

        [Fact]
        public void WriteHeaderTsaCells_InvalidDims_MutatesZeroBytes()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                byte[] snap = (byte[])rom.Data.Clone();
                ushort[] tile = new ushort[32 * 20];

                // mhx=40 > 32 -> rejected before any mutation.
                string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                    rom, tile, 40, 2, false, TSA_RAW_SLOT, TSA_RAW_OFFSET);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(snap.Length, rom.Data.Length);
                Assert.Equal(snap, rom.Data);
            });
        }

        [Fact]
        public void WriteHeaderTsaCells_DegenerateSerialize_MutatesZeroBytes()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                byte[] snap = (byte[])rom.Data.Clone();

                // A too-small tile array makes SerializeHeaderTSA return the
                // 2-byte degenerate output (n = mhy<<5 >= tile.Length), which
                // WriteHeaderTsaCells must reject — NO mutation.
                ushort[] tooSmall = new ushort[8];
                string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                    rom, tooSmall, 1, 20, false, TSA_RAW_SLOT, TSA_RAW_OFFSET);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(snap, rom.Data);
            });
        }

        [Fact]
        public void WriteHeaderTsaCells_Raw_OutOfBoundsRegion_MutatesZeroBytes()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int mhx = 3, mhy = 2;
                byte[] tsa = MakeHeaderTsa(mhx, mhy);
                PlantBytes(rom, TSA_RAW_OFFSET, tsa);
                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, false, TSA_RAW_OFFSET, out int dmhx, out int dmhy);
                Assert.NotNull(tile);

                byte[] snap = (byte[])rom.Data.Clone();
                int lenBefore = rom.Data.Length;

                // tsaDataAddr near the ROM end -> the same-size write would run
                // past EOF -> bounds check fails, NO mutation.
                uint badAddr = (uint)rom.Data.Length - 4; // header footprint > 4
                string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                    rom, tile, dmhx, dmhy, false, TSA_RAW_SLOT, badAddr);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(lenBefore, rom.Data.Length);
                Assert.Equal(snap, rom.Data);
            });
        }

        [Fact]
        public void WriteHeaderTsaCells_NullTile_ReturnsErrorNoMutation()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                byte[] snap = (byte[])rom.Data.Clone();
                string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                    rom, null, 3, 2, false, TSA_RAW_SLOT, TSA_RAW_OFFSET);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(snap, rom.Data);
            });
        }

        [Fact]
        public void WriteHeaderTsaCells_Lz77_OuterScopeRollback_RestoresByteIdentical()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                int mhx = 2, mhy = 2;
                byte[] tsa = MakeHeaderTsa(mhx, mhy);
                PlantBytes(rom, TSA_LZ_OFFSET, LZ77.compress(tsa));
                rom.write_p32(TSA_LZ_SLOT, TSA_LZ_OFFSET);

                ROM prevRom = CoreState.ROM;
                Undo prevUndo = CoreState.Undo;
                try
                {
                    var undo = new Undo();
                    CoreState.ROM = rom;
                    CoreState.Undo = undo;

                    byte[] preData = (byte[])rom.Data.Clone();
                    uint preSlot = rom.u32(TSA_LZ_SLOT);

                    ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                        rom, 32, 20, true, TSA_LZ_OFFSET, out int dmhx, out int dmhy);
                    Assert.NotNull(tile);
                    tile[HeaderCellIndex(dmhy, 1, 1)] =
                        ImageTSAEditorCore.SerializeCell(9, true, true, 4);

                    var ud = new Undo.UndoData
                    {
                        time = DateTime.Now,
                        name = "WriteHeaderTSA",
                        list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                        filesize = (uint)rom.Data.Length,
                    };

                    using (ROM.BeginUndoScope(ud))
                    {
                        string err = ImageTSAEditorCore.WriteHeaderTsaCells(
                            rom, tile, dmhx, dmhy, true, TSA_LZ_SLOT, TSA_LZ_OFFSET);
                        Assert.Equal("", err);
                    }
                    Assert.NotEqual(preSlot, rom.u32(TSA_LZ_SLOT)); // repointed
                    Assert.True(rom.Data.Length >= preData.Length); // appended

                    // Palette half "fails" -> the View rolls the one scope back.
                    undo.Rollback(ud);

                    Assert.Equal(preData.Length, rom.Data.Length);
                    Assert.Equal(preSlot, rom.u32(TSA_LZ_SLOT));
                    Assert.True(preData.AsSpan().SequenceEqual(rom.Data));
                }
                finally
                {
                    CoreState.ROM = prevRom;
                    CoreState.Undo = prevUndo;
                }
            });
        }

        // -----------------------------------------------------------------
        // RenderHeaderMainImageFromCells — an edited header cell changes pixels.
        // -----------------------------------------------------------------

        [Fact]
        public void RenderHeaderMainImageFromCells_EditedCell_DiffersFromUnedited()
        {
            WithImageService(() =>
            {
                var rom = MakeRom();
                PlantImage(rom, MarkerTiles());
                PlantPalette(rom, StandardPalette());

                // All-zero header cells -> the unedited render is fully blank
                // (tile 0 is skipped). Editing one in-region cell to tile 1
                // (the green-marker tile) makes that 8x8 block render, so the
                // pixels differ.
                int mhx = 1, mhy = 1;
                byte[] tsa = new byte[2 + (mhx + 1) * (mhy + 1) * 2];
                tsa[0] = (byte)mhx; tsa[1] = (byte)mhy; // all cells zero
                PlantBytes(rom, TSA_RAW_OFFSET, tsa);
                ushort[] tile = ImageTSAEditorCore.DecodeHeaderTsaCells(
                    rom, 32, 20, false, TSA_RAW_OFFSET, out int dmhx, out int dmhy);
                Assert.NotNull(tile);

                IImage before = ImageTSAEditorCore.RenderHeaderMainImageFromCells(
                    rom, 32, 20, IMAGE_OFFSET, tile, dmhx, dmhy, PALETTE_OFFSET);
                Assert.NotNull(before);

                ushort[] edited = (ushort[])tile.Clone();
                edited[HeaderCellIndex(dmhy, 0, 0)] =
                    ImageTSAEditorCore.SerializeCell(1, false, false, 0);
                IImage after = ImageTSAEditorCore.RenderHeaderMainImageFromCells(
                    rom, 32, 20, IMAGE_OFFSET, edited, dmhx, dmhy, PALETTE_OFFSET);
                Assert.NotNull(after);

                Assert.NotEqual(before.GetPixelData(), after.GetPixelData());
            });
        }

        // -----------------------------------------------------------------
        // Helpers (mirror ImageTSAEditorCoreTests.cs)
        // -----------------------------------------------------------------

        static void WithImageService(Action body)
        {
            var saved = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                body();
            }
            finally { CoreState.ImageService = saved; }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static byte[] MarkerTiles()
        {
            byte[] tiles = new byte[2 * 32];
            FillTile(tiles, 1, 1);
            SetPixel(tiles, 1, 0, 0, 2);
            return tiles;
        }

        static byte[] StandardPalette()
        {
            byte[] pal = new byte[512];
            SetColor(pal, 0, 1, RED);
            SetColor(pal, 0, 2, GREEN);
            SetColor(pal, 1, 1, BLUE);
            SetColor(pal, 1, 2, WHITE);
            return pal;
        }

        static void SetColor(byte[] pal, int bank, int index, ushort c)
        {
            int off = bank * 32 + index * 2;
            pal[off] = (byte)(c & 0xFF);
            pal[off + 1] = (byte)(c >> 8);
        }

        static void FillTile(byte[] tiles, int tile, int colorIndex)
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    SetPixel(tiles, tile, x, y, colorIndex);
        }

        static void SetPixel(byte[] tiles, int tile, int x, int y, int colorIndex)
        {
            int pos = tile * 32 + y * 4 + x / 2;
            byte b = tiles[pos];
            if (x % 2 == 0) b = (byte)((b & 0xF0) | (colorIndex & 0x0F));
            else b = (byte)((b & 0x0F) | ((colorIndex & 0x0F) << 4));
            tiles[pos] = b;
        }

        static ushort Cell(int tileIndex, bool h, bool v, int bank)
            => (ushort)((tileIndex & 0x3FF) | (h ? 0x400 : 0) | (v ? 0x800 : 0) | ((bank & 0xF) << 12));

        static byte[] CellsToBytes(ushort[] cells)
        {
            byte[] b = new byte[cells.Length * 2];
            for (int i = 0; i < cells.Length; i++)
            {
                b[i * 2] = (byte)(cells[i] & 0xFF);
                b[i * 2 + 1] = (byte)(cells[i] >> 8);
            }
            return b;
        }

        static void PlantImage(ROM rom, byte[] tiles)
            => PlantBytes(rom, IMAGE_OFFSET, LZ77.compress(tiles));

        static void PlantPalette(ROM rom, byte[] palette)
            => PlantBytes(rom, PALETTE_OFFSET, palette);

        static void PlantRawCells(ROM rom, uint addr, ushort[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
                U.write_u16(rom.Data, addr + (uint)(i * 2), cells[i]);
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        static void AssertPixel(IImage img, int x, int y, byte r, byte g, byte b, byte a)
        {
            byte[] px = img.GetPixelData();
            int idx = (y * img.Width + x) * 4;
            Assert.Equal(r, px[idx + 0]);
            Assert.Equal(g, px[idx + 1]);
            Assert.Equal(b, px[idx + 2]);
            Assert.Equal(a, px[idx + 3]);
        }
    }
}
