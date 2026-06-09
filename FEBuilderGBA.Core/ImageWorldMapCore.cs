// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform render helpers for the World Map Image editor's REUSE-based
// live previews (#843, NV5a — follow-up to #769 / #500). Renders the five
// world-map graphics that decode via an EXISTING Core primitive, so the
// Avalonia WorldMapImageView can show them and export them to PNG read-only —
// no System.Drawing / WinForms dependency.
//
// WinForms reference (FEBuilderGBA/WorldMapImageForm.cs:44–49): each preview is
// an ImageFormRef whose decode is selected by the Designer control names
// present for its prefix (ImageFormRef.cs:632 Draw()). The real per-preview
// decode + pointer/palette/dims (verified end-to-end against the WF draw path):
//
//   | Preview | image / tsa / palette RomInfo ptr                | decode                        | dims  |
//   |---------|-------------------------------------------------|-------------------------------|-------|
//   | Event   | worldmap_event_image_pointer /                  | ZIMAGE (LZ77) + ZHEADERTSA    | 32x20 |
//   |         | worldmap_event_tsa_pointer /                    | (LZ77) -> DecodeHeaderTSA,    | tiles |
//   |         | worldmap_event_palette_pointer                  | palette_count=4 (64 colors)   |       |
//   | Mini    | worldmap_mini_image_pointer / 0 /               | ZIMAGE (LZ77) ->              | 8x8   |
//   |         | worldmap_mini_palette_pointer                   | LoadROMTiles4bpp              | tiles |
//   | Point1  | worldmap_icon1_pointer / 0 /                    | ZIMAGE (LZ77) ->              | 32x8  |
//   |         | worldmap_icon_palette_pointer                   | LoadROMTiles4bpp              | tiles |
//   | Point2  | worldmap_icon2_pointer / 0 /                    | ZIMAGE (LZ77) ->              | 12x4  |
//   |         | worldmap_icon_palette_pointer                   | LoadROMTiles4bpp              | tiles |
//   | Road    | worldmap_road_tile_pointer / 0 /                | ZIMAGE (LZ77) ->              | 1x15  |
//   |         | worldmap_icon_palette_pointer                   | LoadROMTiles4bpp              | tiles |
//
// CAUTION 1 (event): the WF reality is TWO LZ77 streams — ZIMAGE (LZ77 image,
//   ImageFormRef.cs:645) AND ZHEADERTSA (LZ77 header-TSA, :890). The event
//   resolver LZ77.decompresses BOTH and applies the 4-byte LZ77-header guard to
//   BOTH before DecodeHeaderTSA. (Mini/point/road have ONE LZ77 stream each.)
// CAUTION 2 (event): the event palette is 4 sub-palettes (64 colors / 128
//   bytes) — WMEvent is built with palette_count=4 and DecodeHeaderTSA selects
//   the sub-palette via TSA bits 12–15. Reading only 16 colors mis-renders
//   multi-palette tiles. Mini/point/road are palette_count=1 (16 colors).
//
// Decode reuse (FEBuilderGBA.Core/ImageUtilCore.cs):
//   * DecodeHeaderTSA  (event — mirrors WF ByteToImage16TileHeaderTSA)
//   * LoadROMTiles4bpp (mini/point/road — mirrors WF no-TSA ByteToImage16Tile)
//
// All dereferences are pointer-to-pointer: p32 the RomInfo pointer FIRST to get
// the data offset (matching the WF constructor). Every method is null-safe — a
// bad / truncated / out-of-bounds pointer returns null (never throws).
//
// The MAIN FIELD MAP (:120, NV5b — #846) is rendered here via
// TryRenderMainFieldMap: FE8-ONLY (WorldMapImageForm.DrawWorldMap routes
// version 7 -> DrawWorldMapFE7 (TSA 12-split) and version 6 -> DrawWorldMapFE6
// (256-color liner); ByteToImage16TilePaletteMap is reached only for FE8), it
// resolves worldmap_big_{image,palette,palettemap}, LZ77-decompresses ONLY the
// palette-map (image + palette are RAW), requires the FULL fixed regions
// (image 76,800 B = 480*320/2; palette 512 B = 256*2), and calls the new pure
// primitive ImageUtilCore.ByteToImage16TilePaletteMap.
//
// The COUNTY BORDER (:406, NV5c — #849) is now rendered here via
// TryRenderBorder: FE8-ONLY (worldmap_county_border_palette_pointer is 0x0 for
// FE6/FE7; 0xC27A4/FE8U, 0xC755C/FE8JP). G5 gate: bail (null/false) when
// rom.RomInfo.worldmap_county_border_palette_pointer==0.
// WF path (ImageUtilBorderAP.DrawBorderImages + DrawBorderBitmap):
//   1. LZ77-decompress the parts image; dereference the palette POINTER via
//      p32(worldmap_county_border_palette_pointer) to get the actual palette
//      offset (it IS a pointer-to-pointer — matching WF DrawBorderBitmap:
//      "uint pal = Program.ROM.p32(Program.ROM.RomInfo.worldmap_county_border_palette_pointer)");
//      CalcHeight guard.
//   2. ByteToImage16Tile → parts sheet (palette index 0 = transparent, G4c).
//   3. Parse AP at p32(apAddr), DrawFrame(layer,0)+DrawFrame(layer,1) → AP layer.
//   4. Composite: TryRenderEvent as background; MakeTransparent (alpha-key the
//      AP layer); draw AP layer over the background → 256×160 result.
// Record fields: P0=image_addr, P4=ap_addr, W8=x, W10=y.

