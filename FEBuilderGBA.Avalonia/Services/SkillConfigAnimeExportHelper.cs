// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using global::Avalonia.Platform.Storage;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared helper that wires the SkillConfig views' "Animation Export"
    /// button to the cross-platform <see cref="FEBuilderGBA.SkillSystemsAnimeExportCore"/>
    /// seam (#910). EXPORT ONLY — import stays WinForms-side (separate PR).
    ///
    /// Four SkillConfig views call this (SkillSystem, FE8N Ver2, FE8N Ver3,
    /// FE8U-C SkillSys 0.9x). FE8N Ver1 has no animation pointer and is NOT
    /// wired.
    ///
    /// Output (mirrors WF <c>ImageUtilSkillSystemsAnimeCreator.Export</c>):
    ///   * a .txt script (optional D / S{sound} header + "{wait} g{id}.png")
    ///   * one PNG per unique OBJ id beside the script
    ///   * if the user picks a .gif, an animated GIF instead (GIF delay uses
    ///     <c>U.GameFrameSecToGifFrameSec</c>, matching every other Avalonia
    ///     animation export).
    /// </summary>
    public static class SkillConfigAnimeExportHelper
    {
        /// <summary>
        /// Run the full export flow for the given anime pointer. No-op (returns
        /// silently) when the pointer is 0. Shows errors / success via
        /// <c>CoreState.Services</c>.
        /// </summary>
        public static async Task ExportAsync(Window owner, uint animationPointer, uint selectedId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return;
            if (animationPointer == 0)
            {
                CoreState.Services?.ShowError(R._("No valid animation selected to export."));
                return;
            }

            string suggested = $"SkillAnime_{selectedId:X02}.txt";
            // #1639: pick the handle so we can branch by format — the single-file
            // .gif export routes through the SAF bridge, while the .txt script
            // (which writes sibling PNGs) requires a real local path.
            var file = await FileDialogHelper.SaveFilePick(owner,
                R._("Save Skill Animation"),
                new[]
                {
                    (R._("Skill Animation Script"), "*.txt"),
                    (R._("Animated GIF"), "*.gif"),
                },
                suggested);
            if (file == null) return;
            bool isGif = (file.Name ?? "").EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            string? localPath = file.TryGetLocalPath();
            if (!isGif && string.IsNullOrEmpty(localPath))
            {
                CoreState.Services?.ShowError(R._("Exporting an animation script writes sibling PNG frames and requires desktop file-system access; export as GIF instead, or use a desktop device."));
                return;
            }

            try
            {
                var result = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animationPointer);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    CoreState.Services?.ShowError(R._("Export failed: {0}", result.Error));
                    return;
                }
                if (result.Frames.Count == 0)
                {
                    CoreState.Services?.ShowError(R._("No animation frames to export."));
                    return;
                }

                string? written;
                if (isGif)
                {
                    // Single-file GIF → SAF bridge.
                    written = await FileDialogHelper.WriteViaAsync(file, p => ExportGif(result, p));
                }
                else
                {
                    ExportScript(result, localPath);
                    written = localPath;
                }
                if (written == null) return;
                CoreState.Services?.ShowInfo(R._("Exported to: {0}", written));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        static void ExportScript(SkillAnimeExportResult result, string path)
        {
            string basename = Path.GetFileNameWithoutExtension(path) + "_";
            string basedir = Path.GetDirectoryName(path) ?? ".";

            string songName = null;
            // NameResolver may surface the song name for the comment; ignore
            // failures (the comment is purely cosmetic).
            try
            {
                if (result.SoundId > 0)
                    songName = NameResolver.GetSongName(result.SoundId);
            }
            catch { /* cosmetic only */ }

            var lines = SkillSystemsAnimeExportCore.BuildScriptLines(result, basename, songName);

            // The save dialog may return a path in a folder the user just typed
            // that doesn't exist yet — create it up-front so Save/WriteAllLines
            // don't throw (GetFullPath guards null/relative edge cases).
            string fullDir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(fullDir))
                Directory.CreateDirectory(fullDir);

            // Write one PNG per unique id (matches WF animeHash dedup). The Core
            // export returns IImage (IDisposable); dispose each unique image
            // right after saving it to avoid leaking on repeated exports.
            var written = new HashSet<uint>();
            foreach (var f in result.Frames)
            {
                if (!written.Add(f.Id)) continue;
                string imagefilename = basename.Replace(" ", "_") + "g" + f.Id.ToString("000") + ".png";
                f.Image.Save(Path.Combine(basedir, imagefilename));
                f.Image.Dispose();
            }

            File.WriteAllLines(path, lines);
        }

        static void ExportGif(SkillAnimeExportResult result, string path)
        {
            // Ensure the (possibly newly-typed) output folder exists before we
            // encode, so GifEncoderCore.Encode doesn't throw on a missing dir.
            string fullDir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(fullDir))
                Directory.CreateDirectory(fullDir);

            var gifFrames = new List<GifEncoderCore.GifFrame>();
            // The Core export caches one IImage per OBJ id, so duplicate frames
            // share the same instance — dispose each UNIQUE image exactly once
            // (by reference) after its RGBA buffer has been read.
            var seen = new HashSet<IImage>();
            foreach (var f in result.Frames)
            {
                IImage img = f.Image;
                byte[] rgba;
                if (img.IsIndexed)
                {
                    byte[] indexed = img.GetPixelData();
                    byte[] palette = img.GetPaletteRGBA();
                    rgba = GifEncoderCore.IndexedToRgba(indexed, palette, img.Width, img.Height);
                }
                else
                {
                    rgba = img.GetPixelData();
                }

                gifFrames.Add(new GifEncoderCore.GifFrame
                {
                    Width = img.Width,
                    Height = img.Height,
                    RgbaPixels = rgba,
                    // GBA 60-fps wait → GIF centiseconds (matches all other
                    // Avalonia animation exports).
                    DelayCs = U.GameFrameSecToGifFrameSec(f.Wait),
                });
            }

            GifEncoderCore.Encode(gifFrames, path);

            // Dispose each unique image after encoding (RGBA already copied).
            foreach (var f in result.Frames)
            {
                if (seen.Add(f.Image))
                    f.Image.Dispose();
            }
        }
    }
}
