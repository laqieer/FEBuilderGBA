using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Save-as-UPS tool — parity with WinForms <c>ToolUPSPatchSimpleForm</c> (#1194).
    /// Creates a UPS patch that turns a clean (unmodified) original ROM into the
    /// currently-loaded ROM (<see cref="CoreState.ROM"/>), via Core
    /// <see cref="UPSUtilCore.MakeUPS"/> — the same logic as CLI <c>--makeups</c>.
    /// </summary>
    public class ToolUPSPatchSimpleViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _originalRom = "";
        string _status = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Path to the clean/unmodified original ROM the patch is diffed against.</summary>
        public string OriginalRom { get => _originalRom; set => SetField(ref _originalRom, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }

        public enum MakeResult { Ok, NoRom, OriginalMissing, OriginalUnreadable, Error }

        /// <summary>
        /// Best-effort auto-find of a clean original ROM near the loaded ROM
        /// (mirrors WinForms ToolUPSPatchSimpleForm.Load → MainFormUtil.FindOrignalROM).
        /// Returns "" if none found or on any error.
        /// </summary>
        public string FindOriginal()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || string.IsNullOrEmpty(rom.Filename)) return "";
                string dir = Path.GetDirectoryName(rom.Filename) ?? "";
                string found = ToolTranslateROMCore.FindOrignalROMByLang(
                    dir, CoreState.Language ?? "en", rom.RomInfo.version, "", rom.Filename, "") ?? "";
                // Never suggest the loaded ROM as its own "original" — diffing a ROM
                // against itself yields an empty patch (and the path is the user's own).
                if (!string.IsNullOrEmpty(found) && SamePath(found, rom.Filename)) return "";
                return found;
            }
            catch { return ""; }
        }

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Create a UPS at <paramref name="outputPath"/> that turns <paramref name="originalPath"/>
        /// (the clean ROM) into the currently-loaded ROM. Validate-then-make; never throws.
        /// </summary>
        public MakeResult MakeUps(string originalPath, string outputPath)
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return MakeResult.NoRom;
            if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath)) return MakeResult.OriginalMissing;

            byte[] src;
            try { src = File.ReadAllBytes(originalPath); }
            catch { return MakeResult.OriginalUnreadable; }
            if (src.Length == 0) return MakeResult.OriginalUnreadable;

            try
            {
                // src = clean original, dst = current (modified) ROM. Same as CLI --makeups.
                UPSUtilCore.MakeUPS(src, rom.Data, outputPath);
                return MakeResult.Ok;
            }
            catch { return MakeResult.Error; }
        }

        /// <summary>Suggested default patch name (mirrors WF "PATCH.{timestamp}.ups"); timestamp injected by the caller.</summary>
        public static string SuggestedName(string timestamp) => "PATCH." + timestamp + ".ups";
    }
}
