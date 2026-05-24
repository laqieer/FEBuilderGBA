using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class TextViewerViewModel : ViewModelBase
    {
        uint _currentId;
        string _decodedText = "";
        string _editText = "";
        bool _canWrite;
        int _encodedLength;
        int _originalLength;
        string _lengthWarning = "";
        List<string> _crossReferences = new();

        public uint CurrentId { get => _currentId; set => SetField(ref _currentId, value); }
        public string DecodedText { get => _decodedText; set => SetField(ref _decodedText, value); }
        public string EditText { get => _editText; set => SetField(ref _editText, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public int EncodedLength { get => _encodedLength; set => SetField(ref _encodedLength, value); }
        public int OriginalLength { get => _originalLength; set => SetField(ref _originalLength, value); }
        public string LengthWarning { get => _lengthWarning; set => SetField(ref _lengthWarning, value); }
        public List<string> CrossReferences { get => _crossReferences; set => SetField(ref _crossReferences, value); }

        /// <summary>
        /// Check whether a text pointer value is valid: standard ROM pointer,
        /// UnHuffman-patched pointer, or RAM pointer (IW-RAM / EW-RAM).
        /// Mirrors WinForms TextForm logic.
        /// </summary>
        static bool IsValidTextPointer(uint p)
        {
            if (U.isPointerOrNULL(p)) return true;
            if (FETextEncode.IsUnHuffmanPatchPointer(p)) return true;
            // RAM pointer areas used by some patches
            if (U.is_03RAMPointer(p) || FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(p)) return true;
            if (U.is_02RAMPointer(p) || FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(p)) return true;
            return false;
        }

        /// <summary>
        /// Resolve the text-pointer table base address, mirroring WinForms
        /// <c>TextForm.Init()</c> recovery: try `rom.p32(rom.RomInfo.text_pointer)`;
        /// if the dereferenced address fails <see cref="U.isSafetyOffset(uint, ROM)"/>,
        /// fall back to <c>rom.RomInfo.text_recover_address</c>. Returns 0
        /// when neither path yields a safe offset (ROM unloaded, missing
        /// RomInfo, or both pointers unsafe).
        ///
        /// Used by <see cref="LoadTextList"/>, <see cref="ComputeOriginalEncodedLength"/>,
        /// <see cref="WriteText"/>, <see cref="SearchTexts"/>,
        /// <see cref="FindApproximatelyUnreferencedTexts"/>, and the view's
        /// `OnTextSelected` selection routing. Keeping all consumers on a
        /// single helper ensures the recovery path is uniform.
        /// </summary>
        public uint ResolveTextTableBase()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null) return 0;

            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return 0;
            if (ptr + 4 > (uint)rom.Data.Length) return 0;

            uint baseAddr = rom.p32(ptr);
            if (U.isSafetyOffset(baseAddr, rom)) return baseAddr;

            // Recovery fallback (matches WF TextForm.Init() behavior).
            uint recover = rom.RomInfo.text_recover_address;
            if (recover != 0 && U.isSafetyOffset(recover, rom)) return recover;

            return 0;
        }

        public List<AddrResult> LoadTextList()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null) return new List<AddrResult>();

                uint baseAddr = ResolveTextTableBase();
                if (baseAddr == 0) return new List<AddrResult>();

                var result = new List<AddrResult>();
                for (uint i = 0; i < 0x2000; i++) // reasonable max
                {
                    uint entryAddr = (uint)(baseAddr + i * 4);
                    // Bounds check: need 4 bytes for the pointer read
                    if (entryAddr + 4 > (uint)rom.Data.Length) break;

                    uint textPtr = rom.u32(entryAddr);
                    if (!IsValidTextPointer(textPtr)) break;

                    string preview;
                    try
                    {
                        string decoded = FETextDecode.Direct(i);
                        if (decoded != null)
                            decoded = ConvertEscapeToFEditor(EscapeRawControlChars(decoded));
                        if (decoded != null)
                            decoded = StripControlChars(decoded);
                        if (decoded != null && decoded.Length > 40)
                            decoded = decoded.Substring(0, 40) + "...";
                        preview = decoded ?? "";
                    }
                    catch
                    {
                        preview = "";
                    }

                    string name = U.ToHexString(i) + " " + preview;
                    result.Add(new AddrResult(entryAddr, name, i));
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerViewModel.LoadTextList", ex.ToString());
                return new List<AddrResult>();
            }
        }

        public void LoadText(uint id)
        {
            CurrentId = id;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null)
                {
                    DecodedText = "(no ROM loaded)";
                    CanWrite = false;
                    return;
                }

                string raw = FETextDecode.Direct(id) ?? "(empty)";
                DecodedText = ConvertEscapeToFEditor(EscapeRawControlChars(raw));
                CanWrite = true;

                // Compute original encoded length
                OriginalLength = ComputeOriginalEncodedLength(id);
                // Validate current text
                ValidateText(DecodedText);
                // Find cross-references
                CrossReferences = FindCrossReferences(id);
            }
            catch (Exception ex)
            {
                DecodedText = "(decode error)";
                CanWrite = false;
                Log.Error("TextViewerViewModel.LoadText", ex.ToString());
            }
        }

        /// <summary>
        /// Compute the encoded byte length of the current text stored in ROM for the given text ID.
        /// </summary>
        int ComputeOriginalEncodedLength(uint id)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null) return 0;

                uint textBase = ResolveTextTableBase();
                if (textBase == 0) return 0;

                uint writePointer = textBase + (id * 4);
                if (writePointer + 4 > (uint)rom.Data.Length) return 0;
                if (!U.isSafetyOffset(writePointer, rom)) return 0;

                uint currentPointerValue = rom.u32(writePointer);
                bool currentIsUnHuffman = FETextEncode.IsUnHuffmanPatchPointer(currentPointerValue);
                uint currentDataAddr;
                if (currentIsUnHuffman)
                    currentDataAddr = U.toOffset(FETextEncode.ConvertUnHuffmanPatchToPointer(currentPointerValue));
                else if (U.isPointer(currentPointerValue))
                    currentDataAddr = U.toOffset(currentPointerValue);
                else
                    return 0;

                if (currentDataAddr == 0 || !U.isSafetyOffset(currentDataAddr, rom))
                    return 0;

                var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
                int dataSize;
                if (currentIsUnHuffman)
                    decoder.UnHffmanPatchDecode(currentDataAddr, out dataSize);
                else
                    decoder.Decode(id, out dataSize);
                return Math.Max(0, dataSize);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Validate the given text by encoding it and comparing to original length.
        /// Updates EncodedLength and LengthWarning properties.
        /// </summary>
        public void ValidateText(string text)
        {
            if (CoreState.FETextEncoder == null || string.IsNullOrEmpty(text))
            {
                EncodedLength = 0;
                LengthWarning = "";
                return;
            }

            try
            {
                string escaped = ConvertFEditorToEscape(text);
                byte[] encoded;
                string error = CoreState.FETextEncoder.Encode(escaped, out encoded);
                if (error != null && error.Length > 0)
                {
                    // Try UnHuffman fallback
                    CoreState.FETextEncoder.UnHuffmanEncode(escaped, out encoded);
                }

                int len = encoded?.Length ?? 0;
                EncodedLength = len;

                if (OriginalLength > 0 && len > OriginalLength)
                    LengthWarning = $"Encoded: {len} bytes (original: {OriginalLength} bytes) - EXCEEDS ORIGINAL";
                else if (OriginalLength > 0)
                    LengthWarning = $"Encoded: {len} bytes (original: {OriginalLength} bytes)";
                else
                    LengthWarning = $"Encoded: {len} bytes";
            }
            catch (Exception ex)
            {
                EncodedLength = 0;
                LengthWarning = $"Encoding error: {ex.Message}";
            }
        }

        /// <summary>
        /// Find all ROM entries that reference the given text ID. Delegates to
        /// <see cref="TextReferenceFinder.Find"/> with the per-version
        /// descriptor list assembled by
        /// <see cref="TextRefTableRegistry.BuildForRom"/>.
        ///
        /// Coverage (mirrors WinForms <c>U.MakeVarsIDArray</c> for fixed-table
        /// descriptors): units, classes, items, map settings, support talks,
        /// event haiku, battle talks (both main and 2 for FE6/7), sound room,
        /// ED screens (FE7 includes ed_3c Lyn-ending direct base), OP class
        /// demo, status options, units menu, world-map points (FE8),
        /// dictionary entries (FE8), final-chapter lines + senseki comments
        /// (FE7), map-terrain names (US/EU builds).
        ///
        /// Out of scope (deferred — tracked separately):
        /// recursive event-script scanning (EventCond), menu-definition
        /// pointer chains, patch-defined text-ID parameters, status R-menu
        /// linked lists, FE7 haiku tutorial event pointers.
        /// </summary>
        public List<string> FindCrossReferences(uint textId)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null) return new List<string>();

                var tables = TextRefTableRegistry.BuildForRom(rom);
                return TextReferenceFinder.Find(rom, textId, tables);
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerViewModel.FindCrossReferences: {0}", ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Convert @XXXX escape codes to human-readable [Name] format.
        /// Delegates to the shared <see cref="Services.TextDisplayFormatter"/>
        /// so the conversation viewer tab renders identical strings.
        /// </summary>
        static string ConvertEscapeToFEditor(string str)
            => Services.TextDisplayFormatter.ConvertEscapeToFEditor(str);

        /// <summary>
        /// Strip raw non-printable control characters (0x00-0x1F) that weren't
        /// converted to @XXXX escape codes by FETextDecode.
        /// </summary>
        static string StripControlChars(string str)
            => Services.TextDisplayFormatter.StripControlChars(str);

        /// <summary>
        /// Convert raw control characters (0x00-0x1F) to @XXXX escape format
        /// so they can be processed by ConvertEscapeToFEditor and get proper
        /// names from the TextEscape table (e.g., 0x1F to @001F to [.]).
        /// </summary>
        static string EscapeRawControlChars(string str)
            => Services.TextDisplayFormatter.EscapeRawControlChars(str);

        /// <summary>
        /// Convert FEditor [Name] codes back to @XXXX escape format.
        /// Reverse of ConvertEscapeToFEditor(). Mirrors WinForms TextForm.ConvertFEditorToEscape().
        /// </summary>
        public static string ConvertFEditorToEscape(string str)
        {
            // Handle [LoadFace][0xXXX] → @0010@0XXX
            str = RegexCache.Replace(str, @"\[LoadFace\]\[0x00([0-9A-F][0-9A-F][0-9A-F])\]", "@0010@0$1");
            str = RegexCache.Replace(str, @"\[LoadFace\]\[0x([0-9A-F][0-9A-F][0-9A-F])\]", "@0010@0$1");
            // Convert named codes back via table
            if (CoreState.TextEscape != null)
                str = CoreState.TextEscape.table_replace_rev(str);
            // Strip [N] and [X] markers
            str = str.Replace("[N]", "");
            str = str.Replace("[X]", "");
            // Convert remaining [0xXXXX] codes back to @XXXX
            str = RegexCache.Replace(str, @"\[0x([0-9A-F])\]", "@000$1");
            str = RegexCache.Replace(str, @"\[0x([0-9A-F][0-9A-F])\]", "@00$1");
            str = RegexCache.Replace(str, @"\[0x([0-9A-F][0-9A-F][0-9A-F])\]", "@0$1");
            str = RegexCache.Replace(str, @"\[0x([0-9A-F][0-9A-F][0-9A-F][0-9A-F])\]", "@$1");
            return str;
        }

        /// <summary>
        /// Write edited text back to ROM for the given text ID.
        /// Converts FEditor format to escape codes, Huffman-encodes, and writes to ROM.
        /// </summary>
        public void WriteText(uint id, string text)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                throw new InvalidOperationException("No ROM loaded.");
            if (CoreState.FETextEncoder == null)
                throw new InvalidOperationException("Text encoder not initialized.");

            // Convert FEditor display format back to internal escape codes
            string escaped = ConvertFEditorToEscape(text);

            // Huffman-encode
            byte[] encoded;
            bool useUnHuffman = false;
            string error = CoreState.FETextEncoder.Encode(escaped, out encoded);
            if (error != null && error.Length > 0)
            {
                // Huffman encoding failed — try UnHuffman fallback
                CoreState.FETextEncoder.UnHuffmanEncode(escaped, out encoded);
                useUnHuffman = true;
            }

            uint textBase = ResolveTextTableBase();
            if (textBase == 0)
                throw new InvalidOperationException("Invalid text pointer table.");

            uint writePointer = textBase + (id * 4);
            if (writePointer + 4 > (uint)rom.Data.Length || !U.isSafetyOffset(writePointer, rom))
                throw new InvalidOperationException($"Text ID 0x{id:X} out of range.");

            if (encoded == null || encoded.Length == 0)
            {
                // Empty text: point to same as text ID 0
                if (textBase + 4 > (uint)rom.Data.Length)
                    throw new InvalidOperationException("Text base pointer out of ROM bounds.");
                uint text0Pointer = rom.u32(textBase);
                rom.write_u32(writePointer, text0Pointer);
                return;
            }

            // Get original data size by decoding current text
            if (writePointer + 4 > (uint)rom.Data.Length)
                throw new InvalidOperationException($"Text ID 0x{id:X} pointer out of ROM bounds.");
            uint currentPointerValue = rom.u32(writePointer);
            uint originalSize = 0;
            bool currentIsUnHuffman = FETextEncode.IsUnHuffmanPatchPointer(currentPointerValue);
            uint currentDataAddr;
            if (currentIsUnHuffman)
                currentDataAddr = U.toOffset(FETextEncode.ConvertUnHuffmanPatchToPointer(currentPointerValue));
            else if (U.isPointer(currentPointerValue))
                currentDataAddr = U.toOffset(currentPointerValue);
            else
                currentDataAddr = 0;

            if (currentDataAddr > 0 && U.isSafetyOffset(currentDataAddr, rom))
            {
                try
                {
                    var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
                    int dataSize;
                    if (currentIsUnHuffman)
                        decoder.UnHffmanPatchDecode(currentDataAddr, out dataSize);
                    else
                        decoder.Decode(id, out dataSize);
                    originalSize = (uint)Math.Max(0, dataSize);
                }
                catch
                {
                    originalSize = 0;
                }
            }

            if (originalSize > 20000)
            {
                originalSize = 0;
                currentDataAddr = 0;
            }

            if (currentDataAddr > 0 && originalSize >= (uint)encoded.Length)
            {
                // Fits in original space — overwrite in place
                rom.write_range(currentDataAddr, encoded);
                if (originalSize > (uint)encoded.Length)
                    rom.write_fill(currentDataAddr + (uint)encoded.Length, originalSize - (uint)encoded.Length, 0x00);
                if (useUnHuffman)
                    rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(currentDataAddr)));
                else
                    rom.write_u32(writePointer, U.toPointer(currentDataAddr));
            }
            else
            {
                // Need new space — append to ROM end
                uint paddedSize = U.Padding4((uint)encoded.Length) + 4;
                uint newAddr = U.Padding4((uint)rom.Data.Length);

                if (newAddr + paddedSize >= 0x02000000)
                    throw new InvalidOperationException("ROM would exceed 32MB limit.");

                rom.write_resize_data(newAddr + paddedSize);

                // Clear old data if valid
                if (currentDataAddr > 0 && originalSize > 0 && U.isSafetyOffset(currentDataAddr, rom))
                    rom.write_fill(currentDataAddr, originalSize, 0x00);

                rom.write_range(newAddr, encoded);

                if (useUnHuffman)
                    rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(newAddr)));
                else
                    rom.write_p32(writePointer, newAddr);
            }
        }

        /// <summary>
        /// Search all text entries for content containing the given query.
        /// Returns a filtered list of matching entries.
        /// </summary>
        public List<AddrResult> SearchTexts(string query)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null || string.IsNullOrWhiteSpace(query))
                    return new List<AddrResult>();

                uint baseAddr = ResolveTextTableBase();
                if (baseAddr == 0) return new List<AddrResult>();

                var result = new List<AddrResult>();
                for (uint i = 0; i < 0x2000; i++)
                {
                    uint entryAddr = (uint)(baseAddr + i * 4);
                    // Bounds check: need 4 bytes for the pointer read
                    if (entryAddr + 4 > (uint)rom.Data.Length) break;

                    uint textPtr = rom.u32(entryAddr);
                    if (!IsValidTextPointer(textPtr)) break;

                    try
                    {
                        string decoded = FETextDecode.Direct(i);
                        if (decoded == null) continue;
                        decoded = ConvertEscapeToFEditor(EscapeRawControlChars(decoded));

                        if (decoded.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            string preview = StripControlChars(decoded);
                            if (preview != null && preview.Length > 40)
                                preview = preview.Substring(0, 40) + "...";
                            result.Add(new AddrResult(entryAddr, $"{U.ToHexString(i)} {preview}", i));
                        }
                    }
                    catch (Exception ex) { Log.Error("TextViewerViewModel.SearchTexts text decode: {0}", ex.Message); }
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerViewModel.SearchTexts", ex.ToString());
                return new List<AddrResult>();
            }
        }

        public int GetListCount() => LoadTextList().Count;

        /// <summary>
        /// System-reserved text-id range check, mirrors WinForms
        /// <c>TextForm.IsSystemReserve(textid)</c>:
        /// <list type="bullet">
        ///   <item>FE8 reserves IDs <c>0xE00..0xFFF</c> (patch text-string slots)</item>
        ///   <item>FE7 reserves IDs <c>0x1E00..0x1FFF</c></item>
        /// </list>
        /// Used by <see cref="FindApproximatelyUnreferencedTexts"/> to exclude
        /// system-reserved IDs from the "free area" result list (these are
        /// reserved for patches even when nothing currently references them).
        /// </summary>
        public static bool IsSystemReserve(ROM rom, uint textid)
        {
            if (rom?.RomInfo == null) return false;
            int version = rom.RomInfo.version;
            if (version == 8)
            {
                if (textid >= 0xE00 && textid <= 0xFFF) return true;
            }
            else if (version == 7)
            {
                if (textid >= 0x1E00 && textid <= 0x1FFF) return true;
            }
            return false;
        }

        /// <summary>
        /// Issue #404 free-area scan: find text IDs whose decoded text is
        /// non-empty AND whose ID is NOT referenced by any descriptor-defined
        /// ROM table. Mirrors WinForms <c>TextForm.SearcFreeArea_Click</c> in
        /// intent but is APPROXIMATE — the descriptor set (built by
        /// <see cref="TextRefTableRegistry.BuildForRom"/>) excludes EventCond
        /// scripts, patch-defined text parameters, menu-definition chains,
        /// status R-menu linked lists, and FE7 haiku tutorial event pointers.
        /// WF's implementation also uses <c>AsmMapFileAsmCache.GetVarsIDArray()</c>
        /// and <c>UseTextIDCache.AppendList()</c>, neither of which is yet
        /// ported to Avalonia (the Core <c>IAsmMapCache</c> has no
        /// <c>GetVarsIDArray</c> method; <c>UseTextIDCache</c> is an
        /// <c>object</c> placeholder in <c>CoreState</c>). Until those Core
        /// extractions land, this method should be considered a USEFUL HEURISTIC
        /// (catches obviously-orphaned text slots in vanilla ROMs) but NOT a
        /// definitive list.
        ///
        /// One-pass algorithm (vs the obvious O(N×M) per-ID Find):
        ///   1. Build the descriptor table set ONCE via TextRefTableRegistry.
        ///   2. Call <see cref="TextReferenceFinder.CollectReferencedTextIds"/>
        ///      to accumulate every referenced text ID into a HashSet&lt;uint&gt;
        ///      using the SAME traversal as <see cref="TextReferenceFinder.Find"/>
        ///      (PointerField vs DirectBase, safety floor, fitting-count clamp,
        ///      terminator predicate).
        ///   3. Iterate text IDs <c>1..loadedCount-1</c> (skip ID 0 = system
        ///      write-protect); for each non-system-reserved, non-empty ID
        ///      not in the referenced set, emit an <see cref="AddrResult"/>.
        ///
        /// Output invariants (asserted by `TextViewerParityTests`):
        ///   - <c>tag &lt; loadedCount</c> for every result
        ///   - <c>addr = textBase + tag * 4</c> (matches <see cref="LoadTextList"/>)
        ///   so <see cref="Controls.AddressListControl.SelectAddress"/> dispatch
        ///   works uniformly between the regular list and free-area results.
        /// </summary>
        public List<AddrResult> FindApproximatelyUnreferencedTexts()
        {
            var result = new List<AddrResult>();
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null) return result;

                uint textBase = ResolveTextTableBase();
                if (textBase == 0) return result;

                var tables = TextRefTableRegistry.BuildForRom(rom);
                var referencedIds = TextReferenceFinder.CollectReferencedTextIds(rom, tables);

                // Single-pass scan: walk the text pointer table, terminating
                // at the same condition `LoadTextList` uses (first invalid
                // text pointer). This avoids the previous double-decode
                // (LoadTextList counted + we decoded again per ID).
                for (uint id = 1; id < 0x2000u; id++)
                {
                    uint entryAddr = textBase + id * 4u;
                    if (entryAddr + 4 > (uint)rom.Data.Length) break;
                    uint textPtr = rom.u32(entryAddr);
                    if (!IsValidTextPointer(textPtr)) break;

                    if (IsSystemReserve(rom, id)) continue;
                    if (referencedIds.Contains(id)) continue;

                    // Decode once per surviving ID. Skip IDs whose decoded
                    // text is null/empty/whitespace — avoid false positives
                    // from huge runs of "empty" slots pointing at the same
                    // null string.
                    string decoded;
                    try { decoded = FETextDecode.Direct(id) ?? ""; }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(decoded)) continue;

                    string preview;
                    try
                    {
                        string ftt = ConvertEscapeToFEditor(EscapeRawControlChars(decoded));
                        ftt = StripControlChars(ftt) ?? "";
                        preview = ftt.Length > 40 ? ftt.Substring(0, 40) + "..." : ftt;
                    }
                    catch { preview = ""; }

                    string name = U.ToHexString(id) + " (free) " + preview;
                    result.Add(new AddrResult(entryAddr, name, id));
                }
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerViewModel.FindApproximatelyUnreferencedTexts: {0}", ex.Message);
            }
            return result;
        }

        /// <summary>
        /// Export all ROM texts to a TSV file via TranslateCore.
        /// </summary>
        public int ExportAllTexts(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null) return 0;

            var entries = TranslateCore.DumpTexts(rom);
            TranslateCore.ExportToTSV(entries, path);
            return entries.Count;
        }

        /// <summary>
        /// Import texts from a TSV file and write them back to ROM.
        /// </summary>
        public int ImportAllTexts(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null) return 0;

            var entries = TranslateCore.ImportFromTSV(path);
            if (entries.Count == 0) return 0;

            return TranslateCore.WriteTexts(rom, entries);
        }
    }
}