using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform render helpers for the World Map Image editor's
    /// reuse-based previews (event / mini / point1 / point2 / road) — #843.
    /// See the file-level comment for the verified per-preview pointer / palette
    /// / dims mapping and the two event-specific cautions.
    /// </summary>
    public static class ImageWorldMapCore
    {
        // The GBA LZ77 stream header is 4 bytes (0x10 + a 3-byte uncompressed
        // size). LZ77.getCompressedSize reads input[offset + 3] but only rejects
        // when FEWER THAN 3 bytes remain, so a pointer to the LAST 1-3 bytes of
        // the ROM passes isSafetyOffset yet makes that header read throw
        // IndexOutOfRangeException (#818/#827). Require the FULL 4-byte header to
        // be in-bounds BEFORE any LZ77 call. Mirrors
        // ImageTSAEditorCore.IsLZ77HeaderSafe / ImageBattleScreenCore.IsRegionSafe.
        const int LZ77_HEADER_BYTES = 4;

        // Event palette is 4 sub-palettes * 16 colors * 2 bytes = 128 bytes
        // (CAUTION 2). DecodeHeaderTSA indexes via palOffset = palIndex * 16 * 2,
        // so the palette buffer must span all selectable banks.
        const int EVENT_PALETTE_COLORS = 64;
        const int EVENT_PALETTE_BYTES  = EVENT_PALETTE_COLORS * 2;

        // Mini / point / road are palette_count=1 -> 16 colors / 32 bytes.
        const int ICON_PALETTE_COLORS = 16;

        // Event canvas dims (WF WMEvent = 32*8 x 20*8).
        const int EVENT_TILES_X = 32;
        const int EVENT_TILES_Y = 20;

        // --- Main field map (#846, NV5b) — FE8-only. ---
        // WF ImageUtilMap.DrawWorldMap renders the big field map at a FIXED
        // 480x320 (ImageUtilMap.cs:522). The version that reaches
        // ByteToImage16TilePaletteMap is FE8 (FE6/FE7 route elsewhere).
        const int MAIN_FIELD_VERSION_FE8 = 8;     // ROMFEINFO.version for FE8J/FE8U
        const int MAIN_FIELD_WIDTH  = 480;
        const int MAIN_FIELD_HEIGHT = 320;
        // image is RAW 4bpp -> width*height/2 = 76,800 bytes (must be fully
        // in-bounds; a partial image would yield an incorrect non-null render).
        const int MAIN_FIELD_IMAGE_BYTES = (MAIN_FIELD_WIDTH * MAIN_FIELD_HEIGHT) / 2;
        // palette is RAW 256 colors -> 512 bytes (the palette-map nibble selects
        // any of the 16 sub-palettes, so all 256 colors must be readable).
        const int MAIN_FIELD_PALETTE_COLORS = 256;
        const int MAIN_FIELD_PALETTE_BYTES  = MAIN_FIELD_PALETTE_COLORS * 2;

        // Largest possible header-TSA payload after decompression: 2 header bytes
        // + 32 x 32 u16 cells. (DecodeHeaderTSA accepts header dims up to 32x32.)
        // The event TSA is LZ77 so the decompressed buffer is used directly; this
        // is only a sanity ceiling for the bounded slice when needed.

        /// <summary>
        /// Render the world-map EVENT preview (WF tabPage2, ImageFormRef
        /// "WMEvent"). Resolves the three event pointers (image / TSA / palette)
        /// pointer-to-pointer, LZ77-decompresses BOTH the image AND the TSA
        /// (CAUTION 1), reads the 64-color / 4-sub-palette block (CAUTION 2), and
        /// composes via <see cref="ImageUtilCore.DecodeHeaderTSA"/> at 32x20
        /// tiles = 256x160 px. Returns <c>null</c> (never throws) on any
        /// null / out-of-bounds / corrupt / truncated input.
        /// </summary>
        /// <param name="rom">Loaded ROM (uses <c>rom.RomInfo</c> for the
        /// canonical pointers; tile/palette reads come from <c>rom.Data</c>).</param>
        public static IImage TryRenderEvent(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // --- Resolve the three event pointers (pointer-to-pointer). ---
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_image_pointer, out uint imageAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_tsa_pointer, out uint tsaAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_palette_pointer, out uint paletteAddr))
                return null;

            // --- ZIMAGE: LZ77 image (4-byte-header guard + truncation guard). ---
            if (!TryDecompressGuarded(rom, imageAddr, out byte[] tileData)) return null;

            // --- ZHEADERTSA: LZ77 header-TSA (CAUTION 1 — also LZ77; same
            // 4-byte-header + truncation guard applied to the SECOND stream). ---
            if (!TryDecompressGuarded(rom, tsaAddr, out byte[] tsaData)) return null;

            // --- Event palette: the FULL RAW 64 colors / 128 bytes (CAUTION 2),
            // no LZ77. The size is FIXED, so a truncated/corrupt palette pointer
            // (insufficient bytes to EOF) returns null here -> the preview is null
            // and Export PNG stays disabled. ---
            byte[] palette = ReadRawPalette(rom, paletteAddr, EVENT_PALETTE_BYTES);
            if (palette == null) return null;

            // tsaAddend=0 / paletteShift=0 are the correct event defaults
            // (matches the WF WMEvent ImageFormRef construction).
            return ImageUtilCore.DecodeHeaderTSA(
                tileData, tsaData, palette, EVENT_TILES_X, EVENT_TILES_Y, true, 0, 0);
        }

        /// <summary>
        /// Render the world-map MINI MAP preview (WF tabPage3, ImageFormRef
        /// "WMMini"): single LZ77 image (<c>worldmap_mini_image_pointer</c>) +
        /// the 16-color mini palette (<c>worldmap_mini_palette_pointer</c>) at
        /// 8x8 tiles = 64x64 px. Null-safe.
        /// </summary>
        public static IImage TryRenderMini(ROM rom)
            => TryRenderIconStrip(rom,
                rom?.RomInfo?.worldmap_mini_image_pointer ?? 0,
                rom?.RomInfo?.worldmap_mini_palette_pointer ?? 0,
                8, 8);

        /// <summary>
        /// Render the world-map POINT 1 preview (WF WMPointIconTabPage,
        /// ImageFormRef "WMPoint1"): single LZ77 image
        /// (<c>worldmap_icon1_pointer</c>) + the shared 16-color icon palette
        /// (<c>worldmap_icon_palette_pointer</c>) at 32x8 tiles = 256x64 px.
        /// Null-safe.
        /// </summary>
        public static IImage TryRenderPoint1(ROM rom)
            => TryRenderIconStrip(rom,
                rom?.RomInfo?.worldmap_icon1_pointer ?? 0,
                rom?.RomInfo?.worldmap_icon_palette_pointer ?? 0,
                32, 8);

        /// <summary>
        /// Render the world-map POINT 2 preview (WF WMPointIconTabPage,
        /// ImageFormRef "WMPoint2"): single LZ77 image
        /// (<c>worldmap_icon2_pointer</c>) + the shared 16-color icon palette
        /// at 12x4 tiles = 96x32 px. Null-safe.
        /// </summary>
        public static IImage TryRenderPoint2(ROM rom)
            => TryRenderIconStrip(rom,
                rom?.RomInfo?.worldmap_icon2_pointer ?? 0,
                rom?.RomInfo?.worldmap_icon_palette_pointer ?? 0,
                12, 4);

        /// <summary>
        /// Render the world-map ROAD preview (WF WMPointIconTabPage,
        /// ImageFormRef "WMRoad"): single LZ77 image
        /// (<c>worldmap_road_tile_pointer</c>) + the shared 16-color icon palette
        /// at 1x15 tiles = 8x120 px. Null-safe.
        /// </summary>
        public static IImage TryRenderRoad(ROM rom)
            => TryRenderIconStrip(rom,
                rom?.RomInfo?.worldmap_road_tile_pointer ?? 0,
                rom?.RomInfo?.worldmap_icon_palette_pointer ?? 0,
                1, 15);

        /// <summary>
        /// Render the FE8 World Map MAIN FIELD MAP preview (WF tabPage1,
        /// <c>ImageUtilMap.DrawWorldMap</c> -&gt;
        /// <see cref="ImageUtilCore.ByteToImage16TilePaletteMap"/>) at a fixed
        /// 480×320 px. Resolves the three <c>worldmap_big_*</c> pointers
        /// pointer-to-pointer, LZ77-decompresses ONLY the palette-map (the image
        /// and palette are read RAW), requires the FULL fixed regions (image
        /// 76,800 B, palette 512 B), and composes via the new pure primitive.
        /// Returns <c>null</c> (never throws) on any null / out-of-bounds /
        /// corrupt / truncated input.
        ///
        /// <para><b>FE8-only (#846 correction #2).</b> WF
        /// <c>WorldMapImageForm.DrawWorldMap</c> routes <c>version == 7</c> to
        /// <c>DrawWorldMapFE7</c> (a TSA <c>ByteToImage16TileHeaderTSA</c>
        /// 1024×688 renderer — FE7U's <c>worldmap_big_palettemap_pointer</c> is
        /// documented as "TSA 12-split", NOT a palette-map) and
        /// <c>version == 6</c> to <c>DrawWorldMapFE6</c> (a 256-color
        /// <c>ByteToImage256Liner</c> 240×160 renderer — FE6's palettemap pointer
        /// is <c>0x0</c>). <c>ByteToImage16TilePaletteMap</c> is reached ONLY for
        /// FE8, so this method gates on <c>rom.RomInfo.version == 8</c> and
        /// returns <c>null</c> for FE6/FE7 rather than feed their bytes through
        /// the 16-tile palette-map decoder. (FE7/FE6 main-map previews are
        /// separate future follow-ups.)</para>
        ///
        /// <para><b>LZ77 = palette-map ONLY (#846 correction #1).</b> Per WF
        /// <c>ImageUtilMap.cs:520-526</c>: <c>palettemapUZ =
        /// LZ77.decompress(...)</c> while the image and palette are passed RAW
        /// (<c>Program.ROM.Data, (int)image</c> / <c>(int)palette</c>). The pure
        /// primitive does ZERO decompression; this method LZ77-decompresses ONLY
        /// the palette-map (behind the 4-byte-header + truncation guard on the
        /// COMPRESSED stream) and passes the image + palette raw.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (FE8 only — see the FE8-only note).</param>
        public static IImage TryRenderMainFieldMap(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // CORRECTION 2 — FE8-only. FE6/FE7 route to DrawWorldMapFE6/FE7 (their
            // palettemap pointer is 0x0 / a TSA-12-split, NOT a 16-tile
            // palette-map), so return null rather than mis-decode their bytes.
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE8) return null;

            // Resolve the three big-field pointers (pointer-to-pointer).
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out uint imageAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer, out uint paletteAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palettemap_pointer, out uint paletteMapAddr))
                return null;

            // --- image: the FULL RAW 4bpp region (76,800 B). FIXED size, so an
            // image pointer with fewer bytes to EOF is truncated/corrupt -> null
            // (a partial read would otherwise make the primitive render a
            // partial-but-non-null image and wrongly enable Export PNG). ---
            byte[] image = ReadRawRegion(rom, imageAddr, MAIN_FIELD_IMAGE_BYTES);
            if (image == null) return null;

            // --- palette: the FULL RAW 256-color region (512 B). FIXED size, so
            // a truncated/corrupt palette pointer -> null (same reasoning). ---
            byte[] palette = ReadRawPalette(rom, paletteAddr, MAIN_FIELD_PALETTE_BYTES);
            if (palette == null) return null;

            // --- palette-map: CORRECTION 1 — the ONLY LZ77 stream. Guard the
            // COMPRESSED stream (4-byte-header + truncation), then decompress.
            // The decompressed length (~1280 B = 40*64 nibbles) is NOT size-
            // guarded against the source — the primitive reads it U.at-safe. ---
            if (!TryDecompressGuarded(rom, paletteMapAddr, out byte[] paletteMap)) return null;

            return ImageUtilCore.ByteToImage16TilePaletteMap(
                image, paletteMap, palette, MAIN_FIELD_WIDTH, MAIN_FIELD_HEIGHT);
        }

        // ==================================================================
        // Main Field Map import / Dark Palette import / Dark render (#875)
        // ==================================================================

        // Main palette for import is 4 sub-palettes × 16 colors × 2 bytes = 128 B.
        const int MAIN_IMPORT_PALETTE_BYTES = 4 * 16 * 2;   // 128

        /// <summary>
        /// Result of <see cref="ImportMainFieldMap"/> or
        /// <see cref="ImportDarkPalette"/>: success flag + optional error string.
        /// </summary>
        public class ImportResult
        {
            public bool Success { get; }
            public string Error { get; }
            ImportResult(bool ok, string err) { Success = ok; Error = err; }
            public static ImportResult Ok() => new ImportResult(true, null);
            public static ImportResult Fail(string err) => new ImportResult(false, err);
        }

        /// <summary>
        /// Validate that every pixel in each 8×8 tile of
        /// <paramref name="indexedPixels"/> uses the SAME sub-palette
        /// (<c>pixelValue / 16</c> is uniform across the tile).
        /// Returns an empty string on success, or a WF-style error message
        /// naming the violating tile/pixel — caller shows this to the user and
        /// MUST NOT write anything on a non-empty return.
        /// Mirrors WF <c>ImageUtil.ImageToPaletteMap :2236-2239</c> error path.
        /// </summary>
        public static string ValidateTileMonoPalette(byte[] indexedPixels, int width, int height)
        {
            if (indexedPixels == null || width <= 0 || height <= 0) return "Invalid image data.";
            if (width % 8 != 0 || height % 8 != 0) return "Image dimensions must be multiples of 8.";
            if (indexedPixels.Length < width * height) return "Pixel buffer is too short.";

            for (int y = 0; y < height; y += 8)
            {
                for (int x = 0; x < width; x += 8)
                {
                    int expectedPalette = -1; // negative = unset
                    for (int y8 = 0; y8 < 8; y8++)
                    {
                        for (int x8 = 0; x8 < 8; x8++)
                        {
                            int pixVal = indexedPixels[(y + y8) * width + (x + x8)];
                            int palette = pixVal / 16;
                            if (expectedPalette < 0)
                            {
                                expectedPalette = palette;
                            }
                            else if (palette != expectedPalette)
                            {
                                // Mirrors WF R.Error("TSAフォーマット違反...") path.
                                return R.Error(
                                    "TSA format violation. Starting at X:{0} Y:{1}, within the 8x8 region, pixel at X:{2} Y:{3} uses a different palette number {4}. Others use palette number {5}.\r\n\r\nFix manually, or use the Decrease Color tool.",
                                    x, y, x + x8, y + y8, palette, expectedPalette);
                            }
                        }
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Import the FE8 main field map (image + palette + palette-map) in-place.
        /// Requires <paramref name="indexedPixels"/> to be a 480×320 mono-sub-palette-
        /// per-tile indexed buffer (already validated by caller via
        /// <see cref="ValidateTileMonoPalette"/>);
        /// <paramref name="gbaPalette128"/> = 128 bytes (4×16×2).
        ///
        /// <para><b>Write contract (byte-exact match to WF ImportButton_Click):</b>
        /// <list type="bullet">
        ///   <item>image (76,800 B) → RAW in-place at
        ///     p32(<c>worldmap_big_image_pointer</c>) via <c>rom.write_range</c>
        ///     (fixed size, NO realloc/repoint).</item>
        ///   <item>palette (128 B) → RAW in-place at
        ///     p32(<c>worldmap_big_palette_pointer</c>) via <c>rom.write_range</c>.</item>
        ///   <item>palette-map → LZ77-compressed; in-place if the compressed stream
        ///     fits, otherwise allocated in free space + pointer updated. Mirrors
        ///     WF <c>WriteImageData(WMPaletteMap, …, useLZ77=true)</c> via
        ///     <see cref="ImageImportCore.WriteCompressedToROM"/>.</item>
        /// </list>
        /// All writes land in the ambient undo scope opened by
        /// <c>ROM.BeginUndoScope</c> (no explicit undodata parameter needed).</para>
        ///
        /// <para><b>FE8-only.</b> Returns a failure result for non-FE8 ROMs.</para>
        /// </summary>
        public static ImportResult ImportMainFieldMap(ROM rom,
            byte[] indexedPixels, byte[] gbaPalette128)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return ImportResult.Fail("ROM not loaded.");
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE8)
                return ImportResult.Fail(R.Error("The main field map import is only supported for FE8."));
            if (indexedPixels == null)
                return ImportResult.Fail("Invalid image data.");
            if (indexedPixels.Length < MAIN_FIELD_WIDTH * MAIN_FIELD_HEIGHT)
                return ImportResult.Fail("Image pixel buffer too short.");
            if (gbaPalette128 == null || gbaPalette128.Length < MAIN_IMPORT_PALETTE_BYTES)
                return ImportResult.Fail("Palette buffer must be 128 bytes (4 sub-palettes × 16 colors × 2 bytes).");

            // Resolve image pointer (pointer-to-pointer), must have FULL 76,800 B region.
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out uint imageAddr))
                return ImportResult.Fail("worldmap_big_image_pointer is invalid or out of ROM.");
            if (!IsRegionSafe(rom, imageAddr, MAIN_FIELD_IMAGE_BYTES))
                return ImportResult.Fail("Image region does not fit in ROM (truncated?).");

            // Resolve palette pointer, must have FULL 128 B region.
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer, out uint paletteAddr))
                return ImportResult.Fail("worldmap_big_palette_pointer is invalid or out of ROM.");
            if (!IsRegionSafe(rom, paletteAddr, MAIN_IMPORT_PALETTE_BYTES))
                return ImportResult.Fail("Palette region does not fit in ROM (truncated?).");

            // Encode image — 4bpp tiles, plain (no TSA dedup), 480×320.
            // Mirrors WF ImageUtil.ImageToByte16Tile (ImageUtil.cs :1664).
            byte[] imageBytes = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, MAIN_FIELD_WIDTH, MAIN_FIELD_HEIGHT);
            if (imageBytes == null || imageBytes.Length != MAIN_FIELD_IMAGE_BYTES)
                return ImportResult.Fail("Failed to encode 4bpp tile data.");

            // Encode palette-map nibble stream (EXACT inverse of ByteToImage16TilePaletteMap).
            // Mirrors WF ImageUtil.ImageToPaletteMap (ImageUtil.cs :2211).
            byte[] paletteMap = ImageUtilCore.EncodePaletteMap16Tile(indexedPixels, MAIN_FIELD_WIDTH, MAIN_FIELD_HEIGHT);
            if (paletteMap == null || paletteMap.Length == 0)
                return ImportResult.Fail("Failed to encode palette-map.");

            // Write image RAW in-place (fixed 76,800 B — NO realloc/repoint).
            rom.write_range(imageAddr, imageBytes);

            // Write palette RAW in-place — exactly 128 bytes.
            // FIX B: gbaPalette128 is validated as >= 128 B; writing the full buffer
            // would overwrite past the 128-byte slot if the caller passed a longer
            // array. Slice to exactly MAIN_IMPORT_PALETTE_BYTES (128 B).
            byte[] paletteToWrite = gbaPalette128;
            if (paletteToWrite.Length != MAIN_IMPORT_PALETTE_BYTES)
            {
                paletteToWrite = new byte[MAIN_IMPORT_PALETTE_BYTES];
                Array.Copy(gbaPalette128, paletteToWrite, MAIN_IMPORT_PALETTE_BYTES);
            }
            rom.write_range(paletteAddr, paletteToWrite);

            // Write palette-map LZ77 (in-place if it fits, else free-space + repoint).
            // Mirrors WF WriteImageData(WMPaletteMap, …, useLZ77=true).
            uint pmAddr = ImageImportCore.WriteCompressedToROM(rom, paletteMap,
                rom.RomInfo.worldmap_big_palettemap_pointer);
            if (pmAddr == U.NOT_FOUND)
                return ImportResult.Fail("Failed to write compressed palette-map (no free space?).");

            return ImportResult.Ok();
        }

        /// <summary>
        /// Import only the 128-byte dark palette for the FE8 main field map.
        /// Writes ONLY <c>worldmap_big_dpalette_pointer</c> — does NOT touch the
        /// image or palette-map. Mirrors WF <c>DarkMAPImportButton_Click</c>:
        /// <c>ImageToPalette(bitmap, 4)</c> → <c>WriteImageData(WMdPalette, …,
        /// useLZ77=false)</c> → RAW in-place.
        ///
        /// <para><b>FE8-only.</b> Returns a failure result for non-FE8 ROMs.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="gbaDarkPalette128">128 bytes (4 sub-palettes × 16 colors
        /// × 2 bytes) — the dark variant palette.</param>
        public static ImportResult ImportDarkPalette(ROM rom, byte[] gbaDarkPalette128)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return ImportResult.Fail("ROM not loaded.");
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE8)
                return ImportResult.Fail(R.Error("The dark palette import is only supported for FE8."));
            if (gbaDarkPalette128 == null || gbaDarkPalette128.Length < MAIN_IMPORT_PALETTE_BYTES)
                return ImportResult.Fail("Dark palette buffer must be 128 bytes (4 sub-palettes × 16 colors × 2 bytes).");

            // Resolve the dark palette pointer (pointer-to-pointer).
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_dpalette_pointer, out uint dPaletteAddr))
                return ImportResult.Fail("worldmap_big_dpalette_pointer is invalid or out of ROM.");
            if (!IsRegionSafe(rom, dPaletteAddr, MAIN_IMPORT_PALETTE_BYTES))
                return ImportResult.Fail("Dark palette region does not fit in ROM (truncated?).");

            // Write only the 128-byte dark palette RAW in-place — exactly 128 bytes.
            // FIX B: same exact-length guard as the main palette write.
            byte[] darkToWrite = gbaDarkPalette128;
            if (darkToWrite.Length != MAIN_IMPORT_PALETTE_BYTES)
            {
                darkToWrite = new byte[MAIN_IMPORT_PALETTE_BYTES];
                Array.Copy(gbaDarkPalette128, darkToWrite, MAIN_IMPORT_PALETTE_BYTES);
            }
            rom.write_range(dPaletteAddr, darkToWrite);
            return ImportResult.Ok();
        }

        // ==================================================================
        // Single-LZ77-stream strip import (mini / point1 / point2 / road) +
        // a public guarded palette helper (#1000).
        //
        // Each strip is a single LZ77 image pointer + a 16-color palette. The
        // import is IMAGE-ONLY (the View nearest-color-remaps onto the existing
        // palette first, so the shared palette is NOT written): 4bpp-encode →
        // LZ77-compress → free-space append + repoint the single image pointer.
        // The wait-icon / OP-class-font pattern (#991/#999). Defensive
        // byte-identical fault restore (#885/#923) means a FAILED import mutates
        // ZERO bytes. Runs under the CALLER's ambient undo scope (the View owns
        // _undoService.Begin/Commit/Rollback).
        // ==================================================================

        /// <summary>
        /// Image-only LZ77 strip import. Validates dims (&gt;0, %8==0, buffer
        /// length via long math), validates <paramref name="imagePointerAddr"/>
        /// in-range (+4 overflow-safe), 4bpp-encodes, LZ77-writes to free space +
        /// repoints the single image pointer (<see cref="ImageImportCore.WriteCompressedToROM"/>
        /// owns the slot). Defensive byte-identical fault restore (#885/#923): a
        /// failed import mutates ZERO bytes. Never throws (catch -&gt;
        /// <see cref="RestoreSnapshot"/> + Fail). The shared palette is NOT
        /// written. ROM-MUTATING (runs under the caller's ambient undo scope).
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="imagePointerAddr">The single canonical image pointer slot
        /// (RomInfo) for this strip — e.g. <c>worldmap_mini_image_pointer</c>.</param>
        /// <param name="indexedPixels">Already-remapped indexed pixels (one byte
        /// per pixel, row-major) at the strip's fixed dims. The caller must remap
        /// to the existing shared strip palette first — the strip palette is NOT
        /// written by this seam.</param>
        /// <param name="widthPx">Strip pixel width (multiple of 8).</param>
        /// <param name="heightPx">Strip pixel height (multiple of 8).</param>
        public static ImportResult ImportIconStrip(ROM rom, uint imagePointerAddr,
            byte[] indexedPixels, int widthPx, int heightPx)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return ImportResult.Fail("ROM not loaded.");
            if (indexedPixels == null) return ImportResult.Fail("Invalid image data.");
            if (widthPx <= 0 || heightPx <= 0 || widthPx % 8 != 0 || heightPx % 8 != 0)
                return ImportResult.Fail(R.Error("The image size must be positive multiples of 8."));
            if (indexedPixels.Length < (long)widthPx * heightPx) return ImportResult.Fail("Image pixel buffer too short.");
            if (imagePointerAddr == 0 || (long)imagePointerAddr + 4 > rom.Data.Length)
                return ImportResult.Fail("Image pointer slot is out of range.");

            byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, widthPx, heightPx);
            if (tiles == null || tiles.Length == 0) return ImportResult.Fail("Failed to encode 4bpp tile data.");

            // Defensive snapshot for the byte-identical restore on fault. The
            // caller's ambient undo scope captures the writes for UNDO; this
            // snapshot guarantees a FAILED import mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                uint w = ImageImportCore.WriteCompressedToROM(rom, tiles, imagePointerAddr);
                if (w == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return ImportResult.Fail("Failed to write image. Check ROM free space.");
                }
                return ImportResult.Ok();
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return ImportResult.Fail("World map strip import failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Public guarded helper so the View never uses unchecked p32/u32:
        /// resolve the palette pointer slot to a data offset and read exactly 16
        /// colors. Returns <c>false</c> (<paramref name="palette16"/> = null) on
        /// an invalid / out-of-range / zero pointer slot. READ-ONLY.
        /// </summary>
        public static bool TryGetStripPalette(ROM rom, uint palettePointerAddr, out byte[] palette16)
        {
            palette16 = null;
            if (rom?.Data == null) return false;
            if (!TryResolveDataOffset(rom, palettePointerAddr, out uint palOff)) return false;
            palette16 = ImageUtilCore.GetPalette(rom, palOff, ICON_PALETTE_COLORS);
            return palette16 != null;
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space resize-append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy (a naive Array.Copy would leave the grown tail alive).
        /// Mirrors <c>WaitIconImportCore.RestoreSnapshot</c> (#885/#923).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }

        /// <summary>
        /// Render the FE8 World Map DARK FIELD MAP preview — exactly like
        /// <see cref="TryRenderMainFieldMap"/> but using
        /// <c>worldmap_big_dpalette_pointer</c> for the palette. Mirrors WF
        /// <c>WorldMapImageForm.DrawDarkWorldMap</c>:
        /// image = <c>worldmap_big_image_pointer</c>, palette =
        /// <c>worldmap_big_dpalette_pointer</c>, palettemap =
        /// <c>worldmap_big_palettemap_pointer</c>. FE8-only; returns null for
        /// FE6/FE7 or any bad pointer.
        /// </summary>
        public static IImage TryRenderDarkFieldMap(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE8) return null;

            // Resolve image + dark palette + palette-map (pointer-to-pointer).
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out uint imageAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_dpalette_pointer, out uint dPaletteAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palettemap_pointer, out uint paletteMapAddr))
                return null;

            // image: RAW 76,800 B (fixed).
            byte[] image = ReadRawRegion(rom, imageAddr, MAIN_FIELD_IMAGE_BYTES);
            if (image == null) return null;

            // dark palette: the slot holds only 128 B (4 sub-palettes), but
            // ByteToImage16TilePaletteMap reads up to 512 B (16 sub-palettes).
            // Read the available 128 B and zero-pad to 512 B (unused banks = black).
            byte[] dPaletteRaw = new byte[MAIN_FIELD_PALETTE_BYTES]; // 512 B, zero-filled
            if (!IsRegionSafe(rom, dPaletteAddr, MAIN_IMPORT_PALETTE_BYTES)) return null;
            Array.Copy(rom.Data, dPaletteAddr, dPaletteRaw, 0, MAIN_IMPORT_PALETTE_BYTES);

            // palette-map: LZ77 (only the palette-map is compressed — CORRECTION 1).
            if (!TryDecompressGuarded(rom, paletteMapAddr, out byte[] paletteMap)) return null;

            return ImageUtilCore.ByteToImage16TilePaletteMap(
                image, paletteMap, dPaletteRaw, MAIN_FIELD_WIDTH, MAIN_FIELD_HEIGHT);
        }

        // ==================================================================
        // Shared LZ77-image + 16-color-palette path (mini / point1 / point2 /
        // road). Mirrors the WF no-TSA ByteToImage16Tile branch via the existing
        // Core primitive LoadROMTiles4bpp (isCompressed:true). The 4-byte LZ77
        // header guard is applied HERE (LoadROMTiles4bpp itself does not guard).
        // ==================================================================

        static IImage TryRenderIconStrip(ROM rom, uint imagePointer, uint palettePointer,
            int tilesX, int tilesY)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (tilesX <= 0 || tilesY <= 0) return null;

            // Resolve image + palette (pointer-to-pointer).
            if (!TryResolveDataOffset(rom, imagePointer, out uint imageAddr)) return null;
            if (!TryResolveDataOffset(rom, palettePointer, out uint paletteAddr)) return null;

            // 4-byte LZ77-header guard + truncation guard on the single image
            // stream BEFORE LoadROMTiles4bpp calls LZ77.decompress.
            if (!IsLZ77HeaderSafe(rom, imageAddr)) return null;
            uint compressed = LZ77.getCompressedSize(rom.Data, imageAddr);
            if (compressed == 0) return null;
            if ((ulong)imageAddr + compressed > (ulong)rom.Data.Length) return null;

            // The FULL 16-color palette (palette_count=1) = 32 bytes, raw. FIXED
            // size, so a truncated/corrupt palette pointer returns null here.
            byte[] palette = ReadRawPalette(rom, paletteAddr, ICON_PALETTE_COLORS * 2);
            if (palette == null) return null;

            // LoadROMTiles4bpp reads from CoreState.ROM and LZ77-decompresses
            // internally (isCompressed:true). It is the existing primitive the
            // plan names; we just hardened the stream above. Guard the CoreState
            // coupling: the offset/palette were resolved from the same ROM, so
            // CoreState.ROM must be that ROM for the read to be correct.
            if (CoreState.ROM != rom) return null;
            return ImageUtilCore.LoadROMTiles4bpp(imageAddr, palette, tilesX, tilesY, isCompressed: true);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Dereference a canonical RomInfo pointer (pointer-to-pointer): p32 the
        /// pointer slot to read the encoded data pointer, decode it to a ROM
        /// offset, and validate the offset with <see cref="U.isSafetyOffset(uint, ROM)"/>.
        /// Returns false (no offset) on a zero / non-pointer / unsafe / EOF-truncated
        /// target.
        /// </summary>
        static bool TryResolveDataOffset(ROM rom, uint pointerSlot, out uint dataOffset)
        {
            dataOffset = 0;
            if (pointerSlot == 0) return false;
            // The slot itself must be in-bounds before the u32 read. isSafetyOffset
            // only checks the START offset, so a slot in the last 1-3 bytes of the
            // ROM (truncated ROM / corrupt RomInfo slot near EOF) would still throw
            // IndexOutOfRangeException inside rom.u32 — require the FULL 4-byte read
            // to be in-bounds first (#818/#827 EOF-guard pattern).
            if (!IsRegionSafe(rom, pointerSlot, 4)) return false;
            uint encoded = rom.u32(pointerSlot);
            if (!U.isPointer(encoded)) return false;
            uint off = U.toOffset(encoded);
            if (!U.isSafetyOffset(off, rom)) return false;
            dataOffset = off;
            return true;
        }

        /// <summary>
        /// LZ77-decompress the stream at <paramref name="addr"/> behind the
        /// 4-byte-header guard + truncation guard. <c>LZ77.decompress</c>
        /// silently returns a zero-filled buffer on a truncated stream, so
        /// <c>getCompressedSize == 0</c> + an end-of-ROM bound check is the
        /// truncation guard (and the 4-byte-header guard prevents the header
        /// read itself from throwing). Returns false on any guard failure or an
        /// empty result.
        /// </summary>
        static bool TryDecompressGuarded(ROM rom, uint addr, out byte[] data)
        {
            data = null;
            if (!IsLZ77HeaderSafe(rom, addr)) return false;
            uint compressed = LZ77.getCompressedSize(rom.Data, addr);
            if (compressed == 0) return false;
            if ((ulong)addr + compressed > (ulong)rom.Data.Length) return false;
            byte[] buf = LZ77.decompress(rom.Data, addr);
            if (buf == null || buf.Length == 0) return false;
            data = buf;
            return true;
        }

        /// <summary>
        /// Read the FULL fixed-size <paramref name="sizeBytes"/> raw (uncompressed)
        /// palette block from <paramref name="paletteAddr"/>. The world-map palette
        /// sizes are FIXED (Event = 128 bytes / 64 colors; Icon = 32 bytes / 16
        /// colors), so a PARTIAL read near EOF is a truncated / corrupt palette
        /// pointer, not a smaller-but-valid palette — return <c>null</c> (do NOT
        /// clamp to a partial buffer). A partial buffer would otherwise yield a
        /// non-null (incorrect / transparent) image, and the view enables Export
        /// PNG on <c>img != null</c>. Requires the full <c>[addr, addr+sizeBytes)</c>
        /// region in-bounds (isSafetyOffset on the start + an explicit EOF check).
        /// </summary>
        static byte[] ReadRawPalette(ROM rom, uint paletteAddr, int sizeBytes)
            => ReadRawRegion(rom, paletteAddr, sizeBytes);

        /// <summary>
        /// Read a FULL fixed-size <paramref name="sizeBytes"/> raw region from
        /// <paramref name="addr"/>, or <c>null</c> if the whole
        /// <c>[addr, addr+sizeBytes)</c> region is not in-bounds. Same fixed-size
        /// contract as <see cref="ReadRawPalette"/> (a PARTIAL read near EOF is a
        /// truncated / corrupt pointer, not a smaller-but-valid buffer — return
        /// <c>null</c> so the preview is null and Export PNG stays disabled, never
        /// a partial buffer that would yield an incorrect non-null image). Used
        /// for the FE8 main field map's RAW image (76,800 B) and palette (512 B).
        /// </summary>
        static byte[] ReadRawRegion(ROM rom, uint addr, int sizeBytes)
        {
            if (!IsRegionSafe(rom, addr, sizeBytes)) return null;
            byte[] buf = new byte[sizeBytes];
            Array.Copy(rom.Data, addr, buf, 0, sizeBytes);
            return buf;
        }

        static bool IsLZ77HeaderSafe(ROM rom, uint addr) => IsRegionSafe(rom, addr, LZ77_HEADER_BYTES);

        /// <summary>
        /// True when <c>[addr, addr+bytes)</c> is a valid in-ROM region:
        /// <see cref="U.isSafetyOffset(uint, ROM)"/> domain constraints PLUS an
        /// explicit end-of-range check using <c>ulong</c> arithmetic so the
        /// addition cannot overflow on near-<see cref="uint.MaxValue"/> inputs.
        /// Mirrors ImageTSAEditorCore.IsRegionSafe (#594/#818).
        /// </summary>
        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (bytes <= 0) return false;
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)rom.Data.Length;
        }

        // ==================================================================
        // County Border — NV5c (#849). FE8-only.
        //
        // WF render path:
        //   ImageUtilBorderAP.DrawBorderImages(image_addr, ap_addr, x, y)
        //   → DrawBorderBitmap: decompress parts image; DEREFERENCE the palette
        //     pointer via p32(worldmap_county_border_palette_pointer) to obtain
        //     the actual palette offset (pointer-to-pointer — WF DrawBorderBitmap:
        //     "uint pal = Program.ROM.p32(Program.ROM.RomInfo.worldmap_county_border_palette_pointer)");
        //     CalcHeight; ByteToImage16Tile.
        //   → Parse AP (at U.toOffset(ap_addr)); DrawFrame(0)+DrawFrame(1)
        //     onto a transparent Blank(256,160) canvas.
        //   → composite: event worldmap as background; MakeTransparent on AP
        //     layer; draw AP layer over background → 256×160 result.
        //
        // G5 gate: worldmap_county_border_palette_pointer==0 on FE6/FE7
        // → return null immediately. Using that pointer on FE6/FE7 would
        // p32(0x0) → garbage.
        //
        // P0 = image_addr (encoded GBA pointer → offset via U.toOffset)
        // P4 = ap_addr   (encoded GBA pointer → offset via U.toOffset)
        // W8 = x origin, W10 = y origin.
        // ==================================================================

        // 16-color palette size in bytes (border parts sheet is single-palette).
        const int BORDER_PALETTE_BYTES = 16 * 2; // 32 bytes
        // Minimum decompressed image size for CalcHeight to yield > 0.
        // CalcHeight(256, bin.Length): bin.Length must be > 0.

        /// <summary>
        /// Returns <c>true</c> when the county border preview can be rendered
        /// and exported: the ROM is FE8, the palette pointer is non-zero, the
        /// parts image decompresses to a positive height, and the AP parse
        /// succeeds.
        /// </summary>
        public static bool CanExportBorder(ROM rom, uint imageAddr, uint apAddr,
            int x, int y)
            => TryRenderBorder(rom, imageAddr, apAddr, x, y) != null;

        /// <summary>
        /// Render the COUNTY BORDER (国境) AP preview (256×160 px) matching WF
        /// <c>ImageUtilBorderAP.DrawBorderImages(image_addr, ap_addr, x, y)</c>.
        ///
        /// <para><b>FE8-only (G5).</b> Returns <c>null</c> when
        /// <c>rom.RomInfo.worldmap_county_border_palette_pointer == 0</c> (it is
        /// 0x0 for FE6/FE7). FE6/FE7 do not have a county border — feeding their
        /// ROM bytes through this path would p32(0x0) → garbage.</para>
        ///
        /// <para><b>Parts sheet:</b> LZ77-decompress the image at
        /// <paramref name="imageAddr"/> (GBA-pointer-encoded; converted to offset
        /// internally), dereference the 16-color palette POINTER via
        /// p32(<c>worldmap_county_border_palette_pointer</c>) to obtain the actual
        /// palette offset (pointer-to-pointer, matching WF DrawBorderBitmap),
        /// compute <c>height = CalcHeight(256, bin.Length)</c>; if ≤ 0 return
        /// <c>null</c> (WF blank guard). Render via
        /// <see cref="ImageUtilCore.ByteToImage16Tile"/> (palette index 0 =
        /// transparent, G4c).</para>
        ///
        /// <para><b>AP frames:</b> parse the AP at
        /// <paramref name="apAddr"/> (pointer-encoded); render frame 0 and frame 1
        /// onto a transparent 256×160 canvas via
        /// <see cref="ImageUtilAPCore.RenderFrame"/> with the parts sheet. Source
        /// pixels at index 0 are skipped (transparent key, G4c). Returns
        /// <c>null</c> on parse failure.</para>
        ///
        /// <para><b>Composite:</b> call <see cref="TryRenderEvent"/> for the
        /// background (256×160 event world map); if <c>null</c>, use a blank opaque
        /// 256×160 background (WF always draws a background). Compose the AP layer
        /// over the background → return 256×160 RGBA result.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="imageAddr">Encoded GBA pointer to the parts image
        /// (record field P0).</param>
        /// <param name="apAddr">Encoded GBA pointer to the AP data
        /// (record field P4).</param>
        /// <param name="x">Origin X for the AP blit (record field W8).</param>
        /// <param name="y">Origin Y for the AP blit (record field W10).</param>
        /// <returns>RGBA <see cref="IImage"/> 256×160, or <c>null</c> on any
        /// failure. Never throws.</returns>
        public static IImage TryRenderBorder(ROM rom, uint imageAddr, uint apAddr,
            int x, int y)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // G5 — FE8-only gate.
            uint palettePointerSlot = rom.RomInfo.worldmap_county_border_palette_pointer;
            if (palettePointerSlot == 0) return null;

            // ---- Step 1: render the parts sheet (DrawBorderBitmap) ----
            // imageAddr is an encoded GBA pointer; convert to ROM offset.
            uint imgOffset = U.toOffset(imageAddr);
            if (!U.isSafetyOffset(imgOffset, rom)) return null;

            // 4-byte LZ77 header guard + truncation guard.
            if (!IsLZ77HeaderSafe(rom, imgOffset)) return null;
            uint compressedSize = LZ77.getCompressedSize(rom.Data, imgOffset);
            if (compressedSize == 0) return null;
            if ((ulong)imgOffset + compressedSize > (ulong)rom.Data.Length) return null;

            byte[] bin = LZ77.decompress(rom.Data, imgOffset);
            if (bin == null || bin.Length == 0) return null;

            // CalcHeight(32*8, bin.Length) — WF uses width=256 (32 tiles * 8 px).
            int height = CalcHeight256(bin.Length);
            if (height <= 0) return null; // WF returns Blank(256,32); we return null

            // Dereference the palette pointer: palettePointerSlot holds the ADDRESS
            // of a GBA pointer to the actual palette block — matching WF DrawBorderBitmap:
            // "uint pal = Program.ROM.p32(Program.ROM.RomInfo.worldmap_county_border_palette_pointer)".
            // TryResolveDataOffset does p32(slot) → U.toOffset → safety check.
            if (!TryResolveDataOffset(rom, palettePointerSlot, out uint palOffset)) return null;
            if (!IsRegionSafe(rom, palOffset, BORDER_PALETTE_BYTES)) return null;
            byte[] palette = new byte[BORDER_PALETTE_BYTES];
            Array.Copy(rom.Data, palOffset, palette, 0, BORDER_PALETTE_BYTES);

            IImage parts = ImageUtilCore.ByteToImage16Tile(bin, 0, palette, 0, 256, height);
            if (parts == null) return null;

            // ---- Step 2: parse AP and render two frames onto an AP layer ----
            uint apOffset = U.toOffset(apAddr);
            if (!U.isSafetyOffset(apOffset, rom)) { parts.Dispose(); return null; }

            var ap = new ImageUtilAPCore();
            if (!ap.Parse(rom.Data, apOffset)) { parts.Dispose(); return null; }

            // Frame 0: the border outline / fill.
            IImage layer0 = ap.RenderFrame(parts, 0, x, y, 256, 160);
            // Frame 1: the name text overlay.
            IImage layer1 = ap.RenderFrame(parts, 1, x, y, 256, 160);

            parts.Dispose();

            // ---- Step 3: composite ----
            // Background: TryRenderEvent renders purely from the passed rom
            // (rom.RomInfo + rom.Data only; no CoreState.ROM dependency).
            // If it returns null (FE6/FE7, missing pointers, truncated data, etc.),
            // fall back to a blank opaque 256×160 background (WF always draws one).
            IImage bg = TryRenderEvent(rom);
            if (bg == null)
            {
                bg = MakeOpaqueBlank256x160();
            }

            IImage result = CompositeBorderLayers(bg, layer0, layer1);
            bg.Dispose();
            layer0?.Dispose();
            layer1?.Dispose();
            return result;
        }

        /// <summary>
        /// Compose the AP layers over a background: draw <paramref name="layer0"/>
        /// then <paramref name="layer1"/> on top of <paramref name="background"/>,
        /// skipping pixels with alpha=0 in each layer (transparent key, G4c).
        /// Returns a new 256×160 RGBA <see cref="IImage"/>. Never throws.
        /// </summary>
        static IImage CompositeBorderLayers(IImage background, IImage layer0, IImage layer1)
        {
            if (CoreState.ImageService == null) return null;
            int w = 256, h = 160;
            var result = CoreState.ImageService.CreateImage(w, h);

            // Start from the background pixels.
            byte[] pixels = background?.GetPixelData() ?? new byte[w * h * 4];
            if (pixels.Length < w * h * 4)
            {
                byte[] padded = new byte[w * h * 4];
                Array.Copy(pixels, padded, pixels.Length);
                pixels = padded;
            }

            // Blit layer0 (frame 0 — border outline/fill).
            BlitAlphaLayer(pixels, w, h, layer0);
            // Blit layer1 (frame 1 — name text).
            BlitAlphaLayer(pixels, w, h, layer1);

            result.SetPixelData(pixels);
            return result;
        }

        /// <summary>
        /// Alpha-composite <paramref name="layer"/> (RGBA, 256×160) over
        /// <paramref name="dst"/> in-place. Source pixels with alpha=0 are skipped.
        /// </summary>
        static void BlitAlphaLayer(byte[] dst, int dstW, int dstH, IImage layer)
        {
            if (layer == null) return;
            byte[] src = layer.GetPixelData();
            int pixCount = dstW * dstH;
            int srcLen = src?.Length ?? 0;
            for (int i = 0; i < pixCount; i++)
            {
                int si = i * 4;
                if (si + 3 >= srcLen) break;
                if (src[si + 3] == 0) continue; // transparent — skip
                int di = i * 4;
                if (di + 3 >= dst.Length) break;
                dst[di + 0] = src[si + 0];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        /// <summary>
        /// Create a blank opaque (alpha=255, all black) 256×160 image as the
        /// fallback background when TryRenderEvent returns null.
        /// </summary>
        static IImage MakeOpaqueBlank256x160()
        {
            var img = CoreState.ImageService.CreateImage(256, 160);
            byte[] pixels = new byte[256 * 160 * 4];
            // All pixels black-opaque (R=0,G=0,B=0,A=255).
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            img.SetPixelData(pixels);
            return img;
        }

        /// <summary>
        /// WF <c>ImageUtil.CalcHeight(32*8, bin.Length)</c> for the border parts
        /// sheet. WF formula (verified ImageUtil.cs:890-902):
        /// <code>
        /// height = image_size / (width / 2);   // bin.Length / 128 for width=256
        /// if (image_size % (width / 2) != 0) height++;   // round up
        /// if (height % align != 0) height += align;      // align up (align=8)
        /// return height / align * align;
        /// </code>
        /// Example: bin.Length=32 → height=1 → round-up → align → 8.
        /// Example: bin.Length=4096 → height=32 (exact) → 32.
        /// Returns 0 when bin.Length ≤ 0.
        /// </summary>
        static int CalcHeight256(int binLength)
        {
            if (binLength <= 0) return 0;
            // WF: int height = image_size / (width / 2);
            //                  where image_size = bin.Length, width = 256.
            const int width = 256;
            const int align = 8;
            int height = binLength / (width / 2);   // binLength / 128
            if (binLength % (width / 2) != 0) height++;
            if (height % align != 0) height += align;
            return height / align * align;
        }
    }
}
