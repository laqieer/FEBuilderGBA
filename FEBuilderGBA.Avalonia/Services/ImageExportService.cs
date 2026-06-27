using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared service for exporting Avalonia images (Bitmap / IImage) as PNG files.
    /// Can be used standalone or as a complement to GbaImageControl.ExportPng().
    /// </summary>
    public static class ImageExportService
    {
        /// <summary>
        /// Prompts the user with a save dialog and writes the image as PNG.
        /// </summary>
        /// <param name="image">The Avalonia image to export (must be a Bitmap).</param>
        /// <param name="owner">The owning window for the dialog.</param>
        /// <param name="suggestedName">Default filename suggestion.</param>
        public static async Task SavePng(IImage? image, Window owner, string suggestedName = "export.png")
        {
            if (image == null)
            {
                await MessageBoxWindow.Show(owner, "No image to export.", "Export", MessageBoxMode.Ok);
                return;
            }

            if (image is not Bitmap bmp)
            {
                // Resolve a picker first so we don't claim "unsupported" only
                // after the user picked; but the type check is cheap, so do it now.
                await MessageBoxWindow.Show(owner, "Cannot export this image type. Only Bitmap images are supported.", "Export", MessageBoxMode.Ok);
                return;
            }

            // #1639: pick the IStorageFile handle and write via the SAF bridge so
            // Android content:// targets (no local path) are written through
            // OpenWriteAsync. The overwrite prompt only applies to a real local
            // file (a freshly-picked SAF document is created by the picker).
            string? written = await FileDialogHelper.SaveImageFileVia(owner, suggestedName, async path =>
            {
                await using var stream = File.Create(path);
                bmp.Save(stream);
            });
            if (written == null) return;
            await MessageBoxWindow.Show(owner, $"Image saved to {Path.GetFileName(written)}", "Export", MessageBoxMode.Ok);
        }

        /// <summary>
        /// Exports a Bitmap directly to a file path without a dialog (for automated/batch use).
        /// Returns true on success.
        /// </summary>
        public static bool SavePngToFile(Bitmap? bitmap, string filePath)
        {
            if (bitmap == null || string.IsNullOrEmpty(filePath)) return false;

            try
            {
                using var stream = File.Create(filePath);
                bitmap.Save(stream);
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageExportService.SavePngToFile failed: {0}", ex.Message);
                return false;
            }
        }
    }
}
