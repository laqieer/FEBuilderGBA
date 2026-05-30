// SPDX-License-Identifier: GPL-3.0-or-later
// Core-side import + preview-decode pipeline for 255/224-color cutscene
// backgrounds under the FE8 "BG256Color" patch (#799).
//
// This is the 8bpp sibling of the 16-color BG path
// (`ImageImportCore.Import3PointerHeaderTSA`). A BG255/BG224 entry stores:
//
//   +0  u32 image   pointer (LZ77-compressed 8bpp tiles)
//   +4  u32 flag     = 0 (255-color) or 1 (224-color) — RAW u32, NOT a pointer
//   +8  u32 palette  pointer (RAW 512-byte palette = 256 colors)
//
// It mirrors WinForms `ImageBGForm.ImportButton255` (ImageBGForm.cs:164-205)
// for import and `ImageBGForm.DrawBG` (ImageBGForm.cs:101-133) for preview:
//   * 255-color: `ByteToImage256Tile` (8bpp tile decode), P4 = 0.
//   * 224-color: `ByteToImage224BGTile` (8bpp decode after an index remap),
//     P4 = 1.
//
// The 224 forward/inverse index remaps are byte-exact ports of
// `ImageUtil.Convert255ColorTo224Color` (ImageUtil.cs:4670) and the inverse
// pre-pass inside `ImageUtil.ByteToImage224BGTile` (ImageUtil.cs:666-687).
using System;

namespace FEBuilderGBA
{
    public static class ImageBG256ColorCore
    {
        /// <summary>
        /// Number of bytes a full BG255/BG224 palette occupies in ROM:
        /// 256 colors x 2 bytes = 512 bytes (16 sub-palettes x 16 colors).
        /// </summary>
        public const int PaletteByteSize = 256 * 2;

        /// <summary>BG width in pixels (32 tiles).</summary>
        public const int Width = 32 * 8;

        /// <summary>BG height in pixels (20 tiles).</summary>
        public const int Height = 20 * 8;

        /// <summary>
        /// Matches WinForms `ImageUtil.BG224StartPaletteIndex` (= 2). The
        /// 224-color mode reserves the bottom <c>2 * 16 = 32</c> palette
        /// indices for the system, so user colors live in indices
        /// <c>32..255</c> of the 255-color image but are remapped down so
        /// the ROM never stores an index in the reserved range.
        /// </summary>
        const int BG224StartPaletteIndex = 2;

        /// <summary>Result of an <see cref="Import255ColorBG"/> call.</summary>
        public readonly struct ImportResult
        {
            public bool Success { get; init; }
            public string Error { get; init; }

            public static ImportResult Ok() => new ImportResult { Success = true, Error = string.Empty };
            public static ImportResult Fail(string error) => new ImportResult { Success = false, Error = error };
        }

        /// <summary>
        /// Byte-exact port of WinForms <c>ImageUtil.Convert255ColorTo224Color</c>
        /// (ImageUtil.cs:4670). Remaps the reserved top palette indices so a
        /// 255-color indexed image can be stored as a 224-color background.
        /// Operates in-place on the image indices ONLY — the palette is NOT
        /// touched (mirrors WF).
        ///
        /// Per index <c>a</c> (with <see cref="BG224StartPaletteIndex"/> = 2):
        /// <list type="bullet">
        ///   <item><c>a &lt; 32</c>  → keep (reserved low colors).</item>
        ///   <item><c>a &gt;= 224</c> → <c>0</c> (the top 32 colors are not
        ///     representable in 224-color mode).</item>
        ///   <item>otherwise        → <c>a + 32</c> (so <c>32..223</c> → <c>64..255</c>).</item>
        /// </list>
        /// </summary>
        public static void Convert255ColorTo224Color(byte[] image)
        {
            if (image == null) return;
            for (int i = 0; i < image.Length; i += 1)
            {
                byte a = image[i];
                if (a < BG224StartPaletteIndex * 16)
                {
                    continue;
                }

                if (a >= (16 - 2) * 16)
                {
                    a = 0;
                }
                else
                {
                    a += 16 * 2;
                }
                image[i] = a;
            }
        }

        /// <summary>
        /// Byte-exact inverse of the 224-color remap, mirroring the pre-pass
        /// inside WinForms <c>ImageUtil.ByteToImage224BGTile</c>
        /// (ImageUtil.cs:666-687). Applied to a COPY of the decompressed
        /// image indices before an 8bpp tile decode so the BG224 preview is
        /// correct.
        ///
        /// Per index <c>a</c> (with <see cref="BG224StartPaletteIndex"/> = 2):
        /// <list type="bullet">
        ///   <item><c>a &gt;= 32</c> → <c>a - 32</c> (so <c>64..255</c> → <c>32..223</c>).</item>
        ///   <item>otherwise       → keep.</item>
        /// </list>
        /// Note this is the literal WF inverse pass; it is not a strict
        /// mathematical inverse of the forward map on the unused
        /// <c>32..63</c>/<c>224..255</c> domains (the forward map never emits
        /// those), but it reproduces WF preview output exactly.
        /// </summary>
        public static void Convert224ColorTo255Color(byte[] image)
        {
            if (image == null) return;
            for (int i = 0; i < image.Length; i++)
            {
                byte a = image[i];
                if (a >= BG224StartPaletteIndex * 16)
                {
                    a -= 2 * 16;
                }
                image[i] = a;
            }
        }

