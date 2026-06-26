using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Apply-UPS tool — parity with WinForms <c>ToolUPSOpenSimpleForm</c> (#1460).
    /// Applies a distributed <c>.ups</c> patch to a clean (unmodified) original ROM,
    /// producing the patched ROM, via Core <see cref="UPSUtilCore.ApplyUPS(byte[],byte[],out string)"/>
    /// — the same pipeline as CLI <c>--applyups</c> and the main-window drag-drop path.
    ///
    /// All methods here are pure / headless (validate → apply, never throw, no ROM
    /// mutation). The View owns picking the patched ROM's save target and loading it
    /// into the main window (mirroring WF save-and-reopen vs in-memory load).
    /// </summary>
    public class ToolUPSOpenSimpleViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _upsFile = "";
        string _originalRom = "";
        string _status = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Path to the distributed UPS patch file to apply.</summary>
        public string UpsFile { get => _upsFile; set => SetField(ref _upsFile, value); }
        /// <summary>Path to the clean/unmodified original ROM the patch is applied to.</summary>
        public string OriginalRom { get => _originalRom; set => SetField(ref _originalRom, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }

        public enum ApplyResult
        {
            Ok,
            /// <summary>Apply succeeded but Core reported a non-fatal CRC warning (see the
            /// <c>warning</c> out-param). WinForms applies anyway after a Yes/No prompt; the
            /// View prompts before saving/loading (Copilot plan review finding #2).</summary>
            OkWithWarning,
            UpsMissing,
            UpsInvalid,
            OriginalMissing,
            OriginalUnreadable,
            OriginalNotClean,
            SourceCrcMismatch,
            ApplyFailed,
            Error,
        }

        /// <summary>
        /// Best-effort auto-find of the clean original ROM that a UPS patch applies to,
        /// by reading the UPS file's recorded source CRC32 and scanning the WinForms-equivalent
        /// directory set (mirrors WF <c>UPSOpenSimpleForm_Shown</c> → <c>GetUPSSrcCRC32</c> +
        /// <c>FindOrignalROMByCRC32</c>): the UPS dir, the app base directory
        /// (<see cref="CoreState.BaseDirectory"/>), and the loaded ROM's directory. Returns ""
        /// if the UPS is missing/invalid, no match is found, or on any error.
        /// </summary>
        public string FindOriginalForUps(string upsPath)
        {
            try
            {
                if (string.IsNullOrEmpty(upsPath) || !File.Exists(upsPath)) return "";
                if (!UPSUtilCore.IsUPSFile(upsPath)) return "";

                uint srcCrc = UPSUtilCore.GetUPSSrcCRC32(upsPath);
                if (srcCrc == U.NOT_FOUND || srcCrc == 0) return "";

                string upsDir = Path.GetDirectoryName(Path.GetFullPath(upsPath)) ?? "";
                if (string.IsNullOrEmpty(upsDir)) return "";

                // Pass the full WinForms-equivalent search-dir set (Copilot review finding #3),
                // matching ToolWorkSupport_SelectUPSViewModel's parity fix:
                //   currentDir       = the UPS's directory
                //   romBaseDirectory = the app base directory
                //   lastROMFilename  = the loaded ROM (its directory is also scanned)
                //   emulatorDirectory= Windows-only; not meaningfully cross-platform.
                ROM rom = CoreState.ROM;
                string lastRom = rom?.Filename ?? "";
                string baseDir = CoreState.BaseDirectory ?? "";

                string found = ToolTranslateROMCore.FindOrignalROMByCRC32(upsDir, srcCrc, baseDir, lastRom, "") ?? "";
                return found;
            }
            catch { return ""; }
        }

        /// <summary>
        /// Return true if <paramref name="data"/> is a known clean/unmodified original ROM
        /// (its CRC32 matches an entry in <see cref="ToolTranslateROMCore.GetROMBaseTable"/>).
        /// Mirrors WF <c>MainFormUtil.CheckOrignalROM</c>'s CRC32 check.
        /// </summary>
        public static bool IsCleanOriginal(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            uint crc = new UPSUtilCore.CRC32().Calc(data);
            foreach (var t in ToolTranslateROMCore.GetROMBaseTable())
            {
                if (t.crc32 == crc) return true;
            }
            return false;
        }

        /// <summary>
        /// Convenience overload: read <paramref name="romPath"/> and test it. Never throws.
        /// </summary>
        public static bool IsCleanOriginal(string romPath)
        {
            try
            {
                if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath)) return false;
                return IsCleanOriginal(File.ReadAllBytes(romPath));
            }
            catch { return false; }
        }

        /// <summary>
        /// Apply the UPS patch at <paramref name="upsPath"/> to the clean original ROM at
        /// <paramref name="originalPath"/>, returning the patched ROM bytes in
        /// <paramref name="patched"/>. Validate-then-apply; never throws; no ROM mutation.
        /// The patched bytes are produced by Core <see cref="UPSUtilCore.ApplyUPS(byte[],byte[],out string)"/>.
        ///
        /// On a non-fatal CRC warning (e.g. result-CRC mismatch) returns
        /// <see cref="ApplyResult.OkWithWarning"/> with the patched bytes and a populated
        /// <paramref name="warning"/> so the View can prompt before committing (WF parity).
        /// </summary>
        public ApplyResult ApplyUps(string upsPath, string originalPath, out byte[] patched, out string warning)
        {
            patched = null;
            warning = "";
            try
            {
                if (string.IsNullOrEmpty(upsPath) || !File.Exists(upsPath)) return ApplyResult.UpsMissing;
                if (!UPSUtilCore.IsUPSFile(upsPath)) return ApplyResult.UpsInvalid;

                if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath)) return ApplyResult.OriginalMissing;

                // Read the original ONCE so an IO failure surfaces as OriginalUnreadable rather
                // than being masked as OriginalNotClean (Copilot review finding #5).
                byte[] src;
                try { src = File.ReadAllBytes(originalPath); }
                catch { return ApplyResult.OriginalUnreadable; }
                if (src.Length == 0) return ApplyResult.OriginalUnreadable;

                // The selected original must be an unmodified, official clean ROM — applying a
                // UPS to the wrong/already-modified ROM yields garbage (mirrors WF CheckOrignalROM).
                if (!IsCleanOriginal(src)) return ApplyResult.OriginalNotClean;

                byte[] patch;
                try { patch = File.ReadAllBytes(upsPath); }
                catch { return ApplyResult.ApplyFailed; }

                byte[] result = UPSUtilCore.ApplyUPS(src, patch, out string errorMessage);
                if (result == null)
                {
                    // ApplyUPS returns null only for a hard mismatch (corrupt header / source CRC
                    // mismatch). A source-CRC mismatch means this UPS isn't for this original ROM.
                    return ApplyResult.SourceCrcMismatch;
                }

                patched = result;
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    // Non-null result + non-empty errorMessage = a soft warning (e.g. result CRC
                    // mismatch / patch-CRC mismatch). WF applies anyway after a Yes/No prompt;
                    // surface it so the View can ask the user before committing.
                    warning = errorMessage;
                    return ApplyResult.OkWithWarning;
                }
                return ApplyResult.Ok;
            }
            catch { return ApplyResult.Error; }
        }
    }
}
