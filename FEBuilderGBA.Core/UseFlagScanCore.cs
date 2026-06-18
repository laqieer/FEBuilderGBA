using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, strictly READ-ONLY aggregator for the Avalonia
    /// "Flags-Used-in-Chapter" tool (issue #1192 — port of the WinForms
    /// <c>ToolUseFlagForm.UpdateList</c> flag-usage roll-up).
    ///
    /// Given a chapter (map id) it collects every event-flag reference from the
    /// flag-usage subsystems and returns the merged + WF-sorted list:
    ///   (1) event-condition records   — the flag field of each Turn / Talk /
    ///       Object / Always condition record  (FELint EVENT_COND_*),
    ///   (2) event scripts              — every <see cref="EventScript.ArgType.FLAG"/>
    ///       referenced by the scripts reachable from the chapter's condition
    ///       slots  (FELint EVENTSCRIPT),
    ///   (3) map changes                — the flag field of each map-change record
    ///       (FELint MAPCHANGE),
    ///   (4) death quotes (Haiku)       — the flag field of each per-version Haiku
    ///       record scoped to this chapter  (FELint HAIKU), and
    ///   (5) battle conversations       — the flag field of each per-version
    ///       BattleTalk record scoped to this chapter  (FELint BATTTLE_TALK).
    ///
    /// Reuses the existing Core seams rather than re-deriving them:
    ///   <see cref="MapEventUnitCore.GetCondSlots"/>      (the per-version slot→type table),
    ///   <see cref="MapEventUnitCore.GetEventAddrForMap"/> (map id → cond block),
    ///   <see cref="EventScriptReferenceScanner.EnumerateEventEntries"/> +
    ///   the <see cref="EventScript.ArgType.FLAG"/> walk, and
    ///   <see cref="MapChangeCore.MakeFlagIDArray"/>.
    ///
    /// The Haiku/BattleTalk scanners (#1253) port the WinForms
    /// EventHaiku*Form.MakeFlagIDArray / EventBattleTalk*Form.MakeFlagIDArray data
    /// tables into Core — the same per-version (base-pointer / block-size /
    /// flag-offset / chapter-offset) layouts ExportFilterCore already walks. Like
    /// WF ToolUseFlagForm.UpdateList, the records are read table-wide then scoped to
    /// the selected chapter (a record's chapter field at <c>chapter-offset</c>, or
    /// every chapter when that field is the 0xF0+ "any" marker).
    ///
    /// NO ROM mutation, NO undo. Every read is bounds-guarded; malformed data
    /// truncates a sub-scan and yields a partial list rather than throwing.
    /// </summary>
    public static class UseFlagScanCore
    {
        // Defensive per-slot record-walk bound (a corrupt table with no
        // terminating zero word can never loop forever).
        const int MaxRecordsPerSlot = 4096;

        /// <summary>
        /// Scan chapter <paramref name="mapId"/> and return its merged, WF-sorted
        /// flag-usage list — ONE row per distinct (flag id, source type). Returns
        /// an empty (never null) list when the ROM is missing/foreign or the
        /// chapter has no resolvable event data.
        ///
        /// DEDUP RATIONALE (PR #1254 review): WinForms ToolUseFlagForm does NOT
        /// dedup its underlying FlagList (a flag referenced from N cond-slot event
        /// trees yields N EVENTSCRIPT entries), but its owner-draw list COLLAPSES
        /// them visually — the flag header/name is drawn only when the id changes
        /// (ToolUseFlagForm.Draw: <c>current.ID != FlagList[index-1].ID</c>), so the
        /// user sees each flag's source-types grouped under one header, not the same
        /// flag repeated. The Avalonia AddressListControl is a FLAT list with no
        /// owner-draw grouping, so to reproduce that readable "one flag, its source
        /// types once each" UX we collapse to one row per (flag id, DataType) here,
        /// keeping the FIRST occurrence (its Addr/Tag). This is the right flat-list
        /// UX AND matches what WF actually displays (a single 0x07 [EVENTSCRIPT]
        /// line, not the same flag 8x).
        /// </summary>
        public static List<UseFlagIDCore> Scan(ROM rom, uint mapId)
        {
            var raw = new List<UseFlagIDCore>();
            if (rom?.RomInfo == null) return raw;

            // Collect in WF append order (cond → event-script → map-change →
            // haiku → battle-talk, matching ToolUseFlagForm.UpdateList) so the
            // first-occurrence kept per (id, type) is deterministic.
            AppendEventCondFlags(rom, mapId, raw);
            AppendEventScriptFlags(rom, mapId, raw);
            MapChangeCore.MakeFlagIDArray(rom, mapId, raw);
            AppendHaikuFlags(rom, mapId, raw);
            AppendBattleTalkFlags(rom, mapId, raw);

            var list = DedupByFlagAndType(raw);
            SortLikeWinForms(list);
            return list;
        }

        // One row per (flag id, DataType), keeping the first occurrence in scan
        // order (its Addr/Tag — the first referencing site). A flag used by both
        // an EVENT_COND_* record and an event script still shows BOTH rows (one
        // per source type), so the user can tell where a flag is used; it just
        // never repeats the SAME source type for the SAME flag.
        static List<UseFlagIDCore> DedupByFlagAndType(List<UseFlagIDCore> raw)
        {
            var result = new List<UseFlagIDCore>(raw.Count);
            var seen = new HashSet<(uint, int)>();
            foreach (UseFlagIDCore u in raw)
            {
                if (seen.Add((u.ID, (int)u.DataType)))
                    result.Add(u);
            }
            return result;
        }

        // ------------------------------------------------------------
        // (1) Event-condition record flags.
        //
        // Mirrors the per-slot reads in WF EventCondForm.MakeFlagIDArray: for
        // each Object / Talk / Turn / Always slot, walk the record array
        // (stop at a zero header word) and append the u16 flag at record +2.
        // ALWAYS records whose type word (u16 @ +0) is 1 also expose a second
        // "use flag" (u16 @ +8) — preserved for parity.
        // ------------------------------------------------------------
        static void AppendEventCondFlags(ROM rom, uint mapId, List<UseFlagIDCore> list)
        {
            uint condBlock = MapEventUnitCore.GetEventAddrForMap(rom, mapId);
            if (condBlock == U.NOT_FOUND || !U.isSafetyOffset(condBlock, rom)) return;

            var slots = MapEventUnitCore.GetCondSlots(rom);
            if (slots.Count == 0) return;

            bool isFE7 = rom.RomInfo.version == 7;
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < slots.Count; i++)
            {
                uint slotAddr = (uint)(condBlock + 4 * i);
                if (slotAddr + 4 > romLen) break;

                var slot = slots[i];
                FELintCore.Type type;
                switch (slot.Type)
                {
                    case MapEventUnitCore.CondType.Object: type = FELintCore.Type.EVENT_COND_OBJECT; break;
                    case MapEventUnitCore.CondType.Talk:   type = FELintCore.Type.EVENT_COND_TALK;   break;
                    case MapEventUnitCore.CondType.Turn:   type = FELintCore.Type.EVENT_COND_TURN;   break;
                    case MapEventUnitCore.CondType.Always: type = FELintCore.Type.EVENT_COND_ALWAYS; break;
                    default:
                        // Trap / unit-placement / tutorial / start / end slots are
                        // NOT flag-bearing condition records in WF MakeFlagIDArray.
                        continue;
                }

                uint recAddr = rom.p32(slotAddr);
                if (!U.isSafetyOffset(recAddr, rom)) continue;

                uint stride = SlotRecordStride(rom, slot.Type);
                if (stride == 0) continue;

                uint addr = recAddr;
                for (int n = 0; n < MaxRecordsPerSlot; n++)
                {
                    if (!U.isSafetyOffset(addr, rom)) break;
                    if (addr + stride > romLen) break;
                    if (rom.u32(addr) == 0) break; // zero header word terminates

                    uint flag = rom.u16(addr + 2);
                    UseFlagIDCore.AppendUseFlagID(list, type, addr, slot.Name, flag, mapId, (uint)n);

                    if (slot.Type == MapEventUnitCore.CondType.Always && rom.u16(addr + 0) == 1)
                    {
                        uint useFlag = rom.u16(addr + 8);
                        UseFlagIDCore.AppendUseFlagID(list, type, addr, slot.Name, useFlag, mapId, (uint)n);
                    }

                    // FE7 short Turn record (type==1) is 12 bytes vs the slot stride.
                    if (slot.Type == MapEventUnitCore.CondType.Turn && isFE7 && rom.u8(addr) == 1)
                        addr += 12;
                    else
                        addr += stride;
                }
            }
        }

        static uint SlotRecordStride(ROM rom, MapEventUnitCore.CondType type)
        {
            switch (type)
            {
                case MapEventUnitCore.CondType.Turn: return rom.RomInfo.eventcond_tern_size;
                case MapEventUnitCore.CondType.Talk: return rom.RomInfo.eventcond_talk_size;
                case MapEventUnitCore.CondType.Object:
                case MapEventUnitCore.CondType.Always: return 12;
                default: return 0;
            }
        }

        // ------------------------------------------------------------
        // (2) Event-script FLAG references.
        //
        // EnumerateEventEntries yields the SAME per-map event-script entry
        // points WF MakeFlagIDArrayOne recurses from (tagged by mapid). For the
        // selected chapter we disassemble each entry, collecting every
        // ArgType.FLAG (recursing through POINTER_EVENT with a cycle guard) —
        // identical to WF MakeFlagIDEventScan. The Tag carries the referencing
        // command address so the UI can jump to it.
        // ------------------------------------------------------------
        static void AppendEventScriptFlags(ROM rom, uint mapId, List<UseFlagIDCore> list)
        {
            // The disasm path dereferences CoreState.ROM / CoreState.EventScript /
            // CoreState.CommentCache; only scan when this ROM IS the active one
            // and those are wired (matches EventScriptReferenceScanner gating).
            var es = CoreState.EventScript;
            if (es == null) return;
            if (CoreState.ROM == null || !ReferenceEquals(CoreState.ROM, rom)) return;
            if (CoreState.CommentCache == null) return;

            var entries = EventScriptReferenceScanner.EnumerateEventEntries(rom);

            foreach (var entry in entries)
            {
                if (entry.tag != mapId) continue; // .tag == mapid of the entry

                // WF MakeFlagIDArrayOne builds a FRESH (flag-id-keyed) result list
                // PER event-script entry tree, then appends one UseFlagID per
                // distinct flag id found in that tree. So the dedup scope is the
                // single tree, NOT the whole chapter: a flag referenced 3x inside
                // one tree -> ONE EVENTSCRIPT row (recording the FIRST command),
                // while the same flag appearing in two different cond-slot trees
                // legitimately yields one row each.
                var found = new List<(uint flag, uint cmdAddr)>();
                var tracelist = new List<uint>();
                ScanScriptForFlags(rom, es, entry.addr, tracelist, found);

                foreach (var (flag, cmdAddr) in found)
                {
                    // Mirror WF UseFlagID: Addr = the event-tree root (entry.addr),
                    // Tag = the referencing command address (cmdAddr).
                    UseFlagIDCore.AppendUseFlagID(
                        list, FELintCore.Type.EVENTSCRIPT, entry.addr, "", flag, mapId, cmdAddr);
                }
            }
        }

        // Port of WF EventCondForm.MakeFlagIDEventScan (FLAG path): disassemble an
        // event script, collect each ArgType.FLAG ONCE per distinct flag id (WF
        // U.FindList(list, flag) dedup — keeps the FIRST referencing command),
        // recursing through POINTER_EVENT with a cycle guard. Strictly read-only.
        static void ScanScriptForFlags(
            ROM rom, EventScript es, uint eventAddr,
            List<uint> tracelist, List<(uint flag, uint cmdAddr)> found)
        {
            uint addr = eventAddr;
            uint lastBranchAddr = 0;
            int unknownCount = 0;
            uint romLen = (uint)rom.Data.Length;

            for (int guard = 0; guard < 4096; guard++)
            {
                if (U.toOffset(addr) + 4 > romLen) break;

                EventScript.OneCode code = es.DisAseemble(rom.Data, addr);
                if (code?.Script == null) break;
                if (EventScript.IsExitCode(code, addr, lastBranchAddr)) break;

                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    unknownCount++;
                    if (unknownCount > 10) break;
                }
                else
                {
                    unknownCount = 0;

                    if (code.Script.Has == EventScript.ScriptHas.IF_CONDITIONAL)
                        lastBranchAddr = addr;
                    else if (code.Script.Has == EventScript.ScriptHas.LABEL_CONDITIONAL)
                        lastBranchAddr = 0;

                    if (code.Script.Args != null)
                    {
                        for (int a = 0; a < code.Script.Args.Length; a++)
                        {
                            EventScript.Arg arg = code.Script.Args[a];

                            if (arg.Type == EventScript.ArgType.POINTER_EVENT)
                            {
                                uint v = U.toOffset(EventScript.GetArgValue(code, arg));
                                if (U.isSafetyOffset(v, rom) && tracelist.IndexOf(v) < 0)
                                {
                                    tracelist.Add(v);
                                    ScanScriptForFlags(rom, es, v, tracelist, found);
                                }
                            }
                            else if (arg.Type == EventScript.ArgType.FLAG)
                            {
                                uint v = EventScript.GetArgValue(code, arg);
                                if (v == 0) continue;
                                // WF U.FindList(list, v): one entry per distinct flag id
                                // within this tree, keeping the first command seen.
                                if (ContainsFlag(found, v)) continue;
                                found.Add((v, addr));
                            }
                        }
                    }
                }

                int step = code.Script.Size;
                if (step <= 0) break;
                addr += (uint)step;
            }
        }

        static bool ContainsFlag(List<(uint flag, uint cmdAddr)> found, uint flag)
        {
            for (int i = 0; i < found.Count; i++)
                if (found[i].flag == flag) return true;
            return false;
        }

        // ------------------------------------------------------------
        // (4) Death-quote (Haiku) record flags  +  (5) Battle-conversation
        //     (BattleTalk) record flags.
        //
        // Port of the WF EventHaiku*Form.MakeFlagIDArray / EventBattleTalk*Form
        // .MakeFlagIDArray data tables (the per-version base-pointer / block-size
        // / flag-offset / chapter-offset that UseFlagID.AppendFlagID consumes) —
        // the SAME tables ExportFilterCore.CollectHaiku / CollectBattleTalk walk.
        //
        // WF AppendFlagID reads a u16 flag at flagOffset and a u8 chapter at
        // chapterOffset; a chapter >= 0xF0 becomes the "any chapter" marker
        // (mapid = U.NOT_FOUND). ToolUseFlagForm.UpdateList then keeps a row when
        // its chapter equals the selected map OR is the "any" marker. We replicate
        // that scoping HERE (so only the selected chapter's rows are appended),
        // because UseFlagScanCore.Scan is chapter-scoped by contract.
        // ------------------------------------------------------------

        // Defensive per-table record-walk bound (mirrors ExportFilterCore's 0x400
        // cap — a corrupt table with no terminating record can never loop forever).
        const int MaxHaikuBattleTalkRecords = 0x400;

        static void AppendHaikuFlags(ROM rom, uint mapId, List<UseFlagIDCore> list)
        {
            int version = rom.RomInfo.version;
            uint haikuPtr = rom.RomInfo.event_haiku_pointer;

            if (version == 8)
            {
                // EventHaikuForm: blocksize 12, flag@+4, chapter@+3.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.HAIKU,
                    haikuPtr, 12, 4, 3, TableTerm.U16Sentinel);
            }
            else if (version == 7)
            {
                // EventHaikuFE7Form main: blocksize 16, flag@+12, chapter@+1.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.HAIKU,
                    haikuPtr, 16, 12, 1, TableTerm.U8Zero);
                // + the two Lyn/Eliwood tutorial tables: blocksize 12, flag@+8,
                //   chapter@+1 (WF N1_Init reused per ReInitPointer).
                AppendFlagTable(rom, mapId, list, FELintCore.Type.HAIKU,
                    rom.RomInfo.event_haiku_tutorial_1_pointer, 12, 8, 1, TableTerm.U8Zero);
                AppendFlagTable(rom, mapId, list, FELintCore.Type.HAIKU,
                    rom.RomInfo.event_haiku_tutorial_2_pointer, 12, 8, 1, TableTerm.U8Zero);
            }
            else
            {
                // EventHaikuFE6Form: blocksize 16, flag@+8, chapter@+1.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.HAIKU,
                    haikuPtr, 16, 8, 1, TableTerm.U8Zero);
            }
        }

        static void AppendBattleTalkFlags(ROM rom, uint mapId, List<UseFlagIDCore> list)
        {
            int version = rom.RomInfo.version;
            uint talkPtr = rom.RomInfo.event_ballte_talk_pointer;
            uint talk2Ptr = rom.RomInfo.event_ballte_talk2_pointer;

            if (version == 8)
            {
                // EventBattleTalkForm: blocksize 16, flag@+6, chapter@+4.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.BATTTLE_TALK,
                    talkPtr, 16, 6, 4, TableTerm.U16Sentinel);
            }
            else if (version == 7)
            {
                // EventBattleTalkFE7Form main: blocksize 16, flag@+12, chapter@+2.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.BATTTLE_TALK,
                    talkPtr, 16, 12, 2, TableTerm.U16Sentinel);
                // + N1 (BattleTalk2): blocksize 12, flag@+8, chapter@+1.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.BATTTLE_TALK,
                    talk2Ptr, 12, 8, 1, TableTerm.U8Sentinel);
            }
            else
            {
                // EventBattleTalkFE6Form main: blocksize 12, flag@+8, chapter@+2.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.BATTTLE_TALK,
                    talkPtr, 12, 8, 2, TableTerm.U16Sentinel);
                // + N (BattleTalk2): blocksize 16, flag@+8, chapter@+1.
                AppendFlagTable(rom, mapId, list, FELintCore.Type.BATTTLE_TALK,
                    talk2Ptr, 16, 8, 1, TableTerm.U16Sentinel);
            }
        }

        // Per-version record terminator (mirrors the WF InputFormRef Init
        // DataCount predicate — when to stop counting records). All variants ALSO
        // honor the WF "i>10 && IsEmpty(addr, blocksize*10)" empty-run guard.
        enum TableTerm
        {
            /// <summary>Stop when the record's first u16 is 0xFFFF (FE8 Haiku/BattleTalk, FE7/FE6 BattleTalk).</summary>
            U16Sentinel,
            /// <summary>Stop when the record's first u8 is 0 (FE7 main Haiku, FE6 Haiku, FE7 tutorials).</summary>
            U8Zero,
            /// <summary>Stop when the record's first u8 is 0 or 0xFF (FE7 BattleTalk2).</summary>
            U8Sentinel,
        }

        // Walk one InputFormRef-style flag table: deref the base pointer, then for
        // each fixed-size record read a u16 flag (flagOffset) + u8 chapter
        // (chapterOffset), terminate per the version predicate, and append a
        // chapter-scoped HAIKU/BATTTLE_TALK row. Strictly READ-ONLY: every read is
        // bounds-guarded; malformed data terminates the walk with a partial list.
        static void AppendFlagTable(
            ROM rom, uint mapId, List<UseFlagIDCore> list, FELintCore.Type type,
            uint pointerField, uint blockSize, uint flagOffset, uint chapterOffset,
            TableTerm term)
        {
            if (list == null) return;
            // A 0 pointer field means this table does not exist for the version.
            if (pointerField == 0) return;
            if (!U.isSafetyOffset(pointerField + 3, rom)) return;

            uint baseAddr = rom.p32(pointerField);
            if (!U.isSafetyOffset(baseAddr, rom)) return;

            uint romLen = (uint)rom.Data.Length;
            uint emptyRunLen = blockSize * 10;
            uint addr = baseAddr;

            for (int i = 0; i < MaxHaikuBattleTalkRecords; i++, addr += blockSize)
            {
                // Bounds-check the FULL record (incl. the read offsets) before any
                // read so a near-EOF table can never throw.
                if (!U.isSafetyOffset(addr, rom)) break;
                if (addr + blockSize > romLen) break;

                if (IsTableTerminator(rom, addr, term)) break;
                // WF empty-run guard: after the first 10 records, an all-0x00 /
                // all-0xFF block of blocksize*10 ends the table.
                if (i > 10 && addr + emptyRunLen <= romLen && rom.IsEmpty(addr, emptyRunLen)) break;

                uint flag = rom.u16(addr + flagOffset);
                if (flag == 0) continue; // WF AppendFlagID skips id == 0.

                uint chapter = rom.u8(addr + chapterOffset);
                // WF AppendFlagID: chapter >= 0xF0 → "any chapter" (U.NOT_FOUND).
                bool anyChapter = chapter >= 0xF0;
                if (!anyChapter && chapter != mapId) continue; // chapter scope filter

                uint mapid = anyChapter ? U.NOT_FOUND : mapId;
                UseFlagIDCore.AppendUseFlagID(
                    list, type, addr, U.ToHexString((uint)i), flag, mapid, (uint)i);
            }
        }

        static bool IsTableTerminator(ROM rom, uint addr, TableTerm term)
        {
            switch (term)
            {
                case TableTerm.U16Sentinel: return rom.u16(addr) == 0xFFFF;
                case TableTerm.U8Zero:      return rom.u8(addr) == 0;
                case TableTerm.U8Sentinel:
                    uint v = rom.u8(addr);
                    return v == 0 || v == 0xFF;
                default: return false;
            }
        }

        // ------------------------------------------------------------
        // WF ToolUseFlagForm.UpdateList sort: by flag ID, then MapID, then
        // DataType — so the UI groups all sites of one flag together.
        // ------------------------------------------------------------
        static void SortLikeWinForms(List<UseFlagIDCore> list)
        {
            list.Sort((a, b) =>
            {
                if (a.ID != b.ID) return a.ID.CompareTo(b.ID);
                if (a.MapID != b.MapID) return a.MapID.CompareTo(b.MapID);
                return ((int)a.DataType).CompareTo((int)b.DataType);
            });
        }
    }
}
