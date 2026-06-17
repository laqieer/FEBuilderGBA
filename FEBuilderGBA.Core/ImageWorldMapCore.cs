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
        const int MAIN_FIELD_VERSION_FE7 = 7;     // ROMFEINFO.version for FE7J/FE7U
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
        // FE7 Big Field Map render + import (#1184) — FE7-only, 12-split TSA.
        //
        // The FE7 world-map big field map is COMPLETELY different from the FE8
        // main field map (above): it is a 12-split (4 cols × 3 rows) grid of
        // 256×256 header-TSA pieces composited into one 1024×688 image, with the
        // bottom row visually clipped to 176 px (688 = 256 + 256 + 176). Both the
        // image AND the header-TSA are RAW (uncompressed) — UNLIKE the event
        // image (LZ77). WF reference:
        //   ImageUtilMap.DrawWorldMapFE7 (ImageUtilMap.cs:530):
        //     for the 12 pieces: image = ROM.p32(imagemap += 4),
        //                        tsa   = ROM.p32(tsamap += 4),
        //     ByteToImage16TileHeaderTSA(256, 256, ROM.Data, image, palette, tsa)
        //     BitBlt into the 1024×688 canvas at (x*256, y*256); the bottom row
        //     blits height 176 but the TSA is STILL decoded at 256 (WF quirk @127).
        //
        //   worldmap_big_image_pointer     → an array of 12 image pointers (×4 B).
        //   worldmap_big_palettemap_pointer→ an array of 12 header-TSA pointers
        //                                    (NOT a palette-map — the FE7 comment
        //                                    in ROMFE7U.cs is "TSA 12分割").
        //   worldmap_big_palette_pointer   → the shared 64-color / 4-bank palette.
        //
        // FE7-ONLY: FE8's worldmap_big_* slots are ALSO nonzero/resolvable but
        // hold a single image + palette-map (not pointer arrays), so an explicit
        // rom.RomInfo.version == 7 gate is required (Copilot #1184 plan review #1);
        // FE6's slots are 0x0 (also rejected).
        // ==================================================================

        // The full FE7 big field map is 1024×688 (12-split 256×256 pieces).
        const int FE7_BIG_WIDTH  = 1024;
        const int FE7_BIG_HEIGHT = 688;
        // Each piece is decoded at 256×256 (even the bottom row — WF quirk @127).
        const int FE7_PIECE_SIZE = 256;
        const int FE7_PIECE_COLS = 4;
        const int FE7_PIECE_ROWS = 3;
        const int FE7_PIECE_COUNT = FE7_PIECE_COLS * FE7_PIECE_ROWS; // 12
        // Bottom-row visible height (688 - 256 - 256). The bottom row's image is
        // sliced to this height on import (WF: image = U.subrange(image, 0, 256/2*176)).
        const int FE7_BOTTOM_ROW_VISIBLE_H = FE7_BIG_HEIGHT - 2 * FE7_PIECE_SIZE; // 176
        // One 256×256 4bpp piece image = 256*256/2 = 32,768 bytes (RAW, fixed).
        const int FE7_PIECE_IMAGE_BYTES = (FE7_PIECE_SIZE * FE7_PIECE_SIZE) / 2; // 32,768
        // The bottom-row piece image is sliced to 256/2 * 176 bytes on import.
        const int FE7_BOTTOM_PIECE_IMAGE_BYTES = (FE7_PIECE_SIZE / 2) * FE7_BOTTOM_ROW_VISIBLE_H; // 22,528
        // The FE7 big-map palette is 4 sub-palettes × 16 colors × 2 bytes = 128 B.
        const int FE7_BIG_PALETTE_BANKS = 4;
        const int FE7_BIG_PALETTE_BYTES = FE7_BIG_PALETTE_BANKS * 16 * 2; // 128
        // The header-TSA for a 256×256 piece with margin 0 = 2 + 32*32*2 = 2050 B.
        const int FE7_PIECE_HEADERTSA_BYTES =
            2 + (FE7_PIECE_SIZE / 8) * (FE7_PIECE_SIZE / 8) * 2; // 2050

        /// <summary>
        /// Lightweight FE7 big-field-map import gate (#1184) — true only when the
        /// ROM is FE7 AND all three <c>worldmap_big_*</c> pointer slots resolve to
        /// in-bounds offsets with a valid shared palette + 12-entry pointer arrays.
        /// Does NOT render (so the UI can gate the Import button cheaply and
        /// deterministically — unlike a full <see cref="TryRenderFE7BigFieldMap"/>
        /// which allocates a 1024×688 surface). FE6/FE8 → false. Never throws.
        /// </summary>
        public static bool CanImportFE7BigFieldMap(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE7) return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out uint imageArrayBase))
                return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer, out uint paletteAddr))
                return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palettemap_pointer, out uint tsaArrayBase))
                return false;
            if (!IsRegionSafe(rom, imageArrayBase, FE7_PIECE_COUNT * 4)) return false;
            if (!IsRegionSafe(rom, tsaArrayBase, FE7_PIECE_COUNT * 4)) return false;
            if (!IsRegionSafe(rom, paletteAddr, FE7_BIG_PALETTE_BYTES)) return false;
            return true;
        }

        /// <summary>
        /// Lightweight world-map EVENT-image import gate (#1184) — true only when
        /// the ROM is FE7 or FE8 AND all three <c>worldmap_event_*</c> pointer slots
        /// resolve to in-bounds offsets. Mirrors <see cref="ImportEvent"/>'s version
        /// + pointer guards WITHOUT mutating (so the UI gates the Event Import button
        /// on the EVENT pointers, not the big-map pointers). FE6 (event pointers 0x0)
        /// → false. Never throws.
        /// </summary>
        public static bool CanImportEvent(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE7
                && rom.RomInfo.version != MAIN_FIELD_VERSION_FE8) return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_image_pointer, out _)) return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_tsa_pointer, out _)) return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_palette_pointer, out _)) return false;
            return true;
        }

        /// <summary>
        /// Render the FE7 World Map BIG FIELD MAP preview (12-split header-TSA,
        /// #1184) at a fixed 1024×688 px. Ports WF
        /// <c>ImageUtilMap.DrawWorldMapFE7</c>: resolves the shared palette and the
        /// two 12-entry pointer arrays (<c>worldmap_big_image_pointer</c> →
        /// images, <c>worldmap_big_palettemap_pointer</c> → header-TSAs),
        /// dereferences each piece's image+TSA pointer, decodes each 256×256 piece
        /// via <see cref="ImageUtilCore.DecodeHeaderTSA"/> (the Core equivalent of
        /// WF <c>ByteToImage16TileHeaderTSA</c>), and composites the 12 pieces into
        /// one 1024×688 RGBA canvas (the bottom row is clipped to 176 px — WF
        /// quirk, but the TSA is still decoded at 256). Returns <c>null</c> (never
        /// throws) on any null / out-of-bounds / corrupt input.
        ///
        /// <para><b>FE7-only.</b> FE8's <c>worldmap_big_*</c> slots are also
        /// resolvable (single image + palette-map, NOT pointer arrays), so this
        /// gates on <c>rom.RomInfo.version == 7</c> and returns <c>null</c> for
        /// FE6/FE8 rather than mis-interpret their bytes (Copilot #1184 plan
        /// review #1).</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (FE7 only).</param>
        public static IImage TryRenderFE7BigFieldMap(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE7) return null;

            // Resolve the shared palette + the two 12-entry pointer-array bases.
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out uint imageArrayBase))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer, out uint paletteAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palettemap_pointer, out uint tsaArrayBase))
                return null;

            // The shared 64-color (4-bank) / 128-byte palette (RAW, fixed).
            byte[] palette = ReadRawPalette(rom, paletteAddr, FE7_BIG_PALETTE_BYTES);
            if (palette == null) return null;

            // Each piece's image+TSA pointer lives in the 12-entry arrays. The
            // arrays themselves must be fully in-bounds before any p32 read.
            if (!IsRegionSafe(rom, imageArrayBase, FE7_PIECE_COUNT * 4)) return null;
            if (!IsRegionSafe(rom, tsaArrayBase, FE7_PIECE_COUNT * 4)) return null;

            // The 1024×688 RGBA composite canvas.
            var canvas = CoreState.ImageService.CreateImage(FE7_BIG_WIDTH, FE7_BIG_HEIGHT);
            byte[] canvasPixels = new byte[FE7_BIG_WIDTH * FE7_BIG_HEIGHT * 4];

            int piece = 0;
            for (int row = 0; row < FE7_PIECE_ROWS; row++)
            {
                // The bottom row is visually clipped to 176 px; the TSA is still
                // decoded at 256 (WF quirk @127).
                int visibleH = (row == FE7_PIECE_ROWS - 1)
                    ? FE7_BOTTOM_ROW_VISIBLE_H : FE7_PIECE_SIZE;
                for (int col = 0; col < FE7_PIECE_COLS; col++, piece++)
                {
                    // Dereference this piece's image + header-TSA pointers.
                    if (!TryResolveDataOffset(rom, imageArrayBase + (uint)(piece * 4), out uint imgOff))
                    { canvas.Dispose(); return null; }
                    if (!TryResolveDataOffset(rom, tsaArrayBase + (uint)(piece * 4), out uint tsaOff))
                    { canvas.Dispose(); return null; }

                    // RAW image (32,768 B) + RAW header-TSA (2,050 B), fixed sizes.
                    byte[] pieceImage = ReadRawRegion(rom, imgOff, FE7_PIECE_IMAGE_BYTES);
                    if (pieceImage == null) { canvas.Dispose(); return null; }
                    byte[] pieceTsa = ReadRawRegion(rom, tsaOff, FE7_PIECE_HEADERTSA_BYTES);
                    if (pieceTsa == null) { canvas.Dispose(); return null; }

                    // Decode the 256×256 piece (32×32 tiles, palette banks via TSA
                    // bits 12-15) — the Core equivalent of WF ByteToImage16TileHeaderTSA.
                    // skipTile0:false — the FE7 big map is an opaque background, so a
                    // TSA cell of 0 is a VALID tile-0 reference (WF only skips 0xFFFF),
                    // NOT transparent (Copilot PR #1223 review #1).
                    IImage pieceImg = ImageUtilCore.DecodeHeaderTSA(
                        pieceImage, pieceTsa, palette,
                        FE7_PIECE_SIZE / 8, FE7_PIECE_SIZE / 8, true, 0, 0, skipTile0: false);
                    if (pieceImg == null) { canvas.Dispose(); return null; }

                    // BitBlt the piece's top-left (col*256, row*256) region into the
                    // canvas, clipping the bottom row to its visible height.
                    BlitPiece(canvasPixels, FE7_BIG_WIDTH, FE7_BIG_HEIGHT,
                        pieceImg, col * FE7_PIECE_SIZE, row * FE7_PIECE_SIZE,
                        FE7_PIECE_SIZE, visibleH);
                    pieceImg.Dispose();
                }
            }

            canvas.SetPixelData(canvasPixels);
            return canvas;
        }

        /// <summary>
        /// Blit the top-left <paramref name="copyW"/>×<paramref name="copyH"/>
        /// region of <paramref name="src"/> (an RGBA 256×256 piece) into
        /// <paramref name="dst"/> at (<paramref name="dstX"/>, <paramref name="dstY"/>).
        /// Opaque copy (the big field map is a full background, not a sprite).
        /// </summary>
        static void BlitPiece(byte[] dst, int dstW, int dstH, IImage src,
            int dstX, int dstY, int copyW, int copyH)
        {
            if (src == null) return;
            byte[] s = src.GetPixelData();
            if (s == null) return;
            int srcW = src.Width;
            for (int y = 0; y < copyH; y++)
            {
                int dy = dstY + y;
                if (dy < 0 || dy >= dstH) continue;
                for (int x = 0; x < copyW; x++)
                {
                    int dx = dstX + x;
                    if (dx < 0 || dx >= dstW) continue;
                    int si = (y * srcW + x) * 4;
                    int di = (dy * dstW + dx) * 4;
                    if (si + 3 >= s.Length || di + 3 >= dst.Length) continue;
                    dst[di + 0] = s[si + 0];
                    dst[di + 1] = s[si + 1];
                    dst[di + 2] = s[si + 2];
                    // Copy the SOURCE alpha — do NOT force 255. The FE7 big map is
                    // opaque (RemapFE7BigField → every cell alpha 255), so this is
                    // visually identical for FE7; but forcing 255 would turn any
                    // skipped/blank TSA cell (alpha 0 from DecodeHeaderTSA) into
                    // opaque black, corrupting transparent regions in any shared use
                    // (Copilot PR #1223 re-review #2).
                    dst[di + 3] = s[si + 3];
                }
            }
        }

        /// <summary>
        /// Import a FE7 World Map BIG FIELD MAP (#1184), the inverse of
        /// <see cref="TryRenderFE7BigFieldMap"/>. Ports WF
        /// <c>WorldMapImageFE7Form.ImportButton_Click</c>: validate the 1024×688
        /// source, derive a 4-bank palette + per-tile bank assignment, split into
        /// the 12 256×256 pieces (the bottom row's IMAGE sliced to
        /// <c>256/2*176</c> bytes but its TSA computed at 256 — WF quirk @127),
        /// encode each piece (<see cref="ImageImportCore.EncodeDirectTiles4bpp"/> +
        /// a PLAIN sequential per-tile-bank TSA via <see cref="EncodeFE7PieceTSA"/>
        /// + <see cref="ImageImportCore.EncodeHeaderTSA"/> at margin 0), and write
        /// every region RAW IN-PLACE (fixed size, NO realloc/repoint — WF:
        /// "すべて無圧縮データなので…位置の変換は絶対に起きえません") at the resolved
        /// piece pointers, plus the 128-byte palette.
        ///
        /// <para><b>FE7-only.</b> Returns a non-empty error (ZERO mutation) for
        /// FE6/FE8 (Copilot #1184 plan review #1).</para>
        ///
        /// <para><b>Validate-ALL-before-mutate.</b> Every one of the 25
        /// destinations (12 image regions, 12 header-TSA regions, the palette) is
        /// resolved + bounds-checked BEFORE the first write; the encode is checked
        /// for the exact expected lengths. A defensive <c>(byte[])rom.Data.Clone()</c>
        /// snapshot + length-aware byte-identical fault restore (#885/#923) means a
        /// FAILED import (incl. a partial-write fault) mutates ZERO bytes.</para>
        ///
        /// <para><b>Ambient undo only.</b> All writes go through the no-undoData
        /// <c>rom.write_range(addr, data)</c> overload so they land in the caller's
        /// ambient <c>ROM.BeginUndoScope</c>; this helper does NOT take an
        /// <c>Undo.UndoData</c> (Copilot #1184 plan review #3 — avoids double
        /// recording a range under an ambient scope).</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (FE7 only; writes 12 image + 12 TSA + 1
        /// palette region RAW in-place — no repoint).</param>
        /// <param name="rgba">Source pixels, 4 bytes/pixel (R,G,B,A), 1024×688.</param>
        /// <param name="srcWidth">Source width (must be 1024).</param>
        /// <param name="srcHeight">Source height (must be 688).</param>
        /// <returns>"" on success; a non-empty user-facing error (with ZERO ROM
        /// mutation) on any validation or write failure. Never throws.</returns>
        public static string ImportFE7BigFieldMap(ROM rom, byte[] rgba, int srcWidth, int srcHeight)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return "ROM not loaded.";
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE7)
                return R.Error("The world map big field map import is only supported for FE7.");
            if (CoreState.ImageService == null)
                return "Image service not available.";
            if (rgba == null)
                return "Invalid image data.";
            if (srcWidth != FE7_BIG_WIDTH || srcHeight != FE7_BIG_HEIGHT)
            {
                return R.Error(
                    "The world map big field map must be {0}x{1}.\r\n\r\nSelected image: {2}x{3}.",
                    FE7_BIG_WIDTH, FE7_BIG_HEIGHT, srcWidth, srcHeight);
            }
            if ((long)rgba.Length < (long)srcWidth * srcHeight * 4)
                return "Image pixel data is missing or too short.";

            // Resolve the shared palette + the two 12-entry pointer-array bases.
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out uint imageArrayBase))
                return R.Error("worldmap_big_image_pointer is invalid or out of ROM.");
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer, out uint paletteAddr))
                return R.Error("worldmap_big_palette_pointer is invalid or out of ROM.");
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palettemap_pointer, out uint tsaArrayBase))
                return R.Error("worldmap_big_palettemap_pointer is invalid or out of ROM.");

            // The pointer arrays must be fully in-bounds before any p32 read.
            if (!IsRegionSafe(rom, imageArrayBase, FE7_PIECE_COUNT * 4))
                return R.Error("The world map big field map image pointer table is out of ROM.");
            if (!IsRegionSafe(rom, tsaArrayBase, FE7_PIECE_COUNT * 4))
                return R.Error("The world map big field map TSA pointer table is out of ROM.");
            if (!IsRegionSafe(rom, paletteAddr, FE7_BIG_PALETTE_BYTES))
                return R.Error("The world map big field map palette region is out of ROM.");

            // Defensive snapshot for the byte-identical restore. The reduce/encode
            // do NOT mutate the ROM, so they live INSIDE the try; the caller's
            // ambient undo scope records the writes for UNDO. A FAILED import (incl.
            // a partial-write fault) mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // 1. Derive a 4-bank palette from the source + assign each 8×8 tile
                //    to the best-fitting bank, remapping pixels to local 4bpp indices.
                //    Mirrors WF ImageToPalette(bitmap, 4) + the indexed bitmap's banks.
                //    Uses the FE7-specific remap that maps opaque pixels to ALL 16
                //    colors INCLUDING index 0 — the FE7 big map is an opaque
                //    background where slot 0 is a real color (Copilot PR #1223 review
                //    #2; the generic RemapToMultiPalette reserves index 0 for
                //    transparency, shrinking each bank to 15 usable colors).
                byte[] palette128 = BuildFE7Palette(rgba, srcWidth, srcHeight);
                FE7RemapResult remap = RemapFE7BigField(rgba, srcWidth, srcHeight, palette128);
                if (remap == null || remap.IndexedPixels == null || remap.TilePaletteIndices == null)
                    return R.Error("Color reduction failed for the world map big field map.");

                // 2. Pre-resolve + bounds-check ALL 24 piece destinations and build
                //    the encoded payloads (NO mutation yet). validate-all-before-mutate.
                uint[] imgDest = new uint[FE7_PIECE_COUNT];
                uint[] tsaDest = new uint[FE7_PIECE_COUNT];
                byte[][] imgPayload = new byte[FE7_PIECE_COUNT][];
                byte[][] tsaPayload = new byte[FE7_PIECE_COUNT][];

                int piece = 0;
                for (int row = 0; row < FE7_PIECE_ROWS; row++)
                {
                    bool bottom = (row == FE7_PIECE_ROWS - 1);
                    int expectImgBytes = bottom ? FE7_BOTTOM_PIECE_IMAGE_BYTES : FE7_PIECE_IMAGE_BYTES;
                    for (int col = 0; col < FE7_PIECE_COLS; col++, piece++)
                    {
                        // Resolve + bounds-check this piece's image + TSA destination.
                        if (!TryResolveDataOffset(rom, imageArrayBase + (uint)(piece * 4), out uint imgOff))
                            return R.Error("World map big field map image piece {0} pointer is invalid.", piece);
                        if (!TryResolveDataOffset(rom, tsaArrayBase + (uint)(piece * 4), out uint tsaOff))
                            return R.Error("World map big field map TSA piece {0} pointer is invalid.", piece);
                        if (!IsRegionSafe(rom, imgOff, expectImgBytes))
                            return R.Error("World map big field map image piece {0} region is out of ROM.", piece);
                        if (!IsRegionSafe(rom, tsaOff, FE7_PIECE_HEADERTSA_BYTES))
                            return R.Error("World map big field map TSA piece {0} region is out of ROM.", piece);

                        // Extract this 256×256 piece's local indices + per-tile banks
                        // (the bottom row is still extracted at 256 for the TSA).
                        ExtractFE7Piece(remap, srcWidth, col * FE7_PIECE_SIZE, row * FE7_PIECE_SIZE,
                            out byte[] pieceIdx, out int[] pieceBanks);

                        // Encode the piece image (plain 4bpp tiles, NO dedup — WF
                        // isPackedImage=false) at 256×256, then slice the bottom row
                        // to its visible height.
                        byte[] pieceImage = ImageImportCore.EncodeDirectTiles4bpp(
                            pieceIdx, FE7_PIECE_SIZE, FE7_PIECE_SIZE);
                        if (pieceImage == null || pieceImage.Length != FE7_PIECE_IMAGE_BYTES)
                            return R.Error("Failed to encode world map big field map image piece {0}.", piece);
                        if (bottom)
                            pieceImage = U.subrange(pieceImage, 0, (uint)FE7_BOTTOM_PIECE_IMAGE_BYTES);
                        if (pieceImage.Length != expectImgBytes)
                            return R.Error("World map big field map image piece {0} encoded to the wrong length.", piece);

                        // Encode a PLAIN sequential per-tile-bank TSA at 256×256, then
                        // wrap it as a header-TSA with margin 0 (WF parity).
                        byte[] plainTsa = EncodeFE7PieceTSA(pieceBanks, FE7_PIECE_SIZE, FE7_PIECE_SIZE);
                        byte[] headerTsa = ImageImportCore.EncodeHeaderTSA(
                            plainTsa, FE7_PIECE_SIZE, FE7_PIECE_SIZE, 0);
                        if (headerTsa == null || headerTsa.Length != FE7_PIECE_HEADERTSA_BYTES)
                            return R.Error("Failed to encode world map big field map TSA piece {0}.", piece);

                        imgDest[piece] = imgOff;
                        tsaDest[piece] = tsaOff;
                        imgPayload[piece] = pieceImage;
                        tsaPayload[piece] = headerTsa;
                    }
                }

                // 3. All destinations validated + all payloads encoded — NOW write
                //    everything RAW in-place under the caller's ambient undo. A fault
                //    at any step restores the ROM byte-identically.
                rom.write_range(paletteAddr, palette128);
                for (int p = 0; p < FE7_PIECE_COUNT; p++)
                {
                    rom.write_range(imgDest[p], imgPayload[p]);
                    rom.write_range(tsaDest[p], tsaPayload[p]);
                }
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return "World map big field map import failed: " + ex.Message;
            }
        }

        /// <summary>Result of <see cref="RemapFE7BigField"/>: local 4bpp indices
        /// (0..15, index 0 usable) + per-8×8-tile bank.</summary>
        class FE7RemapResult
        {
            public byte[] IndexedPixels;
            public int[] TilePaletteIndices;
        }

        /// <summary>
        /// Remap the FE7 big-field-map source RGBA to local 4bpp indices + per-tile
        /// banks, mapping EVERY pixel (opaque OR not) to the closest of ALL 16
        /// colors in its tile's chosen bank — INCLUDING index 0 (the FE7 big map is
        /// an opaque background where slot 0 is a real color; Copilot PR #1223
        /// review #2). Differs from <see cref="ImageImportCore.RemapToMultiPalette"/>
        /// only in that it does NOT reserve index 0 for transparency. Each 8×8 tile
        /// is assigned to the bank with the lowest total color distance (over all 16
        /// colors). Returns null on degenerate input. Never throws.
        /// </summary>
        static FE7RemapResult RemapFE7BigField(byte[] rgba, int width, int height, byte[] gbaPalette)
        {
            if (rgba == null || gbaPalette == null) return null;
            if (width % 8 != 0 || height % 8 != 0) return null;
            if (CoreState.ImageService == null) return null;
            int pixelCount = width * height;
            if ((long)rgba.Length < (long)pixelCount * 4) return null;

            // Pre-convert all bank colors (all 16 each) to RGB.
            byte[][] palR = new byte[FE7_BIG_PALETTE_BANKS][];
            byte[][] palG = new byte[FE7_BIG_PALETTE_BANKS][];
            byte[][] palB = new byte[FE7_BIG_PALETTE_BANKS][];
            for (int sp = 0; sp < FE7_BIG_PALETTE_BANKS; sp++)
            {
                palR[sp] = new byte[16]; palG[sp] = new byte[16]; palB[sp] = new byte[16];
                for (int c = 0; c < 16; c++)
                {
                    int pi = (sp * 16 + c) * 2;
                    if (pi + 1 < gbaPalette.Length)
                    {
                        ushort col = (ushort)(gbaPalette[pi] | (gbaPalette[pi + 1] << 8));
                        CoreState.ImageService.GBAColorToRGBA(col, out palR[sp][c], out palG[sp][c], out palB[sp][c]);
                    }
                }
            }

            int tilesX = width / 8;
            int tilesY = height / 8;
            byte[] indexed = new byte[pixelCount];
            int[] tileBanks = new int[tilesX * tilesY];

            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    // Pick the best bank (lowest total distance over ALL 16 colors).
                    long bestDist = long.MaxValue;
                    int bestBank = 0;
                    for (int sp = 0; sp < FE7_BIG_PALETTE_BANKS; sp++)
                    {
                        long total = 0;
                        for (int py = 0; py < 8; py++)
                        {
                            for (int px = 0; px < 8; px++)
                            {
                                int off = ((ty * 8 + py) * width + (tx * 8 + px)) * 4;
                                int pr = rgba[off], pg = rgba[off + 1], pb = rgba[off + 2];
                                int minD = int.MaxValue;
                                for (int c = 0; c < 16; c++) // INCLUDE index 0
                                {
                                    int dr = pr - palR[sp][c], dg = pg - palG[sp][c], db = pb - palB[sp][c];
                                    int d = dr * dr + dg * dg + db * db;
                                    if (d < minD) minD = d;
                                }
                                total += minD;
                            }
                        }
                        if (total < bestDist) { bestDist = total; bestBank = sp; }
                    }
                    tileBanks[ty * tilesX + tx] = bestBank;

                    // Map each pixel to the closest of ALL 16 colors in that bank.
                    for (int py = 0; py < 8; py++)
                    {
                        for (int px = 0; px < 8; px++)
                        {
                            int pos = (ty * 8 + py) * width + (tx * 8 + px);
                            int off = pos * 4;
                            int pr = rgba[off], pg = rgba[off + 1], pb = rgba[off + 2];
                            int best = 0, bestD = int.MaxValue;
                            for (int c = 0; c < 16; c++) // INCLUDE index 0
                            {
                                int dr = pr - palR[bestBank][c], dg = pg - palG[bestBank][c], db = pb - palB[bestBank][c];
                                int d = dr * dr + dg * dg + db * db;
                                if (d < bestD) { bestD = d; best = c; }
                            }
                            indexed[pos] = (byte)best;
                        }
                    }
                }
            }
            return new FE7RemapResult { IndexedPixels = indexed, TilePaletteIndices = tileBanks };
        }

        /// <summary>
        /// Build the 4-bank (128-byte) GBA palette for the FE7 big field map from
        /// the source RGBA by sampling the first 16 unique colors per 8×8-tile
        /// group (the cross-platform equivalent of WF
        /// <c>ImageToPalette(bitmap, 4)</c>, which reads the indexed bitmap's 64
        /// palette entries). Index 0 of each bank is sampled like every other entry
        /// (WF parity). Never throws.
        /// </summary>
        static byte[] BuildFE7Palette(byte[] rgba, int width, int height)
        {
            byte[] result = new byte[FE7_BIG_PALETTE_BYTES];
            if (CoreState.ImageService == null) return result;
            int tilesX = width / 8;
            int tilesY = height / 8;
            int tilesPerBank = (tilesX * tilesY + FE7_BIG_PALETTE_BANKS - 1) / FE7_BIG_PALETTE_BANKS;

            for (int bank = 0; bank < FE7_BIG_PALETTE_BANKS; bank++)
            {
                var seen = new System.Collections.Generic.List<uint>(16);
                int startTile = bank * tilesPerBank;
                int endTile = Math.Min(startTile + tilesPerBank, tilesX * tilesY);
                for (int ti = startTile; ti < endTile && seen.Count < 16; ti++)
                {
                    int ty = (ti / tilesX) * 8;
                    int tx = (ti % tilesX) * 8;
                    for (int py = 0; py < 8 && seen.Count < 16; py++)
                    {
                        for (int px = 0; px < 8 && seen.Count < 16; px++)
                        {
                            int idx = ((ty + py) * width + (tx + px)) * 4;
                            if (idx + 3 >= rgba.Length) continue;
                            uint c = (uint)(rgba[idx] | (rgba[idx + 1] << 8) | (rgba[idx + 2] << 16));
                            if (!seen.Contains(c)) seen.Add(c);
                        }
                    }
                }
                for (int ci = 0; ci < seen.Count && ci < 16; ci++)
                {
                    byte r = (byte)(seen[ci] & 0xFF);
                    byte g = (byte)((seen[ci] >> 8) & 0xFF);
                    byte b = (byte)((seen[ci] >> 16) & 0xFF);
                    ushort gba = CoreState.ImageService.RGBAToGBAColor(r, g, b);
                    int off = (bank * 16 + ci) * 2;
                    result[off] = (byte)(gba & 0xFF);
                    result[off + 1] = (byte)(gba >> 8);
                }
            }
            return result;
        }

        /// <summary>
        /// Extract one 256×256 piece's local 4bpp indices + per-8×8-tile bank from
        /// the full-image multi-palette remap result. The piece's pixels are copied
        /// row-major into a 256×256 buffer (out-of-range source rows/cols default to
        /// index 0 / bank 0); the per-tile banks come from the full-image
        /// <c>TilePaletteIndices</c>. Used for ALL rows — the bottom row is still
        /// extracted at 256 so its TSA (computed at 256) is correct (WF quirk).
        /// </summary>
        static void ExtractFE7Piece(FE7RemapResult remap,
            int fullWidth, int pieceX, int pieceY,
            out byte[] pieceIdx, out int[] pieceBanks)
        {
            pieceIdx = new byte[FE7_PIECE_SIZE * FE7_PIECE_SIZE];
            int pieceTilesX = FE7_PIECE_SIZE / 8;
            pieceBanks = new int[pieceTilesX * pieceTilesX];

            int fullTilesX = fullWidth / 8;
            for (int y = 0; y < FE7_PIECE_SIZE; y++)
            {
                int sy = pieceY + y;
                for (int x = 0; x < FE7_PIECE_SIZE; x++)
                {
                    int sx = pieceX + x;
                    int sIdx = sy * fullWidth + sx;
                    if (sIdx >= 0 && sIdx < remap.IndexedPixels.Length)
                        pieceIdx[y * FE7_PIECE_SIZE + x] = remap.IndexedPixels[sIdx];
                }
            }
            for (int ty = 0; ty < pieceTilesX; ty++)
            {
                for (int tx = 0; tx < pieceTilesX; tx++)
                {
                    int fullTileIdx = ((pieceY / 8) + ty) * fullTilesX + ((pieceX / 8) + tx);
                    int bank = 0;
                    if (fullTileIdx >= 0 && fullTileIdx < remap.TilePaletteIndices.Length)
                        bank = remap.TilePaletteIndices[fullTileIdx];
                    pieceBanks[ty * pieceTilesX + tx] = bank;
                }
            }
        }

        /// <summary>
        /// Encode a PLAIN (sequential, NON-deduplicated) TSA for a piece — WF
        /// <c>ImageToBytePlainTSA</c> (isPackedImage=false): entry N =
        /// <c>(N &amp; 0x3FF) | (bank &lt;&lt; 12)</c>, one entry per 8×8 tile in
        /// row-major order, where <paramref name="tileBanks"/> is the per-tile
        /// palette bank. Output length = <c>width/4 * height/8</c> bytes
        /// (= tilesX*tilesY*2). Used before <see cref="ImageImportCore.EncodeHeaderTSA"/>.
        /// </summary>
        static byte[] EncodeFE7PieceTSA(int[] tileBanks, int width, int height)
        {
            int tilesX = width / 8;
            int tilesY = height / 8;
            int total = tilesX * tilesY;
            byte[] data = new byte[total * 2];
            for (int n = 0; n < total; n++)
            {
                int bank = (n < tileBanks.Length) ? tileBanks[n] : 0;
                ushort entry = (ushort)(((uint)n & 0x3FF) | (((uint)bank & 0xF) << 12));
                data[n * 2] = (byte)(entry & 0xFF);
                data[n * 2 + 1] = (byte)((entry >> 8) & 0xFF);
            }
            return data;
        }

        // ==================================================================
        // FE6 World Map Big Field Map — 256-color LINEAR render + import (#1183).
        //
        // The FE6 world map is COMPLETELY different from the FE7 (12-split TSA)
        // and FE8 (16-tile palette-map) big field maps: it is a FLAT 256-color
        // raster (one byte/pixel = a direct 256-color index, row-major). There
        // are FIVE zoom views (full + 4 quadrants NW/NE/SW/SE), each reading a
        // CONSECUTIVE pointer slot — image at worldmap_big_image_pointer +
        // {0,8,16,24,32} and palette at worldmap_big_palette_pointer +
        // {0,8,16,24,32}. Both the image AND palette are LZ77-compressed.
        //
        // WF reference: WorldMapImageFE6Form + ImageUtilMap.DrawWorldMapFE6
        //   (decode) + ImageFormRef ZLINER256IMAGE/ZPALETTE import (both LZ77).
        // FE6-ONLY: FE7/FE8's worldmap_big_* slots are also resolvable but hold a
        // different format, so an explicit rom.RomInfo.version == 6 gate is
        // required (Copilot #1184 plan review #1, same reasoning as the FE7 gate).
        // ==================================================================

        // The FE6 big field map renders at a fixed 240×160 (one GBA screen).
        const int FE6_VERSION = 6;
        const int FE6_BIG_WIDTH = 240;
        const int FE6_BIG_HEIGHT = 160;

        /// <summary>
        /// Render one FE6 World Map zoom view (the flat 256-color LINEAR big field
        /// map) at 240×160. Resolves the image + palette pointers at
        /// <c>worldmap_big_{image,palette}_pointer + <paramref name="slotByteOffset"/></c>
        /// (one of {0,8,16,24,32} = full / NW / NE / SW / SE) pointer-to-pointer,
        /// LZ77-decompresses BOTH, and decodes via
        /// <see cref="ImageUtilCore.ByteToImage256Liner"/>. FE6-only. Returns
        /// <c>null</c> (never throws) on any null / non-FE6 / out-of-bounds /
        /// corrupt / truncated input.
        /// </summary>
        /// <param name="rom">Loaded ROM (FE6 only).</param>
        /// <param name="slotByteOffset">Byte offset into the consecutive pointer
        /// arrays: 0 = full, 8 = NW, 16 = NE, 24 = SW, 32 = SE.</param>
        public static IImage TryRenderFE6BigFieldMap(ROM rom, uint slotByteOffset)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (rom.RomInfo.version != FE6_VERSION) return null;

            // Resolve the per-zoom image + palette pointers (pointer-to-pointer).
            // TryResolveDataOffset bounds-checks the slot itself before the u32 read.
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer + slotByteOffset, out uint imageAddr))
                return null;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer + slotByteOffset, out uint paletteAddr))
                return null;

            // FE6 image AND palette are BOTH LZ77-compressed (WF DrawWorldMapFE6).
            if (!TryDecompressGuarded(rom, imageAddr, out byte[] image)) return null;
            if (!TryDecompressGuarded(rom, paletteAddr, out byte[] palette)) return null;

            return ImageUtilCore.ByteToImage256Liner(
                image, 0, palette, 0, FE6_BIG_WIDTH, FE6_BIG_HEIGHT);
        }

        /// <summary>
        /// Lightweight FE6 big-field-map render gate — true only when the ROM is
        /// FE6 AND BOTH the slot-0 (full map) image AND palette pointers resolve to
        /// in-bounds offsets. Rendering needs BOTH LZ77 streams (image + palette),
        /// so gating on the image pointer alone could report true while every
        /// preview blank-renders on a bad palette pointer (Copilot #1183 plan
        /// review #2). FE7/FE8 → false. Does NOT render (cheap UI gate). Never throws.
        /// </summary>
        public static bool CanRenderFE6BigFieldMap(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != FE6_VERSION) return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_image_pointer, out _))
                return false;
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_big_palette_pointer, out _))
                return false;
            return true;
        }

        /// <summary>
        /// Lightweight FE6 per-zoom import gate — true only when the ROM is FE6 AND
        /// the image + palette pointer SLOTS at
        /// <c>worldmap_big_{image,palette}_pointer + <paramref name="slotByteOffset"/></c>
        /// are WRITABLE (in-bounds for the <see cref="ROM.write_p32"/> repoint).
        /// Gates on slot WRITABILITY (<c>IsRegionSafe(slot, 4)</c>), NOT on the
        /// currently-pointed image/palette RESOLVING: import allocates a NEW
        /// compressed image + palette and repoints the slots, so requiring the OLD
        /// targets to resolve would wrongly disable import in the corrupt-pointer
        /// repair case (Copilot #1183 plan review #3). FE7/FE8 → false. Never throws.
        /// </summary>
        public static bool CanImportFE6BigFieldMap(ROM rom, uint slotByteOffset)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != FE6_VERSION) return false;
            // The image + palette pointer SLOTS must be in-bounds for the write_p32
            // repoint. Do NOT require the old targets to resolve (repair case).
            if (!IsRegionSafe(rom, rom.RomInfo.worldmap_big_image_pointer + slotByteOffset, 4))
                return false;
            if (!IsRegionSafe(rom, rom.RomInfo.worldmap_big_palette_pointer + slotByteOffset, 4))
                return false;
            return true;
        }

        /// <summary>
        /// Import one FE6 World Map zoom view (240×160 flat 256-color raster) — the
        /// inverse of <see cref="TryRenderFE6BigFieldMap"/>. Ports the WF FE6
        /// <c>ImageFormRef</c> <c>ZLINER256IMAGE</c> + <c>ZPALETTE</c> import path:
        /// encode the source to 256-linear index bytes + a 256-color palette
        /// (<see cref="ImageUtilCore.EncodeImage256Liner"/>; a &gt;256-color source
        /// is REJECTED with ZERO mutation), then LZ77-compress + write BOTH to free
        /// space and repoint their slot pointers at
        /// <c>worldmap_big_{image,palette}_pointer + <paramref name="slotByteOffset"/></c>.
        /// Mirrors #1184 <see cref="ImportFE7BigFieldMap"/>: validate-all-before-
        /// mutate, a defensive snapshot + length-aware byte-identical fault restore
        /// (#885/#923), and ambient undo (the writes land in the caller's
        /// <c>ROM.BeginUndoScope</c>; no <c>Undo.UndoData</c> param so no range is
        /// double-recorded — Copilot #1184 plan review #3).
        ///
        /// <para><b>FE6-only.</b> Returns a non-empty error (ZERO mutation) for
        /// FE7/FE8. Both the image AND the palette slots are repointed = WF parity
        /// (the FE6 form's import rewrites <c>ZLINER256IMAGE</c> AND <c>ZPALETTE</c>).</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (FE6 only).</param>
        /// <param name="slotByteOffset">0/8/16/24/32 = full/NW/NE/SW/SE.</param>
        /// <param name="rgba">Source pixels, 4 bytes/pixel (R,G,B,A), 240×160.</param>
        /// <param name="srcWidth">Source width (must be 240).</param>
        /// <param name="srcHeight">Source height (must be 160).</param>
        /// <returns>"" on success; a non-empty user-facing error (with ZERO ROM
        /// mutation) on any validation or write failure. Never throws.</returns>
        public static string ImportFE6BigFieldMap(ROM rom, uint slotByteOffset,
            byte[] rgba, int srcWidth, int srcHeight)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return "ROM not loaded.";
            if (rom.RomInfo.version != FE6_VERSION)
                return R.Error("The world map big field map import is only supported for FE6.");
            if (CoreState.ImageService == null)
                return "Image service not available.";
            if (rgba == null)
                return "Invalid image data.";
            if (srcWidth != FE6_BIG_WIDTH || srcHeight != FE6_BIG_HEIGHT)
            {
                return R.Error(
                    "The world map big field map must be {0}x{1}.\r\n\r\nSelected image: {2}x{3}.",
                    FE6_BIG_WIDTH, FE6_BIG_HEIGHT, srcWidth, srcHeight);
            }
            if ((long)rgba.Length < (long)srcWidth * srcHeight * 4)
                return "Image pixel data is missing or too short.";

            // The image + palette pointer SLOTS (these get repointed by
            // WriteCompressedToROM). They must be in-bounds before any write.
            uint imageSlot = rom.RomInfo.worldmap_big_image_pointer + slotByteOffset;
            uint paletteSlot = rom.RomInfo.worldmap_big_palette_pointer + slotByteOffset;
            if (!IsRegionSafe(rom, imageSlot, 4))
                return R.Error("worldmap_big_image_pointer slot is invalid or out of ROM.");
            if (!IsRegionSafe(rom, paletteSlot, 4))
                return R.Error("worldmap_big_palette_pointer slot is invalid or out of ROM.");

            // Defensive snapshot for the byte-identical restore. The encode does
            // NOT mutate the ROM, so it lives INSIDE the try; the caller's ambient
            // undo scope records the writes for UNDO. A FAILED import (incl. a
            // partial-write fault between the two WriteCompressedToROM calls)
            // mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // Encode to 256-linear index bytes + 256-color palette. A
                // >256-color source is rejected here (false) with ZERO mutation.
                if (!ImageUtilCore.EncodeImage256Liner(rgba, srcWidth, srcHeight,
                        out byte[] image256, out byte[] palette512)
                    || image256 == null || palette512 == null)
                {
                    return R.Error(
                        "The world map big field map must use 256 colors or fewer.");
                }

                // Write the LZ77-compressed image to free space + repoint the slot.
                uint w1 = ImageImportCore.WriteCompressedToROM(rom, image256, imageSlot);
                if (w1 == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R.Error("Failed to write world map image. Check ROM free space.");
                }

                // Write the LZ77-compressed palette to free space + repoint the slot
                // (WF parity: the FE6 import rewrites ZPALETTE too).
                uint w2 = ImageImportCore.WriteCompressedToROM(rom, palette512, paletteSlot);
                if (w2 == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R.Error("Failed to write world map palette. Check ROM free space.");
                }

                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return "World map big field map import failed: " + ex.Message;
            }
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

            // Defensive snapshot for the byte-identical restore on fault. The
            // caller's ambient undo scope captures the writes for UNDO; this
            // snapshot guarantees a FAILED import mutates ZERO bytes. Taken
            // BEFORE the encode so the encode is INSIDE the try (the "never
            // throws" contract must hold even if EncodeDirectTiles4bpp throws
            // on an unexpected input — the encode does not mutate the ROM, so
            // the restore is a no-op in that case).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                byte[] tiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, widthPx, heightPx);
                if (tiles == null || tiles.Length == 0)
                {
                    RestoreSnapshot(rom, snap);
                    return ImportResult.Fail("Failed to encode 4bpp tile data.");
                }
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

        // ==================================================================
        // Event image import — TWO-STREAM TSA (#1064 PR1).
        //
        // The world-map EVENT graphic is the inverse of TryRenderEvent: it has
        // TWO LZ77 streams (ZIMAGE deduplicated tiles + ZHEADERTSA header-TSA,
        // CAUTION 1) plus a fixed RAW 64-color / 4-bank palette (128 bytes,
        // CAUTION 2 — NEVER compressed; TryRenderEvent reads it raw).
        //
        // WF reference: WMEvent = ImageFormRef(this,"WMEvent",32*8,20*8,
        // palette_count=4, event_image_ptr, event_tsa_ptr, event_palette_ptr);
        // import = ImageFormRef.WriteImageData. We COMPOSE the existing primitives:
        //   DecreaseColorConvertCore.Convert(maxPalette:4,yohaku:16,reserve1st:true)
        //     → banked IndexData (256x160 = 240x160 + 16px yohaku) + 4-bank palette
        //   split IndexData → localIndex(&0x0F) + per-tile bank(/16)
        //   EncodeTSAMultiPalette → dedup tiles + raw TSA (palette in bits 12-15)
        //   EncodeHeaderTSA(rawTsa,256,160,margin:2) → header-TSA (first 2 bytes 0x1D,0x13)
        //   WriteCompressedToROM ZIMAGE + WriteCompressedToROM ZHEADERTSA + raw 128B palette.
        //
        // Validate-all-before-mutate; ONE caller ambient undo scope; defensive
        // byte-identical (length-aware) fault restore (#885/#923) — a FAILED
        // import mutates ZERO bytes (incl. a partial-write fault after ZIMAGE).
        // ==================================================================

        // The visible event map is 240x160 (30x20 tiles); the reducer's 16px
        // (2-tile) right margin (yohaku) extends it to the 256x160 / 32x20-tile
        // event canvas WMEvent draws. The source MUST be the 240x160 visible map.
        const int EVENT_SRC_WIDTH  = 240;
        const int EVENT_SRC_HEIGHT = 160;
        const int EVENT_CANVAS_WIDTH  = EVENT_TILES_X * 8; // 256
        const int EVENT_CANVAS_HEIGHT = EVENT_TILES_Y * 8; // 160
        // TSA tile-index field is bits 0-9 (1024 max unique tiles).
        const int EVENT_MAX_UNIQUE_TILES = 1024;
        // Event palette banks: 4 (CAUTION 2). The reducer caps at maxPalette=4.
        const int EVENT_MAX_BANKS = 4;

        /// <summary>
        /// Import a World Map EVENT image (two-stream TSA, #1064 PR1). Composes
        /// <see cref="DecreaseColorConvertCore.Convert"/> (the method-4
        /// "World Map (event)" preset: <c>maxPalette:4, yohaku:16, reserve1st:true,
        /// ignoreTSA:false</c>) → <see cref="ImageImportCore.EncodeTSAMultiPalette"/>
        /// → <see cref="ImageImportCore.EncodeHeaderTSA"/> and writes the three
        /// canonical event streams: ZIMAGE (LZ77 tiles) to
        /// <c>worldmap_event_image_pointer</c>, ZHEADERTSA (LZ77 header-TSA —
        /// COMPRESSED, matching <see cref="TryRenderEvent"/>) to
        /// <c>worldmap_event_tsa_pointer</c>, and the RAW 64-color / 128-byte
        /// palette to <c>worldmap_event_palette_pointer</c>.
        ///
        /// <para><b>Auto-reduce default with post-reduction validation.</b> The
        /// source MUST be the 240×160 visible event map (the reducer adds the
        /// 16-px / 2-tile right margin to reach the 256×160 / 32×20-tile canvas).
        /// Rejects (return false, ZERO mutation) when: the source is not 240×160,
        /// the reduce does not land on exactly 256×160, &gt;4 banks, any 8×8 tile
        /// mixes banks (<see cref="ValidateEventBankedIndices"/>), or the unique
        /// tile count exceeds 1024.</para>
        ///
        /// <para><b>Pointer guard.</b> All three <c>worldmap_event_*_pointer</c>
        /// slots must be nonzero and resolve to in-ROM offsets (FE6 has them as
        /// 0x0; a truncated/corrupt slot fails the resolve) — rejected with no
        /// mutation BEFORE any write, mirroring <see cref="ImportIconStrip"/>.</para>
        ///
        /// <para><b>Atomic.</b> validate-all-before-mutate; ONE caller ambient undo
        /// scope; defensive byte-identical (length-aware) fault restore — any
        /// fault (incl. a partial-write fault after the ZIMAGE write) restores the
        /// ROM byte- AND length-identical. Never throws.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (writes only the 3 event pointers).</param>
        /// <param name="rgba">Source pixels, 4 bytes/pixel (R,G,B,A), 240×160.</param>
        /// <param name="srcWidth">Source width (must be 240).</param>
        /// <param name="srcHeight">Source height (must be 160).</param>
        /// <param name="error">Empty on success; a user-facing message on failure.</param>
        /// <returns>true on success; false (with <paramref name="error"/> set and
        /// ZERO ROM mutation) on any validation or write failure.</returns>
        public static bool ImportEvent(ROM rom, byte[] rgba, int srcWidth, int srcHeight, out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            { error = "ROM not loaded."; return false; }
            // Explicit FE7/FE8 gate BEFORE any work. The world-map EVENT image uses
            // the IDENTICAL ImageFormRef(...,32*8,20*8,palette_count=4) construction
            // and the version-independent encode path (Convert/EncodeTSAMultiPalette/
            // EncodeHeaderTSA) on BOTH FE7 and FE8 — only this gate and the error
            // string are version-specific. FE7's worldmap_event_* pointers are
            // nonzero/resolvable; FE6 has them as 0x0 (rejected by the pointer guard
            // below, and by this version check) (#1184 generalizes the FE8-only #1098
            // gate to FE7 for the World Map Image (FE7) editor's event-import parity).
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE7
                && rom.RomInfo.version != MAIN_FIELD_VERSION_FE8)
            { error = R.Error("The world map event image import is only supported for FE7 and FE8."); return false; }
            if (rgba == null)
            { error = "Invalid image data."; return false; }
            if (srcWidth != EVENT_SRC_WIDTH || srcHeight != EVENT_SRC_HEIGHT)
            {
                error = R.Error(
                    "The world map event image must be {0}x{1}.\r\n\r\nSelected image: {2}x{3}.",
                    EVENT_SRC_WIDTH, EVENT_SRC_HEIGHT, srcWidth, srcHeight);
                return false;
            }
            if ((long)rgba.Length < (long)srcWidth * srcHeight * 4)
            { error = "Image pixel data is missing or too short."; return false; }

            // --- Pointer guard (no reliance on the UI gate). All three event
            // pointer slots must be nonzero and resolve to in-ROM offsets. FE6 has
            // them as 0x0; a truncated/corrupt slot fails the resolve. Rejected
            // BEFORE any write (WriteCompressedToROM/WriteRawToROM repoint a slot
            // without validating it). ---
            if (!TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_image_pointer, out _) ||
                !TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_tsa_pointer, out _) ||
                !TryResolveDataOffset(rom, rom.RomInfo.worldmap_event_palette_pointer, out _))
            {
                error = R.Error("The world map event image pointers are not set for this ROM.");
                return false;
            }

            // Defensive snapshot for the byte-identical restore. The reduce/encode
            // do NOT mutate the ROM, so they live INSIDE the try (the "never throws"
            // contract holds even if a primitive throws on unexpected input — the
            // restore is then a no-op). The caller's ambient undo scope records the
            // writes for UNDO; this snapshot guarantees a FAILED import (incl. a
            // partial-write fault) mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // 1. Reduce to the banked 256x160 event canvas + 4-bank palette.
                DecreaseColorConvertCore.DecreaseColorConvertResult r =
                    DecreaseColorConvertCore.Convert(rgba, srcWidth, srcHeight,
                        maxPalette: EVENT_MAX_BANKS, yohaku: 16, reserve1st: true, ignoreTSA: false);
                if (r == null || r.IndexData == null || r.GbaPalette == null)
                { error = "Color reduction failed."; return false; }

                // 2. Post-reduction validation (NO mutation yet).
                if (r.Width != EVENT_CANVAS_WIDTH || r.Height != EVENT_CANVAS_HEIGHT)
                {
                    error = R.Error(
                        "Color reduction produced {0}x{1}, expected {2}x{3}.",
                        r.Width, r.Height, EVENT_CANVAS_WIDTH, EVENT_CANVAS_HEIGHT);
                    return false;
                }
                if (r.PaletteBankCount > EVENT_MAX_BANKS)
                {
                    error = R.Error(
                        "The world map event image needs {0} palette banks, but only {1} are available. Simplify the source image so it fits in {1} 16-color palettes and re-import.",
                        r.PaletteBankCount, EVENT_MAX_BANKS);
                    return false;
                }
                string bankErr = ValidateEventBankedIndices(r.IndexData, r.Width, r.Height);
                if (!string.IsNullOrEmpty(bankErr)) { error = bankErr; return false; }

                // 3. Split the banked indices into local 4bpp pixels + per-tile bank.
                if (!SplitEventBankedIndices(r.IndexData, r.Width, r.Height,
                        out byte[] localPixels, out int[] tileBanks))
                {
                    error = R.Error("Could not encode the reduced world map event image. Simplify the source image and re-import.");
                    return false;
                }

                // 4. Encode the multi-bank TSA (palette bits 12-15 per tile) and
                //    wrap it into the header-TSA format (first 2 bytes 0x1D,0x13).
                ImageImportCore.TSAEncodeResult tsa = ImageImportCore.EncodeTSAMultiPalette(
                    localPixels, EVENT_CANVAS_WIDTH, EVENT_CANVAS_HEIGHT, tileBanks);
                if (tsa == null || tsa.TileData == null || tsa.TSAData == null)
                { error = "Failed to encode TSA data."; return false; }
                if (tsa.UniqueTileCount > EVENT_MAX_UNIQUE_TILES)
                {
                    error = R.Error(
                        "Too many unique tiles ({0}); the world map event supports at most {1}.",
                        tsa.UniqueTileCount, EVENT_MAX_UNIQUE_TILES);
                    return false;
                }
                byte[] headerTsa = ImageImportCore.EncodeHeaderTSA(
                    tsa.TSAData, EVENT_CANVAS_WIDTH, EVENT_CANVAS_HEIGHT, 2);
                if (headerTsa == null || headerTsa.Length <= 2)
                { error = "Failed to encode header-TSA."; return false; }

                // The RAW 64-color / 128-byte palette (CAUTION 2 — NOT compressed).
                byte[] palette128 = new byte[EVENT_PALETTE_BYTES]; // 128
                Array.Copy(r.GbaPalette, 0, palette128, 0, EVENT_PALETTE_BYTES);

                // 5. Write all three streams under the caller's ambient undo scope.
                //    A fault at ANY step restores the ROM byte-identically.
                uint imgAddr = ImageImportCore.WriteCompressedToROM(
                    rom, tsa.TileData, rom.RomInfo.worldmap_event_image_pointer);
                if (imgAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = "Failed to write image. Check ROM free space."; return false; }

                uint tsaAddr = ImageImportCore.WriteCompressedToROM(
                    rom, headerTsa, rom.RomInfo.worldmap_event_tsa_pointer);
                if (tsaAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = "Failed to write TSA. Check ROM free space."; return false; }

                uint palAddr = ImageImportCore.WriteRawToROM(
                    rom, palette128, rom.RomInfo.worldmap_event_palette_pointer);
                if (palAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = "Failed to write palette. Check ROM free space."; return false; }

                return true;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                error = "World map event import failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Validate that every 8×8 tile of a banked index buffer (1 byte/pixel,
        /// value = <c>bank*16 + localIndex</c>) uses a SINGLE palette bank and that
        /// the bank is ≤3 (the event has 4 banks max). The bank of a tile is the
        /// first non-zero pixel's <c>value / 16</c>; an all-index-0 tile is bank 0.
        /// Returns "" on success, or a user-facing message naming the violating
        /// tile/pixel. PURE — the caller MUST NOT write on a non-empty return.
        /// (Public so it is reachable for direct unit-test coverage of crafted
        /// banked data — Copilot plan-review finding #2.)
        /// </summary>
        public static string ValidateEventBankedIndices(byte[] indexedPixels, int width, int height)
        {
            if (indexedPixels == null || width <= 0 || height <= 0) return "Invalid image data.";
            if (width % 8 != 0 || height % 8 != 0) return "Image dimensions must be multiples of 8.";
            if (indexedPixels.Length < (long)width * height) return "Pixel buffer is too short.";

            for (int y = 0; y < height; y += 8)
            {
                for (int x = 0; x < width; x += 8)
                {
                    int tileBank = -1; // negative = unset (all index-0 so far)
                    for (int y8 = 0; y8 < 8; y8++)
                    {
                        for (int x8 = 0; x8 < 8; x8++)
                        {
                            int v = indexedPixels[(y + y8) * width + (x + x8)];
                            int bank = v / 16;
                            int local = v & 0x0F;
                            if (bank > EVENT_MAX_BANKS - 1)
                            {
                                return R.Error(
                                    "The world map event image needs more than {0} palette banks: the tile at X:{1} Y:{2} requires bank {3} (only banks 0..{4} are available). Simplify the source image so it fits in {0} 16-color palettes and re-import.",
                                    EVENT_MAX_BANKS, x, y, bank, EVENT_MAX_BANKS - 1);
                            }
                            // An all-zero pixel (bank 0, local 0) is transparent and
                            // does not pin the tile's bank — it is valid in any bank.
                            if (bank == 0 && local == 0) continue;
                            if (tileBank < 0) { tileBank = bank; }
                            else if (bank != tileBank)
                            {
                                return R.Error(
                                    "The world map event image cannot be encoded: every 8x8 tile must use a single 16-color palette bank. The tile starting at X:{0} Y:{1} mixes banks — the pixel at X:{2} Y:{3} needs bank {4} while the rest of the tile uses bank {5}. Simplify the source image so each tile uses one palette and re-import.",
                                    x, y, x + x8, y + y8, bank, tileBank);
                            }
                        }
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Split a validated banked index buffer (value = <c>bank*16 + localIndex</c>)
        /// into local 4bpp pixels (<c>value &amp; 0x0F</c>) and a per-8×8-tile bank
        /// array (<c>value / 16</c> of the tile's first non-zero pixel; 0 for an
        /// all-index-0 tile). Returns false on a structurally invalid buffer.
        /// </summary>
        static bool SplitEventBankedIndices(byte[] indexedPixels, int width, int height,
            out byte[] localPixels, out int[] tileBanks)
        {
            localPixels = null;
            tileBanks = null;
            if (indexedPixels == null || width % 8 != 0 || height % 8 != 0) return false;
            if (indexedPixels.Length < (long)width * height) return false;

            int tilesX = width / 8;
            int tilesY = height / 8;
            localPixels = new byte[width * height];
            tileBanks = new int[tilesX * tilesY];

            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int bank = 0; // default for an all-index-0 tile
                    bool bankSet = false;
                    for (int y8 = 0; y8 < 8; y8++)
                    {
                        for (int x8 = 0; x8 < 8; x8++)
                        {
                            int pos = (ty * 8 + y8) * width + (tx * 8 + x8);
                            int v = indexedPixels[pos];
                            localPixels[pos] = (byte)(v & 0x0F);
                            int b = v / 16;
                            int local = v & 0x0F;
                            // Pin the tile bank to the first non-transparent pixel.
                            if (!bankSet && !(b == 0 && local == 0)) { bank = b; bankSet = true; }
                        }
                    }
                    tileBanks[ty * tilesX + tx] = bank;
                }
            }
            return true;
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

        // ==================================================================
        // County Border IMPORT — OAM/AP assembly (#1064 PR2, closes #1000).
        //
        // The inverse of TryRenderBorder. Concern (c) — ROM writes — of the
        // three-concern split (see ImageUtilBorderAPCore for (a) pure assembly +
        // (b) input-error returns). This seam:
        //   1. FE8-only gate (version==8 AND worldmap_county_border_palette_pointer
        //      != 0) — reject non-FE8 with ZERO mutation (mirrors ImportEvent's
        //      explicit version gate the Copilot PR #1098 review required).
        //   2. Validate the border-record pointer slot (P0 @ +0, P4 @ +4) is in
        //      range.
        //   3. AssembleBorderAP (pure) — rejects null/empty _NAME, wrong dims, the
        //      images.Count>=2 overflow, and clamps the origin (WF parity).
        //   4. Defensive (byte[])rom.Data.Clone() snapshot; LZ77-write the seat
        //      image -> record P0, then raw-write the AP -> record P4, under the
        //      caller's ambient undo. Length-aware byte-identical restore on ANY
        //      fault (incl. a partial fault after the image write) (#885/#923).
        // ==================================================================

        /// <summary>
        /// Import a World Map county BORDER graphic (OAM/AP assembly, #1064 PR2 /
        /// closes #1000). Assembles the two already-decoded INDEXED sheets (the
        /// chosen border sheet + its <c>_NAME</c> companion, each 248×160) into one
        /// seat + AP-data block via <see cref="ImageUtilBorderAPCore.AssembleBorderAP"/>,
        /// then LZ77-writes the seat image to the border record's <c>P0</c> (image
        /// pointer @ <paramref name="borderRecordAddr"/>+0) and raw-writes the AP
        /// data to the record's <c>P4</c> (AP pointer @ +4), repointing both under
        /// the caller's ambient undo scope.
        ///
        /// <para><b>FE8-only.</b> Rejects non-FE8 ROMs (FE6/FE7 have no county
        /// border — <c>worldmap_county_border_palette_pointer</c> is 0x0) with NO
        /// mutation, mirroring <see cref="ImportEvent"/>'s explicit version gate.</para>
        ///
        /// <para><b>Atomic.</b> validate-all-before-mutate; ONE caller ambient undo
        /// scope; defensive byte-identical (length-aware) fault restore — any fault
        /// (incl. a partial-write fault after the image write but during the AP
        /// write/repoint) restores the ROM byte- AND length-identical. Never throws.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (writes only the selected border record's P0/P4).</param>
        /// <param name="sheetIndexed">Main border sheet, indexed (1 byte/pixel), 248×160.</param>
        /// <param name="nameIndexed">Companion <c>_NAME</c> sheet, indexed, 248×160.
        /// A null/empty value (the View's "_NAME missing" case) is rejected.</param>
        /// <param name="palette16">16-color GBA palette (32 bytes), shared by both sheets.</param>
        /// <param name="originX">Origin X (clamped to ≤60, WF parity).</param>
        /// <param name="originY">Origin Y (clamped to ≤50, WF parity).</param>
        /// <param name="borderRecordAddr">ROM offset of the selected 12-byte border
        /// record (P0 image pointer @ +0, P4 AP pointer @ +4).</param>
        /// <param name="error">Empty on success; a user-facing message on failure.</param>
        /// <returns>true on success; false (with <paramref name="error"/> set and
        /// ZERO ROM mutation) on any validation or write failure.</returns>
        public static bool ImportBorder(ROM rom,
            byte[] sheetIndexed, byte[] nameIndexed, byte[] palette16,
            uint originX, uint originY, uint borderRecordAddr, out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            { error = "ROM not loaded."; return false; }

            // 1. FE8-only gate (BEFORE any work). FE6/FE7 have no county border.
            if (rom.RomInfo.version != MAIN_FIELD_VERSION_FE8 ||
                rom.RomInfo.worldmap_county_border_palette_pointer == 0)
            { error = R.Error("The world map border image import is only supported for FE8."); return false; }

            // 2. Validate the border-record pointer slots are in range (P0 @ +0,
            //    P4 @ +4; the 12-byte record's last P4 byte is at +7). Enforce the
            //    project safety-offset rule (>= 0x200) so a bad caller cannot pass a
            //    ROM-header address (e.g. 0x100) and corrupt the header via the
            //    P0/P4 repoint (Copilot PR #1099 review).
            if (borderRecordAddr == 0 ||
                !U.isSafetyOffset(borderRecordAddr, rom) ||
                (long)borderRecordAddr + 8 > rom.Data.Length)
            { error = R.Error("The world map border record address is invalid."); return false; }

            // Defensive snapshot for the byte-identical restore. The assembly does
            // NOT mutate the ROM, so it lives INSIDE the try (the "never throws"
            // contract holds even if a primitive throws on unexpected input — the
            // restore is then a no-op). The caller's ambient undo scope records the
            // writes for UNDO; this snapshot guarantees a FAILED import (incl. a
            // partial-write fault) mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // 3. (a) pure assembly + (b) input-error returns — no mutation yet.
                var asm = ImageUtilBorderAPCore.AssembleBorderAP(
                    sheetIndexed, nameIndexed, palette16, originX, originY);
                if (!asm.Success) { error = asm.Error; return false; }

                // 4. ROM writes under the caller's ambient undo. A fault at ANY
                //    step restores the ROM byte-identically.
                //    P0 (image pointer) is at borderRecordAddr+0; P4 (AP pointer)
                //    at +4 — exactly the slots WriteCompressedToROM / WriteRawToROM
                //    repoint (matching WF WriteImageData(P0) + WriteBinaryData(P4)).
                uint imgAddr = ImageImportCore.WriteCompressedToROM(
                    rom, asm.ImageBytes, borderRecordAddr + 0);
                if (imgAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = R._("Failed to write image. Check ROM free space."); return false; }

                uint apAddr = ImageImportCore.WriteRawToROM(
                    rom, asm.ApBytes, borderRecordAddr + 4);
                if (apAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = R._("Failed to write AP data. Check ROM free space."); return false; }

                return true;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                error = "World map border import failed: " + ex.Message;
                return false;
            }
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
