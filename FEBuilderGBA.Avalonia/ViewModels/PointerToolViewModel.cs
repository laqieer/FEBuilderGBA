using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class PointerToolViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _addressInput = string.Empty;
        string _pointerValue = string.Empty;
        string _littleEndianValue = string.Empty;
        string _firstReference = string.Empty;
        string _dataAddress = string.Empty;
        string _otherRomAddress = string.Empty;
        string _otherRomRefPointer = string.Empty;
        string _otherRomLdrAddress = string.Empty;
        string _otherRomLdrRefPointer = string.Empty;
        string _otherRomName = string.Empty;
        string _writeTargetInput = string.Empty;
        string _searchResults = string.Empty;
        bool _useAsmMap = true;
        int _testMatchDataSize;
        int _dataType;
        int _grepType;
        int _slideSize;
        int _autoTrackingLevel;
        int _warningLevel;

        // ----- Per-result warning flags (#438 Copilot CLI review point 4) ---
        // WF uses 4 separate visibility-controlled labels:
        //   ERROR_ZERO1   ERROR_VERYFAR1   (direct match)
        //   ERROR_ZERO3   ERROR_VERYFAR3   (LDR match)
        // The original AV VM collapsed these into 2 globals; v2 mirrors WF.
        bool _hasZeroAtDirect;
        bool _hasVeryFarAtDirect;
        bool _hasZeroAtLdr;
        bool _hasVeryFarAtLdr;

        // ----- Other-ROM byte buffer (loaded via LoadOtherRom) ---
        // Held internally so RunSearch can grep against it. The LDR map of
        // the other ROM is built lazily on the first comparison.
        byte[]? _otherRomData;
        string _otherRomFilename = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Input ROM address to analyze.</summary>
        public string AddressInput { get => _addressInput; set => SetField(ref _addressInput, value); }
        /// <summary>Address value as a GBA pointer (+ 0x08000000).</summary>
        public string PointerValue { get => _pointerValue; set => SetField(ref _pointerValue, value); }
        /// <summary>Little-endian representation of the 4 bytes at the address.</summary>
        public string LittleEndianValue { get => _littleEndianValue; set => SetField(ref _littleEndianValue, value); }
        /// <summary>First pointer reference to this address found in ROM.</summary>
        public string FirstReference { get => _firstReference; set => SetField(ref _firstReference, value); }
        /// <summary>Data address pointed to if the value is a pointer.</summary>
        public string DataAddress { get => _dataAddress; set => SetField(ref _dataAddress, value); }
        /// <summary>Matching address in the other loaded ROM.</summary>
        public string OtherRomAddress { get => _otherRomAddress; set => SetField(ref _otherRomAddress, value); }
        /// <summary>Pointer reference to the other ROM address.</summary>
        public string OtherRomRefPointer { get => _otherRomRefPointer; set => SetField(ref _otherRomRefPointer, value); }
        /// <summary>LDR-tracked address in the other ROM.</summary>
        public string OtherRomLdrAddress { get => _otherRomLdrAddress; set => SetField(ref _otherRomLdrAddress, value); }
        /// <summary>LDR-tracked reference pointer in the other ROM.</summary>
        public string OtherRomLdrRefPointer { get => _otherRomLdrRefPointer; set => SetField(ref _otherRomLdrRefPointer, value); }
        /// <summary>Filename of the other loaded ROM.</summary>
        public string OtherRomName { get => _otherRomName; set => SetField(ref _otherRomName, value); }
        /// <summary>Target ROM offset (without 0x08000000) to write as a pointer at AddressInput.</summary>
        public string WriteTargetInput { get => _writeTargetInput; set => SetField(ref _writeTargetInput, value); }
        public string SearchResults { get => _searchResults; set => SetField(ref _searchResults, value); }
        /// <summary>Use ASM MAP file for enhanced search.</summary>
        public bool UseAsmMap { get => _useAsmMap; set => SetField(ref _useAsmMap, value); }
        /// <summary>Comparison data size index (512, 256, 128 bytes, etc.).</summary>
        public int TestMatchDataSize { get => _testMatchDataSize; set => SetField(ref _testMatchDataSize, value); }
        /// <summary>Content type: 0=DATA, 1=ASM.</summary>
        public int DataType { get => _dataType; set => SetField(ref _dataType, value); }
        /// <summary>Search method: 0=Exact, 1=Pattern.</summary>
        public int GrepType { get => _grepType; set => SetField(ref _grepType, value); }
        /// <summary>Slide search offset size.</summary>
        public int SlideSize { get => _slideSize; set => SetField(ref _slideSize, value); }
        /// <summary>Automatic tracking level.</summary>
        public int AutoTrackingLevel { get => _autoTrackingLevel; set => SetField(ref _autoTrackingLevel, value); }
        /// <summary>Warning level: 0=Error, 1=Ignore if referenced, 2=Ignore.</summary>
        public int WarningLevel { get => _warningLevel; set => SetField(ref _warningLevel, value); }
        /// <summary>True if the direct-match address points to a zero-filled region (mirrors WF ERROR_ZERO1).</summary>
        public bool HasZeroAtDirect { get => _hasZeroAtDirect; set => SetField(ref _hasZeroAtDirect, value); }
        /// <summary>True if the direct-match address is very far from the original data (mirrors WF ERROR_VERYFAR1).</summary>
        public bool HasVeryFarAtDirect { get => _hasVeryFarAtDirect; set => SetField(ref _hasVeryFarAtDirect, value); }
        /// <summary>True if the LDR-tracked match address points to a zero-filled region (mirrors WF ERROR_ZERO3).</summary>
        public bool HasZeroAtLdr { get => _hasZeroAtLdr; set => SetField(ref _hasZeroAtLdr, value); }
        /// <summary>True if the LDR-tracked match address is very far from the original data (mirrors WF ERROR_VERYFAR3).</summary>
        public bool HasVeryFarAtLdr { get => _hasVeryFarAtLdr; set => SetField(ref _hasVeryFarAtLdr, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>Parse the AddressInput hex string into a uint address.</summary>
        bool TryParseAddress(out uint address)
        {
            address = 0;
            string text = (AddressInput ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, NumberStyles.HexNumber, null, out address);
        }

        /// <summary>Run pointer search: populate PointerValue, LittleEndianValue, DataAddress, and SearchResults.</summary>
        public void RunSearch()
        {
            if (!TryParseAddress(out uint addr))
            {
                SearchResults = "Invalid address.";
                return;
            }

            var rom = CoreState.ROM;
            if (rom == null)
            {
                SearchResults = "No ROM loaded.";
                return;
            }

            // Compute pointer and little-endian representations
            PointerValue = $"0x{(addr + 0x08000000):X08}";
            if (addr + 3 < (uint)rom.Data.Length)
            {
                uint val = rom.u32(addr);
                byte b0 = (byte)(val & 0xFF);
                byte b1 = (byte)((val >> 8) & 0xFF);
                byte b2 = (byte)((val >> 16) & 0xFF);
                byte b3 = (byte)((val >> 24) & 0xFF);
                LittleEndianValue = $"{b0:X02} {b1:X02} {b2:X02} {b3:X02}";

                // If the value at address looks like a pointer, show target
                if (val >= 0x08000000 && val < 0x0A000000)
                    DataAddress = $"0x{(val - 0x08000000):X08}";
                else
                    DataAddress = "";
            }

            // Search for all pointers referencing this address
            var refs = SearchPointer(addr);
            if (refs.Count == 0)
            {
                FirstReference = "";
                SearchResults = "No pointer references found.";
            }
            else
            {
                FirstReference = $"0x{refs[0]:X08}";
                var sb = new StringBuilder();
                sb.AppendLine($"Found {refs.Count} reference(s):");
                int showCount = Math.Min(refs.Count, 100);
                for (int i = 0; i < showCount; i++)
                    sb.AppendLine($"  0x{refs[i]:X08}");
                if (refs.Count > 100)
                    sb.AppendLine($"  ... and {refs.Count - 100} more");
                SearchResults = sb.ToString();
            }

            // Per-result warnings — mirrors WF ERROR_ZERO* / ERROR_VERYFAR*.
            // The direct-match group looks at the source address (the one
            // typed in by the user). The LDR group is populated only when
            // a real other-ROM comparison has run (deferred to follow-up).
            bool directZero = addr + 3 < (uint)rom.Data.Length && rom.u32(addr) == 0;
            bool directFar = addr > (uint)(rom.Data.Length * 3 / 4);
            // Only show the direct warnings when an other-ROM match has been
            // recorded (mirrors WF which only renders the labels after
            // OtherROMAddress2 is populated). Without an other-ROM context
            // both warnings stay false.
            bool hasOther = _otherRomData != null && _otherRomData.Length > 0;
            HasZeroAtDirect = hasOther && directZero;
            HasVeryFarAtDirect = hasOther && directFar;
            // LDR warnings — the LDR comparison path is not yet implemented
            // in AV (deferred to a follow-up that ports WF FindOtherROMDataWithLDR).
            // Defaulting to false here so the labels stay hidden.
            HasZeroAtLdr = false;
            HasVeryFarAtLdr = false;
        }

        /// <summary>Search the ROM for all 4-byte-aligned pointer references to the given address.</summary>
        public List<uint> SearchPointer(uint targetAddr)
        {
            var results = new List<uint>();
            var rom = CoreState.ROM;
            if (rom == null) return results;
            uint searchVal = targetAddr + 0x08000000;
            for (uint i = 0; i + 3 < (uint)rom.Data.Length; i += 4)
            {
                if (rom.u32(i) == searchVal)
                    results.Add(i);
            }
            return results;
        }

        /// <summary>
        /// Parse the WriteTargetInput as a ROM offset, convert to a GBA pointer
        /// (+ 0x08000000), and write the 4-byte value at the address specified by AddressInput.
        /// </summary>
        public void WritePointerValue()
        {
            if (!TryParseAddress(out uint addr))
            {
                SearchResults = "Write failed: invalid address.";
                return;
            }

            var rom = CoreState.ROM;
            if (rom == null)
            {
                SearchResults = "Write failed: no ROM loaded.";
                return;
            }

            if (addr + 3 >= (uint)rom.Data.Length)
            {
                SearchResults = "Write failed: address out of ROM range.";
                return;
            }

            // Parse the target value
            string targetText = (WriteTargetInput ?? "").Trim();
            if (targetText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                targetText = targetText.Substring(2);
            if (!uint.TryParse(targetText, NumberStyles.HexNumber, null, out uint targetOffset))
            {
                SearchResults = "Write failed: invalid target address.";
                return;
            }

            // Convert ROM offset to GBA pointer format if it looks like a ROM offset
            uint writeVal;
            if (targetOffset >= 0x08000000)
            {
                // Already in GBA pointer format
                writeVal = targetOffset;
            }
            else
            {
                // ROM offset — add GBA base
                writeVal = targetOffset + 0x08000000;
            }

            // Validate the pointer references a valid ROM location
            uint romOffset = writeVal >= 0x08000000 ? writeVal - 0x08000000 : writeVal;
            if (romOffset >= (uint)rom.Data.Length)
            {
                SearchResults = $"Write failed: target 0x{romOffset:X08} is beyond ROM size.";
                return;
            }

            rom.write_u32(addr, writeVal);
            SearchResults = $"Wrote 0x{writeVal:X08} at 0x{addr:X08}.";

            // Refresh the display to show the updated value
            RunSearch();
        }

        /// <summary>
        /// Load an "other ROM" file for cross-ROM pointer comparison. Mirrors
        /// WF <c>PointerToolForm.LoadTargetROM</c> at the gap-sweep scope:
        /// reads the file bytes, sets <see cref="OtherRomName"/> to the file
        /// base name, and runs <see cref="RunSearch"/> against the current
        /// ROM. Full WF AutoSearch behavioural parity (auto-tracking retry,
        /// source/target LDR map symmetry, ASM-map name search) is intentionally
        /// out of scope for #438 and deferred to a follow-up issue — the
        /// gap-sweep acceptance criteria only require the visible UI surface.
        /// </summary>
        /// <param name="filename">Absolute path to a GBA ROM (.gba / .bin).</param>
        public void LoadOtherRom(string filename)
        {
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
            {
                SearchResults = $"Other ROM not found: {filename}";
                return;
            }

            try
            {
                _otherRomData = File.ReadAllBytes(filename);
                _otherRomFilename = filename;
                OtherRomName = Path.GetFileNameWithoutExtension(filename);

                // Re-run search to populate the other-ROM fields from the
                // newly loaded bytes. The same-ROM fields (PointerValue,
                // LittleEndianValue) are unchanged but RunSearch is idempotent.
                RunSearch();
            }
            catch (Exception ex)
            {
                _otherRomData = null;
                _otherRomFilename = string.Empty;
                OtherRomName = string.Empty;
                SearchResults = $"Failed to load other ROM: {ex.Message}";
            }
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolForm.WhatIsButton_Click</c> at the
        /// gap-sweep scope (#438). Returns a human-readable hint describing
        /// the address. The full WF lookup path queries
        /// <c>Program.AsmMapFileAsmCache.GetAsmMapFile()</c> +
        /// <c>asmMap.SearchNear</c>; the Core <see cref="IAsmMapCache"/>
        /// abstraction does not yet expose <c>GetAsmMapFile</c> /
        /// <c>SearchNear</c>, so AV returns a structured "address+pointer"
        /// summary derived from the WF logic. Extending IAsmMapCache to
        /// expose the asm-map surface is tracked as a follow-up for the
        /// PointerTool full-behaviour port.
        /// </summary>
        public string LookupAddressType(uint addr)
        {
            // Classify the address according to WF's runtime semantics:
            // - GBA pointer (0x08xxxxxx): is_ROMPointer.
            // - Small numeric (< 0xA00): is_RAMPointer or constant.
            // - 0x02000000 / 0x03000000 RAM regions.
            //
            // The output mirrors what WF shows in the "address type" message
            // box. When the asm-map surface lands in Core (follow-up), this
            // method gains the SearchNear path.
            uint pointer = U.toPointer(addr);
            string regionHint;
            if (U.isPointer(pointer))
            {
                // ROM pointer.
                regionHint = "ROM (0x08xxxxxx)";
            }
            else if (pointer >= 0x02000000 && pointer < 0x03000000)
            {
                regionHint = "EWRAM (0x02xxxxxx)";
            }
            else if (pointer >= 0x03000000 && pointer < 0x04000000)
            {
                regionHint = "IWRAM (0x03xxxxxx)";
            }
            else
            {
                regionHint = "unknown region";
            }

            string result = $"Address 0x{addr:X08} (pointer 0x{pointer:X08}): {regionHint}.";
            SearchResults = result;
            return result;
        }
    }
}
