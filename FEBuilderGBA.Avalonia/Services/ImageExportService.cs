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

            string? path = await FileDialogHelper.SaveImageFile(owner, suggestedName);
            if (string.IsNullOrEmpty(path)) return;

            if (image is Bitmap bmp)
            {
                using var stream = File.Create(path);
                bmp.Save(stream);
                await MessageBoxWindow.Show(owner, $"Image saved to {Path.GetFileName(path)}", "Export", MessageBoxMode.Ok);
            }
            else
            {
                await MessageBoxWindow.Show(owner, "Cannot export this image type. Only Bitmap images are supported.", "Export", MessageBoxMode.Ok);
            }
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
                Log.Error("ImageExportService.SavePngToFile failed: {0}", ex.Message);
                return false;
            }
        }
    }
}
