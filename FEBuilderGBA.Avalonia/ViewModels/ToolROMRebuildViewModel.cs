using System;
using System.IO;
using System.Threading;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ROM Rebuild (defragment) tool — input-surface parity with WinForms
    /// <c>ToolROMRebuildForm</c> (#1171/#1261). Picks the clean original ROM, validates the
    /// rebuild address (WF <c>CheckRebuildAddress</c>), and offers two flows:
    /// <list type="bullet">
    ///   <item><b>Rebuild ROM</b> (#1261 primary): the full Core produce→apply pipeline —
    ///   <see cref="RebuildProducerCore.MakeWithProducer"/> writes a faithful <c>.rebuild</c>
    ///   manifest, <see cref="RebuildApplyCore.Apply"/> reconstructs a defragmented ROM from
    ///   the vanilla base, and the rebuilt bytes are written to the chosen output path. This is
    ///   the actual end-to-end defragment, no longer a WinForms-only follow-up.</item>
    ///   <item><b>Analysis report</b> (#1171): writes a <c>.rebuild</c> analysis report via Core
    ///   <see cref="RebuildCore.WriteRebuildReport"/> — the same Core path the CLI
    ///   <c>--rebuild</c> command uses.</item>
    /// </list>
    ///
    /// The producer's <c>IsComplete</c> gate (and the per-ROM s2pf-12 EA/BIN backstop) can REFUSE
    /// the rebuild for a ROM carrying an installed-but-unportable patch; <see cref="RebuildRom"/>
    /// catches that <see cref="InvalidOperationException"/> and surfaces the message rather than
    /// crashing. On a vanilla FE8U the gate PROCEEDS. As a file producer (no in-place ROM writes)
    /// the VM does not implement the verifiable-data contract.
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

        // ---- #1261: the full produce→apply→write-rebuilt-ROM flow ----

        /// <summary>
        /// A sensible high default rebuild address (offset) where the Core pipeline is fully
        /// faithful: the #1261 end-to-end round-trip proof confirmed 0 <c>Missing!</c> at
        /// <c>0x00B00000</c> on a vanilla FE8U (a real slab of structs relocates above it with
        /// every pointer fixed up, while no struct forward-references the non-rebuild base
        /// region — the slice-1 Apply append-only gap that lower addresses can trip).
        /// </summary>
        public const uint DefaultFullRebuildAddress = 0x00B00000u;

        /// <summary>Outcome of <see cref="RebuildRom"/> (the full defragment flow).</summary>
        public enum RebuildResult
        {
            Ok, NoRom, OriginalMissing, OriginalUnreadable, OriginalNotMatching,
            BadAddress, OutputCollision, GateRefused, Cancelled, ApplyFailed, Error
        }

        /// <summary>
        /// The size (in bytes) of the rebuilt ROM written by the last successful
        /// <see cref="RebuildRom"/> call (0 if none).
        /// </summary>
        public int LastRebuiltSize { get; private set; }

        /// <summary>
        /// Run the FULL defragment: produce a faithful <c>.rebuild</c> manifest from the loaded
        /// ROM against <paramref name="originalPath"/> (the clean diff base), apply it back onto
        /// that vanilla base, and write the rebuilt ROM to <paramref name="outputPath"/>.
        /// <para>
        /// Flow (all headless/Core — the caller may run it on a background thread):
        /// <list type="number">
        ///   <item><see cref="RebuildProducerCore.MakeWithProducer"/>(rom, vanilla, rebuildAddress,
        ///   manifestPath, isUseOtherGraphics:true, isUseOAMSP:false, progress, ct).</item>
        ///   <item><see cref="RebuildApplyCore.Apply"/>(vanilla, manifestPath, extendsAddress,
        ///   isReserved:null, progress) → <see cref="RebuildApplyCore.ApplyResult"/>.</item>
        ///   <item>atomic write of <c>ApplyResult.Rebuilt</c> to <paramref name="outputPath"/>.</item>
        /// </list>
        /// </para>
        /// The producer's <c>IsComplete</c> gate / s2pf-12 backstop may REFUSE for a ROM carrying
        /// an installed-but-unportable patch; that surfaces as <see cref="RebuildResult.GateRefused"/>
        /// with the reason in <see cref="LastMessage"/> (NOT a crash). The manifest + sidecars are
        /// produced beside <paramref name="outputPath"/> (in a temp working dir) and cleaned up.
        /// </summary>
        /// <param name="originalPath">Clean/unmodified original ROM (the diff base).</param>
        /// <param name="rebuildAddress">Offset at/after which data is rebuilt.</param>
        /// <param name="outputPath">Where to write the rebuilt ROM.</param>
        /// <param name="progress">Optional progress reporter (forwarded to producer + apply).</param>
        /// <param name="ct">Cancellation token (a cancelled producer is reported as Cancelled).</param>
        public RebuildResult RebuildRom(string originalPath, uint rebuildAddress, string outputPath,
            IProgress<string> progress = null, CancellationToken ct = default)
        {
            LastMessage = "";
            LastRebuiltSize = 0;

            ROM rom = CoreState.ROM;
            if (string.IsNullOrEmpty(outputPath)) return RebuildResult.Error;

            // Refuse to write the rebuilt ROM over the clean ORIGINAL (the diff base must stay
            // unmodified) or over the currently-loaded working ROM — either would silently destroy
            // a canonical input (Copilot PR #1343 review). The atomic write would otherwise replace
            // them in place. Checked BEFORE the NoRom/OriginalMissing gates so the overwrite is
            // refused even on a degenerate ROM/path, never silently after.
            if ((!string.IsNullOrEmpty(originalPath) && SamePath(outputPath, originalPath))
                || (rom != null && !string.IsNullOrEmpty(rom.Filename) && SamePath(outputPath, rom.Filename)))
            {
                LastMessage = R._("The output ROM must be a different file from the original ROM and the loaded ROM.");
                return RebuildResult.OutputCollision;
            }

            // MakeWithProducer requires rom == CoreState.ROM (its Address validation is
            // CoreState-bound), so we must pass CoreState.ROM and refuse if it is unset.
            if (rom?.Data == null || rom.RomInfo == null) return RebuildResult.NoRom;
            if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath)) return RebuildResult.OriginalMissing;

            AddressCheck check = ValidateRebuildAddress(rebuildAddress);
            // BelowExtends is only a warning (caller decides); NotAligned/Unsafe are hard failures.
            if (check == AddressCheck.NotAligned || check == AddressCheck.Unsafe) return RebuildResult.BadAddress;

            // Load the vanilla base ROM (the producer's diff base + Apply's reconstruction seed).
            var vanilla = new ROM();
            try
            {
                if (!vanilla.Load(originalPath, out string _) || vanilla.Data == null || vanilla.Data.Length == 0)
                    return RebuildResult.OriginalUnreadable;
            }
            catch { return RebuildResult.OriginalUnreadable; }

            // Verify the selected file IS the unmodified original for the loaded game (WF
            // CheckOrignalROM). Rebuilding against the wrong/already-modified ROM corrupts.
            uint targetCrc = rom.RomInfo.orignal_crc32;
            if (targetCrc != 0 && new U.CRC32().Calc(vanilla.Data) != targetCrc)
                return RebuildResult.OriginalNotMatching;

            // Produce the manifest into a private temp working dir (sidecar folders are created
            // beside the manifest). We clean it up in finally — only the rebuilt ROM is kept.
            string workDir = Path.Combine(Path.GetTempPath(), "feb_rebuild_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(workDir);
                string manifestPath = Path.Combine(workDir, "rom.rebuild");

                try
                {
                    RebuildProducerCore.MakeWithProducer(
                        rom, vanilla, rebuildAddress, manifestPath,
                        isUseOtherGraphics: true, isUseOAMSP: false,
                        progress: progress, ct: ct);
                }
                catch (OperationCanceledException)
                {
                    LastMessage = R._("The rebuild was cancelled.");
                    return RebuildResult.Cancelled;
                }
                catch (InvalidOperationException ex)
                {
                    // The IsComplete gate / s2pf-12 EA-BIN backstop refused this ROM. Surface the
                    // reason (it already begins with "ROM rebuild unavailable: ..."); never crash.
                    LastMessage = ex.Message;
                    return RebuildResult.GateRefused;
                }

                if (!File.Exists(manifestPath))
                {
                    LastMessage = R._("The rebuild manifest was not produced.");
                    return RebuildResult.Error;
                }

                // Apply the manifest back onto the vanilla base → the defragmented ROM bytes.
                RebuildApplyCore.ApplyResult apply = RebuildApplyCore.Apply(
                    vanilla, manifestPath, rom.RomInfo.extends_address,
                    isReserved: null, progress: progress);

                if (apply == null || apply.Rebuilt == null)
                {
                    LastMessage = apply?.Message ?? R._("The rebuild produced no ROM data.");
                    return RebuildResult.ApplyFailed;
                }

                LastMessage = apply.Message ?? "";
                if (!apply.Success)
                {
                    // Apply completed but left unresolved pointers (Missing!). Do NOT write a
                    // ROM that is known-corrupt — report the failure with the self-check message.
                    return RebuildResult.ApplyFailed;
                }

                // Atomic, fault-safe write: write to a temp file beside the target, then move it
                // into place (replacing any existing file) so a crash mid-write never leaves a
                // half-written output ROM.
                if (!AtomicWriteAllBytes(outputPath, apply.Rebuilt))
                {
                    LastMessage = R._("The rebuilt ROM could not be written.");
                    return RebuildResult.Error;
                }

                LastRebuiltSize = apply.Rebuilt.Length;
                return RebuildResult.Ok;
            }
            catch (Exception ex)
            {
                LastMessage = ex.Message;
                return RebuildResult.Error;
            }
            finally
            {
                try { Directory.Delete(workDir, true); } catch { /* best-effort cleanup */ }
            }
        }

        /// <summary>
        /// Write <paramref name="data"/> to <paramref name="path"/> atomically: write a sibling
        /// temp file then move it into place (overwrite). A crash mid-write leaves only the temp
        /// file, never a truncated <paramref name="path"/>. Returns false on any IO error.
        /// </summary>
        static bool AtomicWriteAllBytes(string path, byte[] data)
        {
            string tmp = path + ".tmp_" + Guid.NewGuid().ToString("N");
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(tmp, data);
                // File.Move(overwrite:true) is atomic on the same volume (the temp file is a
                // sibling of the target, so they share a volume).
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }
    }
}
