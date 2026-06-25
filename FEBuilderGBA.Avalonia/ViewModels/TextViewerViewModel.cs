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
        /// Injectable prompt callback for the WF AntiHuffman flow (#1028 Slice D).
        /// Invoked by <see cref="WriteText"/> when Huffman encoding fails AND the
        /// AntiHuffman (un-Huffman) patch is NOT installed. The callback shows the
        /// <c>TextBadCharPopupView</c> with the encode-error message and returns
        /// <c>true</c> when the patch is installed after the prompt (the WF
        /// re-check), <c>false</c> to abort. Kept as a callback so the VM stays
        /// synchronous + UI-free — the View owns the modal dialog, exactly per the
        /// Copilot-accepted plan amendment #3. When unset (e.g. headless tests),
        /// WriteText treats the prompt as a "still missing" abort.
        /// </summary>
        public Func<string, bool>? AntiHuffmanPromptCallback { get; set; }

        /// <summary>
        /// Thrown by <see cref="WriteText"/> to signal a WF-faithful abort with NO
        /// ROM mutation when the bad-character text could not be encoded and the
        /// AntiHuffman patch remained uninstalled after the prompt (or the user
        /// chose GiveUp). The View distinguishes this from a real failure so it can
        /// roll back the undo scope without leaving any partial write.
        /// </summary>
        public sealed class EncodeAbortedException : InvalidOperationException
        {
            public EncodeAbortedException(string message) : base(message) { }
        }

        /// <summary>
        /// Whether a text-slot pointer value points into IW-RAM / EW-RAM
        /// (raw 0x02/0x03 or the unHuffman-patched 0x82/0x83 forms). Faithful
        /// port of WinForms <c>TextForm.Is_RAMPointerArea(uint addr)</c>
        /// (TextForm.cs:335-342). Such slots hold runtime-RAM text installed by
        /// patches; WinForms <c>WriteText</c> REFUSES to write to them
        /// (TextForm.cs:466-470 → <c>U.NOT_FOUND</c>) rather than silently
        /// repointing the slot to fresh ROM text. <see cref="WriteText"/> uses
        /// this to abort with no mutation (#1425).
        /// </summary>
        internal static bool Is_RAMPointerArea(uint addr)
        {
            return U.is_03RAMPointer(addr)
                || FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(addr)
                || U.is_02RAMPointer(addr)
                || FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(addr);
        }

        /// <summary>
        /// Read-only (no-mutation) check of whether the CURRENT pointer stored in
        /// text slot <paramref name="id"/> targets IW/EW-RAM — i.e. whether
        /// <see cref="WriteText"/> would refuse it via <see cref="Is_RAMPointerArea"/>.
        /// The View calls this BEFORE its bad-character / AntiHuffman pre-flight so a
        /// RAM-pointer slot is refused first (matching WF order: RAM check → encode),
        /// without ever showing the bad-char popup or Patch Manager. Returns false
        /// when the ROM is unloaded or the slot is out of range (WriteText then
        /// surfaces the appropriate error). #1425.
        /// </summary>
        public bool IsCurrentSlotRamPointer(uint id)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null) return false;

            uint textBase = ResolveTextTableBase();
            if (textBase == 0) return false;

            uint writePointer = textBase + (id * 4);
            if (writePointer + 4 > (uint)rom.Data.Length || !U.isSafetyOffset(writePointer, rom))
                return false;

            return Is_RAMPointerArea(rom.u32(writePointer));
        }

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
        /// Find all ROM entries that reference the given text ID (#1027:
        /// definitive). Combines the descriptive per-table rows from
        /// <see cref="TextReferenceFinder.Find"/> (units, classes, items, map
        /// settings, support talks, haiku, battle talks, sound room, ED, OP class
        /// demo, status options, units menu, world-map points, dictionary,
        /// final-chapter lines, senseki, map-terrain) with the FULL
        /// TEXTID-filtered union from <see cref="MakeVarsIDArrayCore.BuildAllUsedRefs"/>
        /// — which now also folds in EventCond scripts, menu-definition / status
        /// R-menu chains, worldmap events, installed-patch refs and asmmap symbol
        /// refs. When the id is referenced ONLY by one of those non-descriptor
        /// sources, a generic "Referenced (event/menu/patch/symbol)" row is added
        /// so the count is never falsely zero.
        ///
        /// Mirrors WinForms <c>TextForm.UpdateRef</c> (TargetType == TEXTID filter
        /// + UseTextIDCache.MakeUseTextID comment row).
        /// </summary>
        public List<string> FindCrossReferences(uint textId)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null) return new List<string>();

                var tables = TextRefTableRegistry.BuildForRom(rom);
                var refs = TextReferenceFinder.Find(rom, textId, tables);

                // #1027 — full TEXTID-filtered union (EventCond/menu/worldmap/
                // patch/asmmap refs now included). If the descriptor Find produced
                // NO row, fall back to the full union and surface a generic row when
                // the id is referenced there (matches WF UpdateRef, which lists every
                // TargetType==TEXTID UseValsID). Build the union ONLY when refs is
                // empty — it does a full ROM-wide scan (EventCond + patch dir +
                // asmmap files), so running it on every text selection would lag the
                // UI (Copilot review). The common selected-text case already has a
                // descriptor row and skips this entirely.
                if (textId != 0 && refs.Count == 0)
                {
                    try
                    {
                        // GetCachedUsedRefs caches the union per ROM instance (WF's
                        // cached GetVarsIDArray), so repeated selections of texts that
                        // are referenced only via EventCond/asmmap/patch don't re-run
                        // the full ROM-wide scan each time.
                        var used = MakeVarsIDArrayCore.GetCachedUsedRefs(rom);
                        if (used.TextIds.Contains(textId))
                        {
                            refs.Add(R._("Referenced (event/menu/patch/symbol)"));
                        }
                    }
                    catch (Exception ex2)
                    {
                        Log.Error("TextViewerViewModel.FindCrossReferences union: {0}", ex2.Message);
                    }
                }

                // #1028 Slice A review fix: mirror WinForms TextForm.UpdateRef,
                // which appends the user/system text-id reference comment from
                // Program.UseTextIDCache.MakeUseTextID(id) as an extra ref row.
                string comment = CoreState.UseTextIDCache?.GetName(textId) ?? "";
                if (comment.Length > 0)
                {
                    refs.Add(R._("Reference: {0}", comment));
                }
                return refs;
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
        /// Pre-flight (no-mutation) Huffman-encode check for the WF AntiHuffman
        /// flow (#1028 Slice D). Returns the encode-error string (the offending
        /// characters) when the text cannot be Huffman-encoded, or <c>null</c>
        /// when it encodes cleanly. The View calls this before <see cref="WriteText"/>
        /// so it can show the (async) <c>TextBadCharPopupView</c> off the UI thread,
        /// then let WriteText do the WF-faithful re-check synchronously. Reads no
        /// ROM state and writes nothing.
        /// </summary>
        public string? PeekEncodeError(string text)
        {
            if (CoreState.FETextEncoder == null) return null;
            string escaped = ConvertFEditorToEscape(text);
            string error = CoreState.FETextEncoder.Encode(escaped, out _);
            return (error != null && error.Length > 0) ? error : null;
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

            // Resolve the write slot up-front so the RAM-pointer guard below runs
            // BEFORE encoding, exactly like WF TextForm.WriteText (range check →
            // Is_RAMPointerArea → Encode; TextForm.cs:460-473).
            uint textBase = ResolveTextTableBase();
            if (textBase == 0)
                throw new InvalidOperationException("Invalid text pointer table.");

            uint writePointer = textBase + (id * 4);
            if (writePointer + 4 > (uint)rom.Data.Length || !U.isSafetyOffset(writePointer, rom))
                throw new InvalidOperationException($"Text ID 0x{id:X} out of range.");

            // #1425 — WF TextForm.WriteText RAM-pointer write guard
            // (TextForm.cs:466-470). If the CURRENT slot pointer points into
            // IW/EW-RAM (runtime-RAM text installed by patches), REFUSE the write
            // exactly like WF returning U.NOT_FOUND — do NOT silently repoint it to
            // fresh ROM text. This must run BEFORE encoding / the AntiHuffman prompt
            // path and before any ROM mutation, so a RAM slot is refused without
            // ever invoking the bad-char popup or touching the ROM (byte-identical).
            // The View catches EncodeAbortedException, rolls back the undo scope, and
            // shows the message — the WF-faithful no-mutation abort path.
            if (Is_RAMPointerArea(rom.u32(writePointer)))
                throw new EncodeAbortedException(
                    R._("RAMエリアのため、書き込めません.\r\nTextID:{0}", U.To0xHexString(id)));

            // Convert FEditor display format back to internal escape codes
            string escaped = ConvertFEditorToEscape(text);

            // Huffman-encode. WF TextForm.WriteText flow (#1028 Slice D): on an
            // encode failure, check the AntiHuffman patch; if it's missing, prompt
            // (TextBadCharPopupView via the injected callback) and RE-CHECK; if it
            // is still missing, ABORT with NO ROM mutation; only then UnHuffman-
            // encode. The check/prompt/abort happens BEFORE any write below so a
            // GiveUp / still-missing path leaves the ROM byte-identical.
            byte[] encoded;
            bool useUnHuffman = false;
            string error = CoreState.FETextEncoder.Encode(escaped, out encoded);
            if (error != null && error.Length > 0)
            {
                bool useAntiHuffman = PatchDetection.SearchAntiHuffmanPatch(rom);
                if (!useAntiHuffman)
                {
                    // Patch missing — prompt the user (View shows the popup),
                    // then re-check. The callback returns true when the patch is
                    // installed after the prompt (WF re-check); a null callback
                    // (headless) or a false result is treated as "still missing".
                    bool installedAfterPrompt =
                        AntiHuffmanPromptCallback != null && AntiHuffmanPromptCallback(error);
                    useAntiHuffman = installedAfterPrompt && PatchDetection.SearchAntiHuffmanPatch(rom);
                    if (!useAntiHuffman)
                    {
                        // ABORT — no mutation, exactly like WF returning U.NOT_FOUND.
                        throw new EncodeAbortedException(
                            R._("文字:{0}はシステムに登録されていません。", error));
                    }
                }
                // Patch present (or freshly installed) — UnHuffman-encode.
                CoreState.FETextEncoder.UnHuffmanEncode(escaped, out encoded);
                useUnHuffman = true;
            }

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
        /// #1027 result of the definitive free-area scan: the unreferenced text
        /// slots plus a flag indicating whether the scan was DEFINITIVE (the
        /// event-scan prerequisites were met) or whether it could not run because
        /// those prerequisites were missing (in which case the list is empty and
        /// the View shows a status instead of a misleading partial list).
        /// </summary>
        public sealed class FreeAreaScanResult
        {
            public bool IsDefinitive { get; init; }
            public List<AddrResult> Results { get; init; } = new List<AddrResult>();
        }

        /// <summary>
        /// #1027 — DEFINITIVE free-area scan. Faithful port of WinForms
        /// <c>TextForm.SearcFreeArea_Click</c>: builds the full "used text id"
        /// union via <see cref="MakeVarsIDArrayCore.BuildFreeAreaUsedSet"/> (units,
        /// classes, items, EventCond scripts, menu / status-rmenu chains, worldmap
        /// events, support/battle/haiku/sound/ED tables, skills, installed-patch
        /// refs, asmmap symbol refs, AND the user/system/FE8-reserved cache ids)
        /// and returns the complement — text slots whose decoded text is non-empty
        /// and NOT in the union.
        ///
        /// When the event-scan prerequisites are NOT met (the ROM is not the active
        /// <see cref="CoreState.ROM"/>, or EventScript / CommentCache are unwired),
        /// the union would be INCOMPLETE — turning referenced texts into
        /// false-positive "free" results — so the scan reports
        /// <see cref="FreeAreaScanResult.IsDefinitive"/> = false with an EMPTY list.
        ///
        /// Output invariants: <c>addr = textBase + id * 4</c> (matches
        /// <see cref="LoadTextList"/>) so address dispatch is uniform.
        /// </summary>
        public FreeAreaScanResult FindUnreferencedTexts()
        {
            var results = new List<AddrResult>();
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null || rom.Data == null)
                    return new FreeAreaScanResult { IsDefinitive = false };

                var scan = TextFreeAreaCore.FindUnreferencedTextIds(rom, CoreState.UseTextIDCache);
                if (scan.Status != TextFreeAreaCore.ScanStatus.Definitive)
                    return new FreeAreaScanResult { IsDefinitive = false };

                uint textBase = TextFreeAreaCore.ResolveTextTableBase(rom);
                foreach (uint id in scan.FreeTextIds)
                {
                    uint entryAddr = textBase + id * 4u;

                    string preview;
                    try
                    {
                        string decoded = FETextDecode.Direct(id) ?? "";
                        string ftt = ConvertEscapeToFEditor(EscapeRawControlChars(decoded));
                        ftt = StripControlChars(ftt) ?? "";
                        preview = ftt.Length > 40 ? ftt.Substring(0, 40) + "..." : ftt;
                    }
                    catch { preview = ""; }

                    string name = U.ToHexString(id) + " (free) " + preview;
                    results.Add(new AddrResult(entryAddr, name, id));
                }
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerViewModel.FindUnreferencedTexts: {0}", ex.Message);
                return new FreeAreaScanResult { IsDefinitive = false };
            }
            return new FreeAreaScanResult { IsDefinitive = true, Results = results };
        }

        /// <summary>
        /// Back-compat shim for the pre-#1027 callers/tests. Returns the definitive
        /// scan's results when prerequisites are met, otherwise an empty list. New
        /// callers should use <see cref="FindUnreferencedTexts"/> so they can
        /// surface the prerequisites-missing status.
        /// </summary>
        public List<AddrResult> FindApproximatelyUnreferencedTexts()
        {
            return FindUnreferencedTexts().Results;
        }

        /// <summary>
        /// Export all ROM texts to a TSV file via TranslateCore.
        /// </summary>
        public int ExportAllTexts(string path)
        {
            return ExportAllTexts(path, includeAIHints: false, filterIndex: 0);
        }

        /// <summary>
        /// Back-compat 2-arg overload (no export filter = All).
        /// </summary>
        public int ExportAllTexts(string path, bool includeAIHints)
        {
            return ExportAllTexts(path, includeAIHints, filterIndex: 0);
        }

        /// <summary>
        /// Export ROM texts to a TSV file. When <paramref name="includeAIHints"/>
        /// is true, the AI-translation hint block (WF
        /// <c>ToolTranslateROM.AppendAIHintMessage</c>) for each entry is appended
        /// to that row's Text column — escaped like the rest of the column (CR/LF
        /// flattened to <c>\n</c>) so TSV columns are NOT corrupted. The hints are
        /// the unit translate-info lines for every face the text loads (#1028 Slice C).
        ///
        /// <paramref name="filterIndex"/> applies the WF Export Filter category
        /// (#1028 Slice B): 0 / invalid = All (no filter); 1..10 keep only the
        /// text ids that <see cref="ExportFilterCore.BuildFilteredTextIds"/>
        /// collects for that category, mirroring WF
        /// <c>ToolTranslateROM.InitExportFilter</c> + the per-form MakeVarsIDArray
        /// methods exactly.
        /// </summary>
        public int ExportAllTexts(string path, bool includeAIHints, int filterIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null) return 0;

            var entries = TranslateCore.DumpTexts(rom);

            // #1028 Slice B: apply the Export Filter category. null => All.
            HashSet<uint> keep = ExportFilterCore.BuildFilteredTextIds(rom, filterIndex);
            if (keep != null)
            {
                entries = entries.FindAll(e => keep.Contains(e.textId));
            }

            if (includeAIHints)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    string text = entries[i].text ?? "";
                    // WF AppendAIHintMessage operates on the escape-converted text
                    // (FEditorAdv [LoadFace][0xXXX] / engine @0010@XXX]); convert
                    // first so the face-load escapes are detectable.
                    string converted = ToolTranslateROMCore.ConvertEscapeText(text);
                    string hint = ToolTranslateROMCore.AppendAIHintMessage(rom, converted);
                    if (!string.IsNullOrEmpty(hint))
                    {
                        // Append the hint block to the Text column. ExportToTSV
                        // flattens CR/LF to \n on write, so columns stay intact.
                        entries[i] = (entries[i].textId, text + hint);
                    }
                }
            }

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
