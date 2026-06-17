// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageWorldMapCore.ImportEvent + ValidateEventBankedIndices
// (#1064 PR1) — the World Map Image editor's EVENT image import: TWO LZ77
// streams (ZIMAGE deduplicated tiles + ZHEADERTSA header-TSA) plus a fixed RAW
// 64-color / 4-bank palette (128 bytes, never compressed).
//
// The import COMPOSES existing Core primitives:
//   DecreaseColorConvertCore.Convert(maxPalette:4,yohaku:16,reserve1st:true)
//     → banked IndexData (256x160 = 240x160 + 16px yohaku) + 4-bank palette
//   split IndexData → localIndex(&0x0F) + per-tile bank(/16)
//   EncodeTSAMultiPalette → dedup tiles + raw TSA (palette in bits 12-15)
//   EncodeHeaderTSA(rawTsa,256,160,margin:2) → header-TSA (first 2 bytes 0x1D,0x13)
//   WriteCompressedToROM ZIMAGE + ZHEADERTSA + raw 128B palette.
//
// validate-all-before-mutate; ONE caller ambient undo scope; byte-identical
// (length-aware) fault restore (#885/#923) — a FAILED import (incl. a
// partial-write fault after ZIMAGE) mutates ZERO bytes.
//
// Reuses the synthetic-ROM harness shape from ImageWorldMapStripImportTests
// (rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01") + CoreState.ROM).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageWorldMapEventImportTests
    {
        // Distinct planted data offsets for the three event streams, clear of the
        // 0x0..0x200 danger zone and of each other. The 16 MB ROM is mostly
        // zero-filled so WriteCompressedToROM/WriteRawToROM find free space.
        const uint EVENT_IMAGE_OFFSET   = 0x010000;
        const uint EVENT_TSA_OFFSET     = 0x020000;
        const uint EVENT_PALETTE_OFFSET = 0x030000;

        const int SRC_W = 240;
        const int SRC_H = 160;
        const int CANVAS_W = 256;
        const int CANVAS_H = 160;

        // ================================================================
        // Success round-trip with a NON-ZERO-bank tile (mandatory): the synthetic
        // RGBA splits into two color regions so the reducer assigns at least one
        // tile to a non-zero bank, exercising the 128-byte palette + TSA bits
        // 12-15. Asserts the three streams + pointer repoints + TryRenderEvent.
        // ================================================================

        [Fact]
        public void ImportEvent_NonZeroBankTile_RoundTrips()
        {
            WithRom((rom) =>
            {
                PlantEventStreams(rom);

                byte[] rgba = MakeTwoRegionRgba(SRC_W, SRC_H);
                bool ok = ImageWorldMapCore.ImportEvent(rom, rgba, SRC_W, SRC_H, out string err);
                Assert.True(ok, err);
                Assert.Equal("", err);

                // --- All three event pointers repointed into free space. ---
                uint imgOff = U.toOffset(rom.u32(rom.RomInfo.worldmap_event_image_pointer));
                uint tsaOff = U.toOffset(rom.u32(rom.RomInfo.worldmap_event_tsa_pointer));
                uint palOff = U.toOffset(rom.u32(rom.RomInfo.worldmap_event_palette_pointer));
                Assert.True(U.isSafetyOffset(imgOff, rom));
                Assert.True(U.isSafetyOffset(tsaOff, rom));
                Assert.True(U.isSafetyOffset(palOff, rom));
                // The repoints moved off the (compact) planted offsets.
                Assert.NotEqual(EVENT_IMAGE_OFFSET, imgOff);
                Assert.NotEqual(EVENT_TSA_OFFSET, tsaOff);

                // --- ZIMAGE: LZ77 tile data decompresses. ---
                byte[] tiles = LZ77.decompress(rom.Data, imgOff);
                Assert.NotNull(tiles);
                Assert.True(tiles.Length >= 32);                // ≥1 tile (32 bytes)
                Assert.Equal(0, tiles.Length % 32);             // whole tiles

                // --- ZHEADERTSA: LZ77 header-TSA decompresses; first two bytes are
                //     the canonical 256x160/margin-2 header (0x1D, 0x13). ---
                byte[] headerTsa = LZ77.decompress(rom.Data, tsaOff);
                Assert.NotNull(headerTsa);
                Assert.True(headerTsa.Length >= 2);
                Assert.Equal(0x1D, headerTsa[0]);   // masterHeaderX = 256/8-2-1 = 29
                Assert.Equal(0x13, headerTsa[1]);   // masterHeaderY = 160/8-1   = 19

                // At least one TSA cell uses a NON-ZERO palette bank (bits 12-15),
                // proving the multi-bank path was exercised.
                Assert.True(HasNonZeroBankCell(headerTsa), "no non-zero-bank TSA cell");

                // --- Palette: exactly 128 RAW bytes, NOT LZ77-compressed. ---
                // The first byte of a real LZ77 stream is 0x10; the raw palette
                // begins with the bank-0 reserved color (0x0000), so byte 0 = 0.
                Assert.NotEqual(0x10, rom.Data[palOff]);
                byte[] palette = ReadBytes(rom, palOff, 128);
                // 64 colors written; bank-0 slot-0 is the reserved (0,0,0) color.
                Assert.Equal(0, palette[0]);
                Assert.Equal(0, palette[1]);

                // --- TryRenderEvent reproduces a non-null 256x160 render from the
                //     freshly-written streams. ---
                using IImage rendered = ImageWorldMapCore.TryRenderEvent(rom);
                Assert.NotNull(rendered);
                Assert.Equal(CANVAS_W, rendered.Width);
                Assert.Equal(CANVAS_H, rendered.Height);
            });
        }

        // ================================================================
        // The written palette equals the first 128 bytes the reducer produced
        // (proves no truncation/offset error in the raw palette write).
        // ================================================================

        [Fact]
        public void ImportEvent_PaletteEqualsReducerFirst128Bytes()
        {
            WithRom((rom) =>
            {
                PlantEventStreams(rom);

                byte[] rgba = MakeTwoRegionRgba(SRC_W, SRC_H);
                // Reference reduce (same parameters as ImportEvent).
                var r = DecreaseColorConvertCore.Convert(rgba, SRC_W, SRC_H,
                    maxPalette: 4, yohaku: 16, reserve1st: true, ignoreTSA: false);

                bool ok = ImageWorldMapCore.ImportEvent(rom, rgba, SRC_W, SRC_H, out string err);
                Assert.True(ok, err);

                uint palOff = U.toOffset(rom.u32(rom.RomInfo.worldmap_event_palette_pointer));
                byte[] written = ReadBytes(rom, palOff, 128);
                for (int i = 0; i < 128; i++)
                    Assert.Equal(r.GbaPalette[i], written[i]);
            });
        }

        // ================================================================
        // Reject invalid via the public validator (reachable, Copilot finding #2):
        // crafted banked data that mixes two banks within one 8x8 tile, and data
        // whose bank exceeds 3.
        // ================================================================

        [Fact]
        public void ValidateEventBankedIndices_MixedBankInTile_Rejects()
        {
            // 8x8 single tile: top-left pixel bank 1, another pixel bank 2.
            byte[] idx = new byte[8 * 8];
            idx[0] = 1 * 16 + 3;  // bank 1
            idx[9] = 2 * 16 + 4;  // bank 2 — mixed within the same 8x8 tile
            string err = ImageWorldMapCore.ValidateEventBankedIndices(idx, 8, 8);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ValidateEventBankedIndices_BankOver3_Rejects()
        {
            byte[] idx = new byte[8 * 8];
            idx[0] = 4 * 16 + 1;  // bank 4 — exceeds the 4-bank (0..3) event limit
            string err = ImageWorldMapCore.ValidateEventBankedIndices(idx, 8, 8);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ValidateEventBankedIndices_UniformBankPerTile_Accepts()
        {
            // 16x8 = two tiles: tile0 all bank 1, tile1 all bank 2 (each uniform).
            byte[] idx = new byte[16 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int bank = (x < 8) ? 1 : 2;
                    idx[y * 16 + x] = (byte)(bank * 16 + ((x + y) % 15) + 1);
                }
            }
            string err = ImageWorldMapCore.ValidateEventBankedIndices(idx, 16, 8);
            Assert.Equal("", err);
        }

        [Fact]
        public void ValidateEventBankedIndices_AllZeroTile_IsBank0_Accepts()
        {
            // An all-index-0 (transparent) tile is valid bank 0.
            byte[] idx = new byte[8 * 8];
            string err = ImageWorldMapCore.ValidateEventBankedIndices(idx, 8, 8);
            Assert.Equal("", err);
        }

        // ================================================================
        // Public-API reject cases — each mutates ZERO bytes (whole-ROM snapshot
        // byte-identical after).
        // ================================================================

        [Theory]
        [InlineData(256, 160)]  // already-canvas size (not the 240x160 source)
        [InlineData(240, 152)]  // wrong height
        [InlineData(128, 128)]  // wrong both
        public void ImportEvent_WrongSourceSize_MutatesZeroBytes(int w, int h)
        {
            WithRom((rom) =>
            {
                PlantEventStreams(rom);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] rgba = MakeTwoRegionRgba(w, h);
                bool ok = ImageWorldMapCore.ImportEvent(rom, rgba, w, h, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportEvent_NullRgba_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                PlantEventStreams(rom);
                byte[] before = (byte[])rom.Data.Clone();

                bool ok = ImageWorldMapCore.ImportEvent(rom, null, SRC_W, SRC_H, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportEvent_ShortRgba_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                PlantEventStreams(rom);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] tooShort = new byte[SRC_W * SRC_H * 4 - 1];
                bool ok = ImageWorldMapCore.ImportEvent(rom, tooShort, SRC_W, SRC_H, out string err);

                Assert.False(ok);
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportEvent_ZeroEventPointers_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                // Do NOT plant the event streams — the pointer slots stay 0 in a
                // freshly-zeroed synthetic ROM region, so the guard rejects.
                ZeroEventPointers(rom);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] rgba = MakeTwoRegionRgba(SRC_W, SRC_H);
                bool ok = ImageWorldMapCore.ImportEvent(rom, rgba, SRC_W, SRC_H, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        [Fact]
        public void ImportEvent_NullRom_Fails()
        {
            bool ok = ImageWorldMapCore.ImportEvent(null, MakeTwoRegionRgba(SRC_W, SRC_H), SRC_W, SRC_H, out string err);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void ImportEvent_FE6Rom_MutatesZeroBytes()
        {
            // The event-image import is now FE7+FE8 (#1184 generalized the FE8-only
            // #1098 gate to FE7 — the format is identical and the encode path is
            // version-independent). FE6 (version 6) has worldmap_event_* as 0x0; even
            // with planted resolvable pointers the explicit version gate must reject
            // it with no mutation BEFORE any write.
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB
                rom.LoadLow("synth_fe6.gba", data, "AFEJ01"); // FE6 -> version 6
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Plant resolvable event pointer slots so ONLY the version gate can reject.
                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, 0x100000);
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, 0x200000);
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, 0x300000);
                byte[] before = (byte[])rom.Data.Clone();

                bool ok = ImageWorldMapCore.ImportEvent(rom, MakeTwoRegionRgba(SRC_W, SRC_H), SRC_W, SRC_H, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        [Fact]
        public void ImportEvent_FE7Rom_PassesVersionGate()
        {
            // #1184: FE7 (version 7) is now an ACCEPTED event-import target. With
            // resolvable event pointers + the standard two-region source, the import
            // must pass the version gate (it does not fail with the version error;
            // any later failure would be a free-space/encode issue, not the gate).
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB (min for FE7U detection)
                rom.LoadLow("synth_fe7.gba", data, "AE7E01"); // FE7U -> version 7
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, 0x100000);
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, 0x200000);
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, 0x300000);

                bool ok = ImageWorldMapCore.ImportEvent(rom, MakeTwoRegionRgba(SRC_W, SRC_H), SRC_W, SRC_H, out string err);

                // The 16 MB synthetic ROM (mostly 0x00) has abundant free space, so
                // the FE7 import SUCCEEDS end-to-end through the version-independent
                // encode path — the key assertion is that the version gate did NOT
                // reject it.
                Assert.True(ok, "FE7 event import should pass the version gate: " + err);
                Assert.Equal("", err);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        // ================================================================
        // Atomic fault restore — no free space at the FIRST write (32MB 0x01).
        // ROM byte- AND length-identical after.
        // ================================================================

        [Fact]
        public void ImportEvent_ForcedNoFreeSpace_MutatesZeroBytes()
        {
            var savedRom = CoreState.ROM;
            try
            {
                // 32 MB max, filled with 0x01 (neither 0x00 nor 0xFF → FindFreeSpace
                // finds no run; AppendToRomEnd refuses since already at max). The
                // first WriteCompressedToROM returns U.NOT_FOUND → restore.
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01);
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;

                // Plant valid (resolvable, in-range) event pointer slots inside the
                // 0x01-filled ROM so the pointer guard passes and the failure is at
                // the WRITE, not the guard. The pointers resolve to in-ROM offsets;
                // the data there is garbage but the guard only checks resolvability.
                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, 0x100000);
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, 0x200000);
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, 0x300000);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] rgba = MakeTwoRegionRgba(SRC_W, SRC_H);
                bool ok = ImageWorldMapCore.ImportEvent(rom, rgba, SRC_W, SRC_H, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ================================================================
        // Atomic fault restore — PARTIAL write (Copilot finding #4): force the
        // failure AFTER the ZIMAGE + ZHEADERTSA writes SUCCEED (and GROW the ROM)
        // so only the THIRD (palette) write fails — proving the snapshot restores
        // the partial appends + the two ROM grows.
        //
        // To make the first two appends actually succeed (rather than the first
        // failing immediately, which would just duplicate the no-free-space test),
        // the 0x01-filled ROM (no internal free runs) is sized so its headroom to
        // the 32MB ceiling holds EXACTLY the two compressed streams' aligned append
        // sizes but NOT the +128-byte palette. The exact sizes are computed by
        // running the SAME reduce + encode + LZ77 path ImportEvent uses (a flat
        // single-color source keeps the streams small + deterministic).
        // ================================================================

        [Fact]
        public void ImportEvent_PartialWriteFault_RestoresByteAndLengthIdentical()
        {
            var savedRom = CoreState.ROM;
            try
            {
                const uint MAX = 0x02000000;

                // Build the SAME source the import uses and run the reduce + encode
                // to learn the exact compressed ZIMAGE + ZHEADERTSA byte counts, so
                // we can size the ROM so the two appends fit and the palette does not.
                byte[] rgba = MakeFlatRgba(SRC_W, SRC_H, 200, 120, 40);
                var r = DecreaseColorConvertCore.Convert(rgba, SRC_W, SRC_H,
                    maxPalette: 4, yohaku: 16, reserve1st: true, ignoreTSA: false);
                Assert.Equal(CANVAS_W, r.Width);
                Assert.Equal(CANVAS_H, r.Height);
                var (local, banks) = SplitBanked(r.IndexData, r.Width, r.Height);
                var tsa = ImageImportCore.EncodeTSAMultiPalette(local, CANVAS_W, CANVAS_H, banks);
                byte[] headerTsa = ImageImportCore.EncodeHeaderTSA(tsa.TSAData, CANVAS_W, CANVAS_H, 2);
                int zimg = LZ77.compress(tsa.TileData).Length;
                int zhdr = LZ77.compress(headerTsa).Length;

                // FindAndWriteData aligns each append to 4 bytes (U.Padding4) and
                // AppendToRomEnd also Padding4-aligns the current length before
                // appending. With a ROM length that is already a multiple of 4,
                // headroom = Padding4(zimg) + Padding4(zhdr) lets BOTH stream
                // appends fit while the subsequent 128-byte palette append crosses
                // the 32MB ceiling (AppendToRomEnd -> NOT_FOUND).
                uint headroom = Padding4((uint)zimg) + Padding4((uint)zhdr);
                uint size = MAX - headroom;
                size = size & ~3u; // keep the start length 4-aligned

                var rom = new ROM();
                byte[] data = new byte[size];
                Array.Fill(data, (byte)0x01); // no 0x00/0xFF free runs anywhere
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;

                SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, 0x100000);
                SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, 0x200000);
                SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, 0x300000);
                byte[] before = (byte[])rom.Data.Clone();
                int beforeLen = rom.Data.Length;

                bool ok = ImageWorldMapCore.ImportEvent(rom, rgba, SRC_W, SRC_H, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                // The palette write is the one that fails (the two stream writes
                // succeeded + grew the ROM), so the error names the palette step.
                Assert.Contains("palette", err, StringComparison.OrdinalIgnoreCase);
                // Byte- AND length-identical: the ZIMAGE/ZHEADERTSA appends + the
                // two ROM grows were fully rolled back.
                Assert.Equal(beforeLen, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; }
        }

        /// <summary>U.Padding4 is internal to Core; mirror it for the test sizing.</summary>
        static uint Padding4(uint v) => (v + 3u) & ~3u;

        /// <summary>Split a banked index buffer (value = bank*16 + local) into local
        /// 4bpp pixels + per-8x8-tile bank — the same split ImportEvent performs.</summary>
        static (byte[] local, int[] banks) SplitBanked(byte[] idx, int w, int h)
        {
            int tilesX = w / 8, tilesY = h / 8;
            byte[] local = new byte[w * h];
            int[] banks = new int[tilesX * tilesY];
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int bank = 0; bool set = false;
                    for (int y8 = 0; y8 < 8; y8++)
                        for (int x8 = 0; x8 < 8; x8++)
                        {
                            int pos = (ty * 8 + y8) * w + (tx * 8 + x8);
                            int v = idx[pos];
                            local[pos] = (byte)(v & 0x0F);
                            if (!set && !((v / 16) == 0 && (v & 0x0F) == 0)) { bank = v / 16; set = true; }
                        }
                    banks[ty * tilesX + tx] = bank;
                }
            return (local, banks);
        }

        /// <summary>A flat single-color 240x160 RGBA image — reduces to a single
        /// unique tile (tiny, deterministic ZIMAGE/ZHEADERTSA for size math).</summary>
        static byte[] MakeFlatRgba(int w, int h, byte r, byte g, byte b)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }

        // ================================================================
        // Harness
        // ================================================================

        static void WithRom(Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                // TryRenderEvent (the round-trip render assertion) requires an
                // IImageService; the strip-import suite uses the shared StubImageService.
                CoreState.ImageService = new StubImageService();
                body(rom);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000]; // 16 MB (min for FE8U detection)
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>Plant a small LZ77 image + raw TSA + raw palette at the three
        /// planted offsets and wire all three event pointer slots. The planted
        /// data only needs to make the pointer guard resolve (the import overwrites
        /// these slots with freshly-written streams).</summary>
        static void PlantEventStreams(ROM rom)
        {
            // ZIMAGE: a tiny LZ77 stream so the resolve + the pre-import
            // TryRenderEvent (if anyone calls it) does not crash.
            byte[] tiles = new byte[32];
            tiles[0] = 0x11;
            PlantBytes(rom, EVENT_IMAGE_OFFSET, LZ77.compress(tiles));
            SetPtr(rom, rom.RomInfo.worldmap_event_image_pointer, EVENT_IMAGE_OFFSET);

            byte[] header = new byte[2 + 2]; // minimal header-TSA
            header[0] = 0x1D; header[1] = 0x13;
            PlantBytes(rom, EVENT_TSA_OFFSET, LZ77.compress(header));
            SetPtr(rom, rom.RomInfo.worldmap_event_tsa_pointer, EVENT_TSA_OFFSET);

            byte[] pal = new byte[128]; // 64 colors raw
            PlantBytes(rom, EVENT_PALETTE_OFFSET, pal);
            SetPtr(rom, rom.RomInfo.worldmap_event_palette_pointer, EVENT_PALETTE_OFFSET);
        }

        static void ZeroEventPointers(ROM rom)
        {
            rom.write_u32(rom.RomInfo.worldmap_event_image_pointer, 0);
            rom.write_u32(rom.RomInfo.worldmap_event_tsa_pointer, 0);
            rom.write_u32(rom.RomInfo.worldmap_event_palette_pointer, 0);
        }

        /// <summary>
        /// Build a 240x160 RGBA image with TWO color regions (left half one color
        /// set, right half another) so the reducer assigns at least one tile to a
        /// non-zero palette bank — exercising the multi-bank palette + TSA bits.
        /// Each half varies its color across the tile so a tile holds &gt;1 color
        /// (not a single flat color the reducer would merge into bank 0).
        /// </summary>
        static byte[] MakeTwoRegionRgba(int w, int h)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    byte r, g, b;
                    if (x < w / 2)
                    {
                        // Left region: red-dominant gradient.
                        r = (byte)(64 + ((x * 3) & 0xC0));
                        g = (byte)((y * 5) & 0x60);
                        b = (byte)((x + y) & 0x30);
                    }
                    else
                    {
                        // Right region: blue-dominant gradient (a distinct palette).
                        r = (byte)((y * 5) & 0x30);
                        g = (byte)((x + y) & 0x60);
                        b = (byte)(64 + ((x * 3) & 0xC0));
                    }
                    rgba[o + 0] = r;
                    rgba[o + 1] = g;
                    rgba[o + 2] = b;
                    rgba[o + 3] = 255; // opaque
                }
            }
            return rgba;
        }

        /// <summary>True if any header-TSA cell (after the 2-byte header) sets the
        /// palette bits (12-15) to a non-zero bank.</summary>
        static bool HasNonZeroBankCell(byte[] headerTsa)
        {
            for (int i = 2; i + 1 < headerTsa.Length; i += 2)
            {
                ushort cell = (ushort)(headerTsa[i] | (headerTsa[i + 1] << 8));
                if (((cell >> 12) & 0xF) != 0) return true;
            }
            return false;
        }

        static byte[] ReadBytes(ROM rom, uint offset, int len)
        {
            byte[] buf = new byte[len];
            Array.Copy(rom.Data, offset, buf, 0, len);
            return buf;
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        static void SetPtr(ROM rom, uint pointerSlot, uint dataOffset)
            => rom.write_u32(pointerSlot, U.toPointer(dataOffset));
    }
}
