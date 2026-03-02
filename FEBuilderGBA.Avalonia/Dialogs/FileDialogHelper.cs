using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>
    /// Helper for file open/save dialogs using Avalonia 11 StorageProvider API.
    /// </summary>
    public static class FileDialogHelper
    {
        static readonly FilePickerFileType GbaFileType = new("GBA ROM Files")
        {
            Patterns = new[] { "*.gba" },
        };

        static readonly FilePickerFileType AllFileType = new("All Files")
        {
            Patterns = new[] { "*" },
        };

        static readonly FilePickerFileType UpsFileType = new("UPS Patch Files")
        {
            Patterns = new[] { "*.ups" },
        };

        /// <summary>Open a GBA ROM file.</summary>
        public static async Task<string?> OpenRomFile(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open ROM",
                AllowMultiple = false,
                FileTypeFilter = new[] { GbaFileType, AllFileType },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        /// <summary>Save a GBA ROM file.</summary>
        public static async Task<string?> SaveRomFile(Window owner, string? suggestedName = null)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save ROM",
                SuggestedFileName = suggestedName ?? "rom.gba",
                FileTypeChoices = new[] { GbaFileType, AllFileType },
            });

            return file?.TryGetLocalPath();
        }

        /// <summary>Open a UPS patch file.</summary>
        public static async Task<string?> OpenPatchFile(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Patch",
                AllowMultiple = false,
                FileTypeFilter = new[] { UpsFileType, AllFileType },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        /// <summary>Open any file with custom filter.</summary>
        public static async Task<string?> OpenFile(Window owner, string title, string pattern)
        {
            var fileType = new FilePickerFileType(title) { Patterns = new[] { pattern } };
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[] { fileType, AllFileType },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
    }
}