        /// <summary>
        /// Pad (or trim) a GBA palette to exactly <see cref="PaletteByteSize"/>
        /// (512) bytes. Short palettes are zero-extended; over-long palettes
        /// are truncated. Mirrors WF, which always writes a full 512-byte
        /// (256-color) palette for a BG255/BG224 entry.
        /// </summary>
        public static byte[] PadPaletteTo512(byte[] gbaPalette)
        {
            byte[] pal = new byte[PaletteByteSize];
            if (gbaPalette != null)
            {
                int n = Math.Min(gbaPalette.Length, PaletteByteSize);
                Array.Copy(gbaPalette, 0, pal, 0, n);
            }
            return pal;
        }

        /// <summary>
        /// Write a 255/224-color cutscene background.
        ///
        /// <paramref name="indexed8bpp"/> is one byte per pixel (0..255 into
        /// <paramref name="gbaPalette512"/>), laid out as a linear bitmap
        /// (row-major, <paramref name="w"/> x <paramref name="h"/>).
        ///
        /// Steps (mirroring WF <c>ImageBGForm.ImportButton255</c>):
        /// <list type="number">
        ///   <item>If <paramref name="is224"/>: reject any pre-remap index
        ///     &gt;= 224 (would silently black out under the 224 remap), then
        ///     apply <see cref="Convert255ColorTo224Color"/> in place.</item>
        ///   <item>Wrap the indices as an 8bpp <see cref="IImage"/> →
        ///     <c>svc.Encode8bppTiles</c> → 8bpp tile bytes.</item>
        ///   <item>P0: LZ77-compress the tiles and write via
        ///     <see cref="ImageImportCore.WriteCompressedToROM"/>.</item>
        ///   <item>P4: write the RAW u32 mode flag (0 or 1) at
        ///     <paramref name="p4Addr"/> — never a pointer write.</item>
        ///   <item>P8: pad the palette to exactly 512 bytes and write RAW via
        ///     <see cref="ImageImportCore.WriteRawToROM"/>.</item>
        /// </list>
        ///
        /// All three writes are captured for undo: this method opens an
        /// ambient <c>ROM.BeginUndoScope(undo)</c> for its entire body, so
        /// the no-undo writes inside <c>WriteCompressedToROM</c> /
        /// <c>WriteRawToROM</c> record into <paramref name="undo"/> even when
        /// called outside the Avalonia call path. If an ambient scope for the
        /// same UndoData is already active (the Avalonia path opens one via
        /// <c>UndoService</c>), it is reused rather than nested.
        /// </summary>
        public static ImportResult Import255ColorBG(
            ROM rom, byte[] indexed8bpp, byte[] gbaPalette512, int w, int h,
            uint p0Addr, uint p4Addr, uint p8Addr, bool is224,
            IImageService svc, Undo.UndoData undo)
        {
            if (rom == null) return ImportResult.Fail("ROM is null");
            if (indexed8bpp == null) return ImportResult.Fail("Indexed pixel data is null");
            if (svc == null) return ImportResult.Fail("Image service is not available");
            // BG255/BG224 entries are a fixed 256x160 (32x20 tiles) — Decode255ColorBG
            // always decodes with those constants, so a different size could never
            // round-trip. Reject up front (Copilot review on PR #801).
            if (w != Width || h != Height)
                return ImportResult.Fail($"BG255/BG224 must be {Width}x{Height}");
            if (indexed8bpp.Length != w * h)
                return ImportResult.Fail($"Indexed pixel count ({indexed8bpp.Length}) does not match width*height ({w * h})");

            // Undo capture must be nest-safe (Copilot review on PR #801):
            // ROM.BeginUndoScope is NOT stacked — its IDisposable.Dispose() clears
            // the ambient scope to null rather than restoring the previous one. So
            // we save the caller's prior ambient scope, set our own only when it
            // differs, and on exit RESTORE the prior scope (re-opening it) instead
            // of nulling it. The invariant: after this method returns, any
            // pre-existing ambient scope from the caller is intact, AND the
            // P0/P4/P8 writes were captured by `undo`.
            //   - prior == undo  → reuse the existing scope (do not re-open).
            //   - prior == null  → open our scope; restore to null (dispose) on exit.
            //   - prior != undo  → open our scope; restore the prior scope on exit.
            Undo.UndoData priorAmbient = ROM.GetAmbientUndoData();
            bool ownsScope = undo != null && priorAmbient != undo;
            IDisposable scope = ownsScope ? ROM.BeginUndoScope(undo) : null;
            try
            {
                // Operate on a copy so the caller's buffer is not mutated by
                // the 224 remap.
                byte[] indexed = (byte[])indexed8bpp.Clone();

                if (is224)
                {
                    // The 224 forward map sends indices >= 224 to 0 — silently
                    // dropping up to 32 colors. Refuse rather than black out
                    // (Copilot CLI v2 blocker 1 / acceptance gate).
                    byte max = 0;
                    for (int i = 0; i < indexed.Length; i++)
                        if (indexed[i] > max) max = indexed[i];
                    if (max >= (16 - 2) * 16)
                        return ImportResult.Fail("BG224 needs <=224 colors (an index >= 224 was found)");

                    Convert255ColorTo224Color(indexed);
                }

                // Wrap as an 8bpp indexed image and encode to GBA tiles.
                byte[] pal512 = PadPaletteTo512(gbaPalette512);
                IImage image = svc.CreateIndexedImage(w, h, pal512, PaletteByteSize / 2);
                if (image == null) return ImportResult.Fail("Failed to create indexed image");
                byte[] tiles;
                try
                {
                    image.SetPixelData(indexed);
                    tiles = svc.Encode8bppTiles(image);
                }
                finally { image.Dispose(); }
                if (tiles == null || tiles.Length == 0)
                    return ImportResult.Fail("Failed to encode 8bpp tiles");

                // P0: LZ77-compressed tiles.
                uint imgAddr = ImageImportCore.WriteCompressedToROM(rom, tiles, p0Addr);
                if (imgAddr == U.NOT_FOUND)
                    return ImportResult.Fail("Failed to write compressed image data (no free space)");

                // P4: RAW u32 mode flag (0 = 255-color, 1 = 224-color). This
                // slot is NOT a pointer under the BG256Color patch.
                rom.write_u32(p4Addr, is224 ? 1u : 0u);

                // P8: RAW 512-byte palette.
                uint palAddr = ImageImportCore.WriteRawToROM(rom, pal512, p8Addr);
                if (palAddr == U.NOT_FOUND)
                    return ImportResult.Fail("Failed to write palette (no free space)");

                return ImportResult.Ok();
            }
            finally
            {
                if (ownsScope)
                {
                    // Dispose our scope (sets ambient to null), then re-open the
                    // caller's prior scope if there was one so their undo tracking
                    // survives. ROM.BeginUndoScope is not stacked, so a plain
                    // Dispose() would otherwise wipe a different outer scope.
                    scope.Dispose();
                    if (priorAmbient != null)
                    {
                        ROM.BeginUndoScope(priorAmbient);
                    }
                }
            }
        }

