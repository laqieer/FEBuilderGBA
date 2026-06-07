// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform helpers for the battle-screen layout editor (#393).
//
// Extracted from the WinForms ImageBattleScreenForm so the Avalonia
// ImageBattleScreenView can share the TSA + palette + image-pointer read
// and write paths without a System.Windows.Forms or System.Drawing
// dependency. See issue #393.
//
// Design notes (from accepted plan v2 + Copilot CLI plan review round 1):
//
//   * The battle-screen layout is a 32 x 20 cell TSA grid (640 u16 cells).
//     The grid is split across 5 ROM TSA regions:
//       TSA1: y=[0..5],  x=[1..15]   → 90 cells (top-left)
//       TSA2: y=[0..5],  x=[16..30]  → 90 cells (top-right)
//       TSA3: y=[13..19], x=[1..15]  → 105 cells (bottom-left)
//       TSA4: y=[13..19], x=[16..31] → 112 cells (bottom-right)
//       TSA5: y=[0..19],  x=[31..32] → 40 cells; x=32 wraps to x=0.
//     The mid-vertical strip y=[6..12] is the live battle area, drawn by
//     the engine — no TSA entries.
//
//   * Address inputs are GBA pointers in ROM (0x08...). The core
//     dereferences via rom.p32() and writes back via rom.write_u16 / write_p32,
//     which automatically pick up the [ThreadStatic] ROM.BeginUndoScope
//     ambient undo data (Plan v2 Finding #2: no Undo.UndoData parameter
//     anywhere — single undo model).
//
//   * Palette I/O delegates to PaletteCore.ReadPalette / WritePalette
//     for the BGR15 packing and paletteIndex * 0x20 offset arithmetic
//     (Plan v2 Finding #3: reuse the existing helper).

