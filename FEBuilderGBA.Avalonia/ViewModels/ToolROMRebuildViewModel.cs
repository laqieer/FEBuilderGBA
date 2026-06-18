using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ROM Rebuild (defragment) analysis tool — input-surface parity with WinForms
    /// <c>ToolROMRebuildForm</c> (#1171). Picks the clean original ROM, validates the
    /// rebuild address (WF <c>CheckRebuildAddress</c>), and writes a <c>.rebuild</c>
    /// analysis report via Core <see cref="RebuildCore.WriteRebuildReport"/> — the same
    /// Core path the CLI <c>--rebuild</c> command uses.
    ///
    /// NOTE: the full WinForms defragment (per-struct Make + auto-reopen Apply) lives in
    /// <c>ToolROMRebuildMake</c>/<c>ToolROMRebuildApply</c>, which are deeply coupled to
    /// WinForms (<c>InputFormRef</c>, <c>Program.AsmMapFileAsmCache</c>, etc.) and not yet
    /// ported to Core. This tool produces the analysis report and clearly messages that the
    /// compacting Make/Apply phase remains a WinForms-only follow-up; it never fabricates a
    /// compacted ROM. As a file producer (no in-place ROM writes) the VM does not implement
    /// the verifiable-data contract.
    /// </summary>
    public class ToolROMRebuildViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _originalRom = "";
        uint _rebuildAddress;
        string _status = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Path to the clean/unmodified original ROM the modified ROM is diffed against.</summary>
        public string OriginalRom { get => _originalRom; set => SetField(ref _originalRom, value); }
        /// <summary>Rebuild start address (offset). Defaults to the loaded ROM's extends_address.</summary>
        public uint RebuildAddress { get => _rebuildAddress; set => SetField(ref _rebuildAddress, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }

        public enum MakeResult { Ok, NoRom, OriginalMissing, OriginalUnreadable, OriginalNotMatching, BadAddress, Error }

        /// <summary>Outcome of <see cref="ValidateRebuildAddress"/> (mirrors WF CheckRebuildAddress).</summary>
        public enum AddressCheck { Ok, NotAligned, Unsafe, BelowExtends }

        /// <summary>The default rebuild address for the loaded ROM = offset of extends_address.</summary>
        public uint DefaultRebuildAddress()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return U.toOffset(rom.RomInfo.extends_address);
        }

        /// <summary>
        /// Initialize the address default (called when the view opens). Returns false if no
        /// ROM is loaded or the ROM does not use an extended region (nothing to rebuild).
        /// </summary>
        public bool Load()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null) return false;
            uint extends = U.toOffset(rom.RomInfo.extends_address);
            // WF ROMRebuildForm_Load: a ROM not using the extended region can't be rebuilt.
            if (rom.Data.Length <= extends) return false;
            RebuildAddress = extends;
            IsLoaded = true;
            return true;
        }

        /// <summary>
        /// Validate the rebuild address — mirrors WF <c>CheckRebuildAddress</c>: must be
        /// 4-byte aligned, within a safe offset range, and (warning) not below extends_address.
        /// </summary>
        public AddressCheck ValidateRebuildAddress(uint addr)
        {
            if (!U.isPadding4(addr)) return AddressCheck.NotAligned;
            ROM rom = CoreState.ROM;
            // U.isSafetyOffset(uint) dereferences CoreState.ROM.Data — guard the null case
            // (headless tests) and pass the ROM explicitly so a small in-memory ROM is judged
            // against its own length, exactly like the WF check against the loaded ROM.
            if (rom?.Data == null) return AddressCheck.Unsafe;
            if (!U.isSafetyOffset(addr, rom)) return AddressCheck.Unsafe;
            if (rom.RomInfo != null && addr < U.toOffset(rom.RomInfo.extends_address))
                return AddressCheck.BelowExtends;
            return AddressCheck.Ok;
        }

        /// <summary>
        /// Best-effort auto-find of a clean original ROM near the loaded ROM (mirrors WF
        /// ToolROMRebuildForm.Load -> MainFormUtil.FindOrignalROM). Returns "" on any error.
        /// </summary>
        public string FindOriginal()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || string.IsNullOrEmpty(rom.Filename)) return "";
                uint targetCrc = rom.RomInfo.orignal_crc32;
                if (targetCrc == 0) return "";
                string dir = Path.GetDirectoryName(rom.Filename) ?? "";
                // Find by the loaded ROM's known-original CRC32 — locale-independent and only
                // ever returns the CORRECT clean original (same approach as the UPS tool).
                string found = ToolTranslateROMCore.FindOrignalROMByCRC32(dir, targetCrc, "", rom.Filename, "") ?? "";
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
        /// Run the rebuild analysis against <paramref name="originalPath"/> (the clean ROM)
        /// and write the <c>.rebuild</c> report to <paramref name="outputPath"/>. Validate-
        /// then-make; never throws. The work itself is Core/headless so the caller may run it
        /// on a background thread.
        /// </summary>
        public MakeResult MakeRebuild(string originalPath, uint rebuildAddress, string outputPath, IProgress<string> progress = null)
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return MakeResult.NoRom;
            if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath)) return MakeResult.OriginalMissing;

            AddressCheck check = ValidateRebuildAddress(rebuildAddress);
            // BelowExtends is only a warning in WF (Yes/No prompt); the caller decides whether
            // to proceed, so accept it here. NotAligned/Unsafe are hard failures.
            if (check == AddressCheck.NotAligned || check == AddressCheck.Unsafe) return MakeResult.BadAddress;

            byte[] vanilla;
            try { vanilla = File.ReadAllBytes(originalPath); }
            catch { return MakeResult.OriginalUnreadable; }
            if (vanilla.Length == 0) return MakeResult.OriginalUnreadable;

            // Verify the selected file IS the unmodified original for the loaded game (mirrors
            // WF CheckOrignalROM). Rebuilding against the wrong/already-modified ROM is invalid.
            // (RomInfo can be null in headless tests — skip the check then.)
            uint targetCrc = rom.RomInfo?.orignal_crc32 ?? 0;
            if (targetCrc != 0 && new U.CRC32().Calc(vanilla) != targetCrc)
                return MakeResult.OriginalNotMatching;

            RebuildCore.RebuildResult result = RebuildCore.WriteRebuildReport(vanilla, rom.Data, rebuildAddress, outputPath, progress);
            LastMessage = result.Message ?? "";
            return result.Success ? MakeResult.Ok : MakeResult.Error;
        }

        /// <summary>The last Core analysis message (region/free-space stats), for display.</summary>
        public string LastMessage { get; private set; } = "";

        /// <summary>Suggested default report name (mirrors WF "R.{timestamp}.rebuild").</summary>
        public static string SuggestedName(string timestamp) => "R." + timestamp + ".rebuild";
    }
}
