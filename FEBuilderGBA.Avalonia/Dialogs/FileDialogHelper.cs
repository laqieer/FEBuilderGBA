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
        // Reuse static pattern arrays to avoid repeated allocations
        static readonly string[] GbaPatterns = new[] { "*.gba" };
        static readonly string[] AllPatterns = new[] { "*" };
        static readonly string[] UpsPatterns = new[] { "*.ups" };
        static readonly string[] PngPatterns = new[] { "*.png" };
        static readonly string[] ImagePatterns = new[] { "*.png", "*.bmp" };
        static readonly string[] PalPatterns = new[] { "*.pal" };
        static readonly string[] GbapalPatterns = new[] { "*.gbapal" };
        static readonly string[] ActPatterns = new[] { "*.act" };
        static readonly string[] GplPatterns = new[] { "*.gpl" };
        static readonly string[] TxtPatterns = new[] { "*.txt" };
        static readonly string[] AllPalettePatterns = new[] { "*.pal", "*.gbapal", "*.act", "*.gpl", "*.txt" };

        static FilePickerFileType MakeGbaFileType() => new(R._("GBA ROM Files"))
        {
            Patterns = GbaPatterns,
        };

        static FilePickerFileType MakeAllFileType() => new(R._("All Files"))
        {
            Patterns = AllPatterns,
        };

        static FilePickerFileType MakeUpsFileType() => new(R._("UPS Patch Files"))
        {
            Patterns = UpsPatterns,
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

        /// <summary>
        /// Open a GBA ROM and return the picked <see cref="IStorageFile"/> itself
        /// (not collapsed to a local path). On Android the SAF result usually has NO
        /// local path, so callers must read it via the stream API (#1124). Desktop
        /// callers can still call TryGetLocalPath() on the returned handle.
        /// </summary>
        public static async Task<IStorageFile?> OpenRomFilePick(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Open ROM"),
                AllowMultiple = false,
                FileTypeFilter = new[] { MakeGbaFileType(), MakeAllFileType() },
            });
            return files.Count > 0 ? files[0] : null;
        }

        /// <summary>Save a GBA ROM and return the picked IStorageFile (#1124).</summary>
        public static async Task<IStorageFile?> SaveRomFilePick(Window owner, string? suggestedName = null)
        {
            return await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save ROM"),
                SuggestedFileName = suggestedName ?? "rom.gba",
                FileTypeChoices = new[] { MakeGbaFileType(), MakeAllFileType() },
            });
        }

        /// <summary>
        /// Open a decomp project folder (#1129 slice 1). Returns the chosen
        /// directory's local path, or null when cancelled / unavailable.
        /// </summary>
        public static async Task<string?> OpenProjectFolder(Window owner)
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = R._("Open Decomp Project"),
                AllowMultiple = false,
            });

            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
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
            Patterns = PngPatterns,
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
            Patterns = ImagePatterns,
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

        /// <summary>Save an Animation Creator script file (.txt).</summary>
        public static async Task<string?> SaveAnimationScriptFile(Window owner, string? suggestedName = null)
        {
            var fileType = new FilePickerFileType(R._("Animation Script (.txt)")) { Patterns = TxtPatterns };
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = R._("Save Animation Script"),
                SuggestedFileName = suggestedName ?? "anim.txt",
                FileTypeChoices = new[] { fileType, MakeAllFileType() },
            });

            return file?.TryGetLocalPath();
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

        /// <summary>
        /// Save any file with a custom filter + optional suggested name.
        /// Used by the Map Action Animation Export button (#499) plus any
        /// future caller that needs a generic single-format SaveFilePicker.
        /// </summary>
        public static async Task<string?> SaveFile(Window owner, string title, string filterName, string pattern, string? suggestedName = null)
        {
            var fileType = new FilePickerFileType(filterName) { Patterns = new[] { pattern } };
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] { fileType, MakeAllFileType() },
            });

            return file?.TryGetLocalPath();
        }

        /// <summary>
        /// Save any file with multiple format choices (each a name + pattern pair).
        /// The OS picker shows them as a single dropdown — the user can pick the
        /// .txt or .gif variant directly without resorting to "All Files".
        /// Used by the Map Action Animation Export button (#499 Copilot bot
        /// inline review fix: single-pattern picker hid the .gif export path).
        /// </summary>
        public static async Task<string?> SaveFile(Window owner, string title,
            (string Name, string Pattern)[] filters, string? suggestedName = null)
        {
            var choices = new System.Collections.Generic.List<FilePickerFileType>(filters.Length + 1);
            foreach (var (name, pattern) in filters)
            {
                choices.Add(new FilePickerFileType(name) { Patterns = new[] { pattern } });
            }
            choices.Add(MakeAllFileType());

            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = choices,
            });

            return file?.TryGetLocalPath();
        }

        /// <summary>
        /// Save any file with multiple format choices; returns both the chosen
        /// path and the zero-based index of the filter the user selected (FIX 4:
        /// drives enableComment from the chosen filter, not a filename heuristic).
        /// Returns (-1) as filterIndex when the dialog is cancelled.
        /// </summary>
        public static async Task<(string? Path, int FilterIndex)> SaveFileWithFilterIndex(
            Window owner, string title,
            (string Name, string Pattern)[] filters, string? suggestedName = null)
        {
            var choices = new System.Collections.Generic.List<FilePickerFileType>(filters.Length + 1);
            foreach (var (name, pattern) in filters)
            {
                choices.Add(new FilePickerFileType(name) { Patterns = new[] { pattern } });
            }
            choices.Add(MakeAllFileType());

            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = choices,
            });

            if (file == null) return (null, -1);

            string? path = file.TryGetLocalPath();

            // Avalonia StorageProvider does not expose the selected FileTypeChoice
            // index directly. We infer it by matching the saved filename's extension
            // against the filter patterns in order (first match wins). This matches
            // the picker's own auto-append behaviour and is deterministic.
            if (path != null)
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                for (int i = 0; i < filters.Length; i++)
                {
                    string pat = filters[i].Pattern.TrimStart('*').ToLowerInvariant();
                    if (ext == pat) return (path, i);
                }
            }

            return (path, 0); // fallback to first filter
        }

        static FilePickerFileType MakeJascPalFileType() => new(R._("JASC-PAL (Aseprite/GIMP)"))
        {
            Patterns = PalPatterns,
        };

        static FilePickerFileType MakeGbaRawPalFileType() => new(R._("GBA Raw Palette"))
        {
            Patterns = GbapalPatterns,
        };

        static FilePickerFileType MakeAdobeActFileType() => new(R._("Adobe ACT (Photoshop)"))
        {
            Patterns = ActPatterns,
        };

        static FilePickerFileType MakeGimpGplFileType() => new(R._("GIMP GPL Palette"))
        {
            Patterns = GplPatterns,
        };

        static FilePickerFileType MakeHexTextPalFileType() => new(R._("Hex Text Palette"))
        {
            Patterns = TxtPatterns,
        };

        static FilePickerFileType MakeAnyPaletteFileType() => new(R._("All Palette Files"))
        {
            Patterns = AllPalettePatterns,
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
