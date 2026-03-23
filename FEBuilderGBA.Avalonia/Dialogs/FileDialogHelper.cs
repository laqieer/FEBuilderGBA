using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    /// <summary>
    /// Helper for file open/save dialogs using Avalonia 11 StorageProvider API.
    /// All user-visible strings are wrapped with R._() for i18n.
    /// </summary>
    public static class FileDialogHelper
    {
        static FilePickerFileType MakeGbaFileType() => new(R._("GBA ROM Files"))
        {
            Patterns = new[] { "*.gba" },
        };

        static FilePickerFileType MakeAllFileType() => new(R._("All Files"))
        {
            Patterns = new[] { "*" },
        };

        static FilePickerFileType MakeUpsFileType() => new(R._("UPS Patch Files"))
        {
            Patterns = new[] { "*.ups" },
        };

        /// <summary>Open a GBA ROM file.</summary>
        public static async Task<string?> OpenRomFile(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Open ROM"),
                AllowMultiple = false,
                FileTypeFilter = new[] { MakeGbaFileType(), MakeAllFileType() },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        /// <summary>Save a GBA ROM file.</summary>
        public static async Task<string?> SaveRomFile(Window owner, string? suggestedName = null)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save ROM"),
                SuggestedFileName = suggestedName ?? "rom.gba",
                FileTypeChoices = new[] { MakeGbaFileType(), MakeAllFileType() },
            });

            return file?.TryGetLocalPath();
        }

        /// <summary>Open a UPS patch file.</summary>
        public static async Task<string?> OpenPatchFile(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Open Patch"),
                AllowMultiple = false,
                FileTypeFilter = new[] { MakeUpsFileType(), MakeAllFileType() },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        static FilePickerFileType MakePngFileType() => new(R._("PNG Image"))
        {
            Patterns = new[] { "*.png" },
        };

        /// <summary>Save a PNG image file.</summary>
        public static async Task<string?> SaveImageFile(Window owner, string? suggestedName = null)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Export Image"),
                SuggestedFileName = suggestedName ?? "image.png",
                FileTypeChoices = new[] { MakePngFileType() },
            });

            return file?.TryGetLocalPath();
        }

        static FilePickerFileType MakeImageFileType() => new(R._("Image Files"))
        {
            Patterns = new[] { "*.png", "*.bmp" },
        };

        /// <summary>Open an image file (PNG, BMP) for import.</summary>
        public static async Task<string?> OpenImageFile(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Import Image"),
                AllowMultiple = false,
                FileTypeFilter = new[] { MakeImageFileType(), MakeAllFileType() },
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
                FileTypeFilter = new[] { fileType, MakeAllFileType() },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        static FilePickerFileType MakeJascPalFileType() => new(R._("JASC-PAL (Aseprite/GIMP)"))
        {
            Patterns = new[] { "*.pal" },
        };

        static FilePickerFileType MakeGbaRawPalFileType() => new(R._("GBA Raw Palette"))
        {
            Patterns = new[] { "*.gbapal" },
        };

        static FilePickerFileType MakeAdobeActFileType() => new(R._("Adobe ACT (Photoshop)"))
        {
            Patterns = new[] { "*.act" },
        };

        static FilePickerFileType MakeGimpGplFileType() => new(R._("GIMP GPL Palette"))
        {
            Patterns = new[] { "*.gpl" },
        };

        static FilePickerFileType MakeHexTextPalFileType() => new(R._("Hex Text Palette"))
        {
            Patterns = new[] { "*.txt" },
        };

        static FilePickerFileType MakeAnyPaletteFileType() => new(R._("All Palette Files"))
        {
            Patterns = new[] { "*.pal", "*.gbapal", "*.act", "*.gpl", "*.txt" },
        };

        /// <summary>Save a palette file with multi-format choices.</summary>
        public static async Task<string?> SavePaletteFile(Window owner, string? suggestedName = null)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Export Palette"),
                SuggestedFileName = suggestedName ?? "palette.pal",
                FileTypeChoices = new[] { MakeJascPalFileType(), MakeGbaRawPalFileType(), MakeAdobeActFileType(), MakeGimpGplFileType(), MakeHexTextPalFileType() },
            });

            return file?.TryGetLocalPath();
        }

        /// <summary>Open a palette file (any supported format) for import.</summary>
        public static async Task<string?> OpenPaletteFile(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Import Palette"),
                AllowMultiple = false,
                FileTypeFilter = new[] { MakeAnyPaletteFileType(), MakeJascPalFileType(), MakeGbaRawPalFileType(), MakeAdobeActFileType(), MakeGimpGplFileType(), MakeHexTextPalFileType(), MakeAllFileType() },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
    }
}
