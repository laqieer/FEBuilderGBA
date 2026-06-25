using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform editable model for an event / procs / AI script (#1435).
    /// Wraps a <see cref="List{T}"/> of <see cref="EventScript.OneCode"/> and ports
    /// the structural-authoring controller logic that previously lived only in the
    /// WinForms <c>EventScriptInnerControl</c> (insert / delete / move / template /
    /// text-import) plus the <c>AllWriteButton</c> serialize-and-write path.
    ///
    /// All list mutations are PURE (no ROM I/O). Only <see cref="WriteAll"/> mutates
    /// the ROM, and it does so under one ambient undo scope: it writes IN-PLACE when
    /// the serialized script fits the original region, otherwise it relocates to free
    /// space, repoints every reference (raw + LDR via
    /// <see cref="DataExpansionCore.RepointAllReferences"/>), zero-fills the old
    /// region and rewrites the in-memory pointers via
    /// <see cref="EventScript.NotifyChangePointer(System.Collections.Generic.List{EventScript.OneCode}, uint, uint)"/>.
    /// Validate-before-mutate; on any fault the byte-identical original is restored.
    /// </summary>
    public sealed class EventScriptEditorCore
    {
        readonly EventScript _es;
        readonly List<EventScript.OneCode> _codes = new List<EventScript.OneCode>();

        /// <summary>The disassembler/definition table this editor parses with.</summary>
        public EventScript Script => _es;

        /// <summary>The live editable command list. Callers may read it; use the
        /// Insert/Delete/Move helpers to mutate so indentation stays consistent.</summary>
        public IReadOnlyList<EventScript.OneCode> Codes => _codes;

        /// <summary>Number of commands currently in the list.</summary>
        public int Count => _codes.Count;

        public EventScriptEditorCore(EventScript es)
        {
            _es = es ?? throw new ArgumentNullException(nameof(es));
        }

        /// <summary>
        /// Disassemble the script region starting at <paramref name="addr"/> into the
        /// editable list (ports the WinForms <c>ReloadEvent</c> loop). The region length
        /// is auto-detected via this editor's own <see cref="ScanLength"/> (NOT the global
        /// <c>CoreState.EventScript</c>-bound <c>EventScript.SearchEveneLength</c>), so the
        /// scan uses this instance's disassembler — important for Procs/AI/custom widths.
        /// After load, indentation (<c>JisageCount</c>) is recomputed.
        /// </summary>
        public void BuildFromRom(ROM rom, uint addr, bool isWorldMapEvent = false)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            _codes.Clear();

            uint offset = U.toOffset(addr);
            if (rom.Data == null || offset >= (uint)rom.Data.Length)
            {
                return;
            }

            uint length = ScanLength(rom.Data, offset, isWorldMapEvent);
            uint limit = offset + length;
            if (limit > (uint)rom.Data.Length) limit = (uint)rom.Data.Length;

            uint cur = offset;
            // Hard cap mirrors the WinForms safety bound; ScanLength already
            // terminates on TERM/MAPTERM/30-consecutive-unknown.
            int guard = 0;
            while (cur < limit && guard < 100000)
            {
                EventScript.OneCode code = _es.DisAseemble(rom.Data, cur);
                if (code == null || code.Script == null) break;
                _codes.Add(code);
                uint size = (uint)code.Script.Size;
                if (size == 0) size = 4;
                cur += size;
                guard++;
            }

            EventScriptUtil.JisageReorder(_codes);
        }

        /// <summary>
        /// Compute the byte length of the script region at <paramref name="startAddr"/>
        /// using THIS editor's disassembler (<c>_es</c>), so Procs / AI tables and
        /// custom widths terminate correctly. Ports <see cref="EventScript.SearchEveneLength"/>
        /// but drives it off <c>_es</c> instead of <c>CoreState.EventScript</c> (the
        /// global static the original helper hard-codes — see Copilot plan review
        /// finding #1). Terminates on TERM/MAPTERM exit codes or 30 consecutive unknowns.
        /// </summary>
        uint ScanLength(byte[] romdata, uint startAddr, bool isWorldMapEvent)
        {
            uint lastBranchAddr = 0;
            int unknownCount = 0;
            uint addr = startAddr;
            int guard = 0;
            while (guard++ < 100000)
            {
                if (addr >= (uint)romdata.Length) break;
                EventScript.OneCode code = _es.DisAseemble(romdata, addr);
                if (code == null || code.Script == null) break;

                if (EventScript.IsExitCode(code, addr, lastBranchAddr))
                {
                    addr += (uint)code.Script.Size;
                    break;
                }
                else if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    unknownCount++;
                    if (unknownCount > 30) break;
                }
                else
                {
                    unknownCount = 0;
                    if (code.Script.Has == EventScript.ScriptHas.LABEL_CONDITIONAL)
                        lastBranchAddr = 0;
                    else if (code.Script.Has == EventScript.ScriptHas.IF_CONDITIONAL)
                        lastBranchAddr = addr;
                }

                uint size = (uint)code.Script.Size;
                if (size == 0) size = 4;
                addr += size;
            }
            return addr - startAddr;
        }

        /// <summary>
        /// Replace the entire list with a DEEP CLONE of <paramref name="codes"/> (used by
        /// tests and bulk operations). Each <see cref="EventScript.OneCode"/> is cloned via
        /// <see cref="EventScript.CloneCode"/> so the engine's later in-place mutations of
        /// <c>OneCode.ByteData</c> (e.g. <see cref="EventScript.NotifyChangePointer(System.Collections.Generic.List{EventScript.OneCode}, uint, uint)"/>
        /// during relocation) can never reach back into the caller's list (Copilot PR
        /// review #1510 finding — the doc said "clone" but the old impl stored the same
        /// instances). Recomputes indentation.
        /// </summary>
        public void SetCodes(IEnumerable<EventScript.OneCode> codes)
        {
            _codes.Clear();
            if (codes != null)
            {
                foreach (var c in codes)
                {
                    _codes.Add(c == null ? null : EventScript.CloneCode(c));
                }
            }
            EventScriptUtil.JisageReorder(_codes);
        }

        /// <summary>True when the code at <paramref name="index"/> is a terminator
        /// (TERM / MAPTERM). New commands are never inserted AFTER a terminator.</summary>
        public bool IsTermAt(int index)
        {
            if (index < 0 || index >= _codes.Count) return false;
            var has = _codes[index].Script.Has;
            return has == EventScript.ScriptHas.TERM || has == EventScript.ScriptHas.MAPTERM;
        }

        /// <summary>
        /// Insert <paramref name="code"/> relative to <paramref name="selectedIndex"/>
        /// using WinForms <c>NewButton</c> semantics: no selection ⇒ append to end;
        /// selection is a terminator ⇒ insert BEFORE it; otherwise insert AFTER it.
        /// Returns the new selection index. Recomputes indentation.
        /// </summary>
        public int Insert(int selectedIndex, EventScript.OneCode code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));

            int selected;
            if (selectedIndex < 0 || selectedIndex >= _codes.Count)
            {
                _codes.Add(code);
                selected = _codes.Count - 1;
            }
            else if (IsTermAt(selectedIndex))
            {
                _codes.Insert(selectedIndex, code);
                selected = selectedIndex;
            }
            else
            {
                _codes.Insert(selectedIndex + 1, code);
                selected = selectedIndex + 1;
            }

            EventScriptUtil.JisageReorder(_codes);
            return selected;
        }

        /// <summary>
        /// Insert a whole template (a list of codes) at <paramref name="selectedIndex"/>
        /// (clamped to 0). Ports WinForms <c>InsertEventTemplate</c> /
        /// <c>EventAsm.InsertRange</c>. Returns the index of the last inserted code.
        /// </summary>
        public int InsertRange(int selectedIndex, IList<EventScript.OneCode> codes)
        {
            if (codes == null) throw new ArgumentNullException(nameof(codes));
            int insertedPoint = selectedIndex;
            if (insertedPoint < 0) insertedPoint = 0;
            if (insertedPoint > _codes.Count) insertedPoint = _codes.Count;

            _codes.InsertRange(insertedPoint, codes);
            EventScriptUtil.JisageReorder(_codes);
            return insertedPoint + Math.Max(0, codes.Count - 1);
        }

        /// <summary>Delete the code at <paramref name="index"/>. Returns the new
        /// selection index (item above, clamped). Recomputes indentation.</summary>
        public int Delete(int index)
        {
            if (index < 0 || index >= _codes.Count) return Math.Min(index, _codes.Count - 1);
            _codes.RemoveAt(index);
            EventScriptUtil.JisageReorder(_codes);
            int sel = index - 1;
            if (sel < -1) sel = -1;
            if (sel >= _codes.Count) sel = _codes.Count - 1;
            return sel;
        }

        /// <summary>Swap the code at <paramref name="index"/> with the one above it.
        /// Returns the new selection index. No-op (returns index) at the top.</summary>
        public int MoveUp(int index)
        {
            if (index < 1 || index >= _codes.Count) return index;
            (_codes[index - 1], _codes[index]) = (_codes[index], _codes[index - 1]);
            EventScriptUtil.JisageReorder(_codes);
            return index - 1;
        }

        /// <summary>Swap the code at <paramref name="index"/> with the one below it.
        /// Returns the new selection index. No-op (returns index) at the bottom.</summary>
        public int MoveDown(int index)
        {
            if (index < 0 || index + 1 >= _codes.Count) return index;
            (_codes[index + 1], _codes[index]) = (_codes[index], _codes[index + 1]);
            EventScriptUtil.JisageReorder(_codes);
            return index + 1;
        }

        /// <summary>
        /// Build a fresh <see cref="EventScript.OneCode"/> for the chosen command from
        /// its default template bytes (ports WinForms <c>CloneScriptDefaultByte</c> +
        /// re-disassemble). Applies the FSEC=60 / FADESPEED=16 / TRANSITIONSPEED=4
        /// zero-defaults so freshly-inserted timing commands are usable.
        /// </summary>
        public EventScript.OneCode NewCodeFromScript(EventScript.Script script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            byte[] bytes = CloneScriptDefaultByte(script);
            return _es.DisAseemble(bytes, 0);
        }

        /// <summary>Disassemble arbitrary bytes into a single <see cref="EventScript.OneCode"/>
        /// (ports the hex / clipboard / file import path which feeds raw bytes through
        /// <see cref="EventScript.DisAseemble"/>).</summary>
        public EventScript.OneCode NewCodeFromBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            return _es.DisAseemble(bytes, 0);
        }

        /// <summary>
        /// Port of WinForms <c>CloneScriptDefaultByte</c>: clone the command's default
        /// template bytes and substitute sensible defaults for zero FSEC / FADESPEED /
        /// TRANSITIONSPEED args. PURE (operates on a fresh copy).
        /// </summary>
        public static byte[] CloneScriptDefaultByte(EventScript.Script script)
        {
            byte[] data = (byte[])script.Data.Clone();
            for (int n = 0; n < script.Args.Length; n++)
            {
                EventScript.Arg arg = script.Args[n];
                if (data.Length <= arg.Position) continue;

                uint v = U.u8(data, (uint)arg.Position);
                if (arg.Type == EventScript.ArgType.FSEC)
                {
                    if (v == 0) U.write_u8(data, (uint)arg.Position, 60);
                }
                if (arg.Type == EventScript.ArgType.FADESPEED)
                {
                    if (v == 0) U.write_u8(data, (uint)arg.Position, 16);
                }
                else if (arg.Type == EventScript.ArgType.TRANSITIONSPEED)
                {
                    if (v == 0) U.write_u8(data, (uint)arg.Position, 4);
                }
            }
            return data;
        }

        /// <summary>
        /// Parse a single text line of hex pairs into bytes, stopping at the first
        /// non-hex character (ports WinForms <c>LineToEventByte</c>). PURE.
        /// </summary>
        public static byte[] LineToEventByte(string line)
        {
            var ret = new List<byte>();
            if (line == null) return ret.ToArray();
            line = line.Trim();
            int length = line.Length;
            for (int i = 0; i < length; i += 2)
            {
                if (!U.ishex(line[i])) break;
                if (i + 1 >= length) break;
                if (!U.ishex(line[i + 1])) break;
                byte b = (byte)U.atoh(line.Substring(i, 2));
                ret.Add(b);
            }
            return ret.ToArray();
        }

        /// <summary>
        /// Import event commands from a multi-line hex text blob (ports WinForms
        /// <c>TextToEvent</c>). Each line with ≥4 bytes is disassembled and inserted.
        /// <paramref name="insertPoint"/> ≤ -1 appends to the end (preserving file order);
        /// otherwise each command is inserted AT <paramref name="insertPoint"/> (the same
        /// index each line), so a multi-line block ends up REVERSED relative to file order —
        /// this faithfully reproduces the WinForms <c>TextToEvent</c> behaviour. When
        /// <paramref name="clear"/> is true the list is cleared first. Returns the number of
        /// commands imported. ROM-I/O-free, but MUTATES this editor's in-memory command list
        /// (not a pure function).
        /// </summary>
        public int ImportFromText(string text, int insertPoint = -1, bool clear = false)
        {
            if (clear) _codes.Clear();
            int imported = 0;
            if (string.IsNullOrEmpty(text))
            {
                if (clear) EventScriptUtil.JisageReorder(_codes);
                return 0;
            }

            string[] lines = text.Split('\n');
            // Match WinForms: when inserting at a specific point, each successive line
            // is inserted at the SAME index, so the imported block ends up reversed
            // relative to file order — we faithfully reproduce that behaviour.
            foreach (string line in lines)
            {
                byte[] bin = LineToEventByte(line);
                if (bin.Length < 4) continue; // broken or different code
                EventScript.OneCode code = _es.DisAseemble(bin, 0);
                if (insertPoint <= -1)
                {
                    _codes.Add(code);
                }
                else
                {
                    int at = insertPoint;
                    if (at > _codes.Count) at = _codes.Count;
                    _codes.Insert(at, code);
                }
                imported++;
            }

            EventScriptUtil.JisageReorder(_codes);
            return imported;
        }

        /// <summary>
        /// Serialize the editable list to a contiguous byte array (ports the
        /// <c>AllWriteButton</c> serialize loop). Concatenates each command's
        /// <c>ByteData</c> and, when no terminator is present, appends the correct
        /// default terminator for the event kind. PURE.
        /// </summary>
        public byte[] Serialize(ROM rom, bool isWorldMapEvent, bool isTopLevelEvent)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            var databyte = new List<byte>();
            bool hasTerm = false;
            for (int i = 0; i < _codes.Count; i++)
            {
                var code = _codes[i];
                if (code.ByteData != null) databyte.AddRange(code.ByteData);

                // A MAPTERM byte IS a terminator in its own right, so count it regardless
                // of the event kind — otherwise a script that already ends in MAPTERM but
                // is written with isWorldMapEvent==false would get a SECOND terminator
                // appended, changing its bytes (Copilot PR review inline #1). WinForms
                // only checks MAPTERM under the world-map flag; treating it as always-terminal
                // here is strictly safer and never double-terminates.
                if (code.Script.Has == EventScript.ScriptHas.TERM ||
                    code.Script.Has == EventScript.ScriptHas.MAPTERM) hasTerm = true;
            }

            if (!hasTerm)
            {
                if (isWorldMapEvent)
                    databyte.AddRange(rom.RomInfo.Default_event_script_mapterm_code);
                else if (isTopLevelEvent)
                    databyte.AddRange(rom.RomInfo.Default_event_script_toplevel_code);
                else
                    databyte.AddRange(rom.RomInfo.Default_event_script_term_code);
            }

            return databyte.ToArray();
        }

        /// <summary>Outcome of a <see cref="WriteAll"/> call.</summary>
        public enum WriteResult
        {
            /// <summary>Wrote in place; the script fit the original region.</summary>
            InPlace,
            /// <summary>Relocated to free space and repointed references.</summary>
            Relocated,
            /// <summary>Nothing was written (empty list or invalid address).</summary>
            NoOp,
            /// <summary>Could not allocate free space; ROM unchanged.</summary>
            NoFreeSpace,
            /// <summary>
            /// Relocation was needed but NO reference to the old script base could be
            /// found (raw 32-bit or Thumb LDR pool), so the old region was NOT cleared
            /// and nothing was relocated — refusing to orphan the script. ROM unchanged.
            /// (Copilot plan review finding #2.) The caller should keep the script at
            /// its current address (it may be referenced via an event-table / struct /
            /// hardcoded ASM path that the cross-platform repointer does not cover).
            /// </summary>
            NoReferenceRefused,
            /// <summary>
            /// The target base is unsafe to write — outside the safe ROM range
            /// (<c>U.isSafetyOffset</c>: header / danger-zone <c>&lt; 0x200</c>, BIOS, or
            /// past EOF) or not 4-byte aligned. ROM unchanged. (Copilot PR review
            /// finding #3 — ports the WinForms <c>CheckZeroAddressWriteHigh</c> +
            /// <c>CheckPaddingALIGN4</c> gates that the All-Write path runs before mutating.)
            /// </summary>
            UnsafeAddress,
        }

        /// <summary>
        /// When true (default), <see cref="WriteAll"/> refuses to relocate a script
        /// whose old base has no discoverable reference (returns
        /// <see cref="WriteResult.NoReferenceRefused"/> without mutating the ROM). Set
        /// false only when the caller has confirmed the relocation is safe (e.g. a brand
        /// new region with no inbound pointer yet). Mirrors the Avalonia MoveToFreeSpace
        /// no-reference guard.
        /// </summary>
        public bool RefuseRelocateWithoutReference { get; set; } = true;

        /// <summary>
        /// Serialize the editable list and write it to <paramref name="addr"/> under one
        /// ambient undo scope (ports the WinForms <c>AllWriteButton</c> + <c>WriteBinaryData</c>).
        ///
        /// Strategy:
        /// <list type="bullet">
        /// <item>Fits the original region ⇒ write in place and zero-fill the tail.</item>
        /// <item>Does not fit ⇒ allocate free space, write there, repoint every reference
        /// to the old base (raw + LDR), zero-fill the old region, and rewrite the in-memory
        /// pointers via <see cref="EventScript.NotifyChangePointer(System.Collections.Generic.List{EventScript.OneCode}, uint, uint)"/>.</item>
        /// </list>
        /// All writes are recorded into <paramref name="undo"/>; on any exception the
        /// undo is rolled back so the ROM is byte-identical to before the call.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="addr">Script base (pointer or offset).</param>
        /// <param name="isWorldMapEvent">Use the world-map terminator.</param>
        /// <param name="isTopLevelEvent">Use the chapter top-level terminator.</param>
        /// <param name="undo">Undo buffer that receives every write (required).</param>
        /// <param name="newAddr">On return: the (possibly relocated) base OFFSET.</param>
        public WriteResult WriteAll(ROM rom, uint addr, bool isWorldMapEvent, bool isTopLevelEvent,
            Undo.UndoData undo, out uint newAddr)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (undo == null) throw new ArgumentNullException(nameof(undo));

            uint offset = U.toOffset(addr);
            newAddr = offset;

            if (_codes.Count <= 0) return WriteResult.NoOp;
            if (rom.Data == null || offset == 0 || offset >= (uint)rom.Data.Length)
                return WriteResult.NoOp;

            // SAFETY GATE (Copilot PR review finding #3): refuse to write to an unsafe
            // base — header / danger-zone (< 0x200), BIOS, past EOF, or an unaligned
            // address. Ports the WinForms CheckZeroAddressWriteHigh + CheckPaddingALIGN4
            // gates the All-Write path runs before any mutation. No partial write happens
            // because this returns before the undo scope opens.
            if (!U.isSafetyOffset(offset, rom) || (offset & 3) != 0)
                return WriteResult.UnsafeAddress;

            byte[] databyte = Serialize(rom, isWorldMapEvent, isTopLevelEvent);

            // Original region length (so we know whether the new bytes fit and how much
            // tail to zero-fill / how big the old region is when relocating). Drive off
            // THIS editor's disassembler so Procs / world-map widths are correct.
            uint originalSize = ScanLength(rom.Data, offset, isWorldMapEvent);
            if (originalSize == 0) originalSize = (uint)databyte.Length;

            int undoCountBefore = undo.list.Count;
            using (ROM.BeginUndoScope(undo))
            {
                try
                {
                    if ((uint)databyte.Length <= originalSize)
                    {
                        // IN-PLACE: write then zero-fill the tail of the original region.
                        rom.write_range(offset, databyte);
                        uint tail = originalSize - (uint)databyte.Length;
                        if (tail > 0 && offset + (uint)databyte.Length + tail <= (uint)rom.Data.Length)
                        {
                            rom.write_fill(offset + (uint)databyte.Length, tail, 0x00);
                        }
                        newAddr = offset;
                        return WriteResult.InPlace;
                    }

                    // RELOCATE: find free space large enough for the grown script.
                    uint freespace = rom.FindFreeSpace(offset, (uint)databyte.Length);
                    if (freespace == U.NOT_FOUND || freespace == 0)
                    {
                        freespace = rom.FindFreeSpace(0x100, (uint)databyte.Length);
                    }
                    if (freespace == U.NOT_FOUND || freespace == 0 ||
                        freespace + (uint)databyte.Length > (uint)rom.Data.Length)
                    {
                        return WriteResult.NoFreeSpace;
                    }

                    // SAFETY GATE (Copilot plan review finding #2): before any destructive
                    // clear, repoint every reference (raw 32-bit + Thumb LDR pool) to the
                    // old base. Pass null so RepointAllReferences uses OUR ambient scope
                    // (no nested BeginUndoScope). If ZERO references were repointed and the
                    // caller has not opted out, REFUSE: do not write the relocated copy and
                    // do not clear the source — the script may be reached via an
                    // event-table / struct / hardcoded-ASM path the cross-platform repointer
                    // does not cover, and orphaning it would silently corrupt the ROM.
                    int repointed = DataExpansionCore.RepointAllReferences(rom, offset, freespace, null);
                    if (repointed <= 0 && RefuseRelocateWithoutReference)
                    {
                        // Nothing destructive has happened yet (RepointAllReferences only
                        // wrote pointer slots; with repointed==0 it wrote none). Roll back
                        // any stray writes for safety and refuse.
                        RollbackTo(rom, undo, undoCountBefore);
                        newAddr = offset;
                        return WriteResult.NoReferenceRefused;
                    }

                    // Write the relocated copy now that we know it is referenced.
                    rom.write_range(freespace, databyte);

                    // Zero-fill the old region.
                    if (originalSize > 0 && offset + originalSize <= (uint)rom.Data.Length)
                    {
                        rom.write_fill(offset, originalSize, 0x00);
                    }

                    // Update the in-memory pointers so the editable list stays consistent.
                    // NotifyChangePointer compares POINTER-form values in ByteData, so pass
                    // pointer-form addresses (Copilot plan review finding #2).
                    EventScript.NotifyChangePointer(_codes, U.toPointer(offset), U.toPointer(freespace));

                    newAddr = freespace;
                    return WriteResult.Relocated;
                }
                catch
                {
                    // Roll back every write recorded in this scope (byte-identical restore).
                    RollbackTo(rom, undo, undoCountBefore);
                    newAddr = offset;
                    throw;
                }
            }
        }

        /// <summary>
        /// Roll back undo entries appended since <paramref name="keepCount"/>, restoring
        /// the original bytes recorded by each <see cref="Undo.UndoPostion"/>. Each
        /// <c>UndoPostion</c> captured the pre-write bytes (<c>data</c>) at construction
        /// time, so writing them back yields a byte-identical restore. Applied newest-first.
        /// </summary>
        static void RollbackTo(ROM rom, Undo.UndoData undo, int keepCount)
        {
            for (int i = undo.list.Count - 1; i >= keepCount; i--)
            {
                Undo.UndoPostion pos = undo.list[i];
                if (pos == null || pos.data == null) continue;
                if (pos.addr + (uint)pos.data.Length <= (uint)rom.Data.Length)
                {
                    U.write_range(rom.Data, pos.addr, pos.data);
                }
            }
            if (undo.list.Count > keepCount)
            {
                undo.list.RemoveRange(keepCount, undo.list.Count - keepCount);
            }
        }

        /// <summary>
        /// Export the editable list as a multi-line hex text blob (one command per line),
        /// suitable for clipboard / file export and re-import via <see cref="ImportFromText"/>.
        /// PURE.
        /// </summary>
        public string ExportToText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _codes.Count; i++)
            {
                var code = _codes[i];
                if (code.ByteData == null) continue;
                sb.AppendLine(U.convertByteToStringDump(code.ByteData).Trim());
            }
            return sb.ToString();
        }
    }
}
