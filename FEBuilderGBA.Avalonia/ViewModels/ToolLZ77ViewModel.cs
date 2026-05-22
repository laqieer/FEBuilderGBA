using System;
using System.Collections.Generic;
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
    /// </summary>
    public class ToolLZ77ViewModel : ViewModelBase
    {
        public const string THIS_ROM = "<<THIS ROM>>";

        string _decompressSrcPath = THIS_ROM;
        string _decompressDestPath = string.Empty;
        uint _decompressAddress;
        string _compressSrcPath = string.Empty;
        string _compressDestPath = string.Empty;
        uint _zeroClearFrom;
        uint _zeroClearTo;
        string _base64Text = string.Empty;
        string _statusText = string.Empty;
        bool _isBusy;
        bool _isLoaded;

        readonly UndoService _undo = new();

        public string DecompressSrcPath { get => _decompressSrcPath; set => SetField(ref _decompressSrcPath, value); }
        public string DecompressDestPath { get => _decompressDestPath; set => SetField(ref _decompressDestPath, value); }
        public uint DecompressAddress { get => _decompressAddress; set => SetField(ref _decompressAddress, value); }
        public string CompressSrcPath { get => _compressSrcPath; set => SetField(ref _compressSrcPath, value); }
        public string CompressDestPath { get => _compressDestPath; set => SetField(ref _compressDestPath, value); }
        public uint ZeroClearFrom { get => _zeroClearFrom; set => SetField(ref _zeroClearFrom, value); }
        public uint ZeroClearTo { get => _zeroClearTo; set => SetField(ref _zeroClearTo, value); }
        public string Base64Text { get => _base64Text; set => SetField(ref _base64Text, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Initialize()
        {
            DecompressSrcPath = THIS_ROM;
            IsLoaded = true;
        }

        /// <summary>Stub LoadList for editor-list compatibility. LZ77 tool has no list of entries.</summary>
        public List<AddrResult> LoadList()
        {
            return new List<AddrResult>();
        }

        // ---------- Decompress ----------

        /// <summary>Decompress LZ77 data from the current ROM (THIS_ROM) or from a file.</summary>
        public void RunDecompress()
        {
            if (string.IsNullOrWhiteSpace(DecompressDestPath))
            {
                StatusText = R._("Decompress: destination path required.");
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

            uint addr = U.toOffset(DecompressAddress);
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

        // ---------- Compress ----------

        /// <summary>Compress a file into LZ77 format.</summary>
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

        // ---------- Zero Clear (Erase) ----------

        /// <summary>Zero-fill a region of the loaded ROM. Undo-tracked.</summary>
        public void RunZeroClear()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                StatusText = R._("ZeroClear: no ROM loaded.");
                return;
            }

            uint from = U.toOffset(ZeroClearFrom);
            uint to = U.toOffset(ZeroClearTo);
            if (to < from)
            {
                uint tmp = from;
                from = to;
                to = tmp;
            }
            uint size = to - from;

            if (!IsAddressInRom(rom, from) || !IsAddressInRom(rom, to))
            {
                StatusText = R._("ZeroClear: address out of ROM bounds.");
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
                StatusText = R._("ZeroClear failed: {0}", ex.Message);
            }
        }

        static bool IsAddressInRom(ROM rom, uint offset)
        {
            return offset < (uint)rom.Data.Length;
        }

        // ---------- Base64 (Plain) ----------

        /// <summary>Decode the current Base64Text into binary and write to a file.</summary>
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

            // Mirror WinForms ToolLZ77Form.Base64TextToFileButton_Click behavior:
            // Trim() + space → '+' to forgive pasted strings where '+' was URL-encoded as space.
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

        /// <summary>Read a file and populate Base64Text with the base64 representation.</summary>
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
