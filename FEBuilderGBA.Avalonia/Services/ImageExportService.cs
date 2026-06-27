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
                await MessageBoxWindow.Show(owner, "Cannot export this image type. Only Bitmap images are supported.", "Export", MessageBoxMode.Ok);
                return;
            }

            // #1639: pick the IStorageFile handle so we can (a) keep the desktop
            // overwrite-confirmation for a real local path and (b) write via the
            // SAF bridge so Android content:// targets (no local path) are written
            // through OpenWriteAsync.
            var file = await FileDialogHelper.SaveImageFilePick(owner, suggestedName);
            if (file == null) return;

            string? path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                // Desktop overwrite-confirmation (unchanged behavior). A freshly
                // created SAF document never needs this prompt.
                var confirm = await MessageBoxWindow.Show(
                    owner,
                    $"File \"{Path.GetFileName(path)}\" already exists. Overwrite?",
                    "Export",
                    MessageBoxMode.YesNo);
                if (confirm != MessageBoxResult.Yes) return;
            }

            string? written = await FileDialogHelper.WriteViaAsync(file, async path =>
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
