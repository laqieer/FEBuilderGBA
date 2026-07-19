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
        // Portrait entry pointer offsets. Named constants so the #657
        // invalidate path and any future field additions don't depend on
        // magic numbers (Copilot bot review on PR #731).
        //
        // Layout coverage:
        //   D0 / D4 / D8         — present in BOTH FE6 (16-byte entry) AND
        //                          FE7/FE8 (28-byte entry). FE6's D4 is the
        //                          Mini/Map face pointer; D8 is the palette
        //                          pointer in all three versions.
        //   D12 (mouth frames)   — pointer ONLY on FE7/FE8. FE6 reuses
        //                          bytes +12..+15 for u8 mouth-X / mouth-Y /
        //                          two unused fields (not a pointer).
        //   B20-B23 block coords — FE7/FE8 only (do not exist on FE6's
        //                          16-byte entry).
        internal const uint OFFSET_D0_TILE_SHEET     = 0;   // D0: main portrait tiles pointer (FE6/7/8)
        internal const uint OFFSET_D4_MINI_FACE      = 4;   // D4: 32x32 mini face tiles pointer (FE6/7/8)
        internal const uint OFFSET_D8_PALETTE        = 8;   // D8: palette pointer (FE6/7/8)
        internal const uint OFFSET_D12_MOUTH_FRAMES  = 12;  // D12: mouth-frames tiles pointer (FE7/8 only — FE6 reuses bytes +12..+15 for coords)
        internal const uint OFFSET_D16_CLASS_CARD    = 16;  // D16: class-card pointer (FE7/8 only)
        internal const uint OFFSET_B20_MOUTH_BLOCK_X = 20;  // B20: mouth block X (FE7/8 only)
        internal const uint OFFSET_B21_MOUTH_BLOCK_Y = 21;  // B21: mouth block Y (FE7/8 only)
        internal const uint OFFSET_B22_EYE_BLOCK_X   = 22;  // B22: eye block X (FE7/8 only)
        internal const uint OFFSET_B23_EYE_BLOCK_Y   = 23;  // B23: eye block Y (FE7/8 only)
        const int HALFBODY_PALETTE_BYTES = 64;

        static readonly PatchDetection.PatchTableSt[] HalfBodyPatchTable =
        {
            new PatchDetection.PatchTableSt{ name="HALFBODY", ver="FE8U", addr=0x8540, data=new byte[]{0x0A,0x1C} },
            new PatchDetection.PatchTableSt{ name="HALFBODY", ver="FE8U", addr=0x8540, data=new byte[]{0x01,0x3A} },
            new PatchDetection.PatchTableSt{ name="HALFBODY", ver="FE8J", addr=0x843C, data=new byte[]{0x0A,0x1C} },
            new PatchDetection.PatchTableSt{ name="HALFBODY", ver="FE8J", addr=0x843C, data=new byte[]{0x01,0x3A} },
        };

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

        static bool CanReadRomRange(ROM rom, uint offset, uint length)
        {
            if (rom?.Data == null) return false;
            return (ulong)offset + (ulong)length <= (ulong)rom.Data.Length;
        }

        static bool TryReadP32(ROM rom, uint offset, string label, out uint value, out string error)
        {
            value = 0;
            if (!CanReadRomRange(rom, offset, 4))
            {
                error = $"{label} pointer field is outside ROM bounds.";
                return false;
            }

            value = rom.p32(offset);
            error = null;
            return true;
        }

        static bool TryReadEntryP32(ROM rom, uint entryAddr, uint fieldOffset, string label, out uint value, out string error)
        {
            value = 0;
            ulong offset = (ulong)entryAddr + (ulong)fieldOffset;
            if (rom?.Data == null || offset + 4u > (ulong)rom.Data.Length)
            {
                error = $"{label} pointer field is outside ROM bounds.";
                return false;
            }

            value = rom.p32((uint)offset);
            error = null;
            return true;
        }

        static bool CanAccessEntryRange(ROM rom, uint entryAddr, uint fieldOffset, uint length)
        {
            ulong offset = (ulong)entryAddr + (ulong)fieldOffset;
            return rom?.Data != null && offset + (ulong)length <= (ulong)rom.Data.Length;
        }

        static bool IsHalfBodyPatchInstalled(ROM rom)
        {
            return PatchDetection.SearchPatchBool(rom, HalfBodyPatchTable);
        }

        /// <summary>
        /// Eye/mouth crop rectangles (pixels, within each 32x16 animation cell)
        /// used by the 128x112 sheet import to reconstruct the eye/mouth cells
        /// (#1917). Mirrors the wizard's Detail-panel crop NumericUpDowns and the
        /// values <see cref="PortraitImportPreviewCore.RenderFramePreview"/> uses
        /// for the live preview, so the import produces the same cells the
        /// preview shows.
        /// </summary>
        public readonly struct PortraitEyeMouthCrops
        {
            public readonly int EyeX, EyeY, EyeW, EyeH;
            public readonly int MouthX, MouthY, MouthW, MouthH;
            public PortraitEyeMouthCrops(
                int eyeX, int eyeY, int eyeW, int eyeH,
                int mouthX, int mouthY, int mouthW, int mouthH)
            {
                EyeX = eyeX; EyeY = eyeY; EyeW = eyeW; EyeH = eyeH;
                MouthX = mouthX; MouthY = mouthY; MouthW = mouthW; MouthH = mouthH;
            }
        }

        public static ImportOutcome ImportPortrait(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            string undoLabel = "Import Portrait Image")
            => ImportPortrait(rom, entryAddr, loadResult, undoService,
                PortraitPaletteMode.AutoQuantize, null, false, undoLabel);

        public static ImportOutcome ImportPortrait(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
            string undoLabel = "Import Portrait Image",
            byte? mouthBlockX = null,
            byte? mouthBlockY = null,
            byte? eyeBlockX = null,
            byte? eyeBlockY = null,
            PortraitEyeMouthCrops? crops = null)
        {
            if (loadResult == null || !loadResult.Success)
                return ImportOutcome.Fail(loadResult?.Error ?? "No image to import");
            if (loadResult.Width == 96 && loadResult.Height == 80)
            {
                return ImportFaceImage(rom, entryAddr, loadResult, undoService,
                    mode, customPaletteBytes, fuchidori, undoLabel,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);
            }
            if (loadResult.Width == 128 && loadResult.Height == 112)
            {
                return ImportSheet(rom, entryAddr, loadResult, undoService,
                    mode, customPaletteBytes, fuchidori, undoLabel,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY, crops);
            }
            if (loadResult.Width == 160 && loadResult.Height == 160)
            {
                return ImportHalfBodyHackbox(rom, entryAddr, loadResult, undoService,
                    mode, customPaletteBytes, fuchidori, undoLabel,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);
            }
            if (loadResult.Width == 144 && loadResult.Height == 304)
            {
                return ImportOutcome.Fail("Twoparter Hackbox (144x304) is not yet supported; no verified WinForms PART1/PART2 layout exists.");
            }

            return ImportOutcome.Fail(
                $"Unsupported portrait image size {loadResult.Width}x{loadResult.Height}. "
                + "Use a 96x80 exported face PNG, a 128x112 Standard Hackbox, or an FE8 HALFBODY 160x160 Halfbody Hackbox.");
        }

        /// <summary>
        /// Import a single PNG into the portrait sheet (D0) + palette (D8)
        /// slots of the entry at <paramref name="entryAddr"/>. Safe on all
        /// ROM versions (FE6 included) because the writes target fields
        /// that exist on both 16-byte FE6 and 28-byte FE7/8 layouts.
        ///
        /// As of #657 this also zeroes:
        ///   - D4 (Mini/Map face pointer) on every ROM version, and
        ///   - D12 (mouth-frames pointer) on FE7/FE8 only
        /// so the Portrait Image Editor renders those sub-views as BLANK
        /// instead of decoding the previous portrait's tile blocks under
        /// the NEW palette (the "total mess" rendering reported in #657).
        /// FE6 bytes +12..+15 are mouth coords (not a pointer) and stay
        /// untouched on FE6.
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
        ///
        /// Slice A of #663 (mouth/eye block coords): when <paramref name="mouthBlockX"/>,
        /// <paramref name="mouthBlockY"/>, <paramref name="eyeBlockX"/>, or
        /// <paramref name="eyeBlockY"/> are non-null AND the ROM uses the 28-byte
        /// FE7/FE8 portrait entry layout (see <see cref="IsFe7Or8EntryLayout"/>), each
        /// non-null value is written to its corresponding byte inside the same undo
        /// scope as the D0 / D8 writes:
        ///   - <paramref name="mouthBlockX"/> -> entryAddr + 20 (B20)
        ///   - <paramref name="mouthBlockY"/> -> entryAddr + 21 (B21)
        ///   - <paramref name="eyeBlockX"/>   -> entryAddr + 22 (B22)
        ///   - <paramref name="eyeBlockY"/>   -> entryAddr + 23 (B23)
        /// A null parameter means "caller didn't ask"; the byte is left untouched
        /// for backward compatibility (existing callers omit the parameters). Any
        /// byte value (0x00 .. 0xFF) is written as-is — 0xFF is NOT a sentinel.
        /// On FE6 ROMs the 16-byte entry layout reuses those bytes for unrelated
        /// fields, so the writes are skipped silently.
        /// </summary>
        public static ImportOutcome ImportSimple(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
            string undoLabel = "Import Portrait Image",
            byte? mouthBlockX = null,
            byte? mouthBlockY = null,
            byte? eyeBlockX = null,
            byte? eyeBlockY = null)
        {
            if (rom == null) return ImportOutcome.Fail("ROM not loaded");
            if (loadResult == null || !loadResult.Success)
                return ImportOutcome.Fail(loadResult?.Error ?? "No image to import");
            if (entryAddr == 0) return ImportOutcome.Fail("No portrait entry selected");
            if (undoService == null) return ImportOutcome.Fail("Undo service not initialized");

            byte[] keyedRgba = BuildColorKeyedRgba(loadResult);
            if (keyedRgba == null)
                return ImportOutcome.Fail("Failed to prepare portrait pixels.");

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
                    // reading bytes directly from entryAddr + OFFSET_D8_PALETTE
                    // (those are pointer bytes, not palette bytes).
                    if (!TryReadEntryP32(rom, entryAddr, OFFSET_D8_PALETTE,
                        "Target slot D8 palette", out uint palettePtr, out string pointerError))
                        return ImportOutcome.Fail(pointerError);
                    uint paletteOffset = U.toOffset(palettePtr);
                    if (paletteOffset == 0 || !CanReadRomRange(rom, paletteOffset, 32))
                        return ImportOutcome.Fail("Target slot has no valid palette pointer at D8 — pick a different mode.");
                    effectivePalette = rom.getBinaryData(paletteOffset, 32);
                    if (effectivePalette == null || effectivePalette.Length < 32)
                        return ImportOutcome.Fail("Could not read existing palette at D8 pointer.");
                    indexedPixels = ImageImportCore.RemapToExistingPalette(
                        keyedRgba, loadResult.Width, loadResult.Height, effectivePalette, 16);
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
                        keyedRgba, loadResult.Width, loadResult.Height, effectivePalette, 16);
                    if (indexedPixels == null)
                        return ImportOutcome.Fail("Failed to remap pixels to custom palette.");
                    break;
                }
                default: // AutoQuantize
                {
                    var qr = DecreaseColorCore.Quantize(keyedRgba, loadResult.Width, loadResult.Height, 16);
                    if (qr == null) return ImportOutcome.Fail("Color quantization failed");
                    effectivePalette = qr.GBAPalette;
                    indexedPixels = qr.IndexData;
                    break;
                }
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

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, entryAddr + OFFSET_D0_TILE_SHEET);
                if (tileAddr == U.NOT_FOUND)
                {
                    undoService.Rollback();
                    return ImportOutcome.Fail("No free space for tile data");
                }

                // SharePalette intentionally skips the palette write — the
                // existing palette stays in place at the dereferenced offset.
                if (mode != PortraitPaletteMode.SharePalette)
                {
                    uint palAddr = ImageImportCore.WritePaletteToROM(rom, effectivePalette, entryAddr + OFFSET_D8_PALETTE);
                    if (palAddr == U.NOT_FOUND)
                    {
                        undoService.Rollback();
                        return ImportOutcome.Fail("No free space for palette");
                    }
                }

                // #663 Slice A: persist optional mouth/eye block coords to
                // bytes B20-B23. FE7/FE8 only — FE6's 16-byte entry layout uses
                // those bytes for unrelated fields, so the writes are silently
                // skipped on FE6 to keep callers version-agnostic.
                WriteEyeMouthBlockCoords(rom, entryAddr, undoService,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);

                // #657: Simple Import writes D0 (tile data) and D8 (palette)
                // above, plus the optional B20-B23 block coords. Before this
                // fix it did NOT touch D4 (mini face) or D12 (mouth frames)
                // — those still pointed at the PREVIOUS portrait's tile
                // blocks. The Portrait Image Editor decodes those blocks
                // under the NEW palette and renders them as garbled garbage
                // — the user reported a "total mess".
                //
                // INVALIDATE PATH below: Simple Import now also zeroes D4
                // (and D12 on FE7/8) so the editor renders the mini-face
                // and mouth-frame sub-views as BLANK instead of garbled
                // stale data.
                //
                // D4 (Mini/Map face pointer) is present in BOTH the 16-byte
                // FE6 entry layout and the 28-byte FE7/FE8 layout, so zero
                // it unconditionally to avoid garbled mini-face renders on
                // every ROM version (Copilot CLI PR review blocking #1).
                //
                // D12 differs by layout:
                //   - FE7/FE8 (28-byte): D12 is the mouth-frames pointer →
                //     same stale-pointer failure as D4; zero it.
                //   - FE6 (16-byte):     bytes +12..+15 are mouth coords
                //     (u8 X, u8 Y, two unused bytes) — NOT a pointer.
                //     Zeroing them would silently move the mouth to
                //     (0, 0). Leave them alone on FE6.
                rom.write_p32(entryAddr + OFFSET_D4_MINI_FACE, 0);
                if (IsFe7Or8EntryLayout(rom))
                {
                    rom.write_p32(entryAddr + OFFSET_D12_MOUTH_FRAMES, 0);
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
        /// <see cref="ImportSimple(ROM, uint, ImageImportService.LoadResult, UndoService, PortraitPaletteMode, byte[], bool, string, byte?, byte?, byte?, byte?)"/>,
        /// but for 128x112 composite sheets. SharePalette dereferences D8 to
        /// re-use the existing palette; CustomPalette validates 32 bytes.
        ///
        /// Slice A of #663: same eye/mouth block-coord semantics as ImportSimple —
        /// see that overload's XML doc. FE7/FE8 only (the ImportSheet entry is
        /// already gated to that layout by <see cref="IsFe7Or8EntryLayout"/>, so
        /// non-null values are always written here when supplied).
        /// </summary>
        public static ImportOutcome ImportSheet(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
            string undoLabel = "Import Portrait Sheet (128x112)",
            byte? mouthBlockX = null,
            byte? mouthBlockY = null,
            byte? eyeBlockX = null,
            byte? eyeBlockY = null,
            PortraitEyeMouthCrops? crops = null)
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

            // Reconstruct RGBA from the original source and apply the
            // portrait color key before any split/remap step. The older
            // palette-index-0 reconstruction is intentionally not used here:
            // opaque backgrounds are still non-zero before this fix.
            byte[] rgbaPixels = BuildColorKeyedRgba(loadResult);
            if (rgbaPixels == null)
                return ImportOutcome.Fail("Failed to reconstruct RGBA pixels");

            // #1917: reconstruct the eye/mouth animation cells (reseed each cell
            // from the destination face block, overlay only the cropped feature)
            // BEFORE the palette is resolved AND before splitting — so (a) a
            // hackbox sheet whose eye/mouth cells sit on a DIFFERENT background
            // (e.g. white) than the face key (teal) has that background replaced
            // by the transparent face background rather than blitted as a solid
            // rectangle over the face in-game, and (b) AutoQuantize builds its
            // 16-color palette from the FINAL pixels (no slot wasted on the
            // discarded cell background — PR #1928 review). Mirrors WinForms
            // DecreaseColor16, which the preview already runs but the import
            // previously skipped. Gated to the wizard path (block coords + crops
            // supplied); drag-drop / "Import PNG" pass null and keep their
            // existing behaviour. FE7/FE8 only (ImportSheet is already gated).
            if (crops.HasValue && eyeBlockX.HasValue && eyeBlockY.HasValue
                && mouthBlockX.HasValue && mouthBlockY.HasValue)
            {
                PortraitEyeMouthCrops c = crops.Value;
                PortraitImportPreviewCore.ReconstructSheetCellsRgba(
                    rgbaPixels, loadResult.Width, loadResult.Height,
                    eyeBlockX.Value, eyeBlockY.Value, mouthBlockX.Value, mouthBlockY.Value,
                    c.EyeX, c.EyeY, c.EyeW, c.EyeH,
                    c.MouthX, c.MouthY, c.MouthW, c.MouthH,
                    isFe6: false);
            }

            // Resolve mode-specific palette BEFORE opening the undo scope so
            // a pointer/format failure can't leave a no-op rollback entry.
            byte[] effectivePalette;
            switch (mode)
            {
                case PortraitPaletteMode.SharePalette:
                {
                    if (!TryReadEntryP32(rom, entryAddr, OFFSET_D8_PALETTE,
                        "Target slot D8 palette", out uint palettePtr, out string pointerError))
                        return ImportOutcome.Fail(pointerError);
                    uint paletteOffset = U.toOffset(palettePtr);
                    if (paletteOffset == 0 || !CanReadRomRange(rom, paletteOffset, 32))
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
                {
                    var qr = DecreaseColorCore.Quantize(rgbaPixels, loadResult.Width, loadResult.Height, 16);
                    if (qr == null) return ImportOutcome.Fail("Color quantization failed");
                    effectivePalette = qr.GBAPalette;
                    break;
                }
            }

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

                if (!TryReadEntryP32(rom, entryAddr, OFFSET_D0_TILE_SHEET,
                    "Target slot D0 sprite sheet", out uint currentD0, out string pointerError))
                {
                    undoService.Rollback();
                    return ImportOutcome.Fail(pointerError);
                }
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
                    sheetAddr = ImageImportCore.WriteCompressedToROM(rom, sheetTiles, entryAddr + OFFSET_D0_TILE_SHEET);
                }
                else
                {
                    byte[] withHeader = new byte[4 + sheetTiles.Length];
                    withHeader[0] = 0x00; withHeader[1] = 0x04; withHeader[2] = 0x10; withHeader[3] = 0x00;
                    Array.Copy(sheetTiles, 0, withHeader, 4, sheetTiles.Length);
                    sheetAddr = ImageImportCore.WriteRawToROM(rom, withHeader, entryAddr + OFFSET_D0_TILE_SHEET);
                }
                if (sheetAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for sprite sheet"); }

                byte[] miniTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    miniIndexed, parts.MiniW, parts.MiniH);
                if (miniTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode mini face tiles"); }
                uint miniAddr = ImageImportCore.WriteCompressedToROM(rom, miniTiles, entryAddr + OFFSET_D4_MINI_FACE);
                if (miniAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for mini face"); }

                // SharePalette intentionally skips the palette write — the
                // existing palette at the dereferenced D8 offset stays in place
                // (#662). Auto / Custom write the effective palette to D8.
                if (mode != PortraitPaletteMode.SharePalette)
                {
                    uint palAddr = ImageImportCore.WritePaletteToROM(rom, effectivePalette, entryAddr + OFFSET_D8_PALETTE);
                    if (palAddr == U.NOT_FOUND)
                    { undoService.Rollback(); return ImportOutcome.Fail("No free space for palette"); }
                }

                byte[] mouthTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    mouthIndexed, parts.MouthW, parts.MouthH);
                if (mouthTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode mouth tiles"); }
                uint mouthAddr = ImageImportCore.WriteRawToROM(rom, mouthTiles, entryAddr + OFFSET_D12_MOUTH_FRAMES);
                if (mouthAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for mouth data"); }

                // #663 Slice A: persist optional mouth/eye block coords to
                // bytes B20-B23 inside the same undo scope as the pointer writes.
                // FE7/FE8 only — ImportSheet is already gated to that layout,
                // so non-null values are always honored here.
                WriteEyeMouthBlockCoords(rom, entryAddr, undoService,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);

                undoService.Commit();
                return ImportOutcome.Ok();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                return ImportOutcome.Fail($"Sheet import failed: {ex.Message}");
            }
        }

        static ImportOutcome ImportFaceImage(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
            string undoLabel,
            byte? mouthBlockX,
            byte? mouthBlockY,
            byte? eyeBlockX,
            byte? eyeBlockY)
        {
            if (rom == null) return ImportOutcome.Fail("ROM not loaded");
            if (entryAddr == 0) return ImportOutcome.Fail("No portrait entry selected");
            if (undoService == null) return ImportOutcome.Fail("Undo service not initialized");
            if (!IsFe7Or8EntryLayout(rom))
                return ImportOutcome.Fail("96x80 face imports are FE7/FE8 only in the Avalonia portrait importer.");

            byte[] keyedFace = BuildColorKeyedRgba(loadResult);
            if (keyedFace == null) return ImportOutcome.Fail("Failed to prepare portrait pixels.");

            byte[] sheetRgba = PortraitRendererCore.PromoteFaceToPortraitSheet(
                keyedFace, loadResult.Width, loadResult.Height);
            if (sheetRgba == null) return ImportOutcome.Fail("Failed to promote 96x80 face to portrait sheet.");

            byte[] effectivePalette;
            switch (mode)
            {
                case PortraitPaletteMode.SharePalette:
                {
                    if (!TryReadEntryP32(rom, entryAddr, OFFSET_D8_PALETTE,
                        "Target slot D8 palette", out uint palettePtr, out string pointerError))
                        return ImportOutcome.Fail(pointerError);
                    uint paletteOffset = U.toOffset(palettePtr);
                    if (paletteOffset == 0 || !CanReadRomRange(rom, paletteOffset, 32))
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
                {
                    var qr = DecreaseColorCore.Quantize(sheetRgba, 128, 112, 16);
                    if (qr == null) return ImportOutcome.Fail("Color quantization failed");
                    effectivePalette = qr.GBAPalette;
                    break;
                }
            }

            var parts = PortraitRendererCore.SplitPortraitSheet(sheetRgba, 128, 112);
            if (parts == null) return ImportOutcome.Fail("Failed to split promoted portrait sheet.");

            byte[] sheetIndexed = ImageImportCore.RemapToExistingPalette(
                parts.SpriteSheetPixels, parts.SpriteSheetW, parts.SpriteSheetH,
                effectivePalette, 16);
            if (sheetIndexed == null)
                return ImportOutcome.Fail("Failed to remap promoted face to palette.");

            if (fuchidori)
            {
                int blackIdx = ImageUtilCore.FindBlackColorIndex(effectivePalette, 1, 16);
                ImageUtilCore.Fuchidori(sheetIndexed, parts.SpriteSheetW, parts.SpriteSheetH, (byte)blackIdx);
            }

            undoService.Begin(undoLabel);
            try
            {
                byte[] sheetTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    sheetIndexed, parts.SpriteSheetW, parts.SpriteSheetH);
                if (sheetTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode sprite sheet tiles"); }

                if (!TryReadEntryP32(rom, entryAddr, OFFSET_D0_TILE_SHEET,
                    "Target slot D0 sprite sheet", out uint currentD0, out string pointerError))
                {
                    undoService.Rollback();
                    return ImportOutcome.Fail(pointerError);
                }
                uint currentD0Offset = U.toOffset(currentD0);
                bool isCompressed = U.isSafetyOffset(currentD0Offset, rom)
                    && LZ77.iscompress(rom.Data, currentD0Offset);

                uint sheetAddr;
                if (isCompressed)
                {
                    sheetAddr = ImageImportCore.WriteCompressedToROM(rom, sheetTiles, entryAddr + OFFSET_D0_TILE_SHEET);
                }
                else
                {
                    byte[] withHeader = new byte[4 + sheetTiles.Length];
                    withHeader[0] = 0x00; withHeader[1] = 0x04; withHeader[2] = 0x10; withHeader[3] = 0x00;
                    Array.Copy(sheetTiles, 0, withHeader, 4, sheetTiles.Length);
                    sheetAddr = ImageImportCore.WriteRawToROM(rom, withHeader, entryAddr + OFFSET_D0_TILE_SHEET);
                }
                if (sheetAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for sprite sheet"); }

                if (mode != PortraitPaletteMode.SharePalette)
                {
                    uint palAddr = ImageImportCore.WritePaletteToROM(rom, effectivePalette, entryAddr + OFFSET_D8_PALETTE);
                    if (palAddr == U.NOT_FOUND)
                    { undoService.Rollback(); return ImportOutcome.Fail("No free space for palette"); }
                }

                WriteEyeMouthBlockCoords(rom, entryAddr, undoService,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);

                rom.write_p32(entryAddr + OFFSET_D4_MINI_FACE, 0);
                rom.write_p32(entryAddr + OFFSET_D12_MOUTH_FRAMES, 0);

                undoService.Commit();
                return ImportOutcome.Ok();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                return ImportOutcome.Fail($"Face import failed: {ex.Message}");
            }
        }

        static ImportOutcome ImportHalfBodyHackbox(
            ROM rom,
            uint entryAddr,
            ImageImportService.LoadResult loadResult,
            UndoService undoService,
            PortraitPaletteMode mode,
            byte[] customPaletteBytes,
            bool fuchidori,
            string undoLabel,
            byte? mouthBlockX,
            byte? mouthBlockY,
            byte? eyeBlockX,
            byte? eyeBlockY)
        {
            if (rom == null) return ImportOutcome.Fail("ROM not loaded");
            if (loadResult == null || !loadResult.Success)
                return ImportOutcome.Fail(loadResult?.Error ?? "No image to import");
            if (entryAddr == 0) return ImportOutcome.Fail("No portrait entry selected");
            if (undoService == null) return ImportOutcome.Fail("Undo service not initialized");
            if (loadResult.Width != 160 || loadResult.Height != 160)
                return ImportOutcome.Fail("Halfbody Hackbox import requires a 160x160 image.");
            if (rom.RomInfo == null || rom.RomInfo.version != 8)
                return ImportOutcome.Fail("Halfbody Hackbox import requires an FE8 ROM; FE6/FE7 do not have the HALFBODY portrait-extension patch.");
            if (!IsHalfBodyPatchInstalled(rom))
                return ImportOutcome.Fail("Halfbody Hackbox import requires the FE8 HALFBODY portrait-extension patch. Install that patch before importing 160x160 portraits.");
            if (!CanAccessEntryRange(rom, entryAddr, OFFSET_D16_CLASS_CARD, 4)
                || !CanAccessEntryRange(rom, entryAddr, OFFSET_B23_EYE_BLOCK_Y, 1))
                return ImportOutcome.Fail("Target portrait entry is outside ROM bounds.");

            byte[] rgbaPixels = BuildColorKeyedRgba(loadResult);
            if (rgbaPixels == null)
                return ImportOutcome.Fail("Failed to reconstruct halfbody pixels.");

            var parts = PortraitRendererCore.SplitHalfBodyPortraitSheet(
                rgbaPixels, loadResult.Width, loadResult.Height);
            if (parts == null)
                return ImportOutcome.Fail("Failed to split Halfbody Hackbox.");

            byte[] palette64;
            byte[] sheetIndexed;
            byte[] miniIndexed;
            byte[] mouthIndexed;

            switch (mode)
            {
                case PortraitPaletteMode.SharePalette:
                {
                    if (!TryReadEntryP32(rom, entryAddr, OFFSET_D8_PALETTE,
                        "Target slot D8 palette", out uint palettePtr, out string pointerError))
                        return ImportOutcome.Fail(pointerError);
                    uint paletteOffset = U.toOffset(palettePtr);
                    if (paletteOffset == 0 || !CanReadRomRange(rom, paletteOffset, HALFBODY_PALETTE_BYTES))
                        return ImportOutcome.Fail("Target slot does not have a valid 64-byte halfbody palette at D8 — use AutoQuantize or CustomPalette.");
                    palette64 = rom.getBinaryData(paletteOffset, HALFBODY_PALETTE_BYTES);
                    if (palette64 == null || palette64.Length < HALFBODY_PALETTE_BYTES)
                        return ImportOutcome.Fail("Could not read the existing 64-byte halfbody palette at D8.");
                    if (!BuildHalfBodyIndexedWithPalette(parts, palette64, fuchidori,
                        out sheetIndexed, out miniIndexed, out mouthIndexed))
                        return ImportOutcome.Fail("Failed to remap Halfbody Hackbox to the existing palette.");
                    break;
                }
                case PortraitPaletteMode.CustomPalette:
                {
                    if (customPaletteBytes == null || customPaletteBytes.Length != HALFBODY_PALETTE_BYTES)
                        return ImportOutcome.Fail(
                            $"Invalid halfbody custom palette — expected 32 colors (64 bytes), got {customPaletteBytes?.Length ?? 0}.");
                    palette64 = customPaletteBytes;
                    if (!BuildHalfBodyIndexedWithPalette(parts, palette64, fuchidori,
                        out sheetIndexed, out miniIndexed, out mouthIndexed))
                        return ImportOutcome.Fail("Failed to remap Halfbody Hackbox to the custom palette.");
                    break;
                }
                default:
                {
                    if (!BuildHalfBodyIndexedAuto(parts, fuchidori,
                        out palette64, out sheetIndexed, out miniIndexed, out mouthIndexed))
                        return ImportOutcome.Fail("Failed to quantize Halfbody Hackbox to two 16-color palette banks.");
                    break;
                }
            }

            undoService.Begin(undoLabel);
            try
            {
                byte[] sheetTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    sheetIndexed, parts.SpriteSheetW, parts.SpriteSheetH);
                if (sheetTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode halfbody sprite sheet tiles"); }

                byte[] withHeader = new byte[4 + sheetTiles.Length];
                withHeader[0] = 0x00; withHeader[1] = 0x04; withHeader[2] = 0x20; withHeader[3] = 0x00;
                Array.Copy(sheetTiles, 0, withHeader, 4, sheetTiles.Length);
                uint sheetAddr = ImageImportCore.WriteRawToROM(rom, withHeader, entryAddr + OFFSET_D0_TILE_SHEET);
                if (sheetAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for halfbody sprite sheet"); }

                byte[] miniTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    miniIndexed, parts.MiniW, parts.MiniH);
                if (miniTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode mini face tiles"); }
                uint miniAddr = ImageImportCore.WriteCompressedToROM(rom, miniTiles, entryAddr + OFFSET_D4_MINI_FACE);
                if (miniAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for mini face"); }

                if (mode != PortraitPaletteMode.SharePalette)
                {
                    uint palAddr = ImageImportCore.WritePaletteToROM(rom, palette64, entryAddr + OFFSET_D8_PALETTE);
                    if (palAddr == U.NOT_FOUND)
                    { undoService.Rollback(); return ImportOutcome.Fail("No free space for halfbody palette"); }
                }

                byte[] mouthTiles = ImageImportCore.EncodeDirectTiles4bpp(
                    mouthIndexed, parts.MouthW, parts.MouthH);
                if (mouthTiles == null)
                { undoService.Rollback(); return ImportOutcome.Fail("Failed to encode mouth tiles"); }
                uint mouthAddr = ImageImportCore.WriteRawToROM(rom, mouthTiles, entryAddr + OFFSET_D12_MOUTH_FRAMES);
                if (mouthAddr == U.NOT_FOUND)
                { undoService.Rollback(); return ImportOutcome.Fail("No free space for mouth data"); }

                rom.write_p32(entryAddr + OFFSET_D16_CLASS_CARD, 0);
                WriteEyeMouthBlockCoords(rom, entryAddr, undoService,
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);

                undoService.Commit();
                return ImportOutcome.Ok();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                return ImportOutcome.Fail($"Halfbody import failed: {ex.Message}");
            }
        }

        static bool BuildHalfBodyIndexedAuto(
            HalfBodyPortraitSheetParts parts,
            bool fuchidori,
            out byte[] palette64,
            out byte[] sheetIndexed,
            out byte[] miniIndexed,
            out byte[] mouthIndexed)
        {
            palette64 = null;
            sheetIndexed = null;
            miniIndexed = null;
            mouthIndexed = null;
            if (parts == null) return false;

            byte[] topRgba = CopyRgbaRect(parts.SpriteSheetPixels, parts.SpriteSheetW, 0, 0, parts.SpriteSheetW, 32);
            byte[] bottomRgba = CopyRgbaRect(parts.SpriteSheetPixels, parts.SpriteSheetW, 0, 32, parts.SpriteSheetW, 32);
            var topQr = DecreaseColorCore.Quantize(topRgba, parts.SpriteSheetW, 32, 16);
            var bottomQr = DecreaseColorCore.Quantize(bottomRgba, parts.SpriteSheetW, 32, 16);
            if (topQr == null || bottomQr == null) return false;

            byte[] bank0 = PadPaletteBank(topQr.GBAPalette);
            byte[] bank1 = PadPaletteBank(bottomQr.GBAPalette);
            palette64 = new byte[HALFBODY_PALETTE_BYTES];
            Array.Copy(bank0, 0, palette64, 0, 32);
            Array.Copy(bank1, 0, palette64, 32, 32);

            byte[] topIndexed = topQr.IndexData;
            byte[] bottomIndexed = bottomQr.IndexData;
            miniIndexed = ImageImportCore.RemapToExistingPalette(parts.MiniPixels, parts.MiniW, parts.MiniH, bank0, 16);
            mouthIndexed = ImageImportCore.RemapToExistingPalette(parts.MouthPixels, parts.MouthW, parts.MouthH, bank0, 16);
            if (topIndexed == null || bottomIndexed == null || miniIndexed == null || mouthIndexed == null) return false;

            ApplyHalfBodyFuchidoriIfNeeded(fuchidori, bank0, bank1, topIndexed, bottomIndexed,
                parts.SpriteSheetW, miniIndexed, parts.MiniW, parts.MiniH, mouthIndexed, parts.MouthW, parts.MouthH);

            sheetIndexed = CombineHalfBodySheetIndices(topIndexed, bottomIndexed, parts.SpriteSheetW);
            return sheetIndexed != null;
        }

        static bool BuildHalfBodyIndexedWithPalette(
            HalfBodyPortraitSheetParts parts,
            byte[] palette64,
            bool fuchidori,
            out byte[] sheetIndexed,
            out byte[] miniIndexed,
            out byte[] mouthIndexed)
        {
            sheetIndexed = null;
            miniIndexed = null;
            mouthIndexed = null;
            if (parts == null || palette64 == null || palette64.Length < HALFBODY_PALETTE_BYTES) return false;

            byte[] bank0 = new byte[32];
            byte[] bank1 = new byte[32];
            Array.Copy(palette64, 0, bank0, 0, 32);
            Array.Copy(palette64, 32, bank1, 0, 32);

            byte[] topRgba = CopyRgbaRect(parts.SpriteSheetPixels, parts.SpriteSheetW, 0, 0, parts.SpriteSheetW, 32);
            byte[] bottomRgba = CopyRgbaRect(parts.SpriteSheetPixels, parts.SpriteSheetW, 0, 32, parts.SpriteSheetW, 32);
            byte[] topIndexed = ImageImportCore.RemapToExistingPalette(topRgba, parts.SpriteSheetW, 32, bank0, 16);
            byte[] bottomIndexed = ImageImportCore.RemapToExistingPalette(bottomRgba, parts.SpriteSheetW, 32, bank1, 16);
            miniIndexed = ImageImportCore.RemapToExistingPalette(parts.MiniPixels, parts.MiniW, parts.MiniH, bank0, 16);
            mouthIndexed = ImageImportCore.RemapToExistingPalette(parts.MouthPixels, parts.MouthW, parts.MouthH, bank0, 16);
            if (topIndexed == null || bottomIndexed == null || miniIndexed == null || mouthIndexed == null) return false;

            ApplyHalfBodyFuchidoriIfNeeded(fuchidori, bank0, bank1, topIndexed, bottomIndexed,
                parts.SpriteSheetW, miniIndexed, parts.MiniW, parts.MiniH, mouthIndexed, parts.MouthW, parts.MouthH);

            sheetIndexed = CombineHalfBodySheetIndices(topIndexed, bottomIndexed, parts.SpriteSheetW);
            return sheetIndexed != null;
        }

        static void ApplyHalfBodyFuchidoriIfNeeded(
            bool fuchidori,
            byte[] bank0,
            byte[] bank1,
            byte[] topIndexed,
            byte[] bottomIndexed,
            int sheetW,
            byte[] miniIndexed,
            int miniW,
            int miniH,
            byte[] mouthIndexed,
            int mouthW,
            int mouthH)
        {
            if (!fuchidori) return;
            byte black0 = (byte)ImageUtilCore.FindBlackColorIndex(bank0, 1, 16);
            byte black1 = (byte)ImageUtilCore.FindBlackColorIndex(bank1, 1, 16);
            ImageUtilCore.Fuchidori(topIndexed, sheetW, 32, black0);
            ImageUtilCore.Fuchidori(bottomIndexed, sheetW, 32, black1);
            ImageUtilCore.Fuchidori(miniIndexed, miniW, miniH, black0);
            ImageUtilCore.Fuchidori(mouthIndexed, mouthW, mouthH, black0);
        }

        static byte[] PadPaletteBank(byte[] palette)
        {
            byte[] bank = new byte[32];
            if (palette != null)
                Array.Copy(palette, 0, bank, 0, Math.Min(palette.Length, bank.Length));
            return bank;
        }

        static byte[] CopyRgbaRect(byte[] src, int srcW, int srcX, int srcY, int w, int h)
        {
            if (src == null || srcW <= 0 || w <= 0 || h <= 0) return null;
            byte[] dst = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int srcOffset = ((srcY + y) * srcW + srcX) * 4;
                int dstOffset = y * w * 4;
                if (srcOffset < 0 || srcOffset + w * 4 > src.Length) return null;
                Array.Copy(src, srcOffset, dst, dstOffset, w * 4);
            }
            return dst;
        }

        static byte[] CombineHalfBodySheetIndices(byte[] topIndexed, byte[] bottomIndexed, int width)
        {
            if (topIndexed == null || bottomIndexed == null || width <= 0) return null;
            int halfPixels = width * 32;
            if (topIndexed.Length < halfPixels || bottomIndexed.Length < halfPixels) return null;
            byte[] combined = new byte[halfPixels * 2];
            Array.Copy(topIndexed, 0, combined, 0, halfPixels);
            Array.Copy(bottomIndexed, 0, combined, halfPixels, halfPixels);
            return combined;
        }

        /// <summary>
        /// #663 Slice A: write any non-null mouth/eye block coords to bytes
        /// B20-B23 of the portrait entry. Called inside an open
        /// <see cref="UndoService"/> scope so the writes participate in the
        /// same rollback as the D0/D8/D12 writes above. Skipped silently on
        /// FE6 (16-byte entry layout reuses those bytes for unrelated fields).
        /// </summary>
        static void WriteEyeMouthBlockCoords(
            ROM rom, uint entryAddr, UndoService undoService,
            byte? mouthBlockX, byte? mouthBlockY,
            byte? eyeBlockX, byte? eyeBlockY)
        {
            if (rom == null) return;
            if (!IsFe7Or8EntryLayout(rom)) return;
            if (mouthBlockX == null && mouthBlockY == null
                && eyeBlockX == null && eyeBlockY == null) return;

            // ROM.BeginUndoScope (entered by UndoService.Begin) routes
            // rom.write_u8 through the active undo buffer, so a single
            // rollback after these writes also reverts them.
            if (mouthBlockX.HasValue) rom.write_u8(entryAddr + OFFSET_B20_MOUTH_BLOCK_X, mouthBlockX.Value);
            if (mouthBlockY.HasValue) rom.write_u8(entryAddr + OFFSET_B21_MOUTH_BLOCK_Y, mouthBlockY.Value);
            if (eyeBlockX.HasValue)   rom.write_u8(entryAddr + OFFSET_B22_EYE_BLOCK_X,   eyeBlockX.Value);
            if (eyeBlockY.HasValue)   rom.write_u8(entryAddr + OFFSET_B23_EYE_BLOCK_Y,   eyeBlockY.Value);
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

        public static byte[] BuildColorKeyedRgba(ImageImportService.LoadResult loadResult)
        {
            byte[] rgba = GetSourceRgba(loadResult);
            if (rgba == null) return null;
            ApplyPortraitBackgroundColorKey(rgba, loadResult.Width, loadResult.Height);
            return rgba;
        }

        static byte[] GetSourceRgba(ImageImportService.LoadResult loadResult)
        {
            if (loadResult == null || loadResult.Width <= 0 || loadResult.Height <= 0) return null;
            int bytes = loadResult.Width * loadResult.Height * 4;
            if (loadResult.RGBAPixels != null && loadResult.RGBAPixels.Length >= bytes)
            {
                byte[] copy = new byte[bytes];
                Array.Copy(loadResult.RGBAPixels, copy, bytes);
                return copy;
            }
            return ReconstructRgbaIndexAgnostic(loadResult);
        }

        static byte[] ReconstructRgbaIndexAgnostic(ImageImportService.LoadResult loadResult)
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
                    rgba[i * 4 + 3] = 255;
                }
            }
            return rgba;
        }

        public static bool ApplyPortraitBackgroundColorKey(byte[] rgba, int w, int h)
            => PortraitRendererCore.ApplyPortraitBackgroundColorKey(rgba, w, h);

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

            if (dataSize < 4) return 0;
            if (!TryReadP32(rom, pointer, "Portrait table", out uint baseAddr, out _)) return 0;
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            int count = 0;
            int nullCount = 0;
            // 512 mirrors the soft cap in LoadList. Hard ROMs never approach
            // it; the sentinel terminates the scan first.
            for (int i = 0; i < 512; i++)
            {
                ulong addr64 = (ulong)baseAddr + (ulong)i * (ulong)dataSize;
                if (addr64 + (ulong)dataSize > (ulong)rom.Data.Length) break;
                uint addr = (uint)addr64;
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
            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0)
            {
                lines.Add("Invalid portrait_datasize.");
                return new FolderImportResult(0, 0, 0, 0, lines);
            }
            if (!TryReadP32(rom, rom.RomInfo.portrait_pointer,
                "Portrait table", out uint portraitBase, out string pointerError))
            {
                lines.Add(pointerError);
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
                ulong addrLong = (ulong)portraitBase + (ulong)(uint)slotId * (ulong)dataSize;
                if (addrLong + (ulong)dataSize > (ulong)rom.Data.Length)
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
                // committed. Use the same shared dispatch as the editor and
                // wizard so 96x80 faces and 128x112 sheets share validation,
                // color-keying, and reverse assembly.
                ImportOutcome outcome = ImportPortrait(rom, entryAddr, loadResult, undo,
                    undoLabel: $"Import Portrait Image (Batch slot 0x{slotId:X2})");

                if (outcome.Success)
                {
                    imported++;
                    // Record the source file so the per-slot Open/Select
                    // Source buttons light up after a batch import.
                    RecordSourceFile(slotId, filePath);
                    string mode = loadResult.Width == 128 && loadResult.Height == 112
                        ? " (sheet)"
                        : loadResult.Width == 160 && loadResult.Height == 160
                            ? " (halfbody)"
                            : loadResult.Width == 96 && loadResult.Height == 80
                                ? " (face)"
                                : "";
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
        /// Build the shared, color-keyed + auto-quantized preview
        /// <see cref="ImageImportService.LoadResult"/> used by BOTH the
        /// Step-2 source preview (<see cref="BuildPreviewImage"/>) and the
        /// wizard's per-frame live preview (<c>RefreshFramePreview</c> in
        /// ImagePortraitImporterView, via <see cref="PortraitImportPreviewCore.RenderFramePreview"/>).
        /// (#1980) The frame preview previously read the raw, un-keyed
        /// <c>LoadedImage.IndexedPixels</c>/<c>GBAPalette</c> directly, so an
        /// opaque non-zero background never got mapped to transparent index
        /// 0 the way the Step-2 preview does — this method is the single
        /// place both call sites now share so they render from identical
        /// prepared buffers.
        ///
        /// Never mutates <paramref name="loadResult"/>'s buffers: <see
        /// cref="BuildColorKeyedRgba"/> works on a private RGBA copy and
        /// <see cref="DecreaseColorCore.Quantize"/> returns freshly allocated
        /// index/palette arrays. Returns null on any unusable input (null
        /// result, failed load, missing <see cref="CoreState.ImageService"/>,
        /// or a quantize failure) so callers can safely clear their cache.
        /// </summary>
        public static ImageImportService.LoadResult BuildPreparedPreviewLoadResult(
            ImageImportService.LoadResult loadResult)
        {
            if (loadResult == null || !loadResult.Success) return null;

            byte[] keyed = BuildColorKeyedRgba(loadResult);
            if (keyed == null) return null;

            var qr = DecreaseColorCore.Quantize(keyed, loadResult.Width, loadResult.Height, 16);
            if (qr == null || qr.IndexData == null || qr.GBAPalette == null) return null;

            return new ImageImportService.LoadResult
            {
                Success = true,
                Width = loadResult.Width,
                Height = loadResult.Height,
                IndexedPixels = qr.IndexData,
                GBAPalette = qr.GBAPalette,
            };
        }

        /// <summary>
        /// Convenience: build an <see cref="IImage"/> preview from an
        /// already-prepared (color-keyed + quantized) load-result — see
        /// <see cref="BuildPreparedPreviewLoadResult"/>. Ready to drop into a
        /// <c>GbaImageControl</c>. Returns null on failure.
        /// </summary>
        public static IImage BuildPreviewImageFromPrepared(ImageImportService.LoadResult preparedLoadResult)
        {
            if (preparedLoadResult == null || !preparedLoadResult.Success) return null;
            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;

            byte[] rgba = ReconstructRgbaWithPaletteZeroTransparent(preparedLoadResult);
            if (rgba == null) return null;

            IImage img = svc.CreateImage(preparedLoadResult.Width, preparedLoadResult.Height);
            img.SetPixelData(rgba);
            return img;
        }

        /// <summary>
        /// Convenience: build an <see cref="IImage"/> preview from a raw
        /// (un-prepared) load-result, ready to drop into a
        /// <c>GbaImageControl</c>. Returns null on failure. Delegates to
        /// <see cref="BuildPreparedPreviewLoadResult"/> +
        /// <see cref="BuildPreviewImageFromPrepared"/> so this and the
        /// wizard's cached per-load preview share one preparation pipeline
        /// (#1980).
        /// </summary>
        public static IImage BuildPreviewImage(ImageImportService.LoadResult loadResult)
            => BuildPreviewImageFromPrepared(BuildPreparedPreviewLoadResult(loadResult));
    }
}