        /// <summary>
        /// Decode a 255/224-color background to an <see cref="IImage"/> for
        /// preview. Mirrors WF <c>ImageBGForm.DrawBG</c>:
        /// <list type="bullet">
        ///   <item>LZ77-decompress the P0 image.</item>
        ///   <item>Read the RAW 512-byte palette at P8.</item>
        ///   <item>255-color (<paramref name="is224"/> = false): decode the
        ///     8bpp tiles directly (<c>ByteToImage256Tile</c>).</item>
        ///   <item>224-color (<paramref name="is224"/> = true): apply
        ///     <see cref="Convert224ColorTo255Color"/> to a copy of the indices,
        ///     zero the top 32 palette colors (WF copies only 224 colors into
        ///     the decode palette), then decode
        ///     (<c>ByteToImage224BGTile</c>).</item>
        /// </list>
        /// Returns <c>null</c> on any failure (caller renders a blank).
        /// </summary>
        public static IImage Decode255ColorBG(ROM rom, uint p0Addr, uint p8Addr, bool is224, IImageService svc)
        {
            if (rom == null || svc == null) return null;
            if (!U.isPointer(p0Addr) || !U.isPointer(p8Addr)) return null;

            uint imgOff = U.toOffset(p0Addr);
            uint palOff = U.toOffset(p8Addr);
            // Bind the safety check to the PASSED rom (2-arg overload). The
            // single-arg U.isSafetyOffset reads CoreState.ROM, which would make
            // this depend on global state and could NRE when CoreState.ROM is null
            // (Copilot review on PR #801).
            if (!U.isSafetyOffset(imgOff, rom) || !U.isSafetyOffset(palOff, rom)) return null;

            byte[] tiles = LZ77.decompress(rom.Data, imgOff);
            if (tiles == null || tiles.Length == 0) return null;

            // Read the raw 512-byte palette (256 colors). Pad/clip so the
            // decode always sees a full 512-byte palette.
            byte[] pal512 = new byte[PaletteByteSize];
            int palCopy = (int)Math.Min((uint)PaletteByteSize, (uint)rom.Data.Length - palOff);
            if (palCopy > 0)
                Array.Copy(rom.Data, palOff, pal512, 0, palCopy);

            if (is224)
            {
                // WF ByteToImage224BGTile remaps the indices down, then decodes
                // against a palette where only the first 224 colors are kept
                // (top 32 zeroed).
                Convert224ColorTo255Color(tiles);
                for (int i = 16 * 14 * 2; i < PaletteByteSize; i++)
                    pal512[i] = 0;
            }

            return svc.Decode8bppTiles(tiles, 0, Width, Height, pal512);
        }
    }
}
