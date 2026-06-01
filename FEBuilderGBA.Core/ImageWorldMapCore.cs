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
// bad / truncated / out-of-bounds pointer returns null (never throws). The main
// field map (:120, NV5b) and county border (:406, NV5c) are deliberately NOT
// here: they need a NEW Core primitive (ByteToImage16TilePaletteMap) and an
// OAM-from-pointer blit respectively — separate follow-up issues.

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
        {
            if (!IsRegionSafe(rom, paletteAddr, sizeBytes)) return null;
            byte[] buf = new byte[sizeBytes];
            Array.Copy(rom.Data, paletteAddr, buf, 0, sizeBytes);
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
    }
}
