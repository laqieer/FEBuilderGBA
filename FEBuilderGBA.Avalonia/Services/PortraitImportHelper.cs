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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    /// Palette-handling mode for the Portrait Import Wizard (#662).
    ///
    /// <list type="bullet">
    /// <item><description><see cref="AutoQuantize"/> — quantize the source image to 16 best-fit
    /// colors (default; matches the original wizard behavior).</description></item>
    /// <item><description><see cref="SharePalette"/> — read the palette already at the target
    /// slot's D8 pointer (dereferenced) and remap the source pixels to it. Does NOT
    /// write a new palette; only the tile data at D0 is touched.</description></item>
    /// <item><description><see cref="CustomPalette"/> — use a user-supplied .pal/.act/.gpl
    /// file as the palette. Remaps the source pixels to that palette and writes
    /// both the tile data and the new palette.</description></item>
    /// </list>
    /// </summary>
    public enum PortraitPaletteMode
    {
        AutoQuantize = 0,
        SharePalette = 1,
        CustomPalette = 2,
    }

    /// <summary>Aggregate result of a batch folder import (#661).</summary>
    /// <param name="Imported">Files successfully written to ROM.</param>
    /// <param name="Failed">Files that parsed a slot ID but failed during load/quantize/write.</param>
    /// <param name="Skipped">Files skipped because the filename did not encode a slot ID.</param>
    /// <param name="Total">Total .png + .bmp files enumerated in the folder.</param>
    /// <param name="Lines">Human-readable per-file outcome lines, in enumeration order.</param>
    public record FolderImportResult(int Imported, int Failed, int Skipped, int Total, List<string> Lines);

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
            => ImportSimple(rom, entryAddr, loadResult, undoService,
                PortraitPaletteMode.AutoQuantize, null, false, undoLabel);

        /// <summary>
        /// Mode-aware overload (#662). Honors <see cref="PortraitPaletteMode"/>:
        ///   - <see cref="PortraitPaletteMode.AutoQuantize"/>: quantize and write the
        ///     palette (existing behavior).
        ///   - <see cref="PortraitPaletteMode.SharePalette"/>: dereference D8 to find the
        ///     existing palette, remap source pixels, SKIP the palette write.
        ///   - <see cref="PortraitPaletteMode.CustomPalette"/>: use <paramref name="customPaletteBytes"/>
        ///     (must be exactly 32 BGR555 bytes), remap source pixels, write the
        ///     palette to ROM.
        /// When <paramref name="fuchidori"/> is true the indexed-pixel buffer is
        /// post-processed by <see cref="ImageUtilCore.Fuchidori(byte[], int, int, byte)"/>
        /// to add a 1-pixel black outline.
        /// </summary>
        public static ImportOutcome ImportSimple(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
            string undoLabel = "Import Portrait Image")
        {
            if (rom == null) return ImportOutcome.Fail("ROM not loaded");
            if (loadResult == null || !loadResult.Success)
                return ImportOutcome.Fail(loadResult?.Error ?? "No image to import");
            if (entryAddr == 0) return ImportOutcome.Fail("No portrait entry selected");
            if (undoService == null) return ImportOutcome.Fail("Undo service not initialized");

            // Resolve mode-specific palette + indexed pixels BEFORE opening the
            // undo scope (Copilot CLI plan v2 review #1: a bad palette / pointer
            // must fail without leaving a no-op rollback entry on the stack).
            byte[] effectivePalette;
            byte[] indexedPixels;
            switch (mode)
            {
                case PortraitPaletteMode.SharePalette:
                {
                    // Critical fix #1: dereference the D8 pointer rather than
                    // reading bytes directly from entryAddr+8 (those are pointer
                    // bytes, not palette bytes).
                    uint palettePtr = rom.p32(entryAddr + 8);
                    uint paletteOffset = U.toOffset(palettePtr);
                    if (paletteOffset == 0 || paletteOffset + 32 > (uint)rom.Data.Length)
                        return ImportOutcome.Fail("Target slot has no valid palette pointer at D8 — pick a different mode.");
                    effectivePalette = rom.getBinaryData(paletteOffset, 32);
                    if (effectivePalette == null || effectivePalette.Length < 32)
                        return ImportOutcome.Fail("Could not read existing palette at D8 pointer.");
                    indexedPixels = ImageImportCore.RemapToExistingPalette(
                        ReconstructRgbaWithPaletteZeroTransparent(loadResult),
                        loadResult.Width, loadResult.Height, effectivePalette, 16);
                    if (indexedPixels == null)
                        return ImportOutcome.Fail("Failed to remap pixels to existing palette.");
                    break;
                }
                case PortraitPaletteMode.CustomPalette:
                {
                    // Critical fix #2: validate exactly 32 bytes (16 colors).
                    if (customPaletteBytes == null || customPaletteBytes.Length != 32)
                        return ImportOutcome.Fail(
                            $"Invalid custom palette — expected 16 colors (32 bytes), got {customPaletteBytes?.Length ?? 0}.");
                    effectivePalette = customPaletteBytes;
                    indexedPixels = ImageImportCore.RemapToExistingPalette(
                        ReconstructRgbaWithPaletteZeroTransparent(loadResult),
                        loadResult.Width, loadResult.Height, effectivePalette, 16);
                    if (indexedPixels == null)
                        return ImportOutcome.Fail("Failed to remap pixels to custom palette.");
                    break;
                }
                default: // AutoQuantize
                    effectivePalette = loadResult.GBAPalette;
                    indexedPixels = loadResult.IndexedPixels;
                    break;
            }

            if (fuchidori && indexedPixels != null && effectivePalette != null)
            {
                // Pick the darkest available palette index as the outline color,
                // matching WF ImagePortraitImporterForm's FindBlackColorFromPalette
                // call. Skip index 0 (reserved for transparency).
                int blackIdx = ImageUtilCore.FindBlackColorIndex(effectivePalette, 1, 16);
                ImageUtilCore.Fuchidori(indexedPixels, loadResult.Width, loadResult.Height, (byte)blackIdx);
            }

            undoService.Begin(undoLabel);
            try
            {
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(
                    indexedPixels, loadResult.Width, loadResult.Height);
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

                // SharePalette intentionally skips the palette write — the
                // existing palette stays in place at the dereferenced offset.
                if (mode != PortraitPaletteMode.SharePalette)
                {
                    uint palAddr = ImageImportCore.WritePaletteToROM(rom, effectivePalette, entryAddr + 8);
                    if (palAddr == U.NOT_FOUND)
                    {
                        undoService.Rollback();
                        return ImportOutcome.Fail("No free space for palette");
                    }
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
            => ImportSheet(rom, entryAddr, loadResult, undoService,
                PortraitPaletteMode.AutoQuantize, null, false, undoLabel);

        /// <summary>
        /// Mode-aware overload (#662). Same palette-mode + Fuchidori semantics as
        /// <see cref="ImportSimple(ROM, uint, ImageImportService.LoadResult, UndoService, PortraitPaletteMode, byte[], bool, string)"/>,
        /// but for 128x112 composite sheets. SharePalette dereferences D8 to
        /// re-use the existing palette; CustomPalette validates 32 bytes.
        /// </summary>
        public static ImportOutcome ImportSheet(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
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

            // Resolve mode-specific palette BEFORE opening the undo scope so
            // a pointer/format failure can't leave a no-op rollback entry.
            byte[] effectivePalette;
            switch (mode)
            {
                case PortraitPaletteMode.SharePalette:
                {
                    uint palettePtr = rom.p32(entryAddr + 8);
                    uint paletteOffset = U.toOffset(palettePtr);
                    if (paletteOffset == 0 || paletteOffset + 32 > (uint)rom.Data.Length)
                        return ImportOutcome.Fail("Target slot has no valid palette pointer at D8 — pick a different mode.");
                    effectivePalette = rom.getBinaryData(paletteOffset, 32);
                    if (effectivePalette == null || effectivePalette.Length < 32)
                        return ImportOutcome.Fail("Could not read existing palette at D8 pointer.");
                    break;
                }
                case PortraitPaletteMode.CustomPalette:
                    if (customPaletteBytes == null || customPaletteBytes.Length != 32)
                        return ImportOutcome.Fail(
                            $"Invalid custom palette — expected 16 colors (32 bytes), got {customPaletteBytes?.Length ?? 0}.");
                    effectivePalette = customPaletteBytes;
                    break;
                default:
                    effectivePalette = loadResult.GBAPalette;
                    break;
            }

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
                effectivePalette, 16);
            byte[] miniIndexed = ImageImportCore.RemapToExistingPalette(
                parts.MiniPixels, parts.MiniW, parts.MiniH,
                effectivePalette, 16);
            byte[] mouthIndexed = ImageImportCore.RemapToExistingPalette(
                parts.MouthPixels, parts.MouthW, parts.MouthH,
                effectivePalette, 16);
            if (sheetIndexed == null || miniIndexed == null || mouthIndexed == null)
                return ImportOutcome.Fail("Failed to remap sheet parts to palette.");

            if (fuchidori)
            {
                int blackIdx = ImageUtilCore.FindBlackColorIndex(effectivePalette, 1, 16);
                ImageUtilCore.Fuchidori(sheetIndexed, parts.SpriteSheetW, parts.SpriteSheetH, (byte)blackIdx);
                ImageUtilCore.Fuchidori(miniIndexed, parts.MiniW, parts.MiniH, (byte)blackIdx);
                ImageUtilCore.Fuchidori(mouthIndexed, parts.MouthW, parts.MouthH, (byte)blackIdx);
            }

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

                // SharePalette intentionally skips the palette write — the
                // existing palette at the dereferenced D8 offset stays in place
                // (#662). Auto / Custom write the effective palette to D8.
                if (mode != PortraitPaletteMode.SharePalette)
                {
                    uint palAddr = ImageImportCore.WritePaletteToROM(rom, effectivePalette, entryAddr + 8);
                    if (palAddr == U.NOT_FOUND)
                    { undoService.Rollback(); return ImportOutcome.Fail("No free space for palette"); }
                }

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
        /// Parse a portrait slot ID from a filename. Accepts:
        ///   - Hexadecimal prefix: "0x1F.png", "0x1f_anything.bmp" → 31
        ///   - Decimal prefix:     "31.png",  "31_anything.bmp"   → 31
        /// Returns -1 when no recognisable numeric prefix is present.
        /// </summary>
        internal static int ParseSlotIdFromFilename(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return -1;
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(name)) return -1;

            // Hex prefix: 0xNN or 0XNN. Check before decimal so "0x10" doesn't
            // get matched as the decimal "0".
            var hexMatch = System.Text.RegularExpressions.Regex.Match(name, @"^0[xX]([0-9A-Fa-f]+)");
            if (hexMatch.Success && int.TryParse(hexMatch.Groups[1].Value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int hex))
            {
                return hex;
            }
            // Decimal prefix.
            var decMatch = System.Text.RegularExpressions.Regex.Match(name, @"^(\d+)");
            if (decMatch.Success && int.TryParse(decMatch.Groups[1].Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int dec))
            {
                return dec;
            }
            return -1;
        }

        /// <summary>
        /// Count the entries in the portrait table by walking from
        /// <c>portrait_pointer</c> until an all-null sentinel sequence is
        /// hit. Mirrors the scan logic in
        /// <see cref="ViewModels.ImagePortraitImporterViewModel.LoadList"/> so
        /// the batch-import slot-bound check rejects exactly the slots the
        /// wizard's left-side list would consider out of range.
        ///
        /// Returns 0 when the ROM / pointer is unusable.
        /// </summary>
        internal static int CountPortraitTableEntries(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            uint pointer = rom.RomInfo.portrait_pointer;
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (pointer == 0 || dataSize == 0) return 0;

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            int count = 0;
            int nullCount = 0;
            // 512 mirrors the soft cap in LoadList. Hard ROMs never approach
            // it; the sentinel terminates the scan first.
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (rom.u32(addr) == 0)
                {
                    nullCount++;
                    if (nullCount > 3) break;
                }
                else
                {
                    nullCount = 0;
                }
                count = i + 1;
            }
            return count;
        }

        /// <summary>
        /// Batch-import every .png + .bmp file in <paramref name="folderPath"/>
        /// into portrait slots derived from the filename prefix.
        ///
        /// **Per-file undo isolation** (Copilot CLI PR review blocking #1):
        /// every file gets its OWN <see cref="UndoService"/> scope via the
        /// normal (non-external) <see cref="ImportSimple"/> / <see cref="ImportSheet"/>
        /// code path. That means:
        ///   - A file that fails mid-write (e.g. D0 written, palette out of free
        ///     space) rolls back ITS partial ROM bytes — failed files cannot
        ///     pollute the ROM even when other files in the batch succeed.
        ///   - Each successful file gets its own undo stack entry, so the user
        ///     can selectively undo individual portraits afterwards.
        /// Each file is also pre-validated (load + quantize) before any ROM
        /// write — bad PNGs are rejected before <see cref="ImportSimple"/> opens
        /// its scope.
        ///
        /// **Sheet routing** (Copilot CLI PR review blocking #2): 128x112
        /// composite sheets are routed through <see cref="ImportSheet"/> on
        /// FE7/FE8 ROMs so D4 (mini-face) and D12 (mouth frames) get written
        /// alongside D0 + D8. FE6 ROMs reject sheet-sized inputs because the
        /// 16-byte FE6 layout has no mouth-frame pointer at D12 (the FE6 gate
        /// in <see cref="ImportSheet"/> handles this — the failure is reported
        /// per file and does not abort the batch).
        ///
        /// File-naming convention (issue #661):
        ///   "0x1F.png" or "31.png" -> slot 31
        ///   anything else          -> skipped
        ///
        /// Caller is responsible for keeping <paramref name="rom"/> alive for
        /// the duration of the task; ROM reads/writes run on the caller's
        /// thread (no <c>Task.Run</c>).
        /// </summary>
        /// <param name="folderPath">Folder containing .png / .bmp portrait images.</param>
        /// <param name="progress">Optional per-file progress reporter (filename + outcome line).</param>
        /// <param name="undo">Undo service used for each per-file scope.</param>
        /// <param name="rom">Target ROM.</param>
        public static async Task<FolderImportResult> ImportFolderAsync(
            string folderPath,
            IProgress<string> progress,
            UndoService undo,
            ROM rom)
        {
            var lines = new List<string>();
            if (rom == null)
            {
                lines.Add("ROM not loaded.");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }
            if (undo == null)
            {
                lines.Add("Undo service not initialized.");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                lines.Add($"Folder not found: {folderPath}");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }

            // Copilot CLI plan v1 review #2: the target address is
            // `portrait_pointer` dereferenced (it's a pointer into the
            // portrait table). Doing slotId * datasize off the raw pointer
            // field address would land in completely unrelated ROM bytes.
            uint portraitBase = rom.p32(rom.RomInfo.portrait_pointer);
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0)
            {
                lines.Add("Invalid portrait_datasize.");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }

            // Copilot CLI PR review (round 2) #1: enforce the portrait-table
            // upper bound. A filename like "0xFFFF.png" would pass the raw
            // rom.Data.Length bounds check but write outside the portrait
            // table into unrelated ROM data. Use the same sentinel-terminated
            // scan as ImagePortraitImporterViewModel.LoadList so the cap
            // matches what the wizard's left-side list shows the user.
            int portraitCount = CountPortraitTableEntries(rom);
            if (portraitCount <= 0)
            {
                lines.Add("Portrait table not found or empty.");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }

            // Enumerate .png + .bmp files, sorted alphabetically for
            // deterministic ordering across platforms (Directory.EnumerateFiles
            // does NOT guarantee an order).
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(folderPath)
                    .Where(f =>
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext == ".png" || ext == ".bmp";
                    })
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                lines.Add($"Failed to enumerate folder: {ex.Message}");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }

            if (files.Count == 0)
            {
                lines.Add("No .png or .bmp files found in folder.");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }

            int imported = 0, failed = 0, skipped = 0;

            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                int slotId = ParseSlotIdFromFilename(fileName);
                if (slotId < 0)
                {
                    skipped++;
                    string line = $"{fileName} → SKIPPED: no slot ID prefix";
                    lines.Add(line);
                    progress?.Report(line);
                    await Task.Yield();
                    continue;
                }

                // Pre-validate: load + quantize BEFORE the helper opens its
                // undo scope. A bad PNG must not even enter ImportSimple /
                // ImportSheet (so we never push a no-op rollback entry).
                ImageImportService.LoadResult loadResult;
                try
                {
                    loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 0, 0, 16);
                }
                catch (Exception ex)
                {
                    failed++;
                    string line = $"{fileName} → FAILED: load error: {ex.Message}";
                    lines.Add(line);
                    progress?.Report(line);
                    await Task.Yield();
                    continue;
                }
                if (loadResult == null || !loadResult.Success)
                {
                    failed++;
                    string reason = loadResult?.Error ?? "unknown error";
                    string line = $"{fileName} → FAILED: {reason}";
                    lines.Add(line);
                    progress?.Report(line);
                    await Task.Yield();
                    continue;
                }

                // Reject slots beyond the portrait-table cap so a stray
                // "0xFFFF.png" cannot corrupt unrelated ROM data (Copilot CLI
                // PR review round 2 #1).
                if (slotId >= portraitCount)
                {
                    failed++;
                    string line = $"{fileName} → FAILED: slot 0x{slotId:X2} out of range "
                        + $"(portrait table size is 0x{portraitCount:X2})";
                    lines.Add(line);
                    progress?.Report(line);
                    await Task.Yield();
                    continue;
                }

                // Defense-in-depth: also check the underlying ROM bounds in
                // case the table cap was computed against a smaller ROM than
                // the one we're writing to (should be impossible, but cheap).
                long addrLong = (long)portraitBase + (long)slotId * dataSize;
                if (addrLong < 0 || addrLong + dataSize > rom.Data.Length)
                {
                    failed++;
                    string line = $"{fileName} → FAILED: slot 0x{slotId:X2} out of ROM bounds";
                    lines.Add(line);
                    progress?.Report(line);
                    await Task.Yield();
                    continue;
                }
                uint entryAddr = (uint)addrLong;

                // Per-file undo scope (Copilot PR review #1 fix). Each file
                // owns its own scope so a mid-write failure rolls back only
                // ITS partial ROM bytes — successful files in the batch stay
                // committed. Route 128x112 composite sheets to ImportSheet so
                // D4 (mini-face) + D12 (mouth frames) get written alongside
                // D0/D8 (Copilot PR review #2 fix). On FE6 ROMs ImportSheet
                // self-rejects with a clear error.
                bool isSheet = loadResult.Width == 128 && loadResult.Height == 112;
                ImportOutcome outcome = isSheet
                    ? ImportSheet(rom, entryAddr, loadResult, undo,
                        undoLabel: $"Import Portrait Sheet (Batch slot 0x{slotId:X2})")
                    : ImportSimple(rom, entryAddr, loadResult, undo,
                        undoLabel: $"Import Portrait Image (Batch slot 0x{slotId:X2})");

                if (outcome.Success)
                {
                    imported++;
                    // Record the source file so the per-slot Open/Select
                    // Source buttons light up after a batch import.
                    RecordSourceFile(slotId, filePath);
                    string mode = isSheet ? " (sheet)" : "";
                    string line = $"{fileName} → slot 0x{slotId:X2}{mode} → OK";
                    lines.Add(line);
                    progress?.Report(line);
                }
                else
                {
                    failed++;
                    string line = $"{fileName} → slot 0x{slotId:X2} → FAILED: {outcome.Error}";
                    lines.Add(line);
                    progress?.Report(line);
                }
                await Task.Yield();
            }

            return new FolderImportResult(imported, failed, skipped, files.Count, lines);
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
