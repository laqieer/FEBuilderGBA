// SPDX-License-Identifier: GPL-3.0-or-later
// #898 — shared SkillConfig skill-icon Image Import/Export helper.
//
// Wires the WinForms `SkillConfigSkillSystemForm.ImportButton_Click` /
// `ExportButton_Click` parity (and the byte-identical FE8N v2/v3 +
// CSkillSys 0.9.x variants) into a single cross-view helper for the
// Avalonia editors.
//
// Import contract (mirrors WinForms exactly):
//   * Require a 16x16 source image (rejected otherwise).
//   * Remap pixels to the skill palette already in ROM (import never
//     writes the palette — same as WF, which only checks/forces it).
//   * Encode 16x16 -> 128 RAW 4bpp bytes via
//     ImageImportCore.EncodeDirectTiles4bpp. That encoder is
//     byte-for-byte identical to WinForms ImageUtil.ImageToByte16Tile
//     (same tile order, same nibble packing) — verified for #898.
//   * Write the 128 bytes IN-PLACE at the icon byte-address (NO LZ77,
//     NO pointer relocation) under ONE UndoService scope.
//   * On any failure after a partial mutation, roll the scope back.
//
// Export contract: render the 128-byte icon through the SAME 4bpp tile
// decode path the view uses to show the icon (Decode4bppTiles with the
// skill palette) and save it as PNG. Read-only — never touches ROM.
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared Image Import/Export helper for the SkillConfig editors whose
    /// skill icon is a fixed 16x16 4bpp (128-byte) block written RAW at a
    /// known ROM byte-address. Used by SkillConfigSkillSystemView,
    /// SkillConfigFE8UCSkillSys09xView, SkillConfigFE8NVer2SkillView and
    /// SkillConfigFE8NVer3SkillView (all four share the same 128-byte raw
    /// 4bpp icon storage). FE8N v1 is intentionally NOT a caller — its WF
    /// form has no writable icon I/O (render-only) and its icon address
    /// derivation lacks the 0x100 page offset, so wiring it would write to
    /// the wrong slot.
    /// </summary>
    public static class SkillConfigIconIoHelper
    {
        /// <summary>16x16 4bpp icon = 128 raw bytes.</summary>
        public const int IconWidth = 16;
        public const int IconHeight = 16;
        public const uint IconByteSize = 128;

        /// <summary>
        /// Import a 16x16 PNG/BMP as the skill icon, writing 128 raw 4bpp
        /// bytes in-place at <paramref name="iconByteAddr"/>.
        ///
        /// Return contract (so callers can tell cancel from success):
        ///   <list type="bullet">
        ///     <item><c>null</c> — the user cancelled the file dialog. Callers
        ///       must NOT refresh the icon/list (nothing was written).</item>
        ///     <item><c>""</c> — success. The 128 bytes were written; callers
        ///       should refresh the icon preview + list.</item>
        ///     <item>non-empty — a localized error message (nothing written, or
        ///       the partial write was rolled back).</item>
        ///   </list>
        /// On any failure that occurs after a partial ROM mutation, the undo
        /// scope is rolled back so no half-written icon is left.
        /// </summary>
        /// <param name="owner">Window to parent the file dialog.</param>
        /// <param name="rom">Target ROM (must be the live CoreState.ROM).</param>
        /// <param name="iconByteAddr">ROM byte-offset of the 128-byte icon block.</param>
        /// <param name="paletteAddr">ROM byte-offset of the 16-color skill palette.</param>
        /// <param name="undo">Active editor undo service (the helper opens its own scope).</param>
        public static async Task<string?> ImportIconAsync(
            Window owner, ROM rom, uint iconByteAddr, uint paletteAddr, UndoService undo)
        {
            if (owner == null)
                return R._("Internal error: missing ROM or editor context.");

            // File dialog → then the SHARED path-taking core. The FE-Repo
            // button (#1397) calls ImportIconFromPath directly with the chosen
            // FE-Repo file, so there is exactly ONE import body.
            string filePath = await FileDialogHelper.OpenImageFile(owner);
            if (string.IsNullOrEmpty(filePath))
                return null; // user cancelled — distinct from success ("").

            return ImportIconFromPath(rom, iconByteAddr, paletteAddr, undo, filePath);
        }

        /// <summary>
        /// #1397 — path-taking core shared by the file-dialog import
        /// (<see cref="ImportIconAsync"/>) and the FE-Repo button. Same
        /// contract: <c>null</c> only for a null/empty path (treated as
        /// cancel), <c>""</c> on success, non-empty = localized error.
        /// 16x16 strict size + remap-to-existing-16-color-palette — a 17+-color
        /// source sheet is REDUCED onto the ROM palette, never silently
        /// corrupted.
        /// </summary>
        public static string? ImportIconFromPath(
            ROM rom, uint iconByteAddr, uint paletteAddr, UndoService undo, string filePath)
        {
            if (rom == null || rom.Data == null || undo == null)
                return R._("Internal error: missing ROM or editor context.");
            if (string.IsNullOrEmpty(filePath))
                return null; // nothing chosen — treat as cancel.

            // Read the existing skill palette so the user image is remapped to
            // the in-ROM colors (import never overwrites the palette — WF parity).
            byte[] palette = ImageUtilCore.GetPalette(paletteAddr, 16);
            if (palette == null || palette.Length < 32)
                return R._("Could not read the skill palette; aborting import.");

            // Load + strict 16x16 size enforcement + remap.
            var loadResult = ImageImportService.LoadAndRemapFromFile(
                filePath, IconWidth, IconHeight, palette, 16, strictSize: true);
            if (loadResult == null || !loadResult.Success)
            {
                return loadResult?.Error
                    ?? R._("Image must be 16x16 pixels.");
            }

            // Encode to 128 raw 4bpp bytes. EncodeDirectTiles4bpp is
            // byte-for-byte identical to WinForms ImageUtil.ImageToByte16Tile.
            byte[] image = ImageImportCore.EncodeDirectTiles4bpp(
                loadResult.IndexedPixels, IconWidth, IconHeight);
            if (image == null || image.Length != (int)IconByteSize)
                return R._("Failed to encode the icon tile data.");

            // ROM-identity guard: never write through an address derived from a
            // ROM instance that has since been swapped out (e.g. a reload while
            // the dialog was open). Mirrors the #871/#874 guard pattern.
            if (!ReferenceEquals(rom, CoreState.ROM))
                return R._("ROM changed during import; aborting.");

            // Region-safety guard: the destination 128-byte block must be fully
            // inside ROM and at a safe offset.
            if (iconByteAddr == 0
                || !U.isSafetyOffset(iconByteAddr, rom)
                || iconByteAddr + IconByteSize > (uint)rom.Data.Length)
            {
                return R._("Icon address is out of range; aborting import.");
            }

            // One undo scope: BeginUndoScope makes the plain write_range
            // overload ambient-undo tracked, so Rollback() restores the
            // original 128 bytes if anything below throws.
            undo.Begin("Import Skill Icon");
            try
            {
                rom.write_range(iconByteAddr, image);
            }
            catch (Exception ex)
            {
                undo.Rollback();
                Log.ErrorF("SkillConfigIconIoHelper.ImportIconAsync write failed: {0}", ex.Message);
                return R._("Failed to write the icon to ROM; rolled back.");
            }

            undo.Commit();
            return "";
        }

        /// <summary>
        /// Export the 128-byte skill icon at <paramref name="iconByteAddr"/>
        /// as a PNG. Renders through the SAME 4bpp tile decode + skill palette
        /// path the view uses to show the icon. Read-only — never writes ROM.
        /// </summary>
        public static async Task ExportIconAsync(
            Window owner, ROM rom, uint iconByteAddr, uint paletteAddr)
        {
            if (owner == null || rom == null || rom.Data == null) return;

            if (iconByteAddr == 0
                || !U.isSafetyOffset(iconByteAddr, rom)
                || iconByteAddr + IconByteSize > (uint)rom.Data.Length)
            {
                Log.Notify("SkillConfigIconIoHelper.ExportIconAsync: icon address out of range; cannot export.");
                return;
            }

            byte[] palette = ImageUtilCore.GetPalette(paletteAddr, 16);
            if (palette == null || palette.Length < 32)
            {
                Log.Notify("SkillConfigIconIoHelper.ExportIconAsync: could not read skill palette; cannot export.");
                return;
            }

            var imgService = CoreState.ImageService;
            if (imgService == null)
            {
                Log.Notify("SkillConfigIconIoHelper.ExportIconAsync: image service not initialized.");
                return;
            }

            try
            {
                using var img = imgService.Decode4bppTiles(
                    rom.Data, (int)iconByteAddr, IconWidth, IconHeight, palette);
                if (img == null)
                {
                    Log.Notify("SkillConfigIconIoHelper.ExportIconAsync: failed to render the icon.");
                    return;
                }
                // #1639: write via the SAF bridge so Android content:// targets work.
                await FileDialogHelper.SaveImageFileVia(owner, "skill_icon.png", p => img.Save(p));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigIconIoHelper.ExportIconAsync save failed: {0}", ex.Message);
            }
        }
    }
}
