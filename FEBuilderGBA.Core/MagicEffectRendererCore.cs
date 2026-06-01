// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform magic-effect frame renderer for the Avalonia
// ImageMagicFEditorView (#852).
//
// WinForms reference:
//   FEBuilderGBA/ImageUtilMagicFEditor.cs — Draw(), FindFrame(),
//   DrawFrameImage() methods (~lines 296-466).
//   FEBuilderGBA/ImageMagicFEditorForm.cs — DrawSelectedAnime() (~line 141).
//
// Canvas: 240×128 (SCREEN_TILE_WIDTH=30 tiles × 8, SCREEN_TILE_HEIGHT=16 tiles × 8).
// Composite order: BG → back OAM (+12) → front OAM (+8), all with isMagicOAM=true.
// OBJ source sheet width = 256; height = max(64, CalcHeight(256, objLen)).
// Palettes at +20 and +24 are RAW (NOT LZ77). LZ77 streams at +4 and +16.

using System;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform render helper for the FEditor magic-effect frame preview (#852).
    /// Renders a single 0x86 frame from the FEditor magic-animation script as a 240×128
    /// IImage. Read-only: no ROM writes happen here.
    /// </summary>
    public static class MagicEffectRendererCore
    {
        // Magic canvas dimensions (WF ImageUtilMagicFEditor constants).
        /// <summary>Screen width in 8-pixel tiles — mirrors WF SCREEN_TILE_WIDTH (240/8=30).</summary>
        public const int MAGIC_SCREEN_TILE_WIDTH = 30;   // 240 / 8
        /// <summary>Screen height in 8-pixel tiles — mirrors WF SCREEN_TILE_HEIGHT (64*2/8=16).</summary>
        public const int MAGIC_SCREEN_TILE_HEIGHT = 16;  // 64 * 2 / 8
        /// <summary>Canvas width in pixels.</summary>
        public const int MAGIC_CANVAS_WIDTH = MAGIC_SCREEN_TILE_WIDTH * 8;   // 240
        /// <summary>Canvas height in pixels.</summary>
        public const int MAGIC_CANVAS_HEIGHT = MAGIC_SCREEN_TILE_HEIGHT * 8; // 128

        // BG tile-sheet constants (WF BG_SEAT_TILE_WIDTH/HEIGHT).
        const int BG_SHEET_WIDTH_TILES = 256 / 8;  // 32
        const int BG_SHEET_HEIGHT_TILES = 64 / 8;  // 8
        const int BG_SHEET_WIDTH = BG_SHEET_WIDTH_TILES * 8;   // 256
        const int BG_SHEET_HEIGHT = BG_SHEET_HEIGHT_TILES * 8; // 64

        // OBJ tile-sheet width (WF uses 256).
        const int OBJ_SHEET_WIDTH = 256;
        // OBJ min sheet height (WF uses max(64, CalcHeight(256, objLen))).
        const int OBJ_SHEET_MIN_HEIGHT = 64;

        // Tile/4bpp constants (mirrors BattleAnimeRendererCore).
        const int TILE_SIZE = 8;
        const int BYTES_PER_TILE_4BPP = 32; // 4 bits/pixel × 8×8 = 32 bytes

        /// <summary>
        /// Render a single 0x86 frame from a FEditor magic-animation script.
        /// Mirrors WF <c>ImageUtilMagicFEditor.Draw</c> and
        /// <c>DrawFrameImage</c> but writes to an <see cref="IImage"/>
        /// (240×128) instead of a <see cref="System.Drawing.Bitmap"/>.
        ///
        /// <para>FE-gate: if <see cref="ImageUtilMagicCore.SearchMagicSystem"/>
        /// reports <see cref="ImageUtilMagicCore.MagicSystem.No"/>, returns
        /// <c>null</c> immediately without reading any frame data.</para>
        ///
        /// <para>Null-safe: returns <c>null</c> (and sets <paramref name="log"/>)
        /// on any bad pointer / truncated LZ77 / out-of-bounds data; never
        /// throws.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="frameDataAddr">GBA pointer (or raw offset) to the frame-data script.
        ///   Mirrors WF <c>P0</c>. Will be converted via <c>U.toOffset</c> if it is a GBA
        ///   pointer (≥0x08000000), otherwise used as-is.</param>
        /// <param name="frameIndex">0-based index of the 0x86 frame to render.</param>
        /// <param name="objRightToLeftOAM">GBA pointer to the front (right-to-left) OAM table.
        ///   Mirrors WF <c>P4</c>.</param>
        /// <param name="objBGRightToLeftOAM">GBA pointer to the back (BG right-to-left) OAM table.
        ///   Mirrors WF <c>P12</c>.</param>
        /// <param name="log">Diagnostic log set on success (lists BGIMG/BGPAL/OBJIMG/OBJPAL
        ///   addresses) and on failure (error description).</param>
        /// <returns>A 240×128 <see cref="IImage"/> on success, <c>null</c> on any
        ///   failure.</returns>
        public static IImage RenderMagicFrame(
            ROM rom,
            uint frameDataAddr,
            uint frameIndex,
            uint objRightToLeftOAM,
            uint objBGRightToLeftOAM,
            out string log)
        {
            log = string.Empty;

            if (rom == null || rom.Data == null)
            {
                log = "ROM not loaded.";
                return null;
            }
            if (CoreState.ImageService == null)
            {
                log = "ImageService not available.";
                return null;
            }

            // FE-gate: magic system must be installed.
            uint unused1, unused2, unused3;
            var ms = ImageUtilMagicCore.SearchMagicSystem(rom, out unused1, out unused2, out unused3);
            if (ms == ImageUtilMagicCore.MagicSystem.No)
            {
                log = "No magic system patch detected.";
                return null;
            }

            // Convert GBA pointers to ROM offsets.
            uint frameDataOffset = U.isSafetyPointer(frameDataAddr)
                ? U.toOffset(frameDataAddr) : frameDataAddr;
            uint objOAMOffset = U.isSafetyPointer(objRightToLeftOAM)
                ? U.toOffset(objRightToLeftOAM) : objRightToLeftOAM;
            uint bgOAMOffset = U.isSafetyPointer(objBGRightToLeftOAM)
                ? U.toOffset(objBGRightToLeftOAM) : objBGRightToLeftOAM;

            if (!U.isSafetyOffset(frameDataOffset, rom))
            {
                log = $"BAD FRAMEDATA_OFFSET 0x{frameDataOffset:X08}";
                return null;
            }

            // Find the Nth 0x86 frame.
            uint frameRecordOffset = FindMagicFrame(rom, frameDataOffset, frameIndex);
            if (frameRecordOffset == U.NOT_FOUND)
            {
                log = $"Frame {frameIndex} not found.";
                return null;
            }

            return DrawMagicFrameInternal(
                rom, frameRecordOffset, objOAMOffset, bgOAMOffset, out log);
        }

        /// <summary>
        /// Find the byte offset of the Nth 0x86 record in the FEditor magic frame-data stream.
        /// Mirrors WF <c>ImageUtilMagicFEditor.FindFrame</c> exactly:
        /// <list type="bullet">
        ///   <item>byte at [n+3]==0x80 → terminator AND STOP, EXCEPT when [n+1]==0x01
        ///     (the 0x00 0x01 0x00 0x80 continuation) → continue.</item>
        ///   <item>byte at [n+3]==0x85 → skip (continue; advance n+=4 only; NOT counted as a frame).</item>
        ///   <item>byte at [n+3]==0x86 → frame: if it is the Nth (frameI==frameIndex) return this offset;
        ///     else frameI++ and n+=24 (so total n+=28).</item>
        ///   <item>else → break (unknown command).</item>
        /// </list>
        /// EOF bound: guard <c>n + 4 &lt;= rom.Data.Length</c> (and the n+1 read) every
        /// iteration. WF has a latent 3-byte overrun at EOF; we do NOT reproduce it.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="frameDataOffset">Resolved ROM offset of the frame-data script.</param>
        /// <param name="frameIndex">0-based frame index to find.</param>
        /// <returns>ROM offset of the start of the 28-byte 0x86 frame record, or
        ///   <c>U.NOT_FOUND</c> when not found / past limiter.</returns>
        public static uint FindMagicFrame(ROM rom, uint frameDataOffset, uint frameIndex)
        {
            if (rom == null || rom.Data == null) return U.NOT_FOUND;
            uint dataLen = (uint)rom.Data.Length;

            // Safety limiter: 1 MB scan (mirrors WF).
            uint limiter = frameDataOffset + 1024 * 1024;
            if (limiter > dataLen) limiter = dataLen;

            uint frameI = 0;
            for (uint n = frameDataOffset; n < limiter; n += 4)
            {
                // EOF guard: need at least 4 bytes at n.
                if (n + 4 > dataLen) break;

                byte cmd = rom.Data[n + 3]; // command byte is at +3

                if (cmd == 0x80) // terminator
                {
                    // Guard: [n+1] read.
                    if (n + 2 <= dataLen && rom.Data[n + 1] == 0x01)
                    {
                        // 0x00 0x01 0x00 0x80 continuation → advance n+=4, do NOT stop.
                        continue;
                    }
                    break;
                }

                if (cmd == 0x85) // command — skip; NOT a frame; advance n+=4 only.
                {
                    continue;
                }

                if (cmd != 0x86) // unknown command → stop.
                {
                    break;
                }

                // 0x86 frame record. The full record is 28 bytes (4 header + 24 data).
                // Check we can read the full record at n.
                if (n + 28 > dataLen) break;

                if (frameI == frameIndex)
                {
                    return n; // found the Nth frame
                }

                frameI++;
                n += 24; // advance 24 extra (loop will add 4 more → total 28)
            }

            return U.NOT_FOUND;
        }

        /// <summary>
        /// Parse the 7 pointer/offset fields from a 28-byte 0x86 frame record.
        /// Mirrors WF <c>DrawFrameImage</c> header layout comments.
        ///
        /// Layout (offsets relative to record start):
        /// <code>
        ///   +0  frame header (u32, cmd byte at +3 = 0x86)
        ///   +4  objImagePointer    (GBA pointer → toOffset)
        ///   +8  OAMAbsoStart       (raw u32 offset, RELATIVE to objRightToLeftOAM base)
        ///   +12 OAMBGAbsoStart     (raw u32 offset, RELATIVE to objBGRightToLeftOAM base)
        ///   +16 bgImagePointer     (GBA pointer → toOffset)
        ///   +20 objPalettePointer  (GBA pointer → toOffset)
        ///   +24 bgPalettePointer   (GBA pointer → toOffset)
        /// </code>
        /// </summary>
        public static bool TryReadMagicFrameHeader(
            ROM rom,
            uint frameRecordOffset,
            out uint objImageOffset,
            out uint oamAbsoStart,
            out uint oamBGAbsoStart,
            out uint bgImageOffset,
            out uint objPaletteOffset,
            out uint bgPaletteOffset)
        {
            objImageOffset = 0; oamAbsoStart = 0; oamBGAbsoStart = 0;
            bgImageOffset = 0; objPaletteOffset = 0; bgPaletteOffset = 0;

            if (rom == null || rom.Data == null) return false;
            if (frameRecordOffset + 28 > (uint)rom.Data.Length) return false;

            // +4 objImagePointer — GBA pointer
            uint rawObjImg = U.u32(rom.Data, frameRecordOffset + 4);
            if (!U.isSafetyPointer(rawObjImg)) return false;
            objImageOffset = U.toOffset(rawObjImg);

            // +8  OAMAbsoStart — raw u32 relative offset (NOT a GBA pointer)
            oamAbsoStart = U.u32(rom.Data, frameRecordOffset + 8);

            // +12 OAMBGAbsoStart — raw u32 relative offset
            oamBGAbsoStart = U.u32(rom.Data, frameRecordOffset + 12);

            // +16 bgImagePointer — GBA pointer
            uint rawBgImg = U.u32(rom.Data, frameRecordOffset + 16);
            if (!U.isSafetyPointer(rawBgImg)) return false;
            bgImageOffset = U.toOffset(rawBgImg);

            // +20 objPalettePointer — GBA pointer
            uint rawObjPal = U.u32(rom.Data, frameRecordOffset + 20);
            if (!U.isSafetyPointer(rawObjPal)) return false;
            objPaletteOffset = U.toOffset(rawObjPal);

            // +24 bgPalettePointer — GBA pointer
            uint rawBgPal = U.u32(rom.Data, frameRecordOffset + 24);
            if (!U.isSafetyPointer(rawBgPal)) return false;
            bgPaletteOffset = U.toOffset(rawBgPal);

            return true;
        }

        // ------------------------------------------------------------------
        // Internal render helpers
        // ------------------------------------------------------------------

        static IImage DrawMagicFrameInternal(
            ROM rom,
            uint frameRecordOffset,
            uint objOAMOffset,
            uint bgOAMOffset,
            out string log)
        {
            log = string.Empty;

            uint objImageOffset, oamAbsoStart, oamBGAbsoStart,
                 bgImageOffset, objPaletteOffset, bgPaletteOffset;
            if (!TryReadMagicFrameHeader(rom, frameRecordOffset,
                    out objImageOffset, out oamAbsoStart, out oamBGAbsoStart,
                    out bgImageOffset, out objPaletteOffset, out bgPaletteOffset))
            {
                log = $"BAD FRAME HEADER at 0x{frameRecordOffset:X08}";
                return null;
            }

            IImageService svc = CoreState.ImageService;

            // ---- OBJ palette (+20) — RAW (NOT LZ77) ----
            if (objPaletteOffset + 0x20 > (uint)rom.Data.Length)
            {
                log = $"OBJPAL out of bounds 0x{objPaletteOffset:X08}";
                return null;
            }
            byte[] objPalette = rom.getBinaryData(objPaletteOffset, 0x20);

            // ---- BG palette (+24) — RAW (NOT LZ77) ----
            if (bgPaletteOffset + 0x20 > (uint)rom.Data.Length)
            {
                log = $"BGPAL out of bounds 0x{bgPaletteOffset:X08}";
                return null;
            }
            byte[] bgPalette = rom.getBinaryData(bgPaletteOffset, 0x20);

            // ---- Canvas — 240×128 filled with BG palette color 0 (transparent) ----
            byte[] dstPixels = new byte[MAGIC_CANVAS_WIDTH * MAGIC_CANVAS_HEIGHT * 4];

            // ---- BG tiles (+16) — LZ77 ----
            {
                if (!U.isSafetyOffset(bgImageOffset, rom))
                {
                    log = $"BAD BGIMG_OFFSET 0x{bgImageOffset:X08}";
                    return null;
                }
                uint bgCompSize = LZ77.getCompressedSize(rom.Data, bgImageOffset);
                if (bgCompSize == 0)
                {
                    log = $"BG LZ77 header invalid at 0x{bgImageOffset:X08}";
                    return null;
                }
                if ((ulong)bgImageOffset + bgCompSize > (ulong)rom.Data.Length)
                {
                    log = $"BG LZ77 stream truncated at 0x{bgImageOffset:X08}";
                    return null;
                }
                byte[] bgDecomp = LZ77.decompress(rom.Data, bgImageOffset);
                if (bgDecomp == null || bgDecomp.Length == 0)
                {
                    log = $"BG LZ77 decompress failed at 0x{bgImageOffset:X08}";
                    return null;
                }

                // Render BG: 256×bgHeight tile-sheet → scale left 240px to fill 240×128.
                int bgHeight = CalcHeight(BG_SHEET_WIDTH, bgDecomp.Length);

                // Decode BG tiles to RGBA sheet (bgWidth=256, bgHeight).
                byte[] bgSheet = DecodeTilesToRGBA(bgDecomp, bgPalette, BG_SHEET_WIDTH, bgHeight);

                // Scale the left 240px of the 256-wide BG vertically to 240×128.
                ScaleBlit(bgSheet, BG_SHEET_WIDTH, bgHeight,
                          0, 0, MAGIC_CANVAS_WIDTH, bgHeight,
                          dstPixels, MAGIC_CANVAS_WIDTH, MAGIC_CANVAS_HEIGHT,
                          0, 0, MAGIC_CANVAS_WIDTH, MAGIC_CANVAS_HEIGHT);

                var rawObjPtr = U.u32(rom.Data, frameRecordOffset + 4);
                var rawBgPtr  = U.u32(rom.Data, frameRecordOffset + 16);
                var rawObjPal = U.u32(rom.Data, frameRecordOffset + 20);
                var rawBgPal  = U.u32(rom.Data, frameRecordOffset + 24);
                log = $"BGIMG 0x{rawBgPtr:X08}, BGPAL 0x{rawBgPal:X08}, OBJIMG 0x{rawObjPtr:X08}, OBJPAL 0x{rawObjPal:X08}";
            }

            // ---- OBJ tiles (+4) — LZ77 ----
            {
                if (!U.isSafetyOffset(objImageOffset, rom))
                {
                    log = $"BAD OBJIMG_OFFSET 0x{objImageOffset:X08}";
                    return null;
                }
                uint objCompSize = LZ77.getCompressedSize(rom.Data, objImageOffset);
                if (objCompSize == 0)
                {
                    log = $"OBJ LZ77 header invalid at 0x{objImageOffset:X08}";
                    return null;
                }
                if ((ulong)objImageOffset + objCompSize > (ulong)rom.Data.Length)
                {
                    log = $"OBJ LZ77 stream truncated at 0x{objImageOffset:X08}";
                    return null;
                }
                byte[] objDecomp = LZ77.decompress(rom.Data, objImageOffset);
                if (objDecomp == null || objDecomp.Length == 0)
                {
                    log = $"OBJ LZ77 decompress failed at 0x{objImageOffset:X08}";
                    return null;
                }

                // OBJ tile sheet: 256 × max(64, CalcHeight(256, objLen)).
                int objHeight = CalcHeight(OBJ_SHEET_WIDTH, objDecomp.Length);
                if (objHeight < OBJ_SHEET_MIN_HEIGHT) objHeight = OBJ_SHEET_MIN_HEIGHT;

                // Decode OBJ tiles to RGBA sheet.
                byte[] objSheet = DecodeTilesToRGBA(objDecomp, objPalette, OBJ_SHEET_WIDTH, objHeight);

                // ---- Back OAM layer (+12 relative to bgOAMOffset) ----
                uint backOAMStart = bgOAMOffset + oamBGAbsoStart;
                BattleAnimeRendererCore.DrawOAMSprites(
                    rom.Data, backOAMStart,
                    objSheet, OBJ_SHEET_WIDTH, objHeight,
                    dstPixels, MAGIC_CANVAS_WIDTH, MAGIC_CANVAS_HEIGHT,
                    isMagicOAM: true);

                // ---- Front OAM layer (+8 relative to objOAMOffset) ----
                uint frontOAMStart = objOAMOffset + oamAbsoStart;
                BattleAnimeRendererCore.DrawOAMSprites(
                    rom.Data, frontOAMStart,
                    objSheet, OBJ_SHEET_WIDTH, objHeight,
                    dstPixels, MAGIC_CANVAS_WIDTH, MAGIC_CANVAS_HEIGHT,
                    isMagicOAM: true);
            }

            var image = svc.CreateImage(MAGIC_CANVAS_WIDTH, MAGIC_CANVAS_HEIGHT);
            image.SetPixelData(dstPixels);
            return image;
        }

        // ------------------------------------------------------------------
        // Shared 4bpp tile decode (mirrors BattleAnimeRendererCore's decode
        // loop at ~lines 392-430 but for an arbitrary sheet size with
        // index-0 → alpha 0).
        // ------------------------------------------------------------------

        /// <summary>
        /// Decode LZ77-decompressed 4bpp tile data to an RGBA byte array
        /// arranged as a (sheetWidth × sheetHeight) pixel grid.
        /// Index 0 → alpha 0 (transparent). Compatible with
        /// <see cref="BattleAnimeRendererCore.DrawOAMSprites"/>'s source-pixel
        /// format.
        ///
        /// <para>This is the same logic as the decode loop in
        /// <c>BattleAnimeRendererCore.RenderSingleFrame</c> but operates on
        /// an arbitrary sheet height (instead of a fixed 64px).</para>
        /// </summary>
        internal static byte[] DecodeTilesToRGBA(
            byte[] tileData, byte[] gbaPalette,
            int sheetWidth, int sheetHeight)
        {
            if (tileData == null || gbaPalette == null) return new byte[0];

            IImageService svc = CoreState.ImageService;
            byte[] pixels = new byte[sheetWidth * sheetHeight * 4];

            int tilesPerRow = sheetWidth / TILE_SIZE;
            int tilesPerCol = sheetHeight / TILE_SIZE;
            int totalSheetTiles = tilesPerRow * tilesPerCol;
            int totalDataTiles = tileData.Length / BYTES_PER_TILE_4BPP;
            int tileCount = Math.Min(totalDataTiles, totalSheetTiles);

            for (int t = 0; t < tileCount; t++)
            {
                int tileOff = t * BYTES_PER_TILE_4BPP;
                int tileCol = t % tilesPerRow;
                int tileRow = t / tilesPerRow;

                for (int py = 0; py < TILE_SIZE; py++)
                {
                    for (int px = 0; px < TILE_SIZE; px++)
                    {
                        int bytePos = tileOff + py * 4 + px / 2;
                        if (bytePos >= tileData.Length) continue;

                        byte b = tileData[bytePos];
                        int ci = (px % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                        int palOff = ci * 2;
                        if (palOff + 2 > gbaPalette.Length) continue;

                        ushort gbaColor = (ushort)(gbaPalette[palOff] | (gbaPalette[palOff + 1] << 8));
                        byte r, g, bl;
                        if (svc != null)
                        {
                            svc.GBAColorToRGBA(gbaColor, out r, out g, out bl);
                        }
                        else
                        {
                            // Fallback: extract 5-5-5 manually.
                            r  = (byte)(((gbaColor >>  0) & 0x1F) * 255 / 31);
                            g  = (byte)(((gbaColor >>  5) & 0x1F) * 255 / 31);
                            bl = (byte)(((gbaColor >> 10) & 0x1F) * 255 / 31);
                        }

                        int sx = tileCol * TILE_SIZE + px;
                        int sy = tileRow * TILE_SIZE + py;
                        int si = (sy * sheetWidth + sx) * 4;
                        if (si + 3 >= pixels.Length) continue;

                        pixels[si + 0] = r;
                        pixels[si + 1] = g;
                        pixels[si + 2] = bl;
                        pixels[si + 3] = (byte)(ci == 0 ? 0 : 255); // index 0 → transparent
                    }
                }
            }

            return pixels;
        }

        // ------------------------------------------------------------------
        // Scale blit (mirrors WF ImageUtil.Scale: scale src region into dst).
        // Used to scale the BG sheet (240 × bgHeight) vertically to 240×128.
        // ------------------------------------------------------------------

        /// <summary>
        /// Scale a rectangular source region from <paramref name="src"/> onto
        /// a rectangular destination region in <paramref name="dst"/>.
        /// Nearest-neighbour. Replaces ALL destination pixels (opaque composite).
        ///
        /// Mirrors WF <c>ImageUtil.Scale(dst, 0, 0, 240, 128, bg, 0, 0, 240, bg.Height)</c>.
        /// </summary>
        static void ScaleBlit(
            byte[] src, int srcStride, int srcTotalH,
            int srcX, int srcY, int srcW, int srcH,
            byte[] dst, int dstStride, int dstTotalH,
            int dstX, int dstY, int dstW, int dstH)
        {
            if (src == null || dst == null) return;
            if (srcW <= 0 || srcH <= 0 || dstW <= 0 || dstH <= 0) return;

            for (int dy = 0; dy < dstH; dy++)
            {
                int sy = srcY + dy * srcH / dstH;
                if (sy >= srcTotalH) sy = srcTotalH - 1;
                if (sy < 0) continue;

                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = srcX + dx * srcW / dstW;
                    if (sx >= srcStride) sx = srcStride - 1;
                    if (sx < 0) continue;

                    int si = (sy * srcStride + sx) * 4;
                    int di = ((dstY + dy) * dstStride + (dstX + dx)) * 4;
                    if (si + 3 >= src.Length) continue;
                    if (di + 3 >= dst.Length) continue;

                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }

        // ------------------------------------------------------------------
        // CalcHeight helper (mirrors WF ImageUtil.CalcHeight(width, imageSize, align=8)).
        // height = (imageSize / (width/2)) rounded up to align multiple.
        // ------------------------------------------------------------------

        /// <summary>
        /// Calculate the pixel height of a 4bpp tile-sheet given the sheet width
        /// and the decompressed data size. Mirrors WF
        /// <c>ImageUtil.CalcHeight(width, image_size, align=8)</c>.
        /// </summary>
        internal static int CalcHeight(int width, int imageSize, int align = 8)
        {
            if (width <= 0) return 0;
            int bytesPerRow = width / 2; // 4bpp: width/2 bytes per row
            if (bytesPerRow <= 0) return 0;
            int height = imageSize / bytesPerRow;
            if (imageSize % bytesPerRow != 0) height++;
            if (height % align != 0) height += align;
            return height / align * align;
        }
    }
}
