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
        // All four stay false until a real cross-ROM match is computed — the
        // earlier implementation incorrectly raised them from current-ROM
        // state, which produced false positives unrelated to "direct match".
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
        /// <summary>
        /// Byte-swapped GBA pointer at the address, formatted as a single
        /// hex value (e.g. <c>0x45230108</c> for the pointer
        /// <c>0x08012345</c>). Matches WF
        /// <c>PointerToolForm.SearchCurrentROM</c> which stores the
        /// little-endian representation as a uint via
        /// <c>SetAddressText(this.LittleEndian, littleendian)</c>, where
        /// <c>littleendian</c> is the byte-swapped pointer. Avalonia
        /// previously rendered "AA BB CC DD" spaced bytes; the spaced
        /// format broke the double-click navigation (parser couldn't lift
        /// a uint out of it). The single-hex format matches WF and parses
        /// cleanly for the AddressDoubleClick handler.
        /// </summary>
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
            if (text.Length == 0) return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }

        /// <summary>Run pointer search: populate PointerValue, LittleEndianValue, DataAddress, and SearchResults.</summary>
        public void RunSearch()
        {
            if (!TryParseAddress(out uint rawInput))
            {
                SearchResults = "Invalid address.";
                // Copilot CLI review point 4: clear stale cross-ROM results on
                // every early-return path so a previous successful match never
                // lingers after the current input becomes invalid.
                ClearOtherRomFields();
                return;
            }

            var rom = CoreState.ROM;
            if (rom == null)
            {
                SearchResults = "No ROM loaded.";
                ClearOtherRomFields();
                return;
            }

            // WF PointerToolForm.SearchCurrentROM accepts EITHER a raw ROM
            // offset (e.g. 0x100) OR a GBA pointer (e.g. 0x08000100). It
            // normalizes via U.toPointer / U.toOffset so the same path works
            // for both. Avalonia must mirror that — otherwise a pointer-form
            // input ("0x08000100") would double-add the base and SearchPointer
            // would search for 0x10000100.
            uint addr = U.toOffset(rawInput);    // ROM offset (always < 0x02000000)
            uint pointer = U.toPointer(rawInput); // GBA pointer (always 0x08xxxxxx)
            PointerValue = $"0x{pointer:X08}";
            // Mirrors WF:
            //   littleendian = ((pointer >> 24) & 0xFF)
            //                | (((pointer >> 16) & 0xFF) << 8)
            //                | (((pointer >> 8 ) & 0xFF) << 16)
            //                | (((pointer      ) & 0xFF) << 24)
            uint littleEndian =
                  ((pointer >> 24) & 0xFFu)
                | (((pointer >> 16) & 0xFFu) << 8)
                | (((pointer >> 8) & 0xFFu) << 16)
                | (((pointer) & 0xFFu) << 24);
            LittleEndianValue = $"0x{littleEndian:X08}";

            if (addr + 3 < (uint)rom.Data.Length)
            {
                uint val = rom.u32(addr);
                // If the value at address looks like a pointer, show target
                if (val >= 0x08000000 && val < 0x0A000000)
                    DataAddress = $"0x{(val - 0x08000000):X08}";
                else
                    DataAddress = "";
            }

            // Search for all pointers referencing this address. SearchPointer
            // takes an OFFSET (not a pointer) — use the normalized form.
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

            // Cross-ROM search (#966): grep the other-ROM buffer for raw +
            // LDR references to this address and populate the OtherROM* fields
            // plus the four per-result warning flags. When no other ROM is
            // loaded, SearchOtherRom clears all four fields + warnings so the
            // #438 false-positive guard holds (warnings only meaningful once a
            // real cross-ROM search ran). `addr` is the normalized ROM offset
            // of the user-entered address — the SAME target SearchPointer used
            // above (Copilot CLI review point 1: NOT the pointee at addr).
            SearchOtherRom(addr);
        }

        /// <summary>
        /// Cross-ROM pointer search (#966). Greps the loaded other-ROM buffer
        /// for raw 32-bit pointer references and ARM-Thumb LDR literal-pool
        /// references to <paramref name="needAddr"/> (the normalized ROM offset
        /// of the user-entered address), then populates the four OtherROM*
        /// fields and the four per-result warning flags.
        ///
        /// <para>Cross-platform adaptation of WF
        /// <c>PointerToolForm.FindOtherROMData</c> /
        /// <c>FindOtherROMDataWithLDR</c>, reusing the Core seams ported under
        /// #781:</para>
        /// <list type="bullet">
        ///   <item><see cref="U.GrepPointerAll(byte[],uint,uint,uint)"/> — raw
        ///   4-byte-aligned pointer references. The first hit's offset is the
        ///   reference pointer (<see cref="OtherRomRefPointer"/>); the searched
        ///   data address is shown as <see cref="OtherRomAddress"/>.</item>
        ///   <item><see cref="U.GrepPointerAllOnLDR(byte[],uint)"/> — LDR
        ///   literal-pool SLOT offsets (NOT the LDR instruction address). The
        ///   first hit's slot is <see cref="OtherRomLdrRefPointer"/>; the data
        ///   address is <see cref="OtherRomLdrAddress"/>.</item>
        /// </list>
        ///
        /// <para>Warnings follow WF semantics (Copilot CLI review point 2): the
        /// ERROR_ZERO / ERROR_VERYFAR labels are evaluated ONLY when a match was
        /// found (WF <c>IsDataFound</c> gates on <c>IsFoundAddress</c>). On a
        /// no-match / danger-zone / no-other-ROM path the fields are cleared and
        /// the warnings stay false — there is no "ZERO on no-match" branch.</para>
        ///
        /// <para>Bounds (Copilot CLI review point 3): the other-ROM display /
        /// warning guards check <c>_otherRomData.Length</c> explicitly — NOT
        /// <c>U.isSafetyOffset</c> (which checks <c>CoreState.ROM.Data.Length</c>).
        /// The grep helpers scan the other-ROM buffer safely on their own.</para>
        /// </summary>
        void SearchOtherRom(uint needAddr)
        {
            // No other ROM loaded -> no cross-ROM search possible. Clear any
            // stale fields + warnings (Copilot CLI review point 4).
            if (_otherRomData == null || _otherRomData.Length == 0)
            {
                ClearOtherRomFields();
                return;
            }

            // Danger-zone guard: the GBA header / low addresses below 0x200 are
            // never a meaningful pointer target. Mirrors the WF safety floor
            // (U.isSafetyOffset uses >= 0x200). We deliberately do NOT call
            // U.isSafetyOffset here because that overload validates against the
            // CURRENT ROM length, not the other ROM (Copilot CLI review point 3).
            if (needAddr < 0x200)
            {
                ClearOtherRomFields();
                return;
            }

            try
            {
                // ----- Raw 32-bit pointer references in the other ROM ---------
                var raw = U.GrepPointerAll(_otherRomData, needAddr);
                if (raw.Count > 0)
                {
                    // OtherRomAddress = the data address we searched for (the
                    // address referenced in the other ROM); OtherRomRefPointer =
                    // the offset of the first reference. Mirrors WF
                    // OtherROMAddress2 / OtherROMRefPointer2.
                    OtherRomAddress = $"0x{needAddr:X08}";
                    OtherRomRefPointer = $"0x{raw[0]:X08}";
                    // Warnings evaluated against the found address (only when a
                    // match exists — WF IsDataFound parity).
                    HasZeroAtDirect = EvaluateOtherRomWarning(needAddr, out bool directFar);
                    HasVeryFarAtDirect = directFar;
                }
                else
                {
                    // No raw reference -> clear the raw fields; warnings stay
                    // false (WF hides the labels on no-match).
                    OtherRomAddress = "";
                    OtherRomRefPointer = "";
                    HasZeroAtDirect = false;
                    HasVeryFarAtDirect = false;
                }

                // ----- LDR literal-pool references in the other ROM -----------
                var ldr = U.GrepPointerAllOnLDR(_otherRomData, needAddr);
                if (ldr.Count > 0)
                {
                    OtherRomLdrAddress = $"0x{needAddr:X08}";
                    // ldr[0] is the literal-pool SLOT offset (where the matching
                    // pointer word lives) — NOT the LDR instruction address.
                    OtherRomLdrRefPointer = $"0x{ldr[0]:X08}";
                    HasZeroAtLdr = EvaluateOtherRomWarning(needAddr, out bool ldrFar);
                    HasVeryFarAtLdr = ldrFar;
                }
                else
                {
                    OtherRomLdrAddress = "";
                    OtherRomLdrRefPointer = "";
                    HasZeroAtLdr = false;
                    HasVeryFarAtLdr = false;
                }
            }
            catch (Exception ex)
            {
                // A grep must never throw on the UI thread. On any fault clear
                // the fields and surface a diagnostic rather than crashing.
                // Log.Error is params string[] (NO composite formatting) — use
                // a single interpolated string so the exception is actually
                // logged (#969 review point 3).
                ClearOtherRomFields();
                Log.Error($"PointerToolViewModel.SearchOtherRom: {ex}");
            }
        }

        /// <summary>Window size (bytes) WF samples for the zero-region check.</summary>
        const uint ZeroRegionWindow = 0x200;

        /// <summary>
        /// Evaluate the WF ERROR_ZERO / ERROR_VERYFAR warning pair against a
        /// cross-ROM match address found in the OTHER ROM. Returns the ZERO flag
        /// — the matched address sits in a zero-FILLED REGION (see
        /// <see cref="IsZeroRegion"/>); <paramref name="isFar"/> receives the
        /// VERYFAR flag (the matched address lies in the last quarter of the
        /// other ROM — a coarse "too far to be a real match" heuristic).
        ///
        /// <para>Bounds are checked against <c>_otherRomData.Length</c>
        /// explicitly (Copilot CLI plan-review point 3); an out-of-bounds match
        /// address yields both-false (no warning, no throw).</para>
        /// </summary>
        bool EvaluateOtherRomWarning(uint matchAddr, out bool isFar)
        {
            isFar = false;
            if (_otherRomData == null || _otherRomData.Length == 0) return false;
            if (matchAddr >= (uint)_otherRomData.Length) return false;
            isFar = matchAddr > (uint)(_otherRomData.Length * 3 / 4);
            return IsZeroRegion(_otherRomData, matchAddr, matchAddr + ZeroRegionWindow);
        }

        /// <summary>
        /// Port of WF <c>PointerToolForm.checkZeroData</c> (#969 review point
        /// 2): a region <c>[start, end)</c> is "zero" when MORE THAN HALF of its
        /// bytes are <c>0x00</c>. WF samples a 0x200-byte window from the match
        /// address. Bounds-safe: <c>end</c> is clamped to the buffer length, and
        /// a <c>start</c> beyond the buffer returns <c>false</c> — mirroring WF
        /// exactly so the Avalonia "Zero region" warning agrees with WF and with
        /// the label text.
        /// </summary>
        static bool IsZeroRegion(byte[] data, uint start, uint end)
        {
            if (data.Length < start) return false;
            if (data.Length < end) end = (uint)data.Length;
            if (start >= end) return false;

            int zeroCount = 0;
            for (uint i = start; i < end; i++)
            {
                if (data[i] == 0x0) zeroCount++;
            }
            return zeroCount > (int)(end - start) / 2;
        }

        /// <summary>
        /// Reset all four OtherROM* string fields and the four per-result
        /// warning flags. Called on every early-return / no-match path so a
        /// previous successful cross-ROM result never lingers (Copilot CLI
        /// review point 4).
        /// </summary>
        void ClearOtherRomFields()
        {
            OtherRomAddress = "";
            OtherRomRefPointer = "";
            OtherRomLdrAddress = "";
            OtherRomLdrRefPointer = "";
            HasZeroAtDirect = false;
            HasVeryFarAtDirect = false;
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
        /// Parse the WriteTargetInput as a ROM offset (or GBA pointer),
        /// convert to a GBA pointer (+ 0x08000000), and write the 4-byte value
        /// at the address specified by AddressInput. Mirrors WF, which
        /// normalises via <c>addr = U.toOffset(U.atoh(Address.Text))</c> so
        /// pointer-form input works the same as offset-form input.
        /// </summary>
        public void WritePointerValue()
        {
            if (!TryParseAddress(out uint rawInput))
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

            // Normalise to a ROM offset BEFORE the bounds check (mirrors
            // WF U.toOffset(U.atoh(Address.Text))). Without this, a
            // pointer-form input like 0x08000100 always fails the bounds
            // check and pointer-form Address fields never write.
            uint addr = U.toOffset(rawInput);
            if (addr + 3 >= (uint)rom.Data.Length)
            {
                SearchResults = "Write failed: address out of ROM range.";
                return;
            }

            // Parse the target value
            string targetText = (WriteTargetInput ?? "").Trim();
            if (targetText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                targetText = targetText.Substring(2);
            if (targetText.Length == 0 ||
                !uint.TryParse(targetText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint targetOffset))
            {
                SearchResults = "Write failed: invalid target address.";
                return;
            }

            // Convert ROM offset to GBA pointer format if it looks like a ROM
            // offset. Use U.toPointer so the conversion is symmetric with the
            // normalisation above (both work for either input form).
            uint writeVal = U.toPointer(targetOffset);

            // Validate the pointer references a valid ROM location
            uint romOffset = U.toOffset(writeVal);
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
        /// Screenshot-mode seeding hook (#966). The offscreen
        /// <c>--screenshot-all</c> harness has no file picker, so it cannot
        /// exercise <see cref="LoadOtherRom"/> normally — the OtherROM* fields
        /// would render empty. This helper seeds a representative cross-ROM
        /// state for the PNG by using the CURRENT ROM's own bytes as the "other
        /// ROM" and picking a real referenced address: it scans the live ROM for
        /// the first 4-byte-aligned pointer, derefs it to a data address that is
        /// guaranteed to have at least one raw reference, sets that as the input
        /// address, and runs the normal search so the raw OtherROM fields
        /// populate with genuine values. ONLY invoked from the screenshot path
        /// (gated by <c>App.ScreenshotAllMode</c> in the view's
        /// <c>SelectFirstItem</c>); the interactive runtime path is unchanged.
        /// </summary>
        /// <returns>True when a demo state was seeded; false when no ROM /
        /// no referenced address was available.</returns>
        public bool SeedDemoCrossRom()
        {
            var rom = CoreState.ROM;
            if (rom == null || rom.Data == null || rom.Data.Length < 0x400) return false;

            // Find a data address that is referenced by at least one pointer in
            // the live ROM. The simplest guaranteed-referenced address is the
            // target of the first valid pointer we can find: if offset R holds a
            // pointer to offset T, then T is referenced at R. We then search the
            // (current-ROM == other-ROM) buffer for references to T, which finds
            // at least R.
            uint targetOffset = 0;
            for (uint i = 0x200; i + 3 < (uint)rom.Data.Length; i += 4)
            {
                uint v = rom.u32(i);
                if (v >= 0x08000000 && v < 0x0A000000)
                {
                    uint off = v - 0x08000000;
                    // Must be a plausible, in-bounds, non-danger-zone target so
                    // the search produces a populated field.
                    if (off >= 0x200 && off + 3 < (uint)rom.Data.Length)
                    {
                        targetOffset = off;
                        break;
                    }
                }
            }
            if (targetOffset == 0) return false;

            // Use the current ROM bytes as the "other ROM" for the demo so the
            // cross-ROM grep finds the same references. This is screenshot-only.
            _otherRomData = rom.Data;
            _otherRomFilename = rom.Filename ?? "(current ROM)";
            OtherRomName = $"{Path.GetFileNameWithoutExtension(_otherRomFilename)} (demo)";

            AddressInput = $"0x{targetOffset:X08}";
            RunSearch();
            return true;
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolForm.WhatIsButton_Click</c> (#1026).
        /// Returns a human-readable hint describing the address: the region
        /// class (ROM / EWRAM / IWRAM / unknown) PLUS, when the loaded ASM/MAP
        /// symbol table resolves the address, the nearest symbol name.
        ///
        /// <para>The symbol lookup queries
        /// <c>CoreState.AsmMapFileAsmCache.GetAsmMapFile()</c> (the
        /// cross-platform <see cref="AsmMapSymbolFile"/> built by
        /// <see cref="CoreAsmMapCache"/>, or the full WF <c>AsmMapFile</c> when
        /// run under WinForms). On an EXACT pointer match it appends
        /// <c>p.ToStringInfo()</c>; otherwise it walks <c>SearchNear</c> and, if
        /// the address falls inside the nearest symbol's span, appends
        /// "<c>base+offset(0xHEX) name</c>" — verbatim WF
        /// <c>WhatIsButton_Click</c> formatting.</para>
        ///
        /// <para>Null-safe: with no cache or no symbol map (headless / empty
        /// ASM-map) it returns the region hint only and never throws. Full WF
        /// <c>AutoSearch</c> behavioural parity (auto-tracking retry,
        /// source/target LDR-map symmetry, ASM-map name search) remains a
        /// documented follow-up; this method covers the "What is this address?"
        /// symbol-name resolution only.</para>
        /// </summary>
        public string LookupAddressType(uint addr)
        {
            // WF: pointer = U.toPointer(addr); addr = U.toOffset(addr).
            uint pointer = U.toPointer(addr);
            uint offsetAddr = U.toOffset(addr);

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

            // Resolve the nearest ASM/MAP symbol name (mirrors WF
            // PointerToolForm.WhatIsButton_Click). Null-safe at every step.
            string symbol = "";
            try
            {
                var cache = CoreState.AsmMapFileAsmCache;
                var asmMap = cache?.GetAsmMapFile();
                if (asmMap != null)
                {
                    if (asmMap.TryGetValue(pointer, out var p) && p != null)
                    {
                        symbol = p.ToStringInfo();
                    }
                    else
                    {
                        uint near = asmMap.SearchNear(pointer);
                        if (near != U.NOT_FOUND
                            && asmMap.TryGetValue(near, out p)
                            && p != null
                            && pointer < (ulong)near + p.Length)
                        {
                            uint off = pointer - near;
                            symbol = $"{U.To0xHexString(U.toOffset(near))}+{off}(0x{off:X}) {p.ToStringInfo()}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // A symbol lookup must never crash the UI thread. Log and fall
                // back to the region hint only.
                Log.Error($"PointerToolViewModel.LookupAddressType: {ex}");
                symbol = "";
            }

            string result = $"Address 0x{offsetAddr:X08} (pointer 0x{pointer:X08}): {regionHint}.";
            if (symbol.Length > 0)
            {
                result += $" Symbol: {symbol}";
            }
            SearchResults = result;
            return result;
        }
    }
}
