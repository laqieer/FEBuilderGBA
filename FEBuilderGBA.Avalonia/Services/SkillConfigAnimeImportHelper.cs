// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared helper that wires the SkillConfig views' "Animation Import" button
    /// to the cross-platform <see cref="FEBuilderGBA.SkillSystemsAnimeImportCore"/>
    /// seam (SLICE 1 #916 FE8J + SLICE 2 #917 FE8U program-code re-emit). Both
    /// FE8J and FE8U import; FE8U prepends the per-skill program template. The
    /// Core surfaces any failure (e.g. a missing FE8U .dmp) as an error string
    /// with ZERO mutation — this helper just relays it.
    ///
    /// Four SkillConfig views call this (SkillSystem, FE8N Ver2, FE8N Ver3,
    /// FE8U-C SkillSys 0.9x). FE8N Ver1 has no animation pointer and is NOT
    /// wired (its Import button stays a stub).
    ///
    /// Flow (mirrors WF <c>ImageUtilSkillSystemsAnimeCreator.Import</c>):
    ///   * require a non-zero animation pointer slot,
    ///   * pick a .txt script,
    ///   * read its lines + resolve per-frame PNGs against the script directory
    ///     (loaded + quantized to 16 colours via <see cref="ImageImportService"/>),
    ///   * run the Core import under ONE UndoService scope,
    ///   * on failure: rollback + ShowError; on success: refresh + reload.
    /// </summary>
    public static class SkillConfigAnimeImportHelper
    {
        /// <summary>
        /// Run the full import flow. Returns <c>true</c> on a successful write
        /// (so the caller refreshes its preview + list); <c>false</c> when the
        /// user cancelled, the pointer was invalid, or the import failed (the
        /// error has already been surfaced via <c>CoreState.Services</c>).
        /// </summary>
        public static async Task<bool> ImportAsync(
            Window owner, uint animationPointer, UndoService undoService)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return false;

            if (animationPointer == 0)
            {
                CoreState.Services?.ShowError(R._("No valid animation pointer to import into."));
                return false;
            }

            // #1639: this import reads frame PNGs from the script's OWN directory
            // (Path.GetDirectoryName below). A one-file SAF temp copy would break
            // the sibling resolution, so require a real local path — a SAF pick
            // (no local path) shows a clear Android message instead of silently
            // importing from the wrong directory.
            string? path = await FileDialogHelper.OpenFile(owner,
                R._("Import Skill Animation Script"), "*.txt", requireLocalPath: true);
            if (string.IsNullOrEmpty(path))
            {
                // On Android a null here means the SAF pick had no local path
                // (sibling resolution impossible); on desktop it means the user
                // cancelled. Only surface the explanatory message on Android.
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Importing an animation script reads sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                return false;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Cannot read script file: {0}", ex.Message));
                return false;
            }

            string scriptDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

            // Snapshot ROM for a defensive rollback (the Core import also
            // snapshots, but mirroring the CSA import keeps the two-layer net).
            byte[] snapshot = (byte[])rom.Data.Clone();

            undoService.Begin("Import Skill Animation (#913)");
            string importErr;
            try
            {
                importErr = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                    rom, lines, animationPointer,
                    pngName => LoadFrameImage(pngName, scriptDir));
            }
            catch (Exception ex)
            {
                importErr = ex.Message;
            }

            if (!string.IsNullOrEmpty(importErr))
            {
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                undoService.Rollback();
                CoreState.Services?.ShowError(R._("Skill animation import failed: {0}", importErr));
                return false;
            }

            undoService.Commit();
            CoreState.Services?.ShowInfo(R._("Skill animation imported successfully."));
            return true;
        }

        /// <summary>
        /// Load a frame PNG referenced by the script: resolve relative names
        /// against the script directory, load + quantize to 16 colours, return
        /// indexed pixels + dimensions + GBA palette. Returns null on failure.
        /// </summary>
        static (byte[] indexedPixels, int width, int height, byte[] gbaPalette)? LoadFrameImage(
            string pngName, string scriptDir)
        {
            string fullPath = Path.IsPathRooted(pngName)
                ? pngName
                : Path.Combine(scriptDir, pngName);
            try
            {
                var lr = ImageImportService.LoadAndQuantizeFromFile(
                    fullPath, 0, 0, maxColors: 16, strictSize: false,
                    requireTileMultiple: false);
                if (lr == null || !lr.Success || lr.IndexedPixels == null)
                    return null;
                return (lr.IndexedPixels, lr.Width, lr.Height, lr.GBAPalette);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigAnimeImportHelper.LoadFrameImage: {0}", ex.Message);
                return null;
            }
        }
    }
}
