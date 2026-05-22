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

        public bool ZeroClearNeedsConfirmation(uint from, uint to)
        {
            var rom = CoreState.ROM;
            if (rom == null) return false;
            uint borderline = rom.RomInfo.compress_image_borderline_address;
            return from < borderline || to < borderline;
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
                    try { _undo.Rollback(); } catch (Exception rollbackEx) { Log.Error("ZeroClear rollback failed: {0}", rollbackEx.Message); }
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
    }
}