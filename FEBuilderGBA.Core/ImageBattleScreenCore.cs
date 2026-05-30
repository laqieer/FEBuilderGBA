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
        ///     wide, concatenated vertically in order into one tileset.
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

            // --- Blocker 1: RAW 16-bank palette (512 bytes, NO LZ77) ---
            uint palAddr = rom.p32(rom.RomInfo.battle_screen_palette_pointer);
            if (!U.isSafetyOffset(palAddr, rom)) return null;
            ulong palLast = (ulong)palAddr + (ulong)BATTLE_SCREEN_PALETTE_BYTES - 1UL;
            if (palLast >= (ulong)rom.Data.Length) return null;
            byte[] gbaPalette = new byte[BATTLE_SCREEN_PALETTE_BYTES];
            Array.Copy(rom.Data, palAddr, gbaPalette, 0, BATTLE_SCREEN_PALETTE_BYTES);

            // --- Blocker 2: LZ77 image1..image5 concatenated vertically ---
            uint[] imagePointerSlots = new uint[]
            {
                rom.RomInfo.battle_screen_image1_pointer,
                rom.RomInfo.battle_screen_image2_pointer,
                rom.RomInfo.battle_screen_image3_pointer,
                rom.RomInfo.battle_screen_image4_pointer,
                rom.RomInfo.battle_screen_image5_pointer,
            };

            int totalLength = 0;
            byte[][] chunks = new byte[imagePointerSlots.Length][];
            for (int i = 0; i < imagePointerSlots.Length; i++)
            {
                uint imageAddr = rom.p32(imagePointerSlots[i]);
                // Each image1..5 is REQUIRED (WF blits all five): a bad pointer
                // or LZ77 failure must fail the whole render, not skip a chunk.
                if (!U.isSafetyOffset(imageAddr, rom)) return null;
                byte[] chunk = LZ77.decompress(rom.Data, imageAddr);
                if (chunk == null || chunk.Length == 0) return null;
                chunks[i] = chunk;
                totalLength += chunk.Length;
            }
            if (totalLength == 0) return null;

            byte[] tileData = new byte[totalLength];
            int writePos = 0;
            for (int i = 0; i < chunks.Length; i++)
            {
                Array.Copy(chunks[i], 0, tileData, writePos, chunks[i].Length);
                writePos += chunks[i].Length;
            }

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
    }
}
