using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia LZ77 Tool. Provides cross-platform parity with
    /// WinForms ToolLZ77Form for the in-scope tabs: Decompress, Compress, Erase
    /// (Zero Clear), and Base64 (Plain). Move and Recompress tabs are deferred
    /// (need Core extraction of WinForms-only helpers — see PR scope notes).
    ///
    /// ROM reads use CoreState.ROM (Core), not WinForms Program.ROM, so this is
    /// callable from the cross-platform Avalonia project.
    ///
    /// Address fields are exposed as string properties so users can enter "0x..."
    /// (or plain hex) — uint two-way binding with StringFormat fails on hex input.
    /// </summary>
    public class ToolLZ77ViewModel : ViewModelBase
    {
        public const string THIS_ROM = "<<THIS ROM>>";


        /// <summary>
        /// Low-address write policy. Mirrors WinForms OptionForm.write_low_address_enum
        /// driven by the "func_write_low_address" Config key (default 2 = Deny).
        /// </summary>
        public enum LowAddressWritePolicy { NoWarning = 0, Warning = 1, Deny = 2 }

        /// <summary>Read the current low-address write policy from CoreState.Config.</summary>
        public static LowAddressWritePolicy GetLowAddressWritePolicy()
        {
            var cfg = CoreState.Config;
            if (cfg == null) return LowAddressWritePolicy.Deny;
            string s = cfg.at("func_write_low_address", "2");
            if (int.TryParse(s, out int v) && v >= 0 && v <= 2)
                return (LowAddressWritePolicy)v;
            return LowAddressWritePolicy.Deny;
        }
        string _decompressSrcPath = THIS_ROM;
        string _decompressDestPath = string.Empty;
        string _decompressAddressText = "0x0";
        string _compressSrcPath = string.Empty;
        string _compressDestPath = string.Empty;
        string _zeroClearFromText = "0x0";
        string _zeroClearToText = "0x0";
        string _base64Text = string.Empty;
        string _statusText = string.Empty;
        bool _isBusy;
        bool _isLoaded;
        bool _zeroClearConfirmed;
        // Move tab state
        string _moveFromText = "0x0";
        string _moveToText = "0x0";
        string _moveLengthText = "0";
        bool _moveRawFallbackConfirmed;
        bool _movePointerCountConfirmed;
        // Recompress tab state
        bool _recompressConfirmed;
        bool _recompressModifiedAcknowledged;

        readonly UndoService _undo = new();

        public string DecompressSrcPath { get => _decompressSrcPath; set => SetField(ref _decompressSrcPath, value); }
        public string DecompressDestPath { get => _decompressDestPath; set => SetField(ref _decompressDestPath, value); }
        public string DecompressAddressText { get => _decompressAddressText; set => SetField(ref _decompressAddressText, value); }
        public string CompressSrcPath { get => _compressSrcPath; set => SetField(ref _compressSrcPath, value); }
        public string CompressDestPath { get => _compressDestPath; set => SetField(ref _compressDestPath, value); }
        public string ZeroClearFromText { get => _zeroClearFromText; set => SetField(ref _zeroClearFromText, value); }
        public string ZeroClearToText { get => _zeroClearToText; set => SetField(ref _zeroClearToText, value); }
        public string Base64Text { get => _base64Text; set => SetField(ref _base64Text, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool ZeroClearConfirmed { get => _zeroClearConfirmed; set => SetField(ref _zeroClearConfirmed, value); }

        public string MoveFromText { get => _moveFromText; set => SetField(ref _moveFromText, value); }
        public string MoveToText { get => _moveToText; set => SetField(ref _moveToText, value); }
        public string MoveLengthText { get => _moveLengthText; set => SetField(ref _moveLengthText, value); }
        /// <summary>Set by View after user confirms the raw-pointer fallback warning.</summary>
        public bool MoveRawFallbackConfirmed { get => _moveRawFallbackConfirmed; set => SetField(ref _moveRawFallbackConfirmed, value); }
        /// <summary>Set by View after user confirms the multi-pointer warning (pointerCount != 1).</summary>
        public bool MovePointerCountConfirmed { get => _movePointerCountConfirmed; set => SetField(ref _movePointerCountConfirmed, value); }
        /// <summary>Set by View after user confirms running recompress.</summary>
        public bool RecompressConfirmed { get => _recompressConfirmed; set => SetField(ref _recompressConfirmed, value); }
        /// <summary>Set by View after user acknowledges the rom-modified warning (allows continuation).</summary>
        public bool RecompressModifiedAcknowledged { get => _recompressModifiedAcknowledged; set => SetField(ref _recompressModifiedAcknowledged, value); }

        public void Initialize()
        {
            DecompressSrcPath = THIS_ROM;
            IsLoaded = true;
        }

        public List<AddrResult> LoadList()
        {
            return new List<AddrResult>();
        }

        internal static bool TryParseHex(string text, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(2);
            return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        public void RunDecompress()
        {
            if (string.IsNullOrWhiteSpace(DecompressDestPath))
            {
                StatusText = R._("Decompress: destination path required.");
                return;
            }
            if (!TryParseHex(DecompressAddressText, out uint rawAddr))
            {
                StatusText = R._("Decompress: SRC address is not a valid hex value.");
                return;
            }

            byte[] src;
            bool fromThisRom = string.IsNullOrEmpty(DecompressSrcPath) || DecompressSrcPath == THIS_ROM;
            if (fromThisRom)
            {
                var rom = CoreState.ROM;
                if (rom == null)
                {
                    StatusText = R._("Decompress: no ROM loaded.");
                    return;
                }
                src = rom.Data;
            }
            else
            {
                if (!File.Exists(DecompressSrcPath))
                {
                    StatusText = R._("Decompress: source file not found.");
                    return;
                }
                src = File.ReadAllBytes(DecompressSrcPath);
            }

            uint addr = U.toOffset(rawAddr);
            if (src.Length <= addr)
            {
                StatusText = R._("Decompress: address out of range.");
                return;
            }

            try
            {
                byte[] data = LZ77.decompress(src, addr);
                File.WriteAllBytes(DecompressDestPath, data);
                StatusText = R._("Decompress: wrote {0} bytes to {1}.", data.Length, Path.GetFileName(DecompressDestPath));
            }
            catch (Exception ex)
            {
                StatusText = R._("Decompress failed: {0}", ex.Message);
            }
        }

        public void RunCompress()
        {
            if (string.IsNullOrWhiteSpace(CompressSrcPath))
            {
                StatusText = R._("Compress: source path required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(CompressDestPath))
            {
                StatusText = R._("Compress: destination path required.");
                return;
            }
            if (!File.Exists(CompressSrcPath))
            {
                StatusText = R._("Compress: source file not found.");
                return;
            }

            try
            {
                byte[] src = File.ReadAllBytes(CompressSrcPath);
                byte[] compressed = LZ77.compress(src);
                File.WriteAllBytes(CompressDestPath, compressed);
                StatusText = R._("Compress: wrote {0} bytes to {1}.", compressed.Length, Path.GetFileName(CompressDestPath));
            }
            catch (Exception ex)
            {
                StatusText = R._("Compress failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Returns true if the range hits a low-address region (ROM header, fixed tables)
        /// that the config policy considers dangerous. Mirrors WinForms
        /// U.CheckZeroAddressWriteHigh: checked against rom.RomInfo.compress_image_borderline_address.
        /// </summary>
        public bool IsLowAddressRange(uint from, uint to)
        {
            var rom = CoreState.ROM;
            if (rom == null) return false;
            uint borderline = rom.RomInfo.compress_image_borderline_address;
            return from < borderline || to < borderline;
        }

        /// <summary>
        /// Decide what to do for a ZeroClear range under the current LowAddressWritePolicy:
        /// - NoWarning: proceed silently
        /// - Warning: require user confirmation (View prompts; sets ZeroClearConfirmed)
        /// - Deny: block the write entirely (status text set, no prompt, no write)
        /// Returns true if confirmation prompt is required.
        /// </summary>
        public bool ZeroClearNeedsConfirmation(uint from, uint to)
        {
            if (!IsLowAddressRange(from, to)) return false;
            // Warning policy is the only one that prompts; NoWarning skips, Deny blocks via RunZeroClear.
            return GetLowAddressWritePolicy() == LowAddressWritePolicy.Warning;
        }

        public void RunZeroClear()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("ZeroClear: no ROM loaded.");
                return;
            }
            if (!TryParseHex(ZeroClearFromText, out uint fromRaw))
            {
                StatusText = R._("ZeroClear: FROM is not a valid hex value.");
                return;
            }
            if (!TryParseHex(ZeroClearToText, out uint toRaw))
            {
                StatusText = R._("ZeroClear: TO is not a valid hex value.");
                return;
            }

            uint from = U.toOffset(fromRaw);
            uint to = U.toOffset(toRaw);
            if (to < from) { uint tmp = from; from = to; to = tmp; }
            uint size = to - from;

            if (!IsAddressInRom(rom, from) || !IsAddressInRom(rom, to))
            {
                StatusText = R._("ZeroClear: address out of ROM bounds.");
                return;
            }

            if (ZeroClearNeedsConfirmation(from, to) && !ZeroClearConfirmed)
            {
                StatusText = R._("ZeroClear: low address requires confirmation — set ZeroClearConfirmed=true to proceed.");
                return;
            }

            try
            {
                _undo.Begin($"ZeroClear 0x{from:X8}-0x{to:X8} ({size} bytes)");
                rom.write_fill(from, size, 0);
                _undo.Commit();
                StatusText = R._("ZeroClear: wrote 0 to 0x{0:X8}..0x{1:X8} ({2} bytes).", from, to, size);
            }
            catch (Exception ex)
            {
                if (_undo.HasPendingUndo)
                {
                    try { _undo.Rollback(); } catch (Exception rollbackEx) { Log.ErrorF("ZeroClear rollback failed: {0}", rollbackEx.Message); }
                }
                StatusText = R._("ZeroClear failed: {0}", ex.Message);
            }
            finally
            {
                ZeroClearConfirmed = false;
            }
        }

        static bool IsAddressInRom(ROM rom, uint offset)
        {
            return offset < (uint)rom.Data.Length;
        }

        public void RunBase64TextToFile(string outPath)
        {
            if (string.IsNullOrEmpty(Base64Text))
            {
                StatusText = R._("Base64: enter base64 text first.");
                return;
            }
            if (string.IsNullOrWhiteSpace(outPath))
            {
                StatusText = R._("Base64: output path required.");
                return;
            }

            string text = Base64Text.Trim().Replace(' ', '+');
            byte[] bin;
            if (!U.Base64Encode(text, out bin))
            {
                StatusText = R._("Base64: could not decode (invalid base64).");
                return;
            }

            try
            {
                File.WriteAllBytes(outPath, bin);
                StatusText = R._("Base64: wrote {0} bytes to {1}.", bin.Length, Path.GetFileName(outPath));
            }
            catch (Exception ex)
            {
                StatusText = R._("Base64 write failed: {0}", ex.Message);
            }
        }

        public void RunFileToBase64Text(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                StatusText = R._("Base64: source path required.");
                return;
            }
            if (!File.Exists(filePath))
            {
                StatusText = R._("Base64: source file not found.");
                return;
            }

            try
            {
                byte[] bin = File.ReadAllBytes(filePath);
                Base64Text = Convert.ToBase64String(bin);
                StatusText = R._("Base64: encoded {0} bytes.", bin.Length);
            }
            catch (Exception ex)
            {
                StatusText = R._("Base64 read failed: {0}", ex.Message);
            }
        }

        // =================== Move tab ===================

        /// <summary>Result enum for two-phase Move confirmation flow.</summary>
        public enum MovePreflightResult
        {
            ProceedNoPrompt,
            NeedRawFallbackConfirm,
            NeedPointerCountConfirm,
            ErrorAlreadyShown,
        }

        /// <summary>
        /// Preflight check: parse inputs, search pointers, decide whether
        /// confirmation is needed. Returns the action the View should take.
        /// Sets StatusText on validation failures.
        /// </summary>
        public MovePreflightResult MovePreflight(out uint srcOffset, out uint dstOffset, out uint length, out LZ77ToolCore.SearchPointerResult searchResult)
        {
            srcOffset = 0; dstOffset = 0; length = 0;
            searchResult = new LZ77ToolCore.SearchPointerResult();

            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("Move: no ROM loaded.");
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (!TryParseHex(MoveFromText, out uint fromRaw))
            {
                StatusText = R._("Move: FROM is not a valid hex value.");
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (!TryParseHex(MoveToText, out uint toRaw))
            {
                StatusText = R._("Move: TO is not a valid hex value.");
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (!TryParseHex(MoveLengthText, out uint lenRaw))
            {
                StatusText = R._("Move: LENGTH is not a valid hex value.");
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (lenRaw == 0)
            {
                StatusText = R._("Move: LENGTH is 0 (cannot auto-detect length).");
                return MovePreflightResult.ErrorAlreadyShown;
            }

            srcOffset = U.toOffset(fromRaw);
            dstOffset = toRaw == 0 ? 0u : U.toOffset(toRaw);
            length = lenRaw;

            // Surface basic validation errors BEFORE prompting the user, so we
            // don't walk the multi-prompt confirmation flow and then fail inside
            // MoveCompressedData. Mirrors the same checks in the Core helper.
            if (srcOffset == 0)
            {
                StatusText = R._("Move: source address is 0 (bad base address).");
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (length >= LZ77ToolCore.MOVE_LENGTH_LIMIT)
            {
                StatusText = R._("Move: LENGTH 0x{0:X} exceeds 2 MB limit.", length);
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (srcOffset + length > (uint)rom.Data.Length)
            {
                StatusText = R._("Move: source range 0x{0:X8}+0x{1:X} exceeds ROM end.", srcOffset, length);
                return MovePreflightResult.ErrorAlreadyShown;
            }
            if (dstOffset != 0 && (dstOffset + length > (uint)rom.Data.Length))
            {
                StatusText = R._("Move: destination range 0x{0:X8}+0x{1:X} exceeds ROM end.", dstOffset, length);
                return MovePreflightResult.ErrorAlreadyShown;
            }
            // Overlap check (only applies when caller supplied an explicit dst).
            if (dstOffset != 0)
            {
                uint sEnd = srcOffset + length;
                uint dEnd = dstOffset + length;
                if (srcOffset < dEnd && dstOffset < sEnd)
                {
                    StatusText = R._("Move: source 0x{0:X8}+0x{1:X} and destination 0x{2:X8}+0x{1:X} overlap (not supported).", srcOffset, length, dstOffset);
                    return MovePreflightResult.ErrorAlreadyShown;
                }
            }

            // Look up pointers (LDR-first, raw fallback) so the View can decide
            // what prompts to surface BEFORE the actual write.
            searchResult = LZ77ToolCore.SearchPointersForAddress(rom.Data, srcOffset);

            if (searchResult.UsedRawFallback && !MoveRawFallbackConfirmed)
                return MovePreflightResult.NeedRawFallbackConfirm;
            if (searchResult.Pointers.Count != 1 && !MovePointerCountConfirmed)
                return MovePreflightResult.NeedPointerCountConfirm;
            return MovePreflightResult.ProceedNoPrompt;
        }

        /// <summary>
        /// Apply the move. Caller is responsible for calling
        /// <see cref="MovePreflight"/> first and handling the confirmation
        /// prompts via <see cref="MoveRawFallbackConfirmed"/> +
        /// <see cref="MovePointerCountConfirmed"/>.
        /// </summary>
        public LZ77ToolCore.MoveResult RunMove()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("Move: no ROM loaded.");
                return new LZ77ToolCore.MoveResult { Ok = false, ErrorMessage = "no ROM" };
            }
            var preflight = MovePreflight(out uint srcOffset, out uint dstOffset, out uint length, out _);
            if (preflight == MovePreflightResult.ErrorAlreadyShown)
            {
                return new LZ77ToolCore.MoveResult { Ok = false, ErrorMessage = StatusText };
            }
            if (preflight != MovePreflightResult.ProceedNoPrompt)
            {
                StatusText = R._("Move: pending user confirmation — see prompt.");
                return new LZ77ToolCore.MoveResult { Ok = false, ErrorMessage = "pending confirmation" };
            }

            LZ77ToolCore.MoveResult result;
            try
            {
                _undo.Begin($"MoveLZ77 0x{srcOffset:X8} -> 0x{dstOffset:X8} ({length} bytes)");
                result = LZ77ToolCore.MoveCompressedData(rom, srcOffset, dstOffset, length);
                if (result.Ok) _undo.Commit();
                else
                {
                    if (_undo.HasPendingUndo)
                        try { _undo.Rollback(); } catch (Exception rbEx) { Log.ErrorF("Move rollback failed: {0}", rbEx.Message); }
                }
            }
            catch (Exception ex)
            {
                if (_undo.HasPendingUndo)
                    try { _undo.Rollback(); } catch (Exception rbEx) { Log.ErrorF("Move rollback failed: {0}", rbEx.Message); }
                StatusText = R._("Move failed: {0}", ex.Message);
                return new LZ77ToolCore.MoveResult { Ok = false, ErrorMessage = ex.Message };
            }
            finally
            {
                // Reset confirmation flags so subsequent moves require fresh confirmation.
                MoveRawFallbackConfirmed = false;
                MovePointerCountConfirmed = false;
            }

            if (result.Ok)
            {
                StatusText = R._("Move: moved 0x{0:X} bytes from 0x{1:X8} to 0x{2:X8} ({3} pointers rewritten{4}).",
                    length, srcOffset, result.NewAddress, result.PointersRewritten.Count,
                    result.AutoAllocated ? ", auto-allocated" : "");
            }
            else
            {
                StatusText = R._("Move: {0}", result.ErrorMessage);
            }
            return result;
        }

        // =================== Recompress tab ===================

        /// <summary>Result enum for two-phase Recompress confirmation flow.</summary>
        public enum RecompressPreflightResult
        {
            ProceedNoPrompt,
            NeedConfirm,
            NeedRomModifiedAck,
            ErrorAlreadyShown,
        }

        /// <summary>
        /// Preflight check for Recompress: validates ROM is loaded and not
        /// modified. Returns the action the View should take.
        /// </summary>
        public RecompressPreflightResult RecompressPreflight()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("Recompress: no ROM loaded.");
                return RecompressPreflightResult.ErrorAlreadyShown;
            }
            if (rom.Modified && !RecompressModifiedAcknowledged)
                return RecompressPreflightResult.NeedRomModifiedAck;
            if (!RecompressConfirmed)
                return RecompressPreflightResult.NeedConfirm;
            return RecompressPreflightResult.ProceedNoPrompt;
        }

        /// <summary>
        /// Run recompress: scan, iterate candidates, recompress each.
        /// Caller is responsible for calling <see cref="RecompressPreflight"/>
        /// first and handling confirmation via <see cref="RecompressConfirmed"/>.
        /// </summary>
        public (uint totalSize, uint totalCount) RunRecompress()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("Recompress: no ROM loaded.");
                return (0, 0);
            }
            var preflight = RecompressPreflight();
            if (preflight == RecompressPreflightResult.ErrorAlreadyShown)
                return (0, 0);
            if (preflight != RecompressPreflightResult.ProceedNoPrompt)
            {
                StatusText = R._("Recompress: pending user confirmation — see prompt.");
                return (0, 0);
            }

            uint totalSize = 0;
            uint totalCount = 0;
            try
            {
                List<Address> candidates = LZ77ToolCore.ScanForLZ77Candidates(rom);
                _undo.Begin("RecompressLZ77");
                foreach (var a in candidates)
                {
                    var r = LZ77ToolCore.RecompressAt(rom, a.Addr, a.Length);
                    if (r.Ok && r.SavedBytes > 0)
                    {
                        totalSize += r.SavedBytes;
                        totalCount++;
                    }
                }
                _undo.Commit();
            }
            catch (Exception ex)
            {
                if (_undo.HasPendingUndo)
                    try { _undo.Rollback(); } catch (Exception rbEx) { Log.ErrorF("Recompress rollback failed: {0}", rbEx.Message); }
                StatusText = R._("Recompress failed: {0}", ex.Message);
                return (0, 0);
            }
            finally
            {
                RecompressConfirmed = false;
                RecompressModifiedAcknowledged = false;
            }

            if (totalSize == 0)
            {
                StatusText = R._("Recompress: no savings — all entries already optimally compressed.");
            }
            else
            {
                StatusText = R._("Recompress: {0} entries recompressed, {1} bytes saved. Heuristic scan — may miss entries WinForms catches.",
                    totalCount, totalSize);
            }
            return (totalSize, totalCount);
        }
    }
}