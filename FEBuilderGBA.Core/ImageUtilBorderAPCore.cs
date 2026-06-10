// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform port of the WinForms World Map (FE8) COUNTY BORDER importer
// (#1064 PR2 / closes #1000). The render/read side already lives in
// ImageWorldMapCore.TryRenderBorder + ImageUtilAPCore; THIS file is the pure
// assembly + AP-OAM conversion + AP-data builder that the inverse import needs.
//
// WF reference (read end-to-end):
//   * FEBuilderGBA/ImageUtilBorderAP.cs — ImportBorder + BattleOAMToAPOAM +
//     BattleOAMMaxRectngle + the AP-data structure builder.
//   * FEBuilderGBA/ImageUtilOAM.cs — ImportOAM.MakeBorderAP / MakeBattleAnime /
//     MakeBattleAnimeLow / tryCopy / AppendOAM / AppendTermOAM / NextSeat /
//     GetImages / GetRightToLeftOAM / GetOAMByteCount (the border seat packing).
//
// THREE-CONCERN SEPARATION (Copilot plan-review finding 5):
//   (a) PURE assembly — AssembleBorderAP() takes already-decoded INDEXED pixel
//       buffers (the main sheet + its companion _NAME sheet, each 248x160) +
//       origin coords and produces (out_image, out_oam). NO Form / File.* /
//       dialogs / IImage. This is ImportOAM/MakeBorderAP + BattleOAMToAPOAM +
//       the AP-data builder ported to operate over byte[] only.
//   (b) input errors as RETURNS — null/empty name sheet, wrong dims, the
//       "images.Count >= 2 (too large for the 256x160 sheet)" overflow, and the
//       origin clamp surface via the BorderAssembleResult.Error string, NOT WF
//       dialogs.
//   (c) ROM writes live in ImageWorldMapCore.ImportBorder (separate seam): it
//       calls AssembleBorderAP(), then LZ77-writes the image + raw-writes the AP
//       to the border record's P0/P4 under the caller's ambient undo with a
//       byte-identical fault restore. This file performs ZERO ROM mutation.
//
// Reuses the well-tested seat helpers from BattleAnimeOAMImportCore
// (Seat / MakeUseTileData / ExtractBlock / ExtractByPaletteBank / GrepBlockInSeat)
// so the tile-dedup + packing logic is not duplicated; the border-specific
// pieces (5-tile seat, two-frame accumulation into ONE seat, the AP-OAM
// conversion, the AP-data header geometry) are ported faithfully here.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// PURE (no ROM mutation, no System.Drawing) assembler for the FE8 World Map
    /// county-border AP graphic. Mirrors WinForms
    /// <c>ImageUtilBorderAP.ImportBorder</c> minus the file/dialog/ROM-write
    /// concerns. See the file-level comment for the three-concern separation.
    /// </summary>
    public static class ImageUtilBorderAPCore
    {
        // ---- Source grid (the visible border map area) ----
        // WF ImageUtilOAM.SCREEN_TILE_WIDTH = 248/8 = 31, SCREEN_TILE_HEIGHT = 160/8 = 20.
        public const int SRC_WIDTH  = 248;
        public const int SRC_HEIGHT = 160;
        const int SCREEN_TILE_WIDTH  = SRC_WIDTH  / 8; // 31
        const int SCREEN_TILE_HEIGHT = SRC_HEIGHT / 8; // 20

        // ---- Seat (tile sheet) for the BorderAP graphic ----
        // WF ImageUtilOAM.SEAT_TILE_WIDTH = 256/8 = 32, SEAT_BORDERAP_TILE_HEIGHT = 40/8 = 5.
        const int SEAT_TILE_WIDTH = 256 / 8; // 32
        const int SEAT_BORDERAP_TILE_HEIGHT = 40 / 8; // 5

        // ---- GBA OAM centering offsets (WF bitmap_addx / bitmap_addy) ----
        const int BITMAP_ADDX = 0x94; // 148
        const int BITMAP_ADDY = 0x58; //  88

        // ---- OAM shape/size nibbles (byte[1] bits 6-7, byte[3] bits 6-7) ----
        const byte SHAPE_SQUARE     = 0x00;
        const byte SHAPE_HORIZONTAL = 0x40;
        const byte SHAPE_VERTICAL   = 0x80;
        const byte SIZE_TIMES1 = 0x00;
        const byte SIZE_TIMES2 = 0x40;
        const byte SIZE_TIMES4 = 0x80;
        const byte SIZE_TIMES8 = 0xC0;

        // ---- Origin clamp (WF WorldMapImageForm.BORDER_ImportButton_Click) ----
        // origin_x >= 60 -> 60 ; origin_y >= 50 -> 50.
        public const uint ORIGIN_X_MAX = 60;
        public const uint ORIGIN_Y_MAX = 50;

        /// <summary>
        /// Result of <see cref="AssembleBorderAP"/>. On success <see cref="Error"/>
        /// is null and <see cref="ImageBytes"/> (the decompressed seat tile sheet,
        /// ready to LZ77-compress) + <see cref="ApBytes"/> (the raw AP-data block)
        /// are populated. <see cref="ApOamSplit"/> exposes the per-frame AP-OAM
        /// byte boundaries (frame 0 ends at index 0, frame 1 at index 1) for tests.
        /// </summary>
        public sealed class BorderAssembleResult
        {
            /// <summary>Decompressed seat tile sheet (256×40 px → 4bpp tiles). Caller LZ77-compresses.</summary>
            public byte[] ImageBytes { get; set; }
            /// <summary>The AP-data block (header + frame_list + anime_list + frames + animes).</summary>
            public byte[] ApBytes { get; set; }
            /// <summary>16-color GBA palette (32 bytes) shared by both frames (frame 0 establishes it).</summary>
            public byte[] Palette { get; set; }
            /// <summary>Per-frame AP-OAM byte boundaries: [0]=end of frame 0 OAM, [1]=end of frame 1 OAM.</summary>
            public uint[] ApOamSplit { get; set; }
            /// <summary>The clamped origin actually used (WF clamps x→60 / y→50).</summary>
            public uint ClampedOriginX { get; set; }
            public uint ClampedOriginY { get; set; }
            /// <summary>Non-null on any failure.</summary>
            public string Error { get; set; }
            public bool Success => Error == null;
        }

        static BorderAssembleResult Fail(string reason) => new BorderAssembleResult { Error = reason };

        /// <summary>
        /// PURE assembler: from the two already-decoded INDEXED sheets (the chosen
        /// border sheet + its <c>_NAME</c> companion, each 248×160, 1 byte/pixel,
        /// index 0 = transparent) + the 16-color palette + origin coords, produce
        /// the seat tile sheet image + the AP-data block. No ROM mutation.
        ///
        /// <para><b>Two frames into ONE seat.</b> WF accumulates both sheets into
        /// the SAME 256×40 seat (frame 0 = the border outline/fill, frame 1 = the
        /// name text), tracking the OAM-byte split between them. If the combined
        /// tiles overflow the single seat (WF: <c>GetImages().Count >= 2</c>) the
        /// import is rejected ("too large for the 256×160 sheet").</para>
        ///
        /// <para><b>Origin clamp.</b> WF clamps <c>x≥60→60</c>, <c>y≥50→50</c>
        /// (BORDER_ImportButton_Click). The clamped origin is reported in the
        /// result and used for the AP-OAM conversion.</para>
        /// </summary>
        /// <param name="sheetIndexed">Main border sheet, indexed, 248×160 (1 byte/pixel).</param>
        /// <param name="nameIndexed">Companion <c>_NAME</c> sheet, indexed, 248×160.</param>
        /// <param name="palette16">16-color GBA palette (32 bytes), shared by both sheets.</param>
        /// <param name="originX">Origin X (clamped to ≤60).</param>
        /// <param name="originY">Origin Y (clamped to ≤50).</param>
        public static BorderAssembleResult AssembleBorderAP(
            byte[] sheetIndexed, byte[] nameIndexed, byte[] palette16,
            uint originX, uint originY)
        {
            // (b) input-error returns — no dialogs.
            if (sheetIndexed == null) return Fail("The world map border image is missing.");
            if (nameIndexed == null)  return Fail("The world map border name image (_NAME) is missing.");
            if (palette16 == null || palette16.Length < 32)
                return Fail("The world map border palette is invalid (need a 16-color / 32-byte palette).");
            if (sheetIndexed.Length < SRC_WIDTH * SRC_HEIGHT)
                return Fail($"The world map border image must be {SRC_WIDTH}x{SRC_HEIGHT}.");
            if (nameIndexed.Length < SRC_WIDTH * SRC_HEIGHT)
                return Fail($"The world map border name image (_NAME) must be {SRC_WIDTH}x{SRC_HEIGHT}.");

            // WF origin clamp.
            uint clampedX = originX >= ORIGIN_X_MAX ? ORIGIN_X_MAX : originX;
            uint clampedY = originY >= ORIGIN_Y_MAX ? ORIGIN_Y_MAX : originY;

            // ---- Step 1: pack BOTH sheets into ONE 256x40 seat (WF MakeBorderAP x2). ----
            var seat = new BattleAnimeOAMImportCore.Seat(SEAT_TILE_WIDTH, SEAT_BORDERAP_TILE_HEIGHT);
            var battleOam = new List<byte>(512);
            var battleOamSplit = new List<uint>(2);

            // frame 0 (border outline/fill) -> accumulate, record split.
            if (!PackSheetIntoSeat(sheetIndexed, seat, battleOam))
                return Fail("The world map border image is too large to fit in the 256x160 sheet.");
            battleOamSplit.Add((uint)battleOam.Count);

            // frame 1 (name text) -> accumulate into the SAME seat, record split.
            if (!PackSheetIntoSeat(nameIndexed, seat, battleOam))
                return Fail("The world map border image is too large to fit in the 256x160 sheet.");
            battleOamSplit.Add((uint)battleOam.Count);

            // ---- Step 2: 12-byte battle OAM -> 6-byte AP OAM (WF BattleOAMToAPOAM). ----
            var apOamSplit = new List<uint>(2);
            byte[] apOam = BattleOAMToAPOAM(battleOam.ToArray(), battleOamSplit, clampedX, clampedY, apOamSplit);
            if (apOam == null || apOamSplit.Count < 2)
                return Fail("Failed to convert the world map border OAM data.");

            // ---- Step 3: build the AP-data block (WF ImportBorder structure). ----
            byte[] apBytes = BuildApData(apOam, apOamSplit);

            // ---- Step 4: encode the seat as raw 4bpp tile data (= WF out_image). ----
            byte[] image = EncodeSeat4bpp(seat);

            byte[] pal = new byte[32];
            Array.Copy(palette16, 0, pal, 0, 32);

            return new BorderAssembleResult
            {
                ImageBytes = image,
                ApBytes = apBytes,
                Palette = pal,
                ApOamSplit = apOamSplit.ToArray(),
                ClampedOriginX = clampedX,
                ClampedOriginY = clampedY,
                Error = null,
            };
        }

        // ====================================================================
        // Seat packing — port of WF MakeBattleAnimeLow / tryCopy for the border
        // single-palette path (one 256x40 seat, no terminator appended per sheet:
        // WF MakeBattleAnime DOES append a terminator at the end of each call, so
        // we mirror that — the split is recorded AFTER the terminator).
        // ====================================================================

        /// <summary>
        /// Pack one 248×160 indexed sheet into the (possibly partially-filled)
        /// <paramref name="seat"/>, emitting 12-byte battle-OAM records into
        /// <paramref name="oam"/> and a trailing terminator. Returns false when the
        /// seat overflows (WF: a second seat would be needed → GetImages().Count≥2).
        /// Single-palette only (border is palette_count=1), so only bank 0 is packed.
        /// </summary>
        static bool PackSheetIntoSeat(byte[] sheetIndexed,
            BattleAnimeOAMImportCore.Seat seat, List<byte> oam)
        {
            // WF: the source for the border path is single-palette; CopyByPalette(0)
            // re-bases bank-0 pixels to 0..15 (and zeroes the rest). For an already
            // single-bank 16-color sheet this is the identity for bank 0.
            byte[] bankPixels = BattleAnimeOAMImportCore.ExtractByPaletteBank(
                sheetIndexed, SRC_WIDTH, SRC_HEIGHT, 0);

            int backupOamPos = oam.Count;
            if (!PackBankIntoSeat(bankPixels, seat, oam))
            {
                // Seat full: WF would NextSeat() and retry, producing a 2nd image
                // → GetImages().Count >= 2 → rejected by the importer. We surface
                // that as an overflow (false) and roll back the partial OAM.
                oam.RemoveRange(backupOamPos, oam.Count - backupOamPos);
                return false;
            }

            // WF MakeBattleAnime appends a terminator OAM after each sheet.
            AppendTerminator(oam);
            return true;
        }

        /// <summary>
        /// The greedy largest-first rectangle packer (WF MakeBattleAnimeLow). Scans
        /// every non-blank 8×8 tile of <paramref name="bankPixels"/> and packs it
        /// (or a larger rectangle of contiguous non-blank tiles) into the seat,
        /// emitting one 12-byte battle-OAM record per rectangle. Returns false on
        /// seat overflow.
        /// </summary>
        static bool PackBankIntoSeat(byte[] bankPixels,
            BattleAnimeOAMImportCore.Seat seat, List<byte> oam)
        {
            // Blank-tile map (also marks the top-right palette-map tile as used).
            bool[] useTileData = BattleAnimeOAMImportCore.MakeUseTileData(
                bankPixels, SRC_WIDTH, SRC_HEIGHT);

            int end = useTileData.Length;
            for (int i = 0; i < end; i++)
            {
                if (useTileData[i]) continue; // blank or already emitted

                int bx = i % SCREEN_TILE_WIDTH;
                int by = i / SCREEN_TILE_WIDTH;
                int vramX = bx * 8 - BITMAP_ADDX;
                int vramY = by * 8 - BITMAP_ADDY;

                int sx, sy;
                // Largest-first ordering exactly as WF MakeBattleAnimeLow (non-magic).
                if (TryCopy(bankPixels, useTileData, seat, i, 8, 8, out sx, out sy)) { EmitOAM(oam, SHAPE_SQUARE,     SIZE_TIMES8, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 8, 4, out sx, out sy)) { EmitOAM(oam, SHAPE_HORIZONTAL, SIZE_TIMES8, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 4, 8, out sx, out sy)) { EmitOAM(oam, SHAPE_VERTICAL,   SIZE_TIMES8, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 4, 4, out sx, out sy)) { EmitOAM(oam, SHAPE_SQUARE,     SIZE_TIMES4, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 4, 2, out sx, out sy)) { EmitOAM(oam, SHAPE_HORIZONTAL, SIZE_TIMES4, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 2, 4, out sx, out sy)) { EmitOAM(oam, SHAPE_VERTICAL,   SIZE_TIMES4, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 2, 2, out sx, out sy)) { EmitOAM(oam, SHAPE_SQUARE,     SIZE_TIMES2, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 4, 1, out sx, out sy)) { EmitOAM(oam, SHAPE_HORIZONTAL, SIZE_TIMES2, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 1, 4, out sx, out sy)) { EmitOAM(oam, SHAPE_VERTICAL,   SIZE_TIMES2, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 2, 1, out sx, out sy)) { EmitOAM(oam, SHAPE_HORIZONTAL, SIZE_TIMES1, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 1, 2, out sx, out sy)) { EmitOAM(oam, SHAPE_VERTICAL,   SIZE_TIMES1, sx, sy, vramX, vramY); continue; }
                if (TryCopy(bankPixels, useTileData, seat, i, 1, 1, out sx, out sy)) { EmitOAM(oam, SHAPE_SQUARE,     SIZE_TIMES1, sx, sy, vramX, vramY); continue; }

                return false; // seat full
            }
            return true;
        }

        /// <summary>
        /// Port of WF <c>ImportOAM.tryCopy</c>: try to place a w×h tile rectangle
        /// (image-tile index <paramref name="imgTileIdx"/>) into the seat — reuse an
        /// identical existing block if present, else find an empty seat slot and
        /// copy. On success marks the image + seat tiles used and returns the seat
        /// tile coords. Uses the BattleAnimeOAMImportCore Grep/Extract helpers so the
        /// dedup logic is shared (not duplicated).
        /// </summary>
        static bool TryCopy(byte[] bankPixels, bool[] useTileData,
            BattleAnimeOAMImportCore.Seat seat, int imgTileIdx, int w, int h,
            out int outSeatTileX, out int outSeatTileY)
        {
            outSeatTileX = 0;
            outSeatTileY = 0;

            int bx = imgTileIdx % SCREEN_TILE_WIDTH;
            int by = imgTileIdx / SCREEN_TILE_WIDTH;

            // 1. image bounds.
            if (bx + w > SCREEN_TILE_WIDTH) return false;
            if (by + h > SCREEN_TILE_HEIGHT) return false;

            // 2. all w×h source tiles must be unprocessed.
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    if (useTileData[(bx + dx) + (by + dy) * SCREEN_TILE_WIDTH]) return false;

            byte[] block = BattleAnimeOAMImportCore.ExtractBlock(
                bankPixels, SRC_WIDTH, bx * 8, by * 8, w * 8, h * 8);

            // 3. reuse an identical seat block.
            if (BattleAnimeOAMImportCore.GrepBlockInSeat(seat, block, w * 8, h * 8, out outSeatTileX, out outSeatTileY))
            {
                MarkUsed(useTileData, bx, by, w, h, SCREEN_TILE_WIDTH);
                return true;
            }

            // 4. find an empty w×h seat slot.
            for (int sy = 0; sy <= seat.TileH - h; sy++)
                for (int sx = 0; sx <= seat.TileW - w; sx++)
                {
                    if (SeatSlotEmpty(seat, sx, sy, w, h))
                    {
                        CopyBlockToSeat(seat, block, w * 8, h * 8, sx, sy);
                        MarkUsed(seat.Used, sx, sy, w, h, seat.TileW);
                        MarkUsed(useTileData, bx, by, w, h, SCREEN_TILE_WIDTH);
                        outSeatTileX = sx;
                        outSeatTileY = sy;
                        return true;
                    }
                }
            return false;
        }

        static bool SeatSlotEmpty(BattleAnimeOAMImportCore.Seat seat, int sx, int sy, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    if (seat.Used[(sx + dx) + (sy + dy) * seat.TileW]) return false;
            return true;
        }

        static void CopyBlockToSeat(BattleAnimeOAMImportCore.Seat seat, byte[] block,
            int blockW, int blockH, int seatTileX, int seatTileY)
        {
            int dstX = seatTileX * 8;
            int dstY = seatTileY * 8;
            for (int y = 0; y < blockH; y++)
                Array.Copy(block, y * blockW, seat.Pixels, (dstY + y) * seat.PixW + dstX, blockW);
        }

        static void MarkUsed(bool[] flags, int startX, int startY, int w, int h, int gridW)
        {
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    flags[(startX + dx) + (startY + dy) * gridW] = true;
        }

        // ====================================================================
        // 12-byte battle OAM emission (WF AppendOAM / AppendTermOAM)
        // ====================================================================

        static void EmitOAM(List<byte> oam, byte alignShape, byte areaSize,
            int seatTileX, int seatTileY, int vramX, int vramY)
        {
            oam.Add(0x00);
            oam.Add(alignShape);
            oam.Add(0x00);
            oam.Add(areaSize);
            oam.Add((byte)((seatTileX & 0x1F) | ((seatTileY << 5) & 0xE0)));
            oam.Add(0x00); // border is single-palette: oam_palette = 0
            short sx = (short)vramX;
            oam.Add((byte)(sx & 0xFF));
            oam.Add((byte)((sx >> 8) & 0xFF));
            short sy = (short)vramY;
            oam.Add((byte)(sy & 0xFF));
            oam.Add((byte)((sy >> 8) & 0xFF));
            oam.Add(0x00);
            oam.Add(0x00);
        }

        static void AppendTerminator(List<byte> oam)
        {
            oam.Add(0x01);
            for (int i = 0; i < 11; i++) oam.Add(0x00);
        }

        // ====================================================================
        // Seat -> raw 4bpp tile data (WF ImageUtil.ImageToByte16Tile)
        // ====================================================================

        static byte[] EncodeSeat4bpp(BattleAnimeOAMImportCore.Seat seat)
        {
            int tileW = seat.TileW;
            int tileH = seat.TileH;
            byte[] data = new byte[tileW * tileH * 32];
            int outIdx = 0;
            for (int ty = 0; ty < tileH; ty++)
                for (int tx = 0; tx < tileW; tx++)
                {
                    int baseX = tx * 8;
                    int baseY = ty * 8;
                    for (int py = 0; py < 8; py++)
                    {
                        int rowBase = (baseY + py) * seat.PixW + baseX;
                        for (int px = 0; px < 8; px += 2)
                        {
                            byte lo = (byte)(seat.Pixels[rowBase + px] & 0x0F);
                            byte hi = (byte)(seat.Pixels[rowBase + px + 1] & 0x0F);
                            data[outIdx++] = (byte)(lo | (hi << 4));
                        }
                    }
                }
            return data;
        }

        // ====================================================================
        // BattleOAMToAPOAM — port of WF ImageUtilBorderAP.BattleOAMToAPOAM
        // (12-byte battle OAM -> 6-byte AP OAM). Faithful incl. the WF quirks:
        //   * the early-break checks battleOAM[0] (the WHOLE array's first byte),
        //     not battleOAM[i] — so it only short-circuits if the very first entry
        //     is a terminator; otherwise EVERY 12-byte entry (incl. the two
        //     terminator entries) is converted. Ported as-is for bit-for-bit parity.
        //   * MaxRectangle recenters; a >=0x80 tall footprint shifts the Y origin.
        // ====================================================================

        const int BITMAP_ADDX_AP = BITMAP_ADDX; // alias for clarity vs the WF names
        const int BITMAP_ADDY_AP = BITMAP_ADDY;

        static byte[] BattleOAMToAPOAM(byte[] battleOAM, List<uint> battleOAMSplit,
            uint originX, uint originY, List<uint> outApOAMSplit)
        {
            if (battleOAM == null || battleOAM.Length % 12 != 0) return null;

            var ret = new List<byte>(battleOAM.Length / 2);

            int shiftX = (int)originX;
            int shiftY = (int)originY;

            // Max draw range over the WHOLE combined battle OAM (WF MaxRectngle).
            BattleOAMMaxRectangle(battleOAM, out int rcLeft, out int rcTop, out int rcWidth, out int rcHeight);
            if (rcHeight >= 0x80)
            {
                // AP OAM can only store Y up to 0x80; shift the origin for the rest.
                shiftY = rcHeight - 0x80;
            }

            int n = 0;
            for (int i = 0; i < battleOAM.Length; i += 12)
            {
                // WF quirk: checks index 0, not i.
                if (battleOAM[0] == 1) break;

                if (n < battleOAMSplit.Count && (uint)i >= battleOAMSplit[n])
                {
                    outApOAMSplit.Add((uint)ret.Count);
                    n++;
                }

                uint oam0 = 0, oam1 = 0, oam2 = 0;

                int x = (short)U.u16(battleOAM, (uint)i + 6);
                int y = (short)U.u16(battleOAM, (uint)i + 8);
                x += BITMAP_ADDX_AP;
                y += BITMAP_ADDY_AP;

                sbyte image_x = (sbyte)(x - rcLeft - shiftX);
                sbyte image_y = (sbyte)(y - rcTop - shiftY);

                uint tile = battleOAM[i + 4];
                uint tile_x = tile & 0x1F;
                uint tile_y = (tile & 0xE0) >> 5;
                uint apTile = tile_x + (tile_y * 32);

                oam0 |= (uint)((battleOAM[i + 1] & 0xC0) << 8);
                oam1 |= (uint)((battleOAM[i + 3] & 0xC0) << 8);

                oam1 |= (uint)(image_x & 0x1FF);
                oam0 |= (uint)(image_y & 0x0FF);

                oam2 |= (apTile & 0x3FF);

                AppendU16(ret, oam0);
                AppendU16(ret, oam1);
                AppendU16(ret, oam2);
            }
            outApOAMSplit.Add((uint)ret.Count);
            return ret.ToArray();
        }

        static void BattleOAMMaxRectangle(byte[] battle, out int left, out int top, out int width, out int height)
        {
            int xTop = 256, yTop = 160, xBottom = 0, yBottom = 0;
            for (int i = 0; i < battle.Length; i += 12)
            {
                if (battle[0] == 1) break; // WF quirk: index 0
                int x = (short)U.u16(battle, (uint)i + 6);
                int y = (short)U.u16(battle, (uint)i + 8);
                x += BITMAP_ADDX_AP;
                y += BITMAP_ADDY_AP;
                if (x < xTop) xTop = x;
                if (x > xBottom) xBottom = x;
                if (y < yTop) yTop = y;
                if (y > yBottom) yBottom = y;
            }
            left = xTop;
            top = yTop;
            width = xBottom - xTop;
            height = yBottom - yTop;
        }

        // ====================================================================
        // AP-data block builder — port of WF ImportBorder's structure code.
        //
        // ap_data header SHORTs: (frame_list - ap_data)=4, (anime_list - ap_data)=8.
        // frame_list: SHORT (frame_0 - frame_list), SHORT (frame_1 - frame_list).
        // anime_list: SHORT (anim_0 - anime_list), SHORT (anim_1 - anime_list).
        // frame_0: SHORT oam-entry-count, then apOAMSplit[0] AP-OAM bytes.
        // frame_1: SHORT oam-entry-count, then apOAMSplit[1]-apOAMSplit[0] bytes.
        // anim_0: 4,0  0,0xffff (loop). anim_1: 4,1  0,0xffff (loop).
        // The four list offsets are back-patched after the layout is known.
        // ====================================================================

        static byte[] BuildApData(byte[] apOam, List<uint> apOamSplit)
        {
            var newOam = new List<byte>(apOam.Length + 32);

            // ap_data header.
            AppendU16(newOam, 4); // (frame_list - ap_data)
            AppendU16(newOam, 8); // (anime_list - ap_data)

            // frame_list (placeholders, back-patched).
            uint addrFrameList = (uint)newOam.Count;
            AppendU16(newOam, 0);
            AppendU16(newOam, 0);

            // anime_list (placeholders, back-patched).
            uint addrAnimList = (uint)newOam.Count;
            AppendU16(newOam, 0);
            AppendU16(newOam, 0);

            // frame_0.
            uint addrFrame0 = (uint)newOam.Count;
            AppendU16(newOam, apOamSplit[0] / 6); // oam entry count
            for (uint i = 0; i < apOamSplit[0]; i++) newOam.Add(apOam[i]);

            // frame_1.
            uint addrFrame1 = (uint)newOam.Count;
            AppendU16(newOam, (apOamSplit[1] - apOamSplit[0]) / 6);
            for (uint i = apOamSplit[0]; i < apOamSplit[1]; i++) newOam.Add(apOam[i]);

            // anim_0.
            uint addrAnim0 = (uint)newOam.Count;
            AppendU16(newOam, 4); AppendU16(newOam, 0);
            AppendU16(newOam, 0); AppendU16(newOam, 0xffff);

            // anim_1.
            uint addrAnim1 = (uint)newOam.Count;
            AppendU16(newOam, 4); AppendU16(newOam, 1);
            AppendU16(newOam, 0); AppendU16(newOam, 0xffff);

            byte[] outOam = newOam.ToArray();
            // Back-patch the four list offsets (relative to their list start).
            U.write_u16(outOam, 4,  addrFrame0 - addrFrameList);
            U.write_u16(outOam, 6,  addrFrame1 - addrFrameList);
            U.write_u16(outOam, 8,  addrAnim0  - addrAnimList);
            U.write_u16(outOam, 10, addrAnim1  - addrAnimList);
            return outOam;
        }

        static void AppendU16(List<byte> list, uint value)
        {
            list.Add((byte)(value & 0xFF));
            list.Add((byte)((value >> 8) & 0xFF));
        }
    }
}