using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for the battle-screen layout editor (#393).
    /// See file-level comment for the TSA region layout and design notes.
    /// </summary>
    public static class ImageBattleScreenCore
    {
        /// <summary>Width of the TSA map in cells.</summary>
        public const int MAP_X = 32;

        /// <summary>Height of the TSA map in cells.</summary>
        public const int MAP_Y = 20;

        /// <summary>Total cells in the TSA map (32 * 20 = 640).</summary>
        public const int MAP_SIZE = MAP_X * MAP_Y;

        // Byte sizes of each TSA region (cells * 2 bytes per cell).
        const int TSA1_BYTES = 6 * 15 * 2;   // 180
        const int TSA2_BYTES = 6 * 15 * 2;   // 180
        const int TSA3_BYTES = 7 * 15 * 2;   // 210
        const int TSA4_BYTES = 7 * 16 * 2;   // 224
        const int TSA5_BYTES = 20 * 2 * 2;   // 80

        /// <summary>
        /// Validate that <paramref name="addr"/> is a safe ROM offset and
        /// that reading/writing <paramref name="bytes"/> bytes starting at
        /// it stays inside <c>rom.Data</c>.
        ///
        /// Combines <see cref="U.isSafetyOffset(uint, ROM)"/>'s domain
        /// constraints (offset in [0x200, 0x02000000) and within rom.Data)
        /// with an explicit end-of-range check that uses <c>ulong</c>
        /// arithmetic so the addition cannot overflow even on near-uint.MaxValue
        /// inputs. Sentinel <see cref="U.NOT_FOUND"/> is implicitly rejected
        /// via the upper bound. Per Copilot bot PR #594 review round 2.
        /// </summary>
        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (bytes <= 0) return false;
            // Last byte that will be touched: addr + bytes - 1. Use ulong
            // to guard against overflow on huge bytes values.
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)rom.Data.Length;
        }

        // The GBA LZ77 stream header is 4 bytes (0x10 + a 3-byte uncompressed
        // size). LZ77.getCompressedSize / getUncompressSize only reject when
        // FEWER THAN 3 bytes remain, yet they read input[offset + 3] -- so a
        // pointer to the LAST 1-3 bytes of the ROM passes isSafetyOffset but
        // makes that header read throw IndexOutOfRangeException (Copilot PR #818
        // review). Require the FULL 4-byte header to be in-bounds BEFORE any
        // LZ77 call so the null-safe preview path returns null/false instead of
        // throwing. Shared with the #804/#807 loader paths (hardens them too).
        const int LZ77_HEADER_BYTES = 4;
        static bool IsLZ77HeaderSafe(ROM rom, uint addr) => IsRegionSafe(rom, addr, LZ77_HEADER_BYTES);

        /// <summary>
        /// Read the 32 x 20 TSA map from the 5 TSA regions in <paramref name="rom"/>.
        /// Returns a <see cref="ushort"/> array of length <see cref="MAP_SIZE"/>;
        /// cells not covered by any TSA region (the central battle area y=[6..12])
        /// are zero. On corrupt/out-of-bounds pointers in any region, that region
        /// is skipped (leaves zeros) rather than throwing -- matches the
        /// PaletteCore graceful-failure convention (Copilot bot PR #594 review).
        /// </summary>
        public static ushort[] LoadBattleScreen(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return null;
            ushort[] map = new ushort[MAP_SIZE];

            uint addr;

            // TSA1: y=[0..5], x=[1..15] (180 bytes)
            addr = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            if (IsRegionSafe(rom, addr, TSA1_BYTES))
            {
                for (int y = 0; y <= 5; y++)
                {
                    for (int x = 1; x <= 15; x++)
                    {
                        map[y * MAP_X + x] = (ushort)rom.u16(addr);
                        addr += 2;
                    }
                }
            }

            // TSA2: y=[0..5], x=[16..30] (180 bytes)
            addr = rom.p32(rom.RomInfo.battle_screen_TSA2_pointer);
            if (IsRegionSafe(rom, addr, TSA2_BYTES))
            {
                for (int y = 0; y <= 5; y++)
                {
                    for (int x = 16; x <= 30; x++)
                    {
                        map[y * MAP_X + x] = (ushort)rom.u16(addr);
                        addr += 2;
                    }
                }
            }

            // TSA3: y=[13..19], x=[1..15] (210 bytes)
            addr = rom.p32(rom.RomInfo.battle_screen_TSA3_pointer);
            if (IsRegionSafe(rom, addr, TSA3_BYTES))
            {
                for (int y = 13; y <= 19; y++)
                {
                    for (int x = 1; x <= 15; x++)
                    {
                        map[y * MAP_X + x] = (ushort)rom.u16(addr);
                        addr += 2;
                    }
                }
            }

            // TSA4: y=[13..19], x=[16..31] (224 bytes)
            addr = rom.p32(rom.RomInfo.battle_screen_TSA4_pointer);
            if (IsRegionSafe(rom, addr, TSA4_BYTES))
            {
                for (int y = 13; y <= 19; y++)
                {
                    for (int x = 16; x <= 31; x++)
                    {
                        map[y * MAP_X + x] = (ushort)rom.u16(addr);
                        addr += 2;
                    }
                }
            }

            // TSA5: y=[0..19], x=[31..32]. x=32 wraps to x=0. (80 bytes)
            addr = rom.p32(rom.RomInfo.battle_screen_TSA5_pointer);
            if (IsRegionSafe(rom, addr, TSA5_BYTES))
            {
                for (int y = 0; y <= 19; y++)
                {
                    for (int x = 31; x <= 32; x++)
                    {
                        int xx = x == 32 ? 0 : x;
                        map[y * MAP_X + xx] = (ushort)rom.u16(addr);
                        addr += 2;
                    }
                }
            }

            return map;
        }

        // Battle-screen image-pointer slots in WF GetChipImage order
        // (image1..image5). They are LZ77-compressed 8px-wide tile strips
        // stacked vertically into one tileset (#802 Blocker 2).
        const int BATTLE_SCREEN_WIDTH_TILES = 32;   // 256 px
        const int BATTLE_SCREEN_HEIGHT_TILES = 20;  // 160 px
        const int BATTLE_SCREEN_PALETTE_BYTES = 16 * 16 * 2; // 16 banks * 16 colors * 2 bytes = 512

        // The 5 image-pointer slots in WF order (image1..image5). Centralized so
        // RenderSingleImagePreview's index->slot mapping stays in sync with the
        // concatenated loader.
        static uint[] ImagePointerSlots(ROM rom) => new uint[]
        {
            rom.RomInfo.battle_screen_image1_pointer,
            rom.RomInfo.battle_screen_image2_pointer,
            rom.RomInfo.battle_screen_image3_pointer,
            rom.RomInfo.battle_screen_image4_pointer,
            rom.RomInfo.battle_screen_image5_pointer,
        };

        /// <summary>
        /// Width-estimation alignment used by every WF per-image call site here
        /// (WF's default <c>align = 8</c>).
        /// </summary>
        const int LINER_ALIGN = 8;

        /// <summary>
        /// Liner width estimate for an already-resolved ROM offset, ported
        /// VERBATIM from WinForms <c>U.CalcLZ77LinerImageToWidth</c>
        /// (FEBuilderGBA/U.cs:5953-5972). The width is
        /// <c>(uncompSize / 2 / 2 / align) * align</c> -- i.e. it FLOORS to a
        /// multiple of <paramref name="align"/> (NOT a plain <c>uncompSize/4</c>
        /// approximation). Clamps to <paramref name="align"/> when the offset is
        /// unsafe, <c>uncompSize &lt;= 0</c>, or the floored result is
        /// <c>&lt;= 0</c>. Used for image2..image5 (height fixed at 8px).
        /// </summary>
        static int CalcLinerImageToWidth(ROM rom, uint addr, int align = LINER_ALIGN)
        {
            // 4-byte LZ77-header bounds check BEFORE getUncompressSize: a
            // last-1-3-bytes pointer passes isSafetyOffset but makes the
            // header read throw (Copilot PR #818 review). Treat as unmeasurable.
            if (!IsLZ77HeaderSafe(rom, addr)) return align;
            uint size = LZ77.getUncompressSize(rom.Data, addr);
            if (size <= 0) return align;
            int a = (int)size / 2 / 2 / align;
            if (a <= 0) return align;
            return a * align;
        }

        /// <summary>
        /// Natural (width, height) estimate for an already-resolved ROM offset,
        /// ported VERBATIM from WinForms <c>U.CalcLZ77ImageToSize</c>
        /// (FEBuilderGBA/U.cs:5974-5989). Takes the liner width (height-1 guess),
        /// then scans <c>w = 32..1</c> for the first <c>w*8</c> that evenly
        /// divides it -- the "nice divisor" width -- returning
        /// <c>(w*8, width/(w*8)*8)</c>. The WF
        /// <c>Debug.Assert(false)</c>/<c>Size(8,8)</c> tail is UNREACHABLE
        /// (<c>w = 1</c> always divides a multiple-of-8 width), so the Core port
        /// drops the assert and just defaults to <c>(align, align)</c> safely.
        /// Used for image1 (its natural W x H).
        /// </summary>
        static (int width, int height) CalcImageToSize(ROM rom, uint addr, int align = LINER_ALIGN)
        {
            // Height-1 guess: how wide is the strip if it were a single row?
            int width = CalcLinerImageToWidth(rom, addr, align);

            // Find a "nice" divisor width.
            for (int w = 32; w >= 1; w--)
            {
                if (width % (w * 8) == 0)
                {
                    return (w * 8, width / (w * 8) * 8);
                }
            }
            // Unreachable in practice (w=1 divides any multiple-of-8 width); the
            // WF code asserts false here. Default safely.
            return (align, align);
        }

        /// <summary>
        /// Render a live preview of the battle screen (256 x 160) by mirroring
        /// the WinForms <c>ImageBattleScreenForm.GetChipImage</c> +
        /// <c>MakeBattleScreen</c> pipeline cross-platform (#802). Returns an
        /// <see cref="IImage"/> on success, or <c>null</c> (no crash) if any
        /// REQUIRED source is missing/corrupt so we never partial-render a
        /// corrupt ROM.
        ///
        /// Pipeline:
        ///   * Palette (Blocker 1): RAW 16-bank GBA palette (512 bytes) read
        ///     directly at <c>battle_screen_palette_pointer</c> -- NOT LZ77
        ///     (WF passes the palette offset straight to ByteToImage16Tile).
        ///   * Tiles (Blocker 2): LZ77-decompress image1..image5, each 8px
        ///     wide, concatenated vertically in order into one tileset. Each
        ///     stream is validated with <c>LZ77.getCompressedSize</c> first so a
        ///     truncated-but-header-valid chunk (which <c>LZ77.decompress</c>
        ///     would otherwise return as a zero-filled buffer) fails the whole
        ///     render instead of rendering a misleading blank chunk.
        ///   * TSA (Blocker 3): <see cref="LoadBattleScreen"/> returns RAW GBA
        ///     TSA u16 cells. WF MakeBattleScreen reads tile=m&amp;0xff,
        ///     flip=(m&gt;&gt;8)&amp;0x0f, pal=m&gt;&gt;12 with flip mapping
        ///     0=none / 4=hflip / 8=vflip / any-other-nonzero=both. We
        ///     re-encode each cell into the GBA-standard layout DecodeTSA reads
        ///     (tile bits0-9, hFlip bit10, vFlip bit11, pal bits12-15).
        ///   * Alpha (Blocker 4): palette index 0 renders OPAQUE to match WF
        ///     BitBlt (transparent_index = 0xFF never matches a 0..15 index).
        /// </summary>
        public static IImage RenderBattleScreenPreview(ROM rom)
        {
            if (rom == null || rom.RomInfo == null) return null;
            if (rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            // --- Blocker 3: TSA grid (RAW GBA u16 cells) ---
            ushort[] map = LoadBattleScreen(rom);
            if (map == null) return null;

            // --- Blocker 1 + 2: RAW 16-bank palette + concatenated tileset ---
            // Extracted into the shared TryLoadChipsetAndPalette helper (#805)
            // so the chipset preview can reuse the EXACT same load path. A null
            // return (any corrupt/missing REQUIRED source) fails the whole
            // render -- no partial-render of a corrupt ROM.
            if (!TryLoadChipsetAndPalette(rom, out byte[] tileData, out byte[] gbaPalette))
                return null;

            // --- Blocker 3 (cont.): normalize RAW WF cells -> GBA-standard TSA ---
            // WF MakeBattleScreen: tile = m & 0xff, flip = (m >> 8) & 0x0f,
            // pal = m >> 12. flip mapping: 0=none, 4=hflip, 8=vflip,
            // any-other-nonzero=both. Re-encode into DecodeTSA's layout.
            byte[] tsaData = new byte[map.Length * 2];
            for (int i = 0; i < map.Length; i++)
            {
                ushort m = map[i];
                int tile = m & 0xff;
                int flip = (m >> 8) & 0x0f;
                int pal = (m >> 12) & 0x0f;

                bool h, v;
                if (flip == 0) { h = false; v = false; }
                else if (flip == 4) { h = true; v = false; }
                else if (flip == 8) { h = false; v = true; }
                else { h = true; v = true; } // any other nonzero -> both (WF)

                ushort norm = (ushort)((tile & 0x3ff)
                    | (h ? (1 << 10) : 0)
                    | (v ? (1 << 11) : 0)
                    | (pal << 12));

                tsaData[i * 2] = (byte)(norm & 0xff);
                tsaData[i * 2 + 1] = (byte)((norm >> 8) & 0xff);
            }

            // --- Blocker 4: opaque index 0 (match WF BitBlt) ---
            return ImageUtilCore.DecodeTSA(
                tileData, tsaData, gbaPalette,
                BATTLE_SCREEN_WIDTH_TILES, BATTLE_SCREEN_HEIGHT_TILES,
                is4bpp: true, tsaOffset: 0, opaqueIndex0: true);
        }

        /// <summary>
        /// Shared loader (#805) for the battle-screen tileset + palette. Pulled
        /// verbatim out of <see cref="RenderBattleScreenPreview"/> (#802) so the
        /// chipset preview (<see cref="RenderChipsetPreview"/>) reuses the EXACT
        /// same load path: behavior is preserved, not re-implemented.
        ///
        /// Outputs on success:
        ///   * <paramref name="tileData"/> -- image1..image5 LZ77-decompressed
        ///     (each 8px wide), concatenated vertically in order into one
        ///     4bpp tileset.
        ///   * <paramref name="palette"/> -- the RAW 16-bank GBA palette
        ///     (512 bytes) read directly at <c>battle_screen_palette_pointer</c>
        ///     (NOT LZ77 -- WF passes the palette offset straight to
        ///     ByteToImage16Tile).
        ///
        /// Returns <c>false</c> (and null outputs) if ANY of the five REQUIRED
        /// image strips or the palette is missing / out-of-bounds / corrupt /
        /// truncated. Each LZ77 stream is validated with
        /// <c>LZ77.getCompressedSize</c> BEFORE decompressing: a truncated but
        /// header-valid chunk (which <c>LZ77.decompress</c> would otherwise
        /// return as a zero-filled buffer) fails the whole load instead of
        /// silently yielding a blank chunk (#802 PR #804 review contract).
        /// </summary>
        static bool TryLoadChipsetAndPalette(ROM rom, out byte[] tileData, out byte[] palette)
        {
            tileData = null;
            palette = null;
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;

            // --- Blocker 1: RAW 16-bank palette (512 bytes, NO LZ77) ---
            // Factored into TryLoadRawPalette so the per-image preview (#816)
            // reads the EXACT same palette (behavior-preserving).
            if (!TryLoadRawPalette(rom, out byte[] gbaPalette)) return false;

            // --- Blocker 2: LZ77 image1..image5 concatenated vertically ---
            uint[] imagePointerSlots = ImagePointerSlots(rom);

            int totalLength = 0;
            byte[][] chunks = new byte[imagePointerSlots.Length][];
            for (int i = 0; i < imagePointerSlots.Length; i++)
            {
                // Each image1..5 is REQUIRED (WF blits all five): a bad pointer
                // or LZ77 failure must fail the whole render, not skip a chunk.
                // The per-strip decode (pointer deref + isSafetyOffset +
                // getCompressedSize truncation guard + end-of-ROM bound +
                // decompress) is factored into TryDecodeImageStrip so the
                // single-image preview (#816) reuses the EXACT same load path.
                if (!TryDecodeImageStrip(rom, imagePointerSlots[i], out byte[] chunk)) return false;
                chunks[i] = chunk;
                totalLength += chunk.Length;
            }
            if (totalLength == 0) return false;

            byte[] tiles = new byte[totalLength];
            int writePos = 0;
            for (int i = 0; i < chunks.Length; i++)
            {
                Array.Copy(chunks[i], 0, tiles, writePos, chunks[i].Length);
                writePos += chunks[i].Length;
            }

            tileData = tiles;
            palette = gbaPalette;
            return true;
        }

        /// <summary>
        /// Decode ONE LZ77 image strip stored at <paramref name="pointerSlot"/>
        /// (e.g. <c>battle_screen_image2_pointer</c>) into its raw 4bpp tile
        /// bytes. This is the per-strip load path factored verbatim out of
        /// <see cref="TryLoadChipsetAndPalette"/>'s concatenation loop so the
        /// single-image preview (#816) reuses the EXACT same guards: dereference
        /// the pointer, <see cref="U.isSafetyOffset(uint, ROM)"/>, the
        /// <c>LZ77.getCompressedSize == 0</c> truncation guard (a truncated but
        /// header-valid stream that <c>LZ77.decompress</c> would zero-fill must
        /// fail), an end-of-ROM bound on <c>(addr + compressedSize)</c>, then
        /// <c>LZ77.decompress</c>. Returns <c>false</c> (and a null output) on any
        /// failure -- no partial-render of a corrupt strip.
        /// </summary>
        static bool TryDecodeImageStrip(ROM rom, uint pointerSlot, out byte[] tiles)
        {
            tiles = null;
            if (rom == null || rom.Data == null) return false;

            uint imageAddr = rom.p32(pointerSlot);
            // 4-byte LZ77-header bounds check: a pointer to the LAST 1-3 bytes of
            // the ROM passes isSafetyOffset, yet getCompressedSize reads the
            // 4-byte header (input[addr + 3]) and would throw
            // IndexOutOfRangeException. Require the full header in-bounds first so
            // the null-safe path returns false (no throw) -- Copilot PR #818
            // review. (Subsumes the bare isSafetyOffset check.)
            if (!IsLZ77HeaderSafe(rom, imageAddr)) return false;

            // Validate the compressed stream BEFORE decompressing.
            // LZ77.getCompressedSize returns the ACTUAL consumed compressed
            // length only when the full stream decodes within the ROM bounds,
            // and 0 on any corruption / truncation / bad header. A defensive
            // end-of-ROM bound check on (imageAddr + compressedSize) additionally
            // guards an overrun (#802 PR #804 review contract).
            uint compressedSize = LZ77.getCompressedSize(rom.Data, imageAddr);
            if (compressedSize == 0) return false;
            if ((ulong)imageAddr + (ulong)compressedSize > (ulong)rom.Data.Length) return false;

            byte[] chunk = LZ77.decompress(rom.Data, imageAddr);
            if (chunk == null || chunk.Length == 0) return false;
            tiles = chunk;
            return true;
        }

        /// <summary>
        /// Read the RAW 16-bank battle-screen palette (512 bytes, NOT LZ77)
        /// directly at <c>battle_screen_palette_pointer</c> -- the same read
        /// <see cref="TryLoadChipsetAndPalette"/> does for Blocker 1 (WF passes
        /// the palette offset straight to <c>ByteToImage16Tile</c>). Returns
        /// <c>false</c> (null output) if the pointer or its 512-byte span is
        /// out of bounds. Shared by the per-image preview (#816) so it renders
        /// with the SAME palette as the composite/chipset previews.
        /// </summary>
        static bool TryLoadRawPalette(ROM rom, out byte[] gbaPalette)
        {
            gbaPalette = null;
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;

            uint palAddr = rom.p32(rom.RomInfo.battle_screen_palette_pointer);
            if (!U.isSafetyOffset(palAddr, rom)) return false;
            ulong palLast = (ulong)palAddr + (ulong)BATTLE_SCREEN_PALETTE_BYTES - 1UL;
            if (palLast >= (ulong)rom.Data.Length) return false;

            byte[] pal = new byte[BATTLE_SCREEN_PALETTE_BYTES];
            Array.Copy(rom.Data, palAddr, pal, 0, BATTLE_SCREEN_PALETTE_BYTES);
            gbaPalette = pal;
            return true;
        }

        /// <summary>
        /// Load ONE battle-screen image strip (<paramref name="imageIndex"/> in
        /// 0..4 -> image1..image5) at its WinForms per-image dimensions (#816).
        /// Mirrors <c>ImageBattleScreenForm.InitLoadChipsetInfo</c> exactly:
        ///   * <paramref name="imageIndex"/> == 0 (image1): natural W x H via the
        ///     ported <see cref="CalcImageToSize"/> "nice divisor" loop.
        ///   * <paramref name="imageIndex"/> in 1..4 (image2..image5): a single
        ///     horizontal row -- <c>CalcLinerImageToWidth x (1 * 8)</c> (8px tall).
        /// NOTE: these are the WF per-image widths, NOT a slice of the 8px-wide
        /// concatenated ChipCache sheet -- the same tiles laid out at the
        /// per-image width vs. an 8px column produce DIFFERENT images.
        ///
        /// Returns <c>false</c> (and null/zero outputs) for an out-of-range index
        /// or any strip-load failure (corrupt / truncated / out-of-bounds).
        /// </summary>
        public static bool TryLoadSingleImageStrip(ROM rom, int imageIndex,
            out byte[] tiles, out int widthPx, out int heightPx)
        {
            tiles = null;
            widthPx = 0;
            heightPx = 0;
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (imageIndex < 0 || imageIndex > 4) return false;

            uint[] slots = ImagePointerSlots(rom);
            uint pointerSlot = slots[imageIndex];

            if (!TryDecodeImageStrip(rom, pointerSlot, out byte[] chunk)) return false;

            // Compute the per-image dimensions from the ALREADY-resolved strip
            // offset, mirroring WF (image1 = natural; image2..5 = liner x 8).
            uint imageAddr = rom.p32(pointerSlot);
            if (imageIndex == 0)
            {
                var (w, h) = CalcImageToSize(rom, imageAddr);
                widthPx = w;
                heightPx = h;
            }
            else
            {
                widthPx = CalcLinerImageToWidth(rom, imageAddr);
                heightPx = 1 * 8; // WF: srcImageWidth[i] x (1 * 8)
            }

            tiles = chunk;
            return true;
        }

        /// <summary>
        /// Render a single battle-screen image strip
        /// (<paramref name="imageIndex"/> in 0..4 -> image1..image5) to an
        /// <see cref="IImage"/> at its WinForms per-image dimensions (#816,
        /// follow-up to #802/#804/#807). The strip's 4bpp tiles are laid out
        /// ROW-MAJOR at the per-image width via
        /// <see cref="ImageUtilCore.DecodeTileToPixels"/> with palette
        /// <b>bank 0</b> (WF passes the palette base straight to
        /// <c>ImageFormRef</c>/<c>ByteToImage16Tile</c> -- bank 0, NOT a
        /// TSA-derived bank) and palette index 0 OPAQUE (matching the WF
        /// <c>BitBlt</c> with <c>transparent_index = 0xFF</c>).
        ///
        /// Null-safe: an out-of-range <paramref name="imageIndex"/>, a missing
        /// <see cref="CoreState.ImageService"/>, or any strip-load failure
        /// (corrupt / truncated / out-of-bounds) returns <c>null</c> (no crash).
        /// </summary>
        public static IImage RenderSingleImagePreview(ROM rom, int imageIndex)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (imageIndex < 0 || imageIndex > 4) return null;

            if (!TryLoadSingleImageStrip(rom, imageIndex, out byte[] tileData, out int widthPx, out int heightPx))
                return null;
            if (widthPx <= 0 || heightPx <= 0) return null;

            // Same RAW 16-bank palette as the composite/chipset previews; we use
            // bank 0 (WF passes the palette base straight to ByteToImage16Tile).
            if (!TryLoadRawPalette(rom, out byte[] gbaPalette)) return null;

            // Tiles are 8x8 blocks placed ROW-MAJOR within the per-image width:
            // tilesPerRow = widthPx / 8; tile t -> column (t % tilesPerRow),
            // row (t / tilesPerRow). This is exactly how ByteToImage16Tile lays
            // an 8px-tile sheet into a target width.
            const int bytesPerTile = 32; // 4bpp
            int tileCount = tileData.Length / bytesPerTile;
            if (tileCount <= 0) return null;

            int tilesPerRow = widthPx / 8;
            if (tilesPerRow <= 0) return null;

            var image = CoreState.ImageService.CreateImage(widthPx, heightPx);
            byte[] pixels = new byte[widthPx * heightPx * 4]; // RGBA

            for (int tile = 0; tile < tileCount; tile++)
            {
                int col = tile % tilesPerRow;
                int row = tile / tilesPerRow;
                int destX = col * 8;
                int destY = row * 8;
                // Stop if a stray extra tile would fall outside the computed
                // per-image height (DecodeTileToPixels also clips defensively).
                if (destY >= heightPx) break;

                ImageUtilCore.DecodeTileToPixels(
                    tileData, tile, gbaPalette, palIndex: 0,
                    pixels, widthPx, destX, destY,
                    hFlip: false, vFlip: false, is4bpp: true, opaqueIndex0: true);
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Render the WinForms <c>ImageBattleScreenForm.MakeCHIPLIST()</c> chip
        /// list cross-platform (#805, follow-up to #802). The chip list shows
        /// every 8x8 tile of the concatenated battle-screen tileset in 8
        /// adjacent columns of flip/palette-bank permutations -- the reference
        /// grid the WF editor draws above the TSA paint area.
        ///
        /// Mirrors WF MakeCHIPLIST EXACTLY:
        ///   * Canvas = <c>ChipCache.Width * 4 * 2</c> wide x
        ///     <c>ChipCache.Height</c> tall, where <c>ChipCache</c> is the 8px
        ///     wide tile sheet (one tile per 8px row). So width = 8*4*2 = 64,
        ///     height = (tile count) * 8.
        ///   * For each tile row y, 8 columns at x = 8*0 .. 8*7:
        ///       col 0: original  (bank 0)   col 4: original  (bank 1)
        ///       col 1: H-flip    (bank 0)   col 5: H-flip    (bank 1)
        ///       col 2: V-flip    (bank 0)   col 6: V-flip    (bank 1)
        ///       col 3: HV-flip   (bank 0)   col 7: HV-flip   (bank 1)
        ///   * index 0 renders OPAQUE -- WF MakeCHIPLIST blits with
        ///     <c>transparent_index = 0xFF</c> (never matches a 0..15 index),
        ///     same as the battle-screen preview (#802 Blocker 4).
        ///
        /// Returns <c>null</c> (no crash) if <see cref="TryLoadChipsetAndPalette"/>
        /// fails (missing/corrupt required source) or no ImageService is set.
        /// Deterministic; no <c>Program.*</c> / System.Drawing dependency.
        /// </summary>
        public static IImage RenderChipsetPreview(ROM rom)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;

            if (!TryLoadChipsetAndPalette(rom, out byte[] tileData, out byte[] gbaPalette))
                return null;

            // ChipCache (WF) is an 8px-wide tile sheet: one 8x8 tile per 8px
            // row. tile count = tileData.Length / 32 (4bpp = 32 bytes/tile).
            const int bytesPerTile = 32;
            int tileCount = tileData.Length / bytesPerTile;
            if (tileCount <= 0) return null;

            // ChipCache.Width = 8, ChipCache.Height = tileCount * 8.
            // Chip-list canvas = ChipCache.Width * 4 * 2 (= 64) x ChipCache.Height.
            int width = 8 * 4 * 2;        // 8 columns of 8px
            int height = tileCount * 8;

            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA

            // Column layout: {orig, Hflip, Vflip, HVflip} for bank 0, then the
            // same 4 for bank 1 -- exactly WF MakeCHIPLIST's blit order.
            // (hFlip, vFlip, palBank)
            (bool h, bool v, int bank)[] columns =
            {
                (false, false, 0), // col 0
                (true,  false, 0), // col 1
                (false, true,  0), // col 2
                (true,  true,  0), // col 3
                (false, false, 1), // col 4
                (true,  false, 1), // col 5
                (false, true,  1), // col 6
                (true,  true,  1), // col 7
            };

            for (int tile = 0; tile < tileCount; tile++)
            {
                int tileY = tile * 8;
                for (int col = 0; col < columns.Length; col++)
                {
                    var (h, v, bank) = columns[col];
                    ImageUtilCore.DecodeTileToPixels(
                        tileData, tile, gbaPalette, bank,
                        pixels, width, col * 8, tileY,
                        h, v, is4bpp: true, opaqueIndex0: true);
                }
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Write the 32 x 20 TSA map back to the 5 TSA regions in
        /// <paramref name="rom"/>. Writes go through <c>rom.write_u16</c>
        /// which honors the ambient <see cref="ROM.BeginUndoScope"/>
        /// so the caller's <c>UndoService.Begin/Commit/Rollback</c>
        /// envelope captures every byte change.
        /// </summary>
        /// <returns><c>true</c> on success; <c>false</c> if inputs are invalid.</returns>
        public static bool WriteBattleScreen(ROM rom, ushort[] map)
        {
            if (rom == null || rom.RomInfo == null) return false;
            if (map == null || map.Length != MAP_SIZE) return false;

            // Pre-validate ALL 5 region addresses + their byte spans BEFORE
            // any write so a corrupt pointer cannot crash the editor
            // mid-undo-scope (Copilot bot PR #594 review).
            uint tsa1 = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            uint tsa2 = rom.p32(rom.RomInfo.battle_screen_TSA2_pointer);
            uint tsa3 = rom.p32(rom.RomInfo.battle_screen_TSA3_pointer);
            uint tsa4 = rom.p32(rom.RomInfo.battle_screen_TSA4_pointer);
            uint tsa5 = rom.p32(rom.RomInfo.battle_screen_TSA5_pointer);
            if (!IsRegionSafe(rom, tsa1, TSA1_BYTES)) return false;
            if (!IsRegionSafe(rom, tsa2, TSA2_BYTES)) return false;
            if (!IsRegionSafe(rom, tsa3, TSA3_BYTES)) return false;
            if (!IsRegionSafe(rom, tsa4, TSA4_BYTES)) return false;
            if (!IsRegionSafe(rom, tsa5, TSA5_BYTES)) return false;

            uint addr;

            addr = tsa1;
            for (int y = 0; y <= 5; y++)
            {
                for (int x = 1; x <= 15; x++)
                {
                    rom.write_u16(addr, map[y * MAP_X + x]);
                    addr += 2;
                }
            }

            addr = tsa2;
            for (int y = 0; y <= 5; y++)
            {
                for (int x = 16; x <= 30; x++)
                {
                    rom.write_u16(addr, map[y * MAP_X + x]);
                    addr += 2;
                }
            }

            addr = tsa3;
            for (int y = 13; y <= 19; y++)
            {
                for (int x = 1; x <= 15; x++)
                {
                    rom.write_u16(addr, map[y * MAP_X + x]);
                    addr += 2;
                }
            }

            addr = tsa4;
            for (int y = 13; y <= 19; y++)
            {
                for (int x = 16; x <= 31; x++)
                {
                    rom.write_u16(addr, map[y * MAP_X + x]);
                    addr += 2;
                }
            }

            addr = tsa5;
            for (int y = 0; y <= 19; y++)
            {
                for (int x = 31; x <= 32; x++)
                {
                    int xx = x == 32 ? 0 : x;
                    rom.write_u16(addr, map[y * MAP_X + xx]);
                    addr += 2;
                }
            }

            return true;
        }

        /// <summary>
        /// Read a 16-color uncompressed palette block from the
        /// battle-screen palette pointer. Delegates to
        /// <see cref="PaletteCore.ReadPalette"/> with the
        /// <paramref name="paletteIndex"/> argument controlling which
        /// 0x20-byte slot is read (Plan v2 Finding #3: reuse PaletteCore
        /// rather than duplicate the BGR15 packing math).
        /// </summary>
        public static (byte r, byte g, byte b)[] ReadPaletteBlock(ROM rom, int paletteIndex)
        {
            if (rom == null || rom.RomInfo == null) return new (byte, byte, byte)[16];
            uint paletteAddr = rom.p32(rom.RomInfo.battle_screen_palette_pointer);
            return PaletteCore.ReadPalette(rom.Data, paletteAddr, paletteIndex);
        }

        /// <summary>
        /// Write a 16-color uncompressed palette block to the battle-screen
        /// palette pointer at <paramref name="paletteIndex"/> * 0x20 offset.
        /// Delegates to <see cref="PaletteCore.WritePalette"/> which honors
        /// the ambient undo scope. Returns <c>true</c> on success.
        /// </summary>
        public static bool WritePaletteBlock(ROM rom, int paletteIndex, (byte r, byte g, byte b)[] colors)
        {
            if (rom == null || rom.RomInfo == null) return false;
            uint paletteAddr = rom.p32(rom.RomInfo.battle_screen_palette_pointer);
            return PaletteCore.WritePalette(rom, paletteAddr, paletteIndex, colors);
        }

        /// <summary>
        /// Public wrapper around <see cref="TryLoadRawPalette"/> for callers
        /// outside the Core assembly (e.g. the Avalonia view's per-image import
        /// path, #872). Reads the RAW 16-bank battle-screen palette (512 bytes,
        /// NOT LZ77) directly at <c>battle_screen_palette_pointer</c>.
        /// Returns <c>false</c> (null output) if the pointer or its 512-byte
        /// span is out of bounds.
        /// </summary>
        public static bool TryLoadRawPalettePublic(ROM rom, out byte[] gbaPalette)
            => TryLoadRawPalette(rom, out gbaPalette);

        /// <summary>
        /// Read the image pointer stored at <paramref name="pointerSlot"/>
        /// (e.g. <c>RomInfo.battle_screen_image1_pointer</c>). Returns the
        /// resolved ROM offset (0x... without the 0x08 prefix). Returns 0
        /// on null ROM.
        /// </summary>
        public static uint ReadImagePointer(ROM rom, uint pointerSlot)
        {
            if (rom == null) return 0;
            return rom.p32(pointerSlot);
        }

        /// <summary>
        /// Write the image pointer at <paramref name="pointerSlot"/> with
        /// <paramref name="newAddr"/>. The GBA pointer prefix (0x08...) is
        /// added automatically via <c>rom.write_p32</c>. Honors the ambient
        /// undo scope. Returns <c>true</c> on success, <c>false</c> if
        /// <paramref name="pointerSlot"/> or its 4-byte span are not safe
        /// ROM offsets (Copilot bot PR #594 review round 2).
        /// </summary>
        public static bool WriteImagePointer(ROM rom, uint pointerSlot, uint newAddr)
        {
            if (rom == null) return false;
            // pointerSlot+3 is the last byte touched by write_p32. Validate
            // the 4-byte span before writing so we don't throw mid-undo-scope.
            if (!IsRegionSafe(rom, pointerSlot, 4)) return false;
            rom.write_p32(pointerSlot, newAddr);
            return true;
        }

        /// <summary>
        /// Import a single battle-screen image strip (imageIndex 0..4) by
        /// writing new LZ77-compressed 4bpp tile data to ROM free space and
        /// repointing the corresponding image pointer slot.
        ///
        /// This mirrors the WF <c>RevChipImage</c> per-iteration write path:
        ///   1. The caller provides indexed pixel data already mapped to the
        ///      shared battle-screen palette (palette is NOT written by this
        ///      method -- it is shared across all 5 strips).
        ///   2. The pixels are encoded to raw 4bpp tiles via
        ///      <see cref="ImageImportCore.EncodeDirectTiles4bpp"/> (no TSA
        ///      dedup -- each strip is a plain tile sheet laid out at the
        ///      per-image width, exactly how
        ///      <c>ByteToImage16Tile</c>/<c>DecodeTileToPixels</c> reads them).
        ///   3. The tile bytes are LZ77-compressed and written to free space.
        ///   4. The image pointer slot is updated to the new address.
        ///
        /// All writes run under the caller's ambient undo scope
        /// (<see cref="ROM.BeginUndoScope"/>). Caller MUST wrap in
        /// <c>UndoService.Begin/Commit/Rollback</c>. Returns <c>true</c> on
        /// success; <c>false</c> on any validation or write failure (no partial
        /// ROM state on false -- the undo scope reverts all writes so far).
        ///
        /// <paramref name="imageIndex"/> must be in [0..4]. Out-of-range or
        /// null ROM/pixels returns false immediately without touching ROM.
        /// </summary>
        public static bool WritePerImageStrip(ROM rom, int imageIndex, byte[] indexedPixels,
            int widthPx, int heightPx)
        {
            if (rom == null || rom.RomInfo == null) return false;
            if (imageIndex < 0 || imageIndex > 4) return false;
            if (indexedPixels == null) return false;
            if (widthPx <= 0 || heightPx <= 0) return false;
            if (widthPx % 8 != 0 || heightPx % 8 != 0) return false;
            if (indexedPixels.Length != widthPx * heightPx) return false;

            uint[] slots = ImagePointerSlots(rom);
            uint pointerSlot = slots[imageIndex];

            // Validate the pointer slot BEFORE encoding or allocating any data
            // so a truncated/invalid ROM leaves the ROM completely untouched and
            // the caller's undo/rollback has nothing to revert (#874 review fix).
            // Mirrors WriteImagePointer's identical guard (Copilot PR #594 round 2).
            if (!IsRegionSafe(rom, pointerSlot, 4)) return false;

            // Encode indexed pixels to raw 4bpp tile bytes (no TSA dedup --
            // strips are plain tile sheets, matching DecodeTileToPixels layout).
            byte[] tileBytes = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, widthPx, heightPx);
            if (tileBytes == null || tileBytes.Length == 0) return false;

            // LZ77-compress and write to ROM free space + update pointer.
            // ImageImportCore.WriteCompressedToROM uses the ambient undo scope
            // and returns U.NOT_FOUND on failure.
            uint newAddr = ImageImportCore.WriteCompressedToROM(rom, tileBytes, pointerSlot);
            if (newAddr == U.NOT_FOUND) return false;

            return true;
        }

        // -----------------------------------------------------------------
        // Bulk image Import (#988) -- whole 256x160 battle-screen round-trip.
        //
        // Ports WinForms ImageBattleScreenForm.ImportButton_Click + RevChipImage
        // and ImageUtil.ImageToByteKeepTSA. The bulk import takes ONE 256x160
        // indexed image, keeps the existing TSA layout verbatim, and rewrites the
        // tilesheet (split back into the 5 image strips by each strip's ORIGINAL
        // uncompressed length) plus the palette.
        //
        // PALETTE POLICY (#989 Copilot fix -- SAFE single-bank path):
        //   WF's multi-bank import (ImageToPalette(bitmap, 4) + ImageToByte16Tile
        //   keeping only the low nibble) relies on the SOURCE being a pre-banked
        //   indexed image whose palette indices already encode bank*16 + color,
        //   with each 8x8 cell using a single bank matching its TSA `pal` bits.
        //   A flat re-quantized 0..63 index stream does NOT satisfy that (the
        //   bank a cell renders with comes from the TSA, not the quantizer), so
        //   pixels at source indices 16..63 would silently render with the WRONG
        //   bank's colors. Rather than be silently wrong, the bulk import is
        //   restricted to a SINGLE palette bank (<=16 colors): >16-color images
        //   are REJECTED with no mutation. The imported 16-color palette is
        //   written into BANK 0 of the existing 16-bank ROM palette (banks 1..15
        //   preserved verbatim, so TSA cells referencing pal>0 keep their colors).
        //   Full multi-bank correctness is a documented follow-up (needs a
        //   bank-aware quantizer that respects per-cell TSA bank assignment).
        // -----------------------------------------------------------------

        /// <summary>Pixel width of the full battle screen (32 cells * 8).</summary>
        public const int BULK_WIDTH = MAP_X * 8;   // 256

        /// <summary>Pixel height of the full battle screen (20 cells * 8).</summary>
        public const int BULK_HEIGHT = MAP_Y * 8;  // 160

        /// <summary>
        /// Max colors the SAFE single-bank bulk import accepts (#989). >16-color
        /// images are rejected (no mutation) rather than silently wrong-banked.
        /// </summary>
        public const int BULK_MAX_COLORS = 16;

        /// <summary>Bytes for one 16-color GBA palette bank (16 * 2).</summary>
        const int BULK_BANK_BYTES = BULK_MAX_COLORS * 2; // 32

        /// <summary>
        /// Battle-screen-specific TSA-keeping tile encoder (#988). Cross-platform
        /// port of WinForms <c>ImageUtil.ImageToByteKeepTSA</c>, but driven by the
        /// RAW battle-screen TSA map (<see cref="LoadBattleScreen"/> format) rather
        /// than the generic GBA-packed TSA.
        ///
        /// Each map cell <c>m</c> decodes (matching <see cref="RenderBattleScreenPreview"/>
        /// and WF <c>MakeBattleScreen</c>) as:
        ///   <c>tile = m &amp; 0xFF</c> (8-bit), <c>flip = (m &gt;&gt; 8) &amp; 0x0F</c>
        ///   (0 = none, 4 = H, 8 = V, any-other-nonzero = HV), <c>pal = m &gt;&gt; 12</c>
        ///   (palette bank -- preserved, never written into the tile index).
        ///
        /// For each cell, the NEW input tile at the cell's grid position is taken,
        /// the INVERSE flip is applied (each pixel-flip is self-inverse), and the
        /// result is copied over <paramref name="originalTiles"/> at
        /// <c>tile * 32</c>. The TSA itself is unchanged (the caller writes the
        /// existing TSA back verbatim). Cells whose <c>tile*32</c> or source
        /// offset would overrun are skipped (matching WF's per-cell bounds guards).
        ///
        /// Pure and guarded: any size mismatch (null inputs, non-32-multiple
        /// <paramref name="originalTiles"/>, wrong map length) returns <c>null</c>
        /// without throwing.
        /// </summary>
        /// <param name="inputIndexedTiles">The NEW 256x160 image already encoded to
        ///   raw 4bpp tiles via <see cref="ImageImportCore.EncodeDirectTiles4bpp"/>
        ///   (one 32-byte 8x8 tile per grid cell, row-major). Length must be
        ///   <c>MAP_SIZE * 32</c>.</param>
        /// <param name="map">The RAW battle-screen TSA map (length <see cref="MAP_SIZE"/>).</param>
        /// <param name="originalTiles">The current concatenated tilesheet bytes
        ///   (image1..image5 LZ77-decompressed + concatenated). Cloned; never mutated.</param>
        /// <returns>A new tilesheet the same length as <paramref name="originalTiles"/>
        ///   with the touched tiles replaced, or <c>null</c> on any invalid input.</returns>
        public static byte[] EncodeTSAKeep(byte[] inputIndexedTiles, ushort[] map, byte[] originalTiles)
        {
            if (inputIndexedTiles == null || map == null || originalTiles == null) return null;
            if (map.Length != MAP_SIZE) return null;
            if (originalTiles.Length == 0 || originalTiles.Length % 32 != 0) return null;
            if (inputIndexedTiles.Length != MAP_SIZE * 32) return null;

            byte[] dest = (byte[])originalTiles.Clone();

            for (int tsaindex = 0; tsaindex < map.Length; tsaindex++)
            {
                ushort m = map[tsaindex];
                if (m == 0xFFFF) continue; // WF: skip blank cells

                int tileNumber = m & 0xFF;            // battle-screen: 8-bit tile id
                int flip = (m >> 8) & 0x0F;           // 0 / 4=H / 8=V / else=HV

                int destPos = tileNumber * 32;
                if (destPos + 32 > dest.Length) continue;        // WF per-cell guard
                int srcPos = tsaindex * 32;
                if (srcPos + 32 > inputIndexedTiles.Length) continue;

                // Extract the new tile for this grid cell.
                byte[] tile = new byte[32];
                Array.Copy(inputIndexedTiles, srcPos, tile, 0, 32);

                // Apply the INVERSE flip (each flip is self-inverse). The
                // pixel-flip helpers match DecodeTileToPixels' hFlip(srcX=7-px) /
                // vFlip(srcY=7-py) so encode + decode stay consistent.
                byte[] outTile;
                if (flip == 0) outTile = tile;
                else if (flip == 4) outTile = ImageImportCore.FlipTileH4bpp(tile);          // H
                else if (flip == 8) outTile = ImageImportCore.FlipTileV4bpp(tile);          // V
                else outTile = ImageImportCore.FlipTileV4bpp(ImageImportCore.FlipTileH4bpp(tile)); // HV

                Array.Copy(outTile, 0, dest, destPos, 32);
            }

            return dest;
        }

        /// <summary>
        /// Validate-all-before-mutate bulk image import (#988, CORRECTION 3;
        /// #989 SAFE single-bank palette policy). Cross-platform port of WinForms
        /// <c>ImageBattleScreenForm.ImportButton_Click</c> + <c>RevChipImage</c>.
        /// Imports one 256x160 indexed image (SINGLE palette bank, &lt;=16 colors):
        /// keeps the existing TSA layout verbatim and rewrites the tilesheet
        /// (split into the 5 image strips by each strip's ORIGINAL uncompressed
        /// length) and BANK 0 of the existing 16-bank ROM palette.
        ///
        /// PHASE 1 (validate -- NO mutation): re-load the current 5 image strips
        /// (their ORIGINAL uncompressed byte lengths give the chunk boundaries),
        /// the TSA map, the EXISTING 16-bank ROM palette, and validate the
        /// imported pixel dims (256x160) + indices (0..15, single bank) +
        /// palette (&lt;=16 colors); validate all 5 image pointer slots + the
        /// palette pointer slot are safe ROM offsets. Any failure returns a
        /// non-empty error string with ZERO ROM bytes touched.
        ///
        /// PHASE 2 (mutate): encode the input to tiles, run <see cref="EncodeTSAKeep"/>,
        /// split the result into the 5 strips by the captured original lengths,
        /// LZ77-write + repoint each strip, then write the EXISTING 16-bank palette
        /// with BANK 0 replaced by the imported 16 colors + repoint -- all through
        /// <see cref="ImageImportCore"/> which routes every write through the
        /// AMBIENT undo scope. The caller MUST wrap this call in
        /// <c>UndoService.Begin/Commit/Rollback</c> (an ambient
        /// <c>ROM.BeginUndoScope</c>) so a mid-write free-space failure rolls the
        /// whole batch back.
        ///
        /// PALETTE POLICY (#989): SINGLE bank. WF's multi-bank
        /// <c>ImageToPalette(bitmap, 4)</c> path needs a pre-banked indexed source
        /// (per-cell bank == TSA <c>pal</c> bits); a flat re-quantized index stream
        /// can't satisfy that, so &gt;16-color images are REJECTED (no mutation)
        /// rather than silently wrong-banked. Banks 1..15 of the ROM palette are
        /// preserved so TSA cells with <c>pal&gt;0</c> keep their colors.
        /// </summary>
        /// <param name="rom">Target ROM (writes route through its ambient undo scope).</param>
        /// <param name="indexedPixels">256x160 indexed pixels, 1 byte/pixel, values
        ///   0..15 (single palette bank). A value &gt;15 is rejected.</param>
        /// <param name="gbaPalette">The image's quantized palette: 1..16 colors *
        ///   2 bytes (1..32 bytes). &gt;32 bytes (&gt;16 colors / multi-bank) is
        ///   rejected. Written into BANK 0 of the existing ROM palette.</param>
        /// <returns>Empty string on success; a non-empty diagnostic string on any
        ///   validation/write failure (no partial commit -- caller rolls back).</returns>
        public static string ImportBattleScreenBulk(ROM rom, byte[] indexedPixels, byte[] gbaPalette)
        {
            // ---------- PHASE 1: validate everything, mutate nothing ----------
            if (rom == null || rom.RomInfo == null || rom.Data == null)
                return "ROM not loaded.";
            if (indexedPixels == null)
                return "No image pixels.";
            if (indexedPixels.Length != BULK_WIDTH * BULK_HEIGHT)
                return $"Image must be {BULK_WIDTH}x{BULK_HEIGHT} pixels.";
            if (gbaPalette == null || gbaPalette.Length == 0 || gbaPalette.Length % 2 != 0)
                return "Invalid palette data.";
            // SAFE single-bank policy (#989): <=16 colors (<=32 bytes). A
            // multi-bank source would silently render with the WRONG bank, so
            // reject it with no mutation. NOTE: DecreaseColorCore.Quantize returns
            // ColorCount*2 bytes (NOT padded to a full bank), so a 17..31-color
            // source yields >32 bytes here and is correctly rejected.
            if (gbaPalette.Length > BULK_BANK_BYTES)
                return $"Battle-screen bulk import supports a single palette bank (max {BULK_MAX_COLORS} colors). Reduce the image to {BULK_MAX_COLORS} colors first.";
            // Every pixel must be in bank 0 (index 0..15). A >15 index implies a
            // multi-bank source whose bank can't be honored here.
            for (int i = 0; i < indexedPixels.Length; i++)
            {
                if (indexedPixels[i] > (BULK_MAX_COLORS - 1))
                    return $"Battle-screen bulk import supports a single palette bank (max {BULK_MAX_COLORS} colors). Reduce the image to {BULK_MAX_COLORS} colors first.";
            }

            // The palette pointer slot must be a safe ROM offset BEFORE any write.
            uint palettePointerSlot = rom.RomInfo.battle_screen_palette_pointer;
            if (!IsRegionSafe(rom, palettePointerSlot, 4))
                return "Palette pointer slot is out of range.";

            // Read the EXISTING 16-bank (512-byte) ROM palette so we can preserve
            // banks 1..15 and only overwrite bank 0 with the imported colors.
            if (!TryLoadRawPalette(rom, out byte[] existingPalette))
                return "Could not read the existing battle-screen palette.";

            // All 5 image pointer slots must be safe ROM offsets BEFORE any write.
            uint[] imageSlots = ImagePointerSlots(rom);
            for (int i = 0; i < imageSlots.Length; i++)
            {
                if (!IsRegionSafe(rom, imageSlots[i], 4))
                    return $"Image{i + 1} pointer slot is out of range.";
            }

            // Re-load the current 5 strips -- their ORIGINAL uncompressed lengths
            // are the chunk boundaries (WF RevChipImage decompresses each strip to
            // learn its height). A missing/corrupt strip aborts before any write.
            int[] stripLengths = new int[imageSlots.Length];
            int totalOriginalLength = 0;
            byte[][] originalChunks = new byte[imageSlots.Length][];
            for (int i = 0; i < imageSlots.Length; i++)
            {
                if (!TryDecodeImageStrip(rom, imageSlots[i], out byte[] chunk) || chunk == null || chunk.Length == 0)
                    return $"Could not read battle-screen image strip {i + 1}.";
                if (chunk.Length % 32 != 0)
                    return $"Battle-screen image strip {i + 1} is not a whole number of tiles.";
                originalChunks[i] = chunk;
                stripLengths[i] = chunk.Length;
                totalOriginalLength += chunk.Length;
            }

            // The concatenated original tilesheet (same order as the renderer).
            byte[] originalTiles = new byte[totalOriginalLength];
            int wp = 0;
            for (int i = 0; i < originalChunks.Length; i++)
            {
                Array.Copy(originalChunks[i], 0, originalTiles, wp, originalChunks[i].Length);
                wp += originalChunks[i].Length;
            }

            // The current TSA map (kept verbatim -- only the tilesheet changes).
            ushort[] map = LoadBattleScreen(rom);
            if (map == null) return "Could not read the battle-screen TSA map.";

            // Encode the imported pixels to tiles, then apply the TSA-keeping copy.
            byte[] inputTiles = ImageImportCore.EncodeDirectTiles4bpp(indexedPixels, BULK_WIDTH, BULK_HEIGHT);
            if (inputTiles == null || inputTiles.Length == 0)
                return "Failed to encode the imported image to tiles.";

            byte[] newTiles = EncodeTSAKeep(inputTiles, map, originalTiles);
            if (newTiles == null || newTiles.Length != originalTiles.Length)
                return "TSA-keeping encode failed.";

            // ---------- PHASE 2: mutate (all writes via ambient undo scope) ----------
            // Split newTiles back into the 5 strips by the captured original
            // lengths and LZ77-write + repoint each one (WF RevChipImage).
            int readPos = 0;
            for (int i = 0; i < imageSlots.Length; i++)
            {
                int len = stripLengths[i];
                byte[] strip = new byte[len];
                Array.Copy(newTiles, readPos, strip, 0, len);
                readPos += len;

                uint newAddr = ImageImportCore.WriteCompressedToROM(rom, strip, imageSlots[i]);
                if (newAddr == U.NOT_FOUND)
                    return $"Failed to write battle-screen image strip {i + 1} (no free space).";
            }

            // Merge the imported colors into BANK 0 of the existing 16-bank
            // palette (banks 1..15 preserved verbatim), pad bank 0 to a full 16
            // colors, and write the whole 512-byte palette RAW + repoint. This
            // keeps TSA cells that reference pal>0 rendering with their original
            // colors (SAFE single-bank policy, #989).
            byte[] mergedPalette = (byte[])existingPalette.Clone();
            Array.Copy(gbaPalette, 0, mergedPalette, 0, gbaPalette.Length);
            // Zero-fill the remainder of bank 0 if the source had <16 colors
            // (WF ImageToPalette pads the missing entries with black).
            for (int i = gbaPalette.Length; i < BULK_BANK_BYTES; i++)
                mergedPalette[i] = 0;

            uint palAddr = ImageImportCore.WriteRawToROM(rom, mergedPalette, palettePointerSlot);
            if (palAddr == U.NOT_FOUND)
                return "Failed to write battle-screen palette (no free space).";

            return string.Empty;
        }
    }
}
