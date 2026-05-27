// SPDX-License-Identifier: GPL-3.0-or-later
// Shared portrait-import helper used by both ImagePortraitView and the
// Portrait Import Wizard (#657).
//
// Ports the per-slot import pipeline that previously lived as private
// methods on ImagePortraitView into a stand-alone, testable helper so the
// wizard can reuse the same UndoService-scoped write path. (Copilot CLI
// plan v1 review blocking point 2.)
//
// Plan v2 contract (Copilot CLI accepted 2026-05-26):
//   - IsFe7Or8EntryLayout(rom)             — gate the 128x112 sheet path
//     so it cannot corrupt FE6 16-byte portrait entries (review point 1).
//   - ImportSimple / ImportSheet           — caller passes an open
//     UndoService; helper enters Begin, writes, calls Commit on success
//     or Rollback on any failure. Returns ImportOutcome.
//   - RecordSourceFile(portraitIndex,      — replicate the WF
//     sourcePath)                            ImagePortraitForm "Portrait_"
//                                           ResourceCache key so the
//                                           Open/Select Source buttons
//                                           on ImagePortraitView light up.
//                                           (The actual signature takes
//                                           only the portrait index + the
//                                           source path; the cache is
//                                           resolved internally from
//                                           CoreState.ResourceCache, and
//                                           the entry address is derived
//                                           via U.ToHexString(portraitIndex).)
using System;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Outcome of a portrait-slot import.</summary>
    public record ImportOutcome(bool Success, string Error)
    {
        public static ImportOutcome Ok() => new(true, string.Empty);
        public static ImportOutcome Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Shared portrait-slot import helper. Single source of truth for the
    /// PNG -> ROM write pipeline. Both <see cref="Views.ImagePortraitView"/>
    /// and <see cref="Views.ImagePortraitImporterView"/> call into this
    /// helper.
    /// </summary>
    public static class PortraitImportHelper
    {
        /// <summary>
        /// FE7 / FE8 portrait entries are 28 bytes (D0 sheet, D4 mini-face,
        /// D8 palette, D12 mouth frames, D16 class card, B20-B27 coords).
        /// FE6 entries are 16 bytes and re-use bytes 12-15 for mouth coords.
        /// The 128x112 composite-sheet path writes D4 + D12 pointers and
        /// would corrupt FE6 layouts — gate it here.
        /// </summary>
        public static bool IsFe7Or8EntryLayout(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            int v = rom.RomInfo.version;
            return v == 7 || v == 8;
        }

        /// <summary>
        /// Import a single PNG into the portrait sheet (D0) + palette (D8)
        /// slots of the entry at <paramref name="entryAddr"/>. Safe on all
        /// ROM versions (FE6 included) because only D0 + D8 are touched —
        /// both are present in both 16-byte FE6 and 28-byte FE7/8 layouts.
        ///
        /// Mirrors the original <c>ImagePortraitView.ImportImageFromFile</c>
        /// simple-path branch.
        /// </summary>
        public static ImportOutcome ImportSimple(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            string undoLabel = "Import Portrait Image")
        {
            if (rom == null) return ImportOutcome.Fail("ROM not loaded");
            if (loadResult == null || !loadResult.Success)
                return ImportOutcome.Fail(loadResult?.Error ?? "No image to import");
            if (entryAddr == 0) return ImportOutcome.Fail("No portrait entry selected");
            if (undoService == null) return ImportOutcome.Fail("Undo service not initialized");

            undoService.Begin(undoLabel);
            try
            {
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(
                    loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null)
                {
                    undoService.Rollback();
                    return ImportOutcome.Fail("Failed to encode tiles");
                }

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, entryAddr + 0);
                if (tileAddr == U.NOT_FOUND)
                {
                    undoService.Rollback();
                    return ImportOutcome.Fail("No free space for tile data");
                }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, entryAddr + 8);
                if (palAddr == U.NOT_FOUND)
                {
                    undoService.Rollback();
                    return ImportOutcome.Fail("No free space for palette");
                }

                undoService.Commit();
                return ImportOutcome.Ok();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                return ImportOutcome.Fail($"Import failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Import a 128x112 composite portrait sheet. Splits into sprite
        /// sheet (D0), mini/map face (D4), palette (D8), and mouth frames
        /// (D12). FE7 / FE8 only — see <see cref="IsFe7Or8EntryLayout"/>.
        ///
        /// Mirrors the original <c>ImagePortraitView.ImportPortraitSheet</c>.
        /// </summary>
        public static ImportOutcome ImportSheet(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            string undoLabel = "Import Portrait Sheet (128x112)")
        {
            if (rom == null) return ImportOutcome.Fail("ROM not loaded");
            if (loadResult == null || !loadResult.Success)
                return ImportOutcome.Fail(loadResult?.Error ?? "No image to import");
            if (entryAddr == 0) return ImportOutcome.Fail("No portrait entry selected");
            if (undoService == null) return ImportOutcome.Fail("Undo service not initialized");
            if (loadResult.Width != 128 || loadResult.Height != 112)
                return ImportOutcome.Fail("Sheet import requires a 128x112 image");

            // FE6 gate (Copilot CLI plan v1 review blocking point 1):
            // FE6 portrait entries are 16 bytes; bytes 12-15 are mouth/eye
            // coords + flags, not a pointer. Writing a mouth-frames pointer
            // into addr+12 would corrupt those fields.
            if (!IsFe7Or8EntryLayout(rom))
                return ImportOutcome.Fail("128x112 composite sheets are FE7/FE8 only "
                    + "— use the FE6 portrait editor for FE6 ROMs.");

            // Reconstruct RGBA from indexed for the splitter (palette index
            // 0 = transparent, matching WF / ImagePortraitView behavior).
            byte[] rgbaPixels = ReconstructRgbaWithPaletteZeroTransparent(loadResult);
            if (rgbaPixels == null)
                return ImportOutcome.Fail("Failed to reconstruct RGBA pixels");

            var parts = PortraitRendererCore.SplitPortraitSheet(rgbaPixels, loadResult.Width, loadResult.Height);
            if (parts == null)
                return ImportOutcome.Fail("Failed to split portrait sheet.");

            byte[] sheetIndexed = ImageImportCore.RemapToExistingPalette(
                parts.SpriteSheetPixels, parts.SpriteSheetW, parts.SpriteSheetH,
                loadResult.GBAPalette, 16);
            byte[] miniIndexed = ImageImportCore.RemapToExistingPalette(
                parts.MiniPixels, parts.MiniW, parts.MiniH,
                loadResult.GBAPalette, 16);
            byte[] mouthIndexed = ImageImportCore.RemapToExistingPalette(
                parts.MouthPixels, parts.MouthW, parts.MouthH,
                loadResult.GBAPalette, 16);
            if (sheetIndexed == null || miniIndexed == null || mouthIndexed == null)
                return ImportOutcome.Fail("Failed to remap sheet parts to palette.");

            undoService.Begin(undoLabel);
            try
            {
                byte[] sheetTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    sheetIndexed, parts.SpriteSheetW, parts.SpriteSheetH);
                if (sheetTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode sprite sheet tiles"); }

                uint currentD0 = rom.p32(entryAddr + 0);
                // Use the rom-aware isSafetyOffset overload + reuse the
                // computed offset (Copilot bot PR #684 inline review:
                // avoid reaching back through `CoreState.ROM` from helper
                // code that already has a `rom` parameter).
                uint currentD0Offset = U.toOffset(currentD0);
                bool isCompressed = U.isSafetyOffset(currentD0Offset, rom)
                    && LZ77.iscompress(rom.Data, currentD0Offset);

                uint sheetAddr;
                if (isCompressed)
                {
                    sheetAddr = ImageImportCore.WriteCompressedToROM(rom, sheetTiles, entryAddr + 0);
                }
                else
                {
                    byte[] withHeader = new byte[4 + sheetTiles.Length];
                    withHeader[0] = 0x00; withHeader[1] = 0x04; withHeader[2] = 0x10; withHeader[3] = 0x00;
                    Array.Copy(sheetTiles, 0, withHeader, 4, sheetTiles.Length);
                    sheetAddr = ImageImportCore.WriteRawToROM(rom, withHeader, entryAddr + 0);
                }
                if (sheetAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for sprite sheet"); }

                byte[] miniTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    miniIndexed, parts.MiniW, parts.MiniH);
                if (miniTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode mini face tiles"); }
                uint miniAddr = ImageImportCore.WriteCompressedToROM(rom, miniTiles, entryAddr + 4);
                if (miniAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for mini face"); }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, entryAddr + 8);
                if (palAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for palette"); }

                byte[] mouthTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    mouthIndexed, parts.MouthW, parts.MouthH);
                if (mouthTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode mouth tiles"); }
                uint mouthAddr = ImageImportCore.WriteRawToROM(rom, mouthTiles, entryAddr + 12);
                if (mouthAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for mouth data"); }

                undoService.Commit();
                return ImportOutcome.Ok();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                return ImportOutcome.Fail($"Sheet import failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Record the source-file path of an imported portrait so that the
        /// Open Source / Select Source buttons on
        /// <see cref="Views.ImagePortraitView"/> light up.
        ///
        /// Matches WF <c>ImagePortraitForm.ImportButton_Click</c> behavior of
        /// <c>Program.ResourceCache.Update("Portrait_" + ToHexString(idx), filename)</c>.
        /// </summary>
        public static void RecordSourceFile(int portraitIndex, string sourcePath)
        {
            if (portraitIndex < 0) return;
            if (string.IsNullOrEmpty(sourcePath)) return;

            if (CoreState.ResourceCache is EtcCacheResource cache)
            {
                string srcKey = "Portrait_" + U.ToHexString((uint)portraitIndex);
                cache.Update(srcKey, sourcePath);
            }
        }

        /// <summary>
        /// Build RGBA pixel bytes from an <see cref="ImageImportService.LoadResult"/>,
        /// treating palette index 0 as fully transparent (matches WF + the
        /// existing ImagePortraitView behavior). Returns null on failure.
        /// (Copilot CLI plan v1 review important point 3.)
        /// </summary>
        public static byte[] ReconstructRgbaWithPaletteZeroTransparent(
            ImageImportService.LoadResult loadResult)
        {
            if (loadResult == null || loadResult.IndexedPixels == null
                || loadResult.GBAPalette == null) return null;

            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;

            int w = loadResult.Width, h = loadResult.Height;
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < loadResult.IndexedPixels.Length && i < w * h; i++)
            {
                int palIdx = loadResult.IndexedPixels[i];
                int palOff = palIdx * 2;
                if (palOff + 2 <= loadResult.GBAPalette.Length)
                {
                    ushort gbaColor = (ushort)(loadResult.GBAPalette[palOff]
                        | (loadResult.GBAPalette[palOff + 1] << 8));
                    svc.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                    rgba[i * 4 + 0] = r;
                    rgba[i * 4 + 1] = g;
                    rgba[i * 4 + 2] = b;
                    rgba[i * 4 + 3] = (byte)(palIdx == 0 ? 0 : 255);
                }
            }
            return rgba;
        }

        /// <summary>
        /// Convenience: build an <see cref="IImage"/> preview from a quantized
        /// load-result, ready to drop into a <c>GbaImageControl</c>. Returns
        /// null on failure.
        /// </summary>
        public static IImage BuildPreviewImage(ImageImportService.LoadResult loadResult)
        {
            if (loadResult == null || !loadResult.Success) return null;
            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;

            byte[] rgba = ReconstructRgbaWithPaletteZeroTransparent(loadResult);
            if (rgba == null) return null;

            IImage img = svc.CreateImage(loadResult.Width, loadResult.Height);
            img.SetPixelData(rgba);
            return img;
        }
    }
}
