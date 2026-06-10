// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the World Map (FE8) county-BORDER image import — #1064 PR2 / closes
// #1000 — the OAM/AP assembly that is the inverse of TryRenderBorder.
//
// Two layers under test:
//   * ImageUtilBorderAPCore.AssembleBorderAP — PURE assembly from two already-
//     decoded INDEXED sheets (main + _NAME) + origin -> seat image + AP-data
//     block. Asserts the AP header offsets ((frame_list-ap_data)=4 /
//     (anime_list-ap_data)=8), the two-frame OAM split, and the origin clamp.
//   * ImageWorldMapCore.ImportBorder — the ROM-write seam: FE8-only gate,
//     LZ77-write image -> record P0, raw-write AP -> record P4, byte-identical
//     fault restore (incl. the partial-write-after-image-write case).
//
// Reuses the synthetic-ROM harness shape from ImageWorldMapEventImportTests /
// ImageWorldMapBorderCoreTests (rom.LoadLow(..., "BE8E01") + StubImageService +
// [Collection("SharedState")]).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageWorldMapBorderImportTests
    {
        // Border source sheet dims (WF SCREEN_TILE_WIDTH=31 / 248x160).
        const int SRC_W = ImageUtilBorderAPCore.SRC_WIDTH;   // 248
        const int SRC_H = ImageUtilBorderAPCore.SRC_HEIGHT;  // 160

        // The 12-byte border record we import into (well clear of the danger zone).
        const uint BORDER_RECORD_ADDR  = 0x080000;
        // Planted (pre-import) P0/P4 targets so the record's slots resolve.
        const uint BORDER_IMAGE_OFFSET = 0x090000;
        const uint BORDER_AP_OFFSET    = 0x0A0000;
        // Palette block (the county-border palette pointer is a pointer-to-pointer).
        const uint BORDER_PALETTE_OFFSET = 0x0B0000;

        // GBA 5-5-5 colors.
        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;
        const ushort WHITE = 0x7FFF;

        // ================================================================
        // (1) PURE assembly — AP header geometry + two-frame split + clamp.
        // ================================================================

        [Fact]
        public void AssembleBorderAP_ApHeaderAndSplitOffsets_AreCorrect()
        {
            // Two distinct sheets, each with a handful of non-blank tiles so each
            // frame emits ≥1 OAM entry (12-byte battle -> 6-byte AP).
            byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 3, startTile: 0);
            byte[] name  = MakeSheetWithTiles(index: 2, tilesToFill: 2, startTile: 40);
            byte[] pal   = MakeBorderPalette();

            var asm = ImageUtilBorderAPCore.AssembleBorderAP(sheet, name, pal, 0, 0);
            Assert.True(asm.Success, asm.Error);
            Assert.NotNull(asm.ImageBytes);
            Assert.NotNull(asm.ApBytes);

            // ---- AP header SHORTs: (frame_list - ap_data)=4, (anime_list - ap_data)=8. ----
            Assert.True(asm.ApBytes.Length >= 12);
            ushort frameListOff = (ushort)(asm.ApBytes[0] | (asm.ApBytes[1] << 8));
            ushort animListOff  = (ushort)(asm.ApBytes[2] | (asm.ApBytes[3] << 8));
            Assert.Equal(4, frameListOff);
            Assert.Equal(8, animListOff);

            // ---- The four list offsets are back-patched and non-zero (frame_0,
            //      frame_1 relative to frame_list; anim_0, anim_1 relative to
            //      anim_list). frame_0 immediately follows the two lists (offset 8
            //      from frame_list base = +12 absolute). ----
            ushort frame0Off = (ushort)(asm.ApBytes[4] | (asm.ApBytes[5] << 8));
            ushort frame1Off = (ushort)(asm.ApBytes[6] | (asm.ApBytes[7] << 8));
            ushort anim0Off  = (ushort)(asm.ApBytes[8]  | (asm.ApBytes[9] << 8));
            ushort anim1Off  = (ushort)(asm.ApBytes[10] | (asm.ApBytes[11] << 8));
            Assert.Equal(8, frame0Off);                 // frame_0 - frame_list = 8
            Assert.True(frame1Off > frame0Off);          // frame_1 after frame_0
            Assert.True(anim1Off > anim0Off);            // anim_1 after anim_0

            // ---- Two-frame AP-OAM split: [0]=end of frame 0 OAM, [1]=end of
            //      frame 1 OAM; each is a whole number of 6-byte AP-OAM entries. ----
            Assert.Equal(2, asm.ApOamSplit.Length);
            Assert.True(asm.ApOamSplit[0] > 0);
            Assert.True(asm.ApOamSplit[1] > asm.ApOamSplit[0]);
            Assert.Equal(0u, asm.ApOamSplit[0] % 6);
            Assert.Equal(0u, asm.ApOamSplit[1] % 6);

            // ---- frame_0's stored oam-entry-count SHORT == apOamSplit[0]/6. ----
            // frame_0 starts at absolute offset frame_list_base(4) + frame0Off(8) = +12.
            int frame0Abs = 4 + frame0Off;
            ushort frame0Count = (ushort)(asm.ApBytes[frame0Abs] | (asm.ApBytes[frame0Abs + 1] << 8));
            Assert.Equal(asm.ApOamSplit[0] / 6, frame0Count);

            // ---- The seat image is 256x40 = 32*5 tiles * 32 bytes = 5120 bytes. ----
            Assert.Equal(32 * 5 * 32, asm.ImageBytes.Length);

            // ---- anim_0 / anim_1 scripts: (4, frameIndex) then (0, 0xffff). ----
            int anim0Abs = 8 + anim0Off;
            Assert.Equal(4, asm.ApBytes[anim0Abs] | (asm.ApBytes[anim0Abs + 1] << 8)); // wait=4
            Assert.Equal(0, asm.ApBytes[anim0Abs + 2] | (asm.ApBytes[anim0Abs + 3] << 8)); // frame=0
            Assert.Equal(0, asm.ApBytes[anim0Abs + 4] | (asm.ApBytes[anim0Abs + 5] << 8)); // wait=0
            Assert.Equal(0xffff, asm.ApBytes[anim0Abs + 6] | (asm.ApBytes[anim0Abs + 7] << 8)); // loop
            int anim1Abs = 8 + anim1Off;
            Assert.Equal(1, asm.ApBytes[anim1Abs + 2] | (asm.ApBytes[anim1Abs + 3] << 8)); // anim_1 frame=1
        }

        [Theory]
        [InlineData(0u, 0u, 0u, 0u)]      // in-range: unchanged
        [InlineData(30u, 25u, 30u, 25u)]  // in-range: unchanged
        [InlineData(60u, 50u, 60u, 50u)]  // at the boundary: unchanged (>= clamps)
        [InlineData(100u, 80u, 60u, 50u)] // out-of-range: clamped to 60 / 50
        [InlineData(70u, 40u, 60u, 40u)]  // x clamped, y unchanged
        public void AssembleBorderAP_OriginClamp_MatchesWF(uint inX, uint inY, uint expX, uint expY)
        {
            byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 2, startTile: 0);
            byte[] name  = MakeSheetWithTiles(index: 2, tilesToFill: 1, startTile: 40);
            byte[] pal   = MakeBorderPalette();

            var asm = ImageUtilBorderAPCore.AssembleBorderAP(sheet, name, pal, inX, inY);
            Assert.True(asm.Success, asm.Error);
            Assert.Equal(expX, asm.ClampedOriginX);
            Assert.Equal(expY, asm.ClampedOriginY);
        }

        [Fact]
        public void AssembleBorderAP_NullName_Rejects()
        {
            byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 2, startTile: 0);
            var asm = ImageUtilBorderAPCore.AssembleBorderAP(sheet, null, MakeBorderPalette(), 0, 0);
            Assert.False(asm.Success);
            Assert.False(string.IsNullOrEmpty(asm.Error));
        }

        [Fact]
        public void AssembleBorderAP_TooLargeSheet_Rejects()
        {
            // A FULLY-filled sheet with EVERY tile unique cannot dedup into the
            // 256x40 (32x5 = 160 tiles) seat once the top-right palette-map tile
            // is excluded and BOTH sheets share it -> overflow -> reject (WF
            // images.Count >= 2). 31x20 = 620 visible tiles >> 160 seat tiles, and
            // unique per-tile content defeats dedup.
            byte[] sheet = MakeUniquePerTileSheet();
            byte[] name  = MakeUniquePerTileSheet();
            byte[] pal   = MakeBorderPalette();

            var asm = ImageUtilBorderAPCore.AssembleBorderAP(sheet, name, pal, 0, 0);
            Assert.False(asm.Success);
            Assert.Contains("too large", asm.Error, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // (2) ROM-write seam — happy path render-smoke.
        // ================================================================

        [Fact]
        public void ImportBorder_HappyPath_WritesRepointsAndRenders()
        {
            WithRom((rom) =>
            {
                PlantBorderRecordAndGraphic(rom);

                byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 4, startTile: 0);
                byte[] name  = MakeSheetWithTiles(index: 2, tilesToFill: 2, startTile: 40);
                byte[] pal   = MakeBorderPalette();

                bool ok = ImageWorldMapCore.ImportBorder(
                    rom, sheet, name, pal, 0, 0, BORDER_RECORD_ADDR, out string err);
                Assert.True(ok, err);
                Assert.Equal("", err);

                // --- Record P0 (image pointer @ +0) + P4 (AP pointer @ +4) repointed. ---
                uint p0 = rom.u32(BORDER_RECORD_ADDR + 0);
                uint p4 = rom.u32(BORDER_RECORD_ADDR + 4);
                Assert.True(U.isPointer(p0));
                Assert.True(U.isPointer(p4));
                uint imgOff = U.toOffset(p0);
                uint apOff  = U.toOffset(p4);
                Assert.True(U.isSafetyOffset(imgOff, rom));
                Assert.True(U.isSafetyOffset(apOff, rom));
                Assert.NotEqual(BORDER_IMAGE_OFFSET, imgOff);  // moved off the planted slot
                Assert.NotEqual(BORDER_AP_OFFSET, apOff);

                // --- Image: LZ77 tile data decompresses to the 256x40 seat (5120 B). ---
                byte[] seat = LZ77.decompress(rom.Data, imgOff);
                Assert.NotNull(seat);
                Assert.Equal(32 * 5 * 32, seat.Length);

                // --- AP: header (frame_list-ap_data)=4 / (anime_list-ap_data)=8, and
                //     ImageUtilAPCore parses it (≥2 frames). ---
                Assert.Equal(4u, rom.u16(apOff + 0));
                Assert.Equal(8u, rom.u16(apOff + 2));
                var ap = new ImageUtilAPCore();
                Assert.True(ap.Parse(rom.Data, apOff), ap.ErrorMessage);

                // --- Render smoke: TryRenderBorder produces a non-null 256x160 image
                //     from the freshly-written record (P0 image + P4 AP). ---
                using IImage rendered = ImageWorldMapCore.TryRenderBorder(
                    rom, p0, p4, 0, 0);
                Assert.NotNull(rendered);
                Assert.Equal(256, rendered.Width);
                Assert.Equal(160, rendered.Height);
            });
        }

        // ================================================================
        // (3) Palette-index regression (Copilot plan-review BLOCKING fix).
        //
        // The encoded tile bytes must use the EXISTING palette indices the caller
        // supplied — NOT reducer-generated ones. We assemble from an explicitly
        // index-N sheet and assert the seat's first non-zero nibble == N.
        // ================================================================

        [Fact]
        public void AssembleBorderAP_EncodesSuppliedIndices_NotReduced()
        {
            // A sheet whose first tile is filled with palette index 5 (a high index
            // a reducer would re-number). The seat MUST encode nibble 5.
            byte[] sheet = MakeSheetWithTiles(index: 5, tilesToFill: 1, startTile: 0);
            byte[] name  = MakeSheetWithTiles(index: 5, tilesToFill: 1, startTile: 40);
            byte[] pal   = MakeBorderPalette();

            var asm = ImageUtilBorderAPCore.AssembleBorderAP(sheet, name, pal, 0, 0);
            Assert.True(asm.Success, asm.Error);

            // The first packed tile lands at seat tile (0,0) -> seat bytes [0..31].
            // Every nibble of an index-5-filled tile is 0x5 -> byte 0x55.
            bool found55 = false;
            for (int i = 0; i < 32; i++)
                if (asm.ImageBytes[i] == 0x55) { found55 = true; break; }
            Assert.True(found55,
                "Expected the seat to encode the SUPPLIED palette index 5 (nibble 0x5), " +
                "proving indices are NOT reducer-renumbered.");
        }

        // ================================================================
        // (4) FE8-only gate — non-FE8 rejects with ZERO mutation.
        // ================================================================

        [Fact]
        public void ImportBorder_NonFE8Rom_MutatesZeroBytes()
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB (FE7U min)
                rom.LoadLow("synth_fe7.gba", data, "AE7E01"); // FE7U -> version 7
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                // FE7 has worldmap_county_border_palette_pointer == 0, so the gate rejects.
                byte[] before = (byte[])rom.Data.Clone();

                byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 2, startTile: 0);
                byte[] name  = MakeSheetWithTiles(index: 2, tilesToFill: 1, startTile: 40);
                bool ok = ImageWorldMapCore.ImportBorder(
                    rom, sheet, name, MakeBorderPalette(), 0, 0, BORDER_RECORD_ADDR, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        [Fact]
        public void ImportBorder_NullRom_Fails()
        {
            bool ok = ImageWorldMapCore.ImportBorder(
                null, new byte[SRC_W * SRC_H], new byte[SRC_W * SRC_H], MakeBorderPalette(),
                0, 0, BORDER_RECORD_ADDR, out string err);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(err));
        }

        // ================================================================
        // (5) Missing _NAME via the public seam — reject, ZERO mutation.
        // ================================================================

        [Fact]
        public void ImportBorder_NullName_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                PlantBorderRecordAndGraphic(rom);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 2, startTile: 0);
                bool ok = ImageWorldMapCore.ImportBorder(
                    rom, sheet, null, MakeBorderPalette(), 0, 0, BORDER_RECORD_ADDR, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        // ================================================================
        // (6) Too-large sheet via the public seam — reject, ZERO mutation.
        // ================================================================

        [Fact]
        public void ImportBorder_TooLargeSheet_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                PlantBorderRecordAndGraphic(rom);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] sheet = MakeUniquePerTileSheet();
                byte[] name  = MakeUniquePerTileSheet();
                bool ok = ImageWorldMapCore.ImportBorder(
                    rom, sheet, name, MakeBorderPalette(), 0, 0, BORDER_RECORD_ADDR, out string err);

                Assert.False(ok);
                Assert.Contains("too large", err, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, rom.Data);
            });
        }

        // ================================================================
        // (7) Byte-identical restore — PARTIAL write (image OK, AP write fails).
        //
        // Size a 0x01-filled ROM (no internal free runs) so the headroom to the
        // 32MB ceiling holds the compressed IMAGE append but NOT the subsequent AP
        // append: the image write succeeds + grows the ROM, then the AP write
        // returns NOT_FOUND -> snapshot restore restores the ROM byte- AND
        // length-identical (the image append + the grow are fully rolled back).
        // ================================================================

        [Fact]
        public void ImportBorder_PartialWriteFault_RestoresByteAndLengthIdentical()
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            try
            {
                const uint MAX = 0x02000000;

                // Assemble the SAME data the import uses to learn the exact
                // compressed IMAGE + raw AP byte counts, so the ROM is sized so the
                // image append fits and the AP append does not.
                byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 3, startTile: 0);
                byte[] name  = MakeSheetWithTiles(index: 2, tilesToFill: 2, startTile: 40);
                byte[] pal   = MakeBorderPalette();
                var asm = ImageUtilBorderAPCore.AssembleBorderAP(sheet, name, pal, 0, 0);
                Assert.True(asm.Success, asm.Error);
                int zimg = LZ77.compress(asm.ImageBytes).Length;
                int apLen = asm.ApBytes.Length;

                // headroom = Padding4(zimg) lets the image append fit; the AP append
                // (Padding4(apLen)) then crosses the 32MB ceiling -> NOT_FOUND.
                uint headroom = Padding4((uint)zimg);
                uint size = (MAX - headroom) & ~3u;

                var rom = new ROM();
                byte[] data = new byte[size];
                Array.Fill(data, (byte)0x01); // no 0x00/0xFF free runs
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Plant a resolvable border record (P0/P4) so the only failure is at
                // the AP WRITE, not the gate. The record slots resolve to in-ROM
                // garbage; the gate only checks the FE8 version + palette pointer +
                // record bounds (all satisfied by a valid palette plant + in-range
                // record addr).
                PlantBorderPalette(rom);
                SetPtr(rom, BORDER_RECORD_ADDR + 0, 0x100000);
                SetPtr(rom, BORDER_RECORD_ADDR + 4, 0x200000);
                byte[] before = (byte[])rom.Data.Clone();
                int beforeLen = rom.Data.Length;

                bool ok = ImageWorldMapCore.ImportBorder(
                    rom, sheet, name, pal, 0, 0, BORDER_RECORD_ADDR, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                // The AP write is the one that fails (the image write succeeded + grew
                // the ROM), so the error names the AP step.
                Assert.Contains("AP", err, StringComparison.OrdinalIgnoreCase);
                // Byte- AND length-identical: the image append + the grow rolled back.
                Assert.Equal(beforeLen, rom.Data.Length);
                Assert.Equal(before, rom.Data);

                // Suppress the unused-warning + document the AP-len relationship.
                Assert.True(apLen > 0);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        // ================================================================
        // (8) Invalid record address — reject, ZERO mutation.
        // ================================================================

        [Fact]
        public void ImportBorder_RecordAddrOutOfRange_MutatesZeroBytes()
        {
            WithRom((rom) =>
            {
                PlantBorderPalette(rom);
                byte[] before = (byte[])rom.Data.Clone();

                byte[] sheet = MakeSheetWithTiles(index: 1, tilesToFill: 2, startTile: 0);
                byte[] name  = MakeSheetWithTiles(index: 2, tilesToFill: 1, startTile: 40);
                // A record address past EOF.
                uint badAddr = (uint)rom.Data.Length - 2;
                bool ok = ImageWorldMapCore.ImportBorder(
                    rom, sheet, name, MakeBorderPalette(), 0, 0, badAddr, out string err);

                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
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
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB (FE8U min)
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                body(rom);
            }
            finally { CoreState.ROM = savedRom; CoreState.ImageService = savedSvc; }
        }

        /// <summary>Plant the border palette + a valid 12-byte record whose P0/P4
        /// resolve to small planted graphic blocks (the import overwrites P0/P4).</summary>
        static void PlantBorderRecordAndGraphic(ROM rom)
        {
            PlantBorderPalette(rom);

            // Plant a tiny parts image + AP so the pre-import record resolves and a
            // pre-import TryRenderBorder (if called) would not crash.
            byte[] bin = new byte[32]; bin[0] = 0x11;
            Array.Copy(LZ77.compress(bin), 0, rom.Data, BORDER_IMAGE_OFFSET, LZ77.compress(bin).Length);
            // A minimal AP block (re-used from the border-render test layout).
            byte[] ap = BuildMinimalAPData();
            Array.Copy(ap, 0, rom.Data, BORDER_AP_OFFSET, ap.Length);

            SetPtr(rom, BORDER_RECORD_ADDR + 0, BORDER_IMAGE_OFFSET);
            SetPtr(rom, BORDER_RECORD_ADDR + 4, BORDER_AP_OFFSET);
            // W8 / W10 (x/y origin) at +8 — leave as 0.
            rom.write_u32(BORDER_RECORD_ADDR + 8, 0);
        }

        /// <summary>Plant the 16-color border palette via the pointer-to-pointer slot.</summary>
        static void PlantBorderPalette(ROM rom)
        {
            uint slot = rom.RomInfo.worldmap_county_border_palette_pointer;
            if (slot == 0) return;
            SetPtr(rom, slot, BORDER_PALETTE_OFFSET);
            byte[] pal = MakeBorderPalette();
            Array.Copy(pal, 0, rom.Data, BORDER_PALETTE_OFFSET, pal.Length);
        }

        // ---- Synthetic indexed sheets (248x160, 1 byte/pixel) ----

        /// <summary>A 248x160 indexed sheet that is blank except for the first
        /// <paramref name="tilesToFill"/> 8x8 tiles (starting at tile
        /// <paramref name="startTile"/>), each filled with palette <paramref name="index"/>.</summary>
        static byte[] MakeSheetWithTiles(byte index, int tilesToFill, int startTile)
        {
            byte[] sheet = new byte[SRC_W * SRC_H];
            int tilesX = SRC_W / 8; // 31
            for (int t = 0; t < tilesToFill; t++)
            {
                int tile = startTile + t;
                int tx = tile % tilesX;
                int ty = tile / tilesX;
                for (int py = 0; py < 8; py++)
                    for (int px = 0; px < 8; px++)
                        sheet[(ty * 8 + py) * SRC_W + (tx * 8 + px)] = index;
            }
            return sheet;
        }

        /// <summary>A 248x160 indexed sheet where every 8x8 tile has a PROVABLY
        /// UNIQUE pattern (defeats seat dedup) so the combined tiles overflow the
        /// 256x40 (= 32x5 = 160-slot) seat. Each tile encodes its global tile id in
        /// the first three pixels as three 4-bit nibbles (12 bits), so all 620 tiles
        /// (31x20) per sheet are distinct — far more than the 160-tile seat — and no
        /// largest-rectangle merge or dedup can pack them.</summary>
        static byte[] MakeUniquePerTileSheet()
        {
            byte[] sheet = new byte[SRC_W * SRC_H];
            int tilesX = SRC_W / 8; // 31
            int tilesY = SRC_H / 8; // 20
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int tile = ty * tilesX + tx; // 0..619 (< 4096, fits in 3 nibbles)
                    int row = ty * 8 * SRC_W + tx * 8;
                    // Encode the 12-bit tile id as three raw nibbles (4-bit indices
                    // 0..15). These three pixels alone make every tile's content
                    // unique, so dedup / largest-rectangle merge cannot pack them.
                    sheet[row + 0] = (byte)(tile        & 0x0F);
                    sheet[row + 1] = (byte)((tile >> 4)  & 0x0F);
                    sheet[row + 2] = (byte)((tile >> 8)  & 0x0F);
                    // A non-zero filler so the tile is clearly non-blank (a tile is
                    // "blank"/skipped only when ALL 64 pixels are 0).
                    sheet[row + 3] = 0x07;
                }
            return sheet;
        }

        /// <summary>16-color GBA palette: 0=transparent, 1=RED, 2=GREEN, 3=BLUE,
        /// 5=WHITE (used by the palette-index regression test), rest distinct.</summary>
        static byte[] MakeBorderPalette()
        {
            byte[] pal = new byte[32];
            void Set(int i, ushort c) { pal[i * 2] = (byte)(c & 0xFF); pal[i * 2 + 1] = (byte)(c >> 8); }
            Set(0, 0x0000);
            Set(1, RED);
            Set(2, GREEN);
            Set(3, BLUE);
            Set(4, 0x1234);
            Set(5, WHITE);
            for (int i = 6; i < 16; i++) Set(i, (ushort)(0x2000 + i)); // distinct fillers
            return pal;
        }

        // ---- A minimal AP block (2 frames, 0 OAM each) for the pre-import plant. ----
        static byte[] BuildMinimalAPData()
        {
            // Same layout as ImageWorldMapBorderCoreTests.BuildMinimalAPData (20 B).
            byte[] d = new byte[20];
            WriteU16(d, 0, 4);   // frameDataOffset
            WriteU16(d, 2, 8);   // animeTableOffset
            WriteU16(d, 4, 8);   // frame0 @ +12
            WriteU16(d, 6, 10);  // frame1 @ +14
            WriteU16(d, 8, 4);   // anime0 @ +12
            WriteU16(d, 10, 8);  // anime1 @ +16
            WriteU16(d, 12, 0);
            WriteU16(d, 14, 0);
            WriteU16(d, 16, 0);  // anime1 wait=0
            WriteU16(d, 18, 1);  // anime1 frame=1
            return d;
        }

        static void WriteU16(byte[] d, int off, uint v)
        {
            d[off] = (byte)(v & 0xFF);
            d[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        static void SetPtr(ROM rom, uint slot, uint dataOffset)
            => rom.write_u32(slot, U.toPointer(dataOffset));

        /// <summary>U.Padding4 is internal to Core; mirror it for the test sizing.</summary>
        static uint Padding4(uint v) => (v + 3u) & ~3u;
    }
}
