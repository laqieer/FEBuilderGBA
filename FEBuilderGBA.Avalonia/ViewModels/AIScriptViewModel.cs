// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptViewModel — Avalonia parity rebuild for #410. Mirrors
// `AIScriptForm` (panel3 filter + panel6 list + panel5 write + ControlPanel
// detail). Per Copilot CLI plan-review v2 #2, this is a `partial class` so
// `AIScriptViewModel.NavigationTargets.cs` can extend with the
// INavigationTargetSource manifest. Per Copilot v2 #3, AI1 and AI2 are
// separate pointer tables — FilterIndex toggles between
// `RomInfo.ai1_pointer` (0) and `RomInfo.ai2_pointer` (1), no combined list.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class AIScriptViewModel : ViewModelBase
    {
        // ----------------------------------------------------------------
        // Backing fields
        // ----------------------------------------------------------------

        int _filterIndex;
        uint _topAddress;
        uint _readCount;
        uint _currentAddr;
        uint _baseAddr;
        uint _readByteCount;
        string _commentText = "";
        string _asmText = "";
        string _scriptCodeName = "";
        bool _isLoaded;
        bool _isLoading;

        // Display-only disassembly backing state. Populated by
        // DisassembleScript() (#757) and edited in place by the per-row /
        // structural helpers; opcode write-back IS implemented in
        // WriteScript() (#760), which serializes these rows back to the ROM.
        // These two lists are the display-only backing model for the
        // Disassembly list — the canonical edit state, not a deferred stub.
        readonly List<EventScript.OneCode> _disassembled = new();
        readonly List<uint> _rowOffsets = new();

        // ----------------------------------------------------------------
        // Public observable properties
        // ----------------------------------------------------------------

        /// <summary>0 = AI1 pointer table; 1 = AI2 pointer table.</summary>
        public int FilterIndex
        {
            get => _filterIndex;
            set => SetField(ref _filterIndex, value);
        }

        /// <summary>Top address used by the read-config bar reload.</summary>
        public uint TopAddress
        {
            get => _topAddress;
            set => SetField(ref _topAddress, value);
        }

        /// <summary>Number of pointer slots to read in the master list.</summary>
        public uint ReadCount
        {
            get => _readCount;
            set => SetField(ref _readCount, value);
        }

        /// <summary>The currently loaded script address (after pointer resolution).</summary>
        public uint CurrentAddr
        {
            get => _currentAddr;
            set => SetField(ref _currentAddr, value);
        }

        /// <summary>The address of the pointer slot in the AI table.</summary>
        public uint BaseAddr
        {
            get => _baseAddr;
            set => SetField(ref _baseAddr, value);
        }

        /// <summary>Script body length in bytes (CalcLength of the loaded script).</summary>
        public uint ReadByteCount
        {
            get => _readByteCount;
            set => SetField(ref _readByteCount, value);
        }

        /// <summary>The comment text for the currently selected opcode.</summary>
        public string CommentText
        {
            get => _commentText;
            set => SetField(ref _commentText, value);
        }

        /// <summary>The hex-dump form of the currently selected opcode bytes.</summary>
        public string AsmText
        {
            get => _asmText;
            set => SetField(ref _asmText, value);
        }

        /// <summary>The human-readable script command name for the current opcode.</summary>
        public string ScriptCodeName
        {
            get => _scriptCodeName;
            set => SetField(ref _scriptCodeName, value);
        }

        /// <summary>True once an entry has been loaded from the address list.</summary>
        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetField(ref _isLoaded, value);
        }

        /// <summary>True while LoadList / LoadEntry are running (suppresses dirty flags).</summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetField(ref _isLoading, value);
        }

        // ----------------------------------------------------------------
        // List loaders (FilterIndex-aware)
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the address-list entries for the currently selected AI
        /// table (0 = AI1, 1 = AI2). Per Copilot CLI plan-review v2 #3 the
        /// tables are separate — no combined list. Mirrors WF
        /// AIScriptForm.Init's pointer-walk with the WF validity check:
        /// non-pointer / non-null = stop, table-not-expanded stops at
        /// the known AI1/AI2 entry count (PR #571 Copilot bot review #4 —
        /// avoid over-enumerating into unrelated data on unexpanded ROMs).
        ///
        /// When ReadCount > 0 it also clamps the result to that many
        /// entries so a user-edited "Read Count" actually limits the
        /// scan (parity with WF panel3 ReadCount).
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint tablePointer = _filterIndex == 1
                ? rom.RomInfo.ai2_pointer
                : rom.RomInfo.ai1_pointer;
            if (tablePointer == 0) return result;

            // If the user set TopAddress, scan from there; otherwise
            // resolve the pointer to the in-ROM table base.
            uint baseAddr;
            if (_topAddress != 0 && U.isSafetyOffset(_topAddress))
                baseAddr = U.toOffset(_topAddress);
            else
                baseAddr = rom.p32(tablePointer);

            if (!U.isSafetyOffset(baseAddr))
                return result;

            // WF AIScriptForm.Init's validity check: for an unexpanded
            // table (baseAddr matches the resolved pointer), stop at the
            // configured AI1/AI2 count from EventUnitForm. For an
            // expanded ROM region, scan until terminator. Replicate by
            // computing the WF count cap when applicable.
            uint? unexpandedCap = null;
            try
            {
                // Mirror WF U.isExtrendsROMArea inline (Core has no port yet).
                uint extendsAddr = rom.RomInfo.extends_address;
                bool isInExpandedArea = extendsAddr != 0
                    && baseAddr >= U.toOffset(extendsAddr);
                bool isUnexpanded = !isInExpandedArea
                                 && baseAddr == rom.p32(tablePointer);
                if (isUnexpanded)
                {
                    // EventUnitForm.AI1/AI2 counts live in WinForms-only
                    // code; for the Core / VM here we cap conservatively
                    // at the known maximum (256) to avoid over-enumeration
                    // while still permitting the full WF entry set.
                    unexpandedCap = 256;
                }
            }
            catch
            {
                // Fall back to no cap if the synthetic ROM lacks
                // extends_address wiring.
            }

            uint maxEntries = unexpandedCap ?? 4096;
            // Honor user-driven ReadCount as a soft cap (when > 0).
            if (_readCount > 0 && _readCount < maxEntries)
                maxEntries = _readCount;

            for (uint i = 0; i < maxEntries; i++)
            {
                uint slotAddr = baseAddr + i * 4;
                if (!U.isSafetyOffset(slotAddr + 3))
                    break;

                uint slot = rom.u32(slotAddr);
                if (slot == 0xFFFFFFFF)
                    break;
                if (!U.isPointerOrNULL(slot))
                    break;

                string name = $"{i:X02} {(_filterIndex == 1 ? "AI2" : "AI1")}";
                result.Add(new AddrResult(slotAddr, name, i));
            }

            return result;
        }

        /// <summary>
        /// Read the AI script at the given pointer-slot address. The slot
        /// holds a 32-bit pointer to the script body; this method follows
        /// it, computes the byte length via AIScript.CalcLength, and
        /// populates the header fields.
        /// </summary>
        public void LoadEntry(uint pointerSlotAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Loading a (possibly different) entry invalidates the editable
            // model: clear it so a stale disassembly from a previously-loaded
            // entry can't be serialized into / repointed onto this one (Copilot
            // review — stale-model data-loss path). The View re-disassembles for
            // the newly-loaded entry immediately after; until then HasDisassembly
            // is false and Write/New/Remove are blocked.
            _disassembled.Clear();
            _rowOffsets.Clear();

            BaseAddr = pointerSlotAddr;
            if (!U.isSafetyOffset(pointerSlotAddr + 3))
            {
                CurrentAddr = 0;
                ReadByteCount = 0;
                IsLoaded = false;
                return;
            }

            uint scriptAddr = rom.p32(pointerSlotAddr);
            if (!U.isSafetyOffset(scriptAddr))
            {
                CurrentAddr = 0;
                ReadByteCount = 0;
                IsLoaded = false;
                return;
            }

            CurrentAddr = scriptAddr;
            ReadByteCount = CalcScriptLength(scriptAddr);
            IsLoaded = true;
        }

        /// <summary>
        /// Compute the length of the AI script at the given address. AI
        /// scripts use 16-byte instructions terminated by opcode 0x03
        /// (EXIT), optionally followed by 0x1B / 0x1C continuation
        /// instructions. Mirrors WF AIScriptForm.CalcLength.
        /// </summary>
        public static uint CalcScriptLength(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return 0;

            uint start = addr;
            uint limit = (uint)rom.Data.Length;
            while (addr + 16 <= limit)
            {
                uint code = rom.u8(addr + 0);
                addr += 16;
                if (addr + 16 > limit)
                    break;
                if (code == 0x03)
                {
                    // EXIT - check if followed by 0x1B / 0x1C continuation.
                    uint nextcode = rom.u8(addr + 0);
                    if (nextcode != 0x1B && nextcode != 0x1C)
                        break;
                    addr += 16;
                }
            }
            return addr - start;
        }

        // ----------------------------------------------------------------
        // Disassembly (display-only, #757)
        // ----------------------------------------------------------------

        /// <summary>
        /// Disassemble the currently-loaded AI script into human-readable
        /// display lines (mnemonic + decoded args + comment), replacing the
        /// raw hex dump the Avalonia editor used to show. Mirrors the
        /// EventScriptViewModel.DisassembleAt rendering loop, but walks the
        /// FIXED 16-byte AI instruction grid: WinForms AIScriptForm.CalcLength
        /// steps by 16 and the shared CoreState.AIScript is built with
        /// `new EventScript(16)` so an unrecognized opcode decodes as ONE
        /// 16-byte WORD row (not four 4-byte rows).
        ///
        /// This is display-only: it populates the backing _disassembled /
        /// _rowOffsets lists for possible future row selection but does NOT
        /// touch any edit/write state. The byte range to walk is the
        /// [CurrentAddr, CurrentAddr + ReadByteCount) window computed by
        /// CalcScriptLength, which already encodes the EXIT / continuation
        /// semantics — so we deliberately do NOT early-break on
        /// ScriptHas.TERM and always advance by the 16-byte grid step.
        /// </summary>
        public IReadOnlyList<string> DisassembleScript()
        {
            _disassembled.Clear();
            _rowOffsets.Clear();

            var lines = new List<string>();

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null || !IsLoaded || CurrentAddr == 0)
                return lines;

            // Ensure the shared AI EventScript is loaded. Mirror
            // EventScriptViewModel's lazy-load guard, but with the AI
            // unknown-opcode width of 16 (#757).
            EventScript es = CoreState.AIScript;
            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    es = new EventScript(16);
                    es.Load(EventScript.EventScriptType.AI);
                    CoreState.AIScript = es;
                }
                catch (Exception ex)
                {
                    lines.Add($"[Error loading AI script definitions: {ex.Message}]");
                    return lines;
                }
            }

            // Clamp the end to the ROM length with overflow-safe (ulong)
            // arithmetic — a hand-typed CurrentAddr + ReadByteCount near
            // uint.MaxValue must NOT wrap (mirrors the old view guard's
            // (ulong) cast that this method subsumes).
            uint end = (uint)Math.Min(
                (ulong)CurrentAddr + ReadByteCount, (ulong)rom.Data.Length);

            // Walk COMPLETE 16-byte slots only. `stopped` tracks an early bail
            // (disassembly error / decode failure) so we don't tack a bogus
            // "partial" row onto a run we terminated mid-stream. The loop
            // bound uses (ulong) so `off + 16` can't wrap past end.
            uint off = CurrentAddr;
            bool stopped = false;
            for (; (ulong)off + 16 <= end; off += 16)
            {
                EventScript.OneCode code;
                try
                {
                    code = es.DisAseemble(rom.Data, off);
                }
                catch
                {
                    lines.Add($"0x{off:X06}: [Disassembly error]");
                    stopped = true;
                    break;
                }

                if (code == null || code.ByteData == null)
                {
                    lines.Add($"0x{off:X06}: [Failed to decode]");
                    stopped = true;
                    break;
                }

                _disassembled.Add(code);
                _rowOffsets.Add(off);
                lines.Add(FormatAiRow(code, off));
            }

            // Trailing partial remainder: a ReadByteCount that is not a
            // multiple of 16 leaves 1..15 bytes that can't form a full
            // instruction. Only surfaced when the slot walk ran to completion
            // (not after an early error/decode-failure break).
            if (!stopped && off < end)
            {
                uint n = end - off;
                lines.Add($"0x{off:X06}: [partial 16-byte instruction — {n} bytes]");
            }

            return lines;
        }

        /// <summary>
        /// Render a single decoded AI opcode as a display line:
        /// "0xADDR: MNEMONIC  [hex bytes]  argName=value, ...  // comment".
        /// Mirrors the EventScriptViewModel row format. AI-specific only in
        /// that it is fed 16-byte instruction slots.
        /// </summary>
        static string FormatAiRow(EventScript.OneCode code, uint off)
        {
            string cmdName = EventScript.makeCommandComboText(code.Script, false);
            string hexBytes = U.HexDumpLiner(code.ByteData).Trim();
            string line = $"0x{off:X06}: {cmdName}  [{hexBytes}]";

            if (code.Script != null && code.Script.Args != null && code.Script.Args.Length > 0)
            {
                var argParts = new List<string>();
                for (int i = 0; i < code.Script.Args.Length; i++)
                {
                    var arg = code.Script.Args[i];
                    if (arg.Type == EventScript.ArgType.FIXED) continue;
                    string argStr = EventScript.GetArg(code, i, out _);
                    string name = arg.Name ?? arg.Symbol.ToString();
                    argParts.Add($"{name}={argStr}");
                }
                if (argParts.Count > 0)
                    line += "  " + string.Join(", ", argParts);
            }

            if (!string.IsNullOrEmpty(code.Comment))
                line += $"  // {code.Comment}";

            return line;
        }

        /// <summary>
        /// Re-render the current _disassembled model into display lines WITHOUT
        /// re-reading the ROM. Used by the View after UpdateRow so an in-memory
        /// edit is reflected immediately (DisassembleScript() would re-read the
        /// unmodified ROM bytes and discard the edit). Reuses the same row
        /// formatter DisassembleScript() uses.
        /// </summary>
        public IReadOnlyList<string> GetDisplayLines()
        {
            var lines = new List<string>(_disassembled.Count);
            for (int i = 0; i < _disassembled.Count; i++)
            {
                EventScript.OneCode code = _disassembled[i];
                uint off = i < _rowOffsets.Count ? _rowOffsets[i] : 0;
                lines.Add(FormatAiRow(code, off));
            }
            return lines;
        }

        // ----------------------------------------------------------------
        // File export / import (#965). Mirrors WF AIScriptForm.EventToTextAll
        // / EventToFile (export) and FileToEvent / TextToEvent / LineToEventByte
        // (import). AI is a FIXED 16-byte-per-opcode stream with no conditional
        // jumps/labels, so this is a straight byte-stream round-trip — no
        // deferred-jump resolution. The View owns the file dialog + disk I/O;
        // all decode/encode logic lives here (Core-pure).
        // ----------------------------------------------------------------

        /// <summary>
        /// Serialize every decoded instruction in the editable model into the
        /// WF-COMPATIBLE per-opcode text format — one line per opcode:
        /// <c>&lt;hexbytes&gt;\t//&lt;ScriptName&gt;[argName:value]&lt;literal Info text&gt;…</c>
        /// followed by the row's comment when present, then '\n'. The leading
        /// hex prefix is byte-identical to WF
        /// (<c>EventScriptInnerControl.EventToTextOne</c>), which is the ONLY
        /// part <see cref="ImportFromText"/> consumes — so Export → Import →
        /// Export round-trips losslessly. The WinForms-coupled rich per-ArgType
        /// previews (UnitForm.GetUnitName, InputFormRef.GetAI1, decoded TEXT,
        /// …) are decorative comment-only text and are intentionally OMITTED
        /// (they are unavailable in the cross-platform VM and do not affect the
        /// import round-trip).
        ///
        /// Lazily disassembles from <see cref="CurrentAddr"/> when the model is
        /// empty but a script is loaded (Copilot plan-review #2 — Export must
        /// never emit an empty script after a load-without-refresh). Returns an
        /// empty string when nothing is loaded / disassembled.
        /// </summary>
        public string ExportToText()
        {
            // Repopulate the editable model if a script is loaded but the model
            // is empty (e.g. Export pressed right after LoadEntry, before any
            // ReloadList re-read). DisassembleScript is a no-op when not loaded.
            if (_disassembled.Count == 0 && IsLoaded && CurrentAddr != 0)
                DisassembleScript();

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _disassembled.Count; i++)
                sb.Append(FormatExportLine(_disassembled[i]));
            return sb.ToString();
        }

        /// <summary>
        /// Render ONE decoded opcode as the WF-compatible export line. Mirrors
        /// the structure of <c>EventScriptInnerControl.EventToTextOne</c>:
        /// continuous 2-digit upper-hex of ByteData, then <c>\t//</c>, the
        /// script name (<c>Info[0]</c>), and for each odd <c>Info[i]</c> token
        /// that carries a symbol (<c>[X</c>) the matching non-FIXED arg as
        /// <c>[argName:value]</c> with the <c>Info[i+1]</c> literal appended.
        /// A trailing per-row comment (when present) is emitted after a second
        /// <c>//</c> so it survives the round-trip as a leading-hex-free line
        /// that import skips. Null/length-guarded for unknown opcodes whose
        /// Script/Info/Args may be minimal (Copilot plan-review #4).
        /// </summary>
        static string FormatExportLine(EventScript.OneCode code)
        {
            var sb = new System.Text.StringBuilder();

            byte[] bytes = code?.ByteData ?? System.Array.Empty<byte>();
            for (int n = 0; n < bytes.Length; n++)
                sb.Append(bytes[n].ToString("X2"));

            sb.Append("\t//");

            EventScript.Script sc = code?.Script;
            string[] info = sc?.Info;
            if (info != null && info.Length > 0)
            {
                sb.Append(info[0]);

                EventScript.Arg[] args = sc.Args ?? System.Array.Empty<EventScript.Arg>();
                for (int i = 1; i < info.Length; i += 2)
                {
                    char symbol = ' ';
                    if (info[i] != null && info[i].Length > 2)
                        symbol = info[i][1]; // "[X" → X is the symbol name

                    for (int n = 0; n < args.Length; n++)
                    {
                        EventScript.Arg arg = args[n];
                        if (EventScript.IsFixedArg(arg))
                            continue; // FIXED args are not editable / not surfaced
                        if (symbol != arg.Symbol)
                            continue;

                        sb.Append('[');
                        string hexstring = EventScript.GetArg(code, n, out _);
                        sb.Append(arg.Name);
                        sb.Append(':');
                        sb.Append(hexstring);
                        sb.Append(']');
                        break;
                    }

                    if (i + 1 < info.Length && info[i + 1] != null)
                        sb.Append(info[i + 1]);
                }
            }

            if (!string.IsNullOrEmpty(code?.Comment))
            {
                sb.Append("  //");
                sb.Append(code.Comment);
            }

            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Rebuild the editable opcode model from exported text (mirrors WF
        /// AIScriptForm.TextToEvent(..., isClear:true) + LineToEventByte).
        /// Each line is parsed for its LEADING hex pairs only via
        /// <see cref="ReadLeadingHexBytes"/> (WF LineToEventByte parity): the
        /// scan reads 2-hex-digit pairs and STOPS at the first non-hex char, so
        /// the <c>\t//…</c> comment and any blank / comment-only line are
        /// tolerated, and a lone trailing nibble is dropped (NOT padded). A line
        /// yielding fewer than 4 bytes is skipped (WF's <c>bin.Length &lt; 4</c>
        /// "broken or non-code" guard). Each surviving 4..16-byte instruction is
        /// right-padded with zero bytes to the FIXED 16-byte AI width by
        /// <see cref="PadToInstruction"/> (a parse longer than 16 bytes is
        /// rejected and that line skipped), then decoded via
        /// AIScript.DisAseemble. On any successful parse the model is REPLACED
        /// (clear-then-fill), JisageReorder is run, and row offsets are rebuilt.
        ///
        /// Does NOT write to the ROM — the caller refreshes the view and the
        /// user clicks Write to persist (preserving the established undo flow).
        /// Returns the number of opcodes imported (0 = nothing valid found, the
        /// model is left UNCHANGED).
        /// </summary>
        public int ImportFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            EventScript es = CoreState.AIScript;
            if (es == null) return 0;

            var parsed = new List<EventScript.OneCode>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                byte[] raw = ReadLeadingHexBytes(line);
                if (raw.Length < 4)
                    continue; // blank / comment-only / broken line (WF parity)

                byte[]? bytes = PadToInstruction(raw);
                if (bytes == null)
                    continue; // over-length (> 16 bytes) — not a single AI opcode

                EventScript.OneCode code;
                try
                {
                    code = es.DisAseemble(bytes, 0);
                }
                catch
                {
                    continue;
                }
                if (code == null || code.ByteData == null)
                    continue;

                parsed.Add(code);
            }

            if (parsed.Count == 0) return 0;

            _disassembled.Clear();
            _disassembled.AddRange(parsed);
            EventScriptUtil.JisageReorder(_disassembled);
            RebuildRowOffsets();
            return _disassembled.Count;
        }

        /// <summary>
        /// Read the LEADING hex byte pairs from a line, stopping at the first
        /// non-hex character (mirrors WF AIScriptForm.LineToEventByte). A lone
        /// trailing nibble is dropped. Whitespace is NOT skipped — like WF, the
        /// scan stops at the first space/tab (the exported hex dump has no
        /// internal separators), so a "<hex>\t//comment" line yields exactly the
        /// opcode bytes.
        /// </summary>
        static byte[] ReadLeadingHexBytes(string line)
        {
            if (string.IsNullOrEmpty(line)) return System.Array.Empty<byte>();
            var ret = new List<byte>();
            int length = line.Length;
            for (int i = 0; i + 1 < length; i += 2)
            {
                if (!U.ishex(line[i]) || !U.ishex(line[i + 1]))
                    break;
                ret.Add((byte)U.atoh(line.Substring(i, 2)));
            }
            return ret.ToArray();
        }

        /// <summary>
        /// Validate + right-pad a parsed instruction to the FIXED 16-byte AI
        /// width. A 16-byte instruction passes through; a 4..15-byte
        /// instruction is right-padded with zero bytes; an instruction longer
        /// than 16 bytes is rejected (null). Callers have already enforced the
        /// 4-byte floor.
        /// </summary>
        static byte[]? PadToInstruction(byte[] raw)
        {
            if (raw.Length > 16) return null;
            if (raw.Length == 16) return raw;
            byte[] padded = new byte[16];
            System.Array.Copy(raw, padded, raw.Length);
            return padded;
        }

        // ----------------------------------------------------------------
        // Per-row edit accessors (#760). The View binds the Disassembly
        // list selection to the Binary Code / Description boxes through
        // these helpers, then re-decodes a hand-edited 16-byte instruction
        // back into the model with UpdateRow.
        // ----------------------------------------------------------------

        /// <summary>
        /// Space-separated 2-digit upper-hex dump of an instruction's bytes —
        /// the exact "Binary Code" format the Disassembly list rows show
        /// (between the "[..]") and the form <see cref="ParseInstructionHex"/> /
        /// Update / New round-trip. Shared by <see cref="GetRowHex"/> and the AI
        /// Script "Script Change" opcode picker (#766), which copies a chosen
        /// command's default <see cref="EventScript.Script.Data"/> bytes into the
        /// Binary Code box via this same formatter so they re-decode identically.
        /// </summary>
        public static string FormatInstructionHex(byte[]? bytes)
        {
            if (bytes == null) return "";
            return U.HexDumpLiner(bytes).Trim();
        }

        /// <summary>
        /// Space-separated 2-digit hex dump of the instruction bytes at the
        /// given row (matching the "[..]" hex in the Disassembly list rows).
        /// Returns null on an out-of-range index.
        /// </summary>
        public string? GetRowHex(int index)
        {
            if (index < 0 || index >= _disassembled.Count) return null;
            byte[] data = _disassembled[index].ByteData;
            if (data == null) return null;
            return FormatInstructionHex(data);
        }

        /// <summary>
        /// Human-readable script command name for the given row (the WF
        /// "ScriptCodeName" hint). Returns null on an out-of-range index.
        /// </summary>
        public string? GetRowOpcodeName(int index)
        {
            if (index < 0 || index >= _disassembled.Count) return null;
            return EventScript.makeCommandComboText(_disassembled[index].Script, false);
        }

        /// <summary>
        /// Re-decode a hand-edited instruction at <paramref name="index"/> from
        /// <paramref name="hexText"/> and replace the model row. Mirrors WF
        /// AIScriptForm.OneLineDisassembler: parse the hex dump, pad a short
        /// instruction with zero bytes up to the fixed 16-byte width, reject an
        /// over-length / non-hex entry, then run AIScript.DisAseemble over the
        /// 16 bytes. AI is a FIXED 16-byte grid, so exactly ONE instruction is
        /// accepted (length &gt; 16 is rejected — multi-instruction edits and
        /// EXIT normalization are out of scope, see #760 follow-up).
        ///
        /// Returns the refreshed display line on success, or null (leaving the
        /// model untouched) on a bad index, empty / non-hex input, an
        /// over-length entry, or a decode failure.
        /// </summary>
        public string? UpdateRow(int index, string hexText)
        {
            if (index < 0 || index >= _disassembled.Count) return null;

            byte[]? bytes = ParseInstructionHex(hexText);
            if (bytes == null) return null;

            EventScript es = CoreState.AIScript;
            if (es == null) return null;

            // Preserve the row's existing comment. DisAseemble(bytes, 0) would
            // populate OneCode.Comment from CommentCache.At(0) (offset 0, wrong),
            // dropping the row's real per-offset comment. The comment is keyed by
            // ROM offset, which a same-size hex edit does not change.
            string origComment = _disassembled[index].Comment;
            EventScript.OneCode code;
            try
            {
                code = es.DisAseemble(bytes, 0);
            }
            catch
            {
                return null;
            }
            if (code == null || code.ByteData == null) return null;
            code.Comment = origComment;

            _disassembled[index] = code;
            uint off = index < _rowOffsets.Count ? _rowOffsets[index] : 0;
            return FormatAiRow(code, off);
        }

        /// <summary>
        /// Parse a hex dump (with or without separating whitespace) into a
        /// single AI instruction. Mirrors the WF OneLineDisassembler width
        /// rule: short instructions are right-padded with zero bytes up to the
        /// fixed 16-byte width; an instruction longer than 16 bytes, an empty
        /// string, or any non-hex / odd-nibble content yields null. AI's fixed
        /// grid is exactly one 16-byte instruction (CoreState.AIScript is
        /// `new EventScript(16)`).
        /// </summary>
        static byte[]? ParseInstructionHex(string hexText)
        {
            if (hexText == null) return null;

            // Strip ALL whitespace so the space-separated form emitted by
            // GetRowHex ("05 32 FF ..") parses the same as a continuous dump.
            var sb = new System.Text.StringBuilder(hexText.Length);
            foreach (char c in hexText)
            {
                if (char.IsWhiteSpace(c)) continue;
                bool isHex = (c >= '0' && c <= '9')
                          || (c >= 'a' && c <= 'f')
                          || (c >= 'A' && c <= 'F');
                if (!isHex) return null; // non-hex content
                sb.Append(c);
            }

            string cleaned = sb.ToString();
            if (cleaned.Length == 0) return null;          // empty
            if ((cleaned.Length & 1) != 0) return null;    // odd nibble count

            int byteCount = cleaned.Length / 2;
            if (byteCount > 16) return null;               // over one instruction

            byte[] parsed = U.convertStringDumpToByte(cleaned);
            if (parsed.Length == 16) return parsed;

            // Right-pad a short instruction to the fixed 16-byte width.
            byte[] padded = new byte[16];
            Array.Copy(parsed, padded, parsed.Length);
            return padded;
        }

        // ----------------------------------------------------------------
        // Structural edits (#763): insert / remove a 16-byte AI instruction.
        // Mirror WF AIScriptForm.NewButton_Click / RemoveButton_Click — both
        // mutate the in-memory list then run EventScriptUtil.JisageReorder.
        // These change the script length, so a subsequent WriteScript() takes
        // the free-space realloc + pointer-repoint path rather than the
        // same-size in-place path.
        // ----------------------------------------------------------------

        /// <summary>
        /// Insert a hand-typed 16-byte instruction AFTER the row at
        /// <paramref name="index"/> (mirrors WF NewButton_Click:
        /// AIAsm.Insert(SelectedIndex + 1, code), or Add when nothing is
        /// selected). The bytes are parsed via the same width rule as the
        /// per-row hex edit (one 16-byte instruction; short input is
        /// zero-padded). After inserting, EventScriptUtil.JisageReorder is run
        /// (WF parity) and _rowOffsets is rebuilt as CurrentAddr + i*16 so the
        /// display lines carry the right per-row addresses.
        ///
        /// Returns the formatted display line for the newly-inserted row on
        /// success (non-null = success), or null (leaving the model untouched)
        /// on empty / non-hex / over-length input or a decode failure.
        /// </summary>
        public string? InsertRow(int index, string hexText)
        {
            byte[]? bytes = ParseInstructionHex(hexText);
            if (bytes == null) return null;

            EventScript es = CoreState.AIScript;
            if (es == null) return null;

            EventScript.OneCode code;
            try
            {
                code = es.DisAseemble(bytes, 0);
            }
            catch
            {
                return null;
            }
            if (code == null || code.ByteData == null) return null;

            int insertAt;
            if (index < 0 || index >= _disassembled.Count)
            {
                _disassembled.Add(code);
                insertAt = _disassembled.Count - 1;
            }
            else
            {
                insertAt = index + 1;
                _disassembled.Insert(insertAt, code);
            }

            // WF parity: re-run the auto-indent / label reorder pass.
            EventScriptUtil.JisageReorder(_disassembled);
            RebuildRowOffsets();

            uint off = insertAt < _rowOffsets.Count ? _rowOffsets[insertAt] : CurrentAddr;
            return FormatAiRow(_disassembled[insertAt], off);
        }

        /// <summary>
        /// Remove the instruction at <paramref name="index"/> (mirrors WF
        /// RemoveButton_Click: AIAsm.RemoveAt(SelectedIndex)). Refuses to
        /// remove the LAST remaining instruction so the script never becomes
        /// empty (an empty script has no EXIT terminator and cannot be
        /// written safely). After removal, EventScriptUtil.JisageReorder is run
        /// and _rowOffsets is rebuilt. Returns true when a row was removed,
        /// false on an out-of-range index or when only one row remains.
        /// </summary>
        public bool RemoveRow(int index)
        {
            if (index < 0 || index >= _disassembled.Count) return false;
            if (_disassembled.Count <= 1) return false; // never empty

            _disassembled.RemoveAt(index);
            EventScriptUtil.JisageReorder(_disassembled);
            RebuildRowOffsets();
            return true;
        }

        /// <summary>
        /// Recompute the per-row offsets as CurrentAddr + i*16 for the current
        /// row count. AI is a FIXED 16-byte grid, so after a structural edit the
        /// rows are simply re-laid-out contiguously from CurrentAddr. (The real
        /// ROM offsets only matter once WriteScript relocates the script; until
        /// then these drive the display-line addresses.)
        /// </summary>
        void RebuildRowOffsets()
        {
            _rowOffsets.Clear();
            for (int i = 0; i < _disassembled.Count; i++)
                _rowOffsets.Add(CurrentAddr + (uint)i * 16);
        }

        // ----------------------------------------------------------------
        // Serialize + write-back (#760/#763, mirrors WF AllWriteButton_Click).
        // ----------------------------------------------------------------

        /// <summary>16-byte AI EXIT terminator (opcode 0x03). Appended by
        /// SerializeScript when the script does not already end in EXIT,
        /// matching WF AllWriteButton_Click's terminal-code append.</summary>
        static readonly byte[] ExitTerminator = new byte[16]
        {
            0x03, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        /// <summary>
        /// Concatenate every decoded instruction's ByteData, in row order,
        /// then guarantee the script ends in an EXIT (0x03) terminator —
        /// mirroring WF AIScriptForm.AllWriteButton_Click, which appends a
        /// 16-byte EXIT when the last opcode's first byte is not 0x03. This
        /// keeps the New/Remove realloc path from writing a script that runs
        /// off its end into whatever follows in free space. No double-append:
        /// when the model already ends in EXIT, nothing extra is added.
        /// Returns an empty array when nothing has been disassembled.
        /// </summary>
        public byte[] SerializeScript()
        {
            int total = 0;
            byte lastFirstByte = 0;
            bool any = false;
            for (int i = 0; i < _disassembled.Count; i++)
            {
                byte[] b = _disassembled[i].ByteData;
                if (b == null || b.Length == 0) continue;
                total += b.Length;
                lastFirstByte = b[0];
                any = true;
            }

            // WF parity: append a 16-byte EXIT when the script is non-empty and
            // does not already terminate with opcode 0x03.
            bool appendExit = any && lastFirstByte != 0x03;
            if (appendExit) total += ExitTerminator.Length;

            byte[] result = new byte[total];
            int pos = 0;
            for (int i = 0; i < _disassembled.Count; i++)
            {
                byte[] b = _disassembled[i].ByteData;
                if (b == null || b.Length == 0) continue;
                Array.Copy(b, 0, result, pos, b.Length);
                pos += b.Length;
            }
            if (appendExit)
                Array.Copy(ExitTerminator, 0, result, pos, ExitTerminator.Length);
            return result;
        }

        /// <summary>
        /// Write the serialized instruction bytes back to the ROM. Callers MUST
        /// wrap this call in a UndoService.Begin / Commit block so the writes
        /// register into the ambient undo scope the View opens.
        ///
        /// Two paths, chosen by the serialized length vs the loaded
        /// ReadByteCount (mirrors WF AIScriptForm.AllWriteButton_Click, whose
        /// WriteBinaryData reuses the slot in place when the size matches and
        /// reallocates to free space otherwise):
        ///
        /// • SAME-SIZE (bytes.Length == ReadByteCount): a strict in-place write
        ///   at CurrentAddr via rom.write_range. Refuses (returns false,
        ///   mutating nothing) when the target is not a safe ROM offset (rejects
        ///   header / out-of-range addresses a mistyped Address box could
        ///   supply) or when the slice would run off the end of the ROM. No
        ///   relocation — CurrentAddr / ReadByteCount are unchanged.
        ///
        /// • SIZE-CHANGED (New/Remove edited the row count, so SerializeScript
        ///   now appends/omits the EXIT terminator): the script is appended to
        ///   free space (AppendBinaryDataHeadless) and the AI pointer slot
        ///   (BaseAddr) is repointed via rom.write_p32 — both signed against the
        ///   SAME undoData so the realloc + repoint commit (and roll back) as one
        ///   transaction. Requires a non-null undoData (the realloc must be
        ///   undo-tracked) and a BaseAddr that is itself a safe 4-byte pointer
        ///   slot, validated BEFORE the allocation so we never allocate then fail
        ///   to repoint. On success CurrentAddr / ReadByteCount are updated to the
        ///   new location / length.
        ///
        /// Returns true only when the write fully succeeded.
        /// </summary>
        public bool WriteScript(Undo.UndoData? undoData = null)
        {
            if (_disassembled.Count == 0) return false;

            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return false;

            byte[] bytes = SerializeScript();

            if (bytes.Length == ReadByteCount)
            {
                // ---- SAME-SIZE in-place path (strictly no relocation) ----
                // (ulong) arithmetic so a near-uint.MaxValue CurrentAddr cannot
                // wrap past the ROM length.
                if ((ulong)CurrentAddr + (ulong)bytes.Length > (ulong)rom.Data.Length) return false;
                // Safety-offset guard: refuse header / low / out-of-range targets
                // so a mistyped Address box cannot mutate the ROM header under
                // undo. Check both the start and the last byte of the slice.
                if (bytes.Length > 0
                    && (!U.isSafetyOffset(CurrentAddr)
                        || !U.isSafetyOffset(CurrentAddr + (uint)bytes.Length - 1)))
                    return false;

                // Single-range write → one undo entry (registers into the ambient
                // UndoService scope opened by the View).
                rom.write_range(CurrentAddr, bytes);
                return true;
            }

            // ---- SIZE-CHANGED realloc + repoint path ----
            // The realloc MUST be undo-tracked: a null undoData means the caller
            // is not inside an undo transaction, so refuse rather than allocate
            // an orphan region with no way to roll it back.
            if (undoData == null) return false;

            // Validate the pointer slot BEFORE appending so we never allocate
            // free space and then fail to repoint it (leaking the allocation).
            if (!U.isSafetyOffset(BaseAddr)
                || (ulong)BaseAddr + 4 > (ulong)rom.Data.Length)
                return false;

            uint oldAddr = CurrentAddr;
            uint oldSize = ReadByteCount;

            uint newAddr = AppendBinaryDataHeadless(rom, bytes, undoData);
            if (!U.isSafetyOffset(newAddr)) return false;

            // Repoint the AI table slot at the new script location — same
            // undoData/transaction as the append so RunUndo reverses both.
            rom.write_p32(BaseAddr, newAddr, undoData);

            // Move the per-row comment / lint annotations to the new location so
            // a relocate does not orphan them at the old offset (mirrors WF
            // AllWriteButton's CommentCache update; same RepointEtcData pattern
            // DataExpansionCore.ExpandTableTo uses on table relocation).
            CoreState.CommentCache?.RepointEtcData(oldAddr, oldSize, newAddr);
            CoreState.LintCache?.RepointEtcData(oldAddr, oldSize, newAddr);

            CurrentAddr = newAddr;
            ReadByteCount = (uint)bytes.Length;
            return true;
        }

        /// <summary>
        /// Headless equivalent of InputFormRef.AppendBinaryData. Routes through
        /// the registered CoreState.AppendBinaryData delegate when wired
        /// (WinForms registers InputFormRef.AppendBinaryData → free-space
        /// search). When no delegate is wired (Avalonia today / headless tests),
        /// falls back to appending at the current ROM end, growing rom.Data to
        /// fit, and signing the appended range into <paramref name="undoData"/>.
        /// Returns the new data OFFSET (not a GBA pointer) — write_p32 applies
        /// the toPointer conversion — or U.NOT_FOUND on failure.
        /// </summary>
        static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer, Undo.UndoData undoData)
        {
            if (buffer == null || buffer.Length == 0) return U.NOT_FOUND;

            // Prefer the wired allocator (free-space search) when present.
            var allocator = CoreState.AppendBinaryData;
            if (allocator != null)
                return allocator(buffer, undoData);

            // ROM-end fallback: grow rom.Data and append at the old end. The
            // undoData captured the pre-grow file size at NewUndoData time, so
            // RunUndo resizes the ROM back (discarding the appended bytes). We
            // still record the written range for parity.
            uint appendAt = (uint)rom.Data.Length;
            if (!rom.write_resize_data((uint)(appendAt + buffer.Length)))
                return U.NOT_FOUND;

            // Sign the appended range into the same transaction (after the grow,
            // so the offset is in-bounds for the UndoPostion before-image read).
            rom.write_range(appendAt, buffer, undoData);
            return appendAt;
        }

        /// <summary>True once a script has been disassembled into the editable
        /// model (i.e. Re-read has run). The View guards Write on this so an
        /// empty/never-loaded model does not attempt a (misleading) write.</summary>
        public bool HasDisassembly => _disassembled.Count > 0;

        /// <summary>Number of instruction rows currently in the editable model.
        /// The View uses this to pick a post-edit selection after New/Remove
        /// without re-materializing the display lines.</summary>
        public int RowCount => _disassembled.Count;

        // ----------------------------------------------------------------
        // List expansion (#1020). Mirrors WF
        // AIScriptForm.AddressListExpandsEventNoCopyPointer.
        // ----------------------------------------------------------------

        /// <summary>
        /// Expand the active AI pointer table (ai1 when FilterIndex==0, ai2 when 1)
        /// to <paramref name="newCount"/> 4-byte pointer slots. Mirrors WF
        /// <c>AIScriptForm.AddressListExpandsEventNoCopyPointer</c>:
        /// <see cref="DataExpansionCore.ExpandTableTo"/> copies the old slots,
        /// zero-fills the new ones, writes the 0xFFFFFFFF terminator, wipes the old
        /// region and repoints the canonical slot ai*[0]; this then repoints the two
        /// additional consecutive base-pointer slots ai*[1]/ai*[2] that WF also
        /// repoints (isPointer-guarded, so ROMs without them are unaffected). The
        /// caller wraps this in an UndoService scope; ExpandTableTo + the write_p32
        /// calls are recorded by that ambient scope. <paramref name="undo"/> is
        /// reserved (kept for signature parity / future RepointAllReferences hardening).
        /// </summary>
        /// <returns>Empty string on success, an error message otherwise.</returns>
        public string ExpandList(uint newCount, Undo.UndoData? undo)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM not loaded.");
            uint tablePointer = (FilterIndex == 1) ? rom.RomInfo.ai2_pointer : rom.RomInfo.ai1_pointer;
            if (!U.isSafetyOffset(tablePointer)) return R._("AI pointer table not found.");
            if (newCount < ReadCount)
                return R._("New count ({0}) must be greater than or equal to current count ({1}).", newCount, ReadCount);
            if (newCount == ReadCount) return ""; // no-op success

            var result = DataExpansionCore.ExpandTableTo(rom, tablePointer, 4, ReadCount, newCount);
            if (!result.Success) return result.Error ?? R._("Table expansion failed.");

            // WF: the AI table base lives in 3 consecutive pointer slots (ai*[3]).
            // ExpandTableTo repointed slot [0]; repoint [1] and [2] too, guarded by
            // isPointer (WF: `if (!U.isPointer(p)) continue;`) and an EOF bound.
            for (uint i = 1; i < 3; i++)
            {
                uint slot = tablePointer + i * 4;
                if (slot + 4 > (uint)rom.Data.Length) break;
                if (U.isPointer(rom.u32(slot)))
                    rom.write_p32(slot, result.NewBaseAddress);
            }

            // Refresh the read-config from the new pointer base (NOT a ROM write).
            // The VM exposes the table base as TopAddress (there is no
            // ReadStartAddress on this VM); LoadList() reseeds its scan from this.
            TopAddress = result.NewBaseAddress;
            ReadCount = result.NewCount;
            return "";
        }

        // ----------------------------------------------------------------
        // Legacy header-only commit (kept for the stable VM surface). The
        // real opcode write-back now lives in WriteScript() (#760).
        // ----------------------------------------------------------------

        /// <summary>
        /// Header-only commit retained for backward compatibility. Use
        /// WriteScript() for the actual same-size in-place opcode write.
        /// </summary>
        public void Write()
        {
            IsLoaded = true;
        }

        // ----------------------------------------------------------------
        // POINTER_AI* parameter jump (#1600). Mirrors WF
        // AIScriptForm.ParamLabel_Clicked dispatch for the 5 AI sub-editors
        // (POINTER_AIUNIT/AITILE/AICOORDINATE/AIRANGE/AICALLTALK). The View
        // surfaces the per-opcode parameters as 5 rows; clicking an AI-pointer
        // row jumps to the matching sub-editor seeded at the pointer (allocating
        // a 4-byte ASM block when null/broken for the 3 ASM types).
        //
        // CONSISTENCY (Copilot plan-review #1600): the resolved/allocated pointer
        // is written into the selected row's IN-MEMORY OneCode.ByteData — NOT
        // into ROM at the row's offset (which is virtual after Update/New/Remove).
        // WriteScript() serializes that model on Write, so the jump composes with
        // pending row edits and a later Write persists the pointer correctly.
        // ----------------------------------------------------------------

        /// <summary>
        /// Resolve the Nth (1-based) NON-FIXED argument of the disassembled
        /// opcode at <paramref name="rowIdx"/> — the parameter shown on the View's
        /// param row <paramref name="paramRow"/> (1..5). Returns false when the
        /// row/param is out of range. Mirrors how FormatAiRow enumerates args
        /// (skipping FIXED), so the param-row order matches the displayed args.
        /// </summary>
        public bool TryGetParamArg(int rowIdx, int paramRow,
            out EventScript.OneCode code, out EventScript.Arg arg, out uint value)
        {
            code = null!;
            arg = null!;
            value = 0;
            if (rowIdx < 0 || rowIdx >= _disassembled.Count) return false;
            if (paramRow < 1) return false;

            EventScript.OneCode c = _disassembled[rowIdx];
            if (c?.Script?.Args == null) return false;

            int seen = 0;
            for (int i = 0; i < c.Script.Args.Length; i++)
            {
                EventScript.Arg a = c.Script.Args[i];
                if (a.Type == EventScript.ArgType.FIXED) continue;
                seen++;
                if (seen == paramRow)
                {
                    code = c;
                    arg = a;
                    value = EventScript.GetArgValue(c, a);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Number of non-FIXED parameters the opcode at <paramref name="rowIdx"/>
        /// exposes (the count of populated param rows the View should show).
        /// </summary>
        public int GetParamCount(int rowIdx)
        {
            if (rowIdx < 0 || rowIdx >= _disassembled.Count) return 0;
            EventScript.OneCode c = _disassembled[rowIdx];
            if (c?.Script?.Args == null) return 0;
            int seen = 0;
            for (int i = 0; i < c.Script.Args.Length; i++)
                if (c.Script.Args[i].Type != EventScript.ArgType.FIXED) seen++;
            return seen;
        }

        /// <summary>Display label (arg name) for a param row, or "" when absent.</summary>
        public string GetParamLabel(int rowIdx, int paramRow)
        {
            if (!TryGetParamArg(rowIdx, paramRow, out _, out var arg, out _)) return "";
            return arg.Name ?? arg.Symbol.ToString();
        }

        /// <summary>Decoded preview text for a param row, or "" when absent.</summary>
        public string GetParamValueText(int rowIdx, int paramRow)
        {
            if (rowIdx < 0 || rowIdx >= _disassembled.Count) return "";
            // Reuse EventScript.GetArg for the decoded textual form. Resolve the
            // raw arg index that maps to this param row first.
            EventScript.OneCode c = _disassembled[rowIdx];
            if (c?.Script?.Args == null) return "";
            int seen = 0;
            for (int i = 0; i < c.Script.Args.Length; i++)
            {
                if (c.Script.Args[i].Type == EventScript.ArgType.FIXED) continue;
                seen++;
                if (seen == paramRow)
                    return EventScript.GetArg(c, i, out _);
            }
            return "";
        }

        /// <summary>
        /// Classify the param row's argument as an AI sub-editor pointer.
        /// Returns <see cref="AiPointerKind.None"/> when the row is out of range
        /// or the arg is not a POINTER_AI* type. The View uses this to decide
        /// whether the param-label click should jump.
        /// </summary>
        public AiPointerKind ClassifyParam(int rowIdx, int paramRow)
        {
            if (!TryGetParamArg(rowIdx, paramRow, out _, out var arg, out _))
                return AiPointerKind.None;
            return AIScriptPointerJumpCore.ClassifyArg(arg);
        }

        /// <summary>
        /// True when the param row holds a Coordinate/Range/CallTalk pointer that
        /// is currently null or broken (so a jump would allocate). The View uses
        /// this to prompt before allocation (WF AllocIfNeed parity).
        /// </summary>
        public bool ParamNeedsAlloc(int rowIdx, int paramRow)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (!TryGetParamArg(rowIdx, paramRow, out _, out var arg, out uint value))
                return false;
            AiPointerKind kind = AIScriptPointerJumpCore.ClassifyArg(arg);
            if (!AIScriptPointerJumpCore.IsAllocKind(kind)) return false;
            bool isNull = value == 0 || value == U.NOT_FOUND;
            return isNull || AIScriptPointerJumpCore.IsBrokenData(rom, kind, value);
        }

        /// <summary>
        /// Apply the POINTER_AI* jump for a param row: classify the arg, allocate
        /// a fresh 4-byte ASM block when the Coordinate/Range/CallTalk pointer is
        /// null/broken (undo-tracked append), and write the resolved/allocated
        /// pointer into the selected row's IN-MEMORY OneCode.ByteData (clone-
        /// replace, like UpdateRow). Returns the sub-editor kind + the pointer the
        /// View should open the sub-editor at, and whether an allocation happened.
        ///
        /// Returns false (kind=None) when the row is not an AI-pointer param, or
        /// when an allocation was needed but failed (out of free space) — in which
        /// case nothing is mutated and the View aborts the jump.
        /// </summary>
        public bool ApplyPointerJump(int rowIdx, int paramRow, Undo.UndoData? undodata,
            out AiPointerKind kind, out uint pointerValue, out bool allocated)
        {
            kind = AiPointerKind.None;
            pointerValue = 0;
            allocated = false;

            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (!TryGetParamArg(rowIdx, paramRow, out EventScript.OneCode code,
                    out EventScript.Arg arg, out uint value))
                return false;

            kind = AIScriptPointerJumpCore.ClassifyArg(arg);
            if (kind == AiPointerKind.None) return false;

            if (!AIScriptPointerJumpCore.AllocIfNeed(rom, kind, value, undodata,
                    out pointerValue, out allocated))
            {
                // Allocation was required (null/broken ASM block) but failed.
                kind = AiPointerKind.None;
                return false;
            }

            // Write the resolved/allocated pointer into a CLONE of the row's
            // ByteData and clone-replace the row, mirroring UpdateRow's
            // _disassembled[index] = code so pending edits on OTHER rows are
            // untouched. Only a 4-byte arg is written (WritePointerIntoBytes
            // guards Size != 4). For Units/Tiles (which are never reallocated) the
            // pointer is unchanged, so this is a faithful no-op rewrite.
            byte[] src = code.ByteData ?? Array.Empty<byte>();
            byte[] newBytes = (byte[])src.Clone();
            AIScriptPointerJumpCore.WritePointerIntoBytes(newBytes, arg, pointerValue);

            EventScript es = CoreState.AIScript;
            if (es != null)
            {
                try
                {
                    string origComment = code.Comment;
                    EventScript.OneCode rebuilt = es.DisAseemble(newBytes, 0);
                    if (rebuilt != null && rebuilt.ByteData != null)
                    {
                        rebuilt.Comment = origComment;
                        _disassembled[rowIdx] = rebuilt;
                    }
                    else
                    {
                        code.ByteData = newBytes; // fall back to in-place byte swap
                    }
                }
                catch
                {
                    code.ByteData = newBytes;
                }
            }
            else
            {
                code.ByteData = newBytes;
            }

            return true;
        }
    }
}
