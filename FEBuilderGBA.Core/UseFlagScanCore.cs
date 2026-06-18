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
    /// PRIMARY flag-usage subsystems and returns the merged + WF-sorted list:
    ///   (1) event-condition records   — the flag field of each Turn / Talk /
    ///       Object / Always condition record  (FELint EVENT_COND_*),
    ///   (2) event scripts              — every <see cref="EventScript.ArgType.FLAG"/>
    ///       referenced by the scripts reachable from the chapter's condition
    ///       slots  (FELint EVENTSCRIPT), and
    ///   (3) map changes                — the flag field of each map-change record
    ///       (FELint MAPCHANGE).
    ///
    /// Reuses the existing Core seams rather than re-deriving them:
    ///   <see cref="MapEventUnitCore.GetCondSlots"/>      (the per-version slot→type table),
    ///   <see cref="MapEventUnitCore.GetEventAddrForMap"/> (map id → cond block),
    ///   <see cref="EventScriptReferenceScanner.EnumerateEventEntries"/> +
    ///   the <see cref="EventScript.ArgType.FLAG"/> walk, and
    ///   <see cref="MapChangeCore.MakeFlagIDArray"/>.
    ///
    /// DEFERRED (#1253): the Haiku × {FE6,FE7,FE8} and BattleTalk × {FE6,FE7,FE8}
    /// scanners (WinForms EventHaiku*Form.MakeFlagIDArray / EventBattleTalk*Form.MakeFlagIDArray)
    /// are NOT included here. Each needs its subsystem's per-version data-table
    /// layout (InputFormRef Init base-pointer / block-size / count) ported to
    /// Core first — out of scope for this slice, tracked by follow-up issue #1253.
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
        /// flag-usage list. Returns an empty (never null) list when the ROM is
        /// missing/foreign or the chapter has no resolvable event data.
        /// </summary>
        public static List<UseFlagIDCore> Scan(ROM rom, uint mapId)
        {
            var list = new List<UseFlagIDCore>();
            if (rom?.RomInfo == null) return list;

            AppendEventCondFlags(rom, mapId, list);
            AppendEventScriptFlags(rom, mapId, list);
            MapChangeCore.MakeFlagIDArray(rom, mapId, list);

            SortLikeWinForms(list);
            return list;
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
            var tracelist = new List<uint>();
            var seen = new HashSet<ulong>(); // (flag,cmdAddr) dedup across slots

            foreach (var entry in entries)
            {
                if (entry.tag != mapId) continue; // .tag == mapid of the entry
                ScanScriptForFlags(rom, es, entry.addr, mapId, tracelist, seen, list);
            }
        }

        // Port of WF EventCondForm.MakeFlagIDEventScan (FLAG path): disassemble
        // an event script, collect ArgType.FLAG, recurse through POINTER_EVENT.
        static void ScanScriptForFlags(
            ROM rom, EventScript es, uint eventAddr, uint mapId,
            List<uint> tracelist, HashSet<ulong> seen, List<UseFlagIDCore> list)
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
                                    ScanScriptForFlags(rom, es, v, mapId, tracelist, seen, list);
                                }
                            }
                            else if (arg.Type == EventScript.ArgType.FLAG)
                            {
                                uint v = EventScript.GetArgValue(code, arg);
                                if (v == 0) continue;
                                // Dedup identical (flag id, referencing command) pairs so a
                                // script reached from multiple slots is not double-listed.
                                ulong key = ((ulong)v << 32) | addr;
                                if (!seen.Add(key)) continue;
                                UseFlagIDCore.AppendUseFlagID(
                                    list, FELintCore.Type.EVENTSCRIPT, addr, "", v, mapId, addr);
                            }
                        }
                    }
                }

                int step = code.Script.Size;
                if (step <= 0) break;
                addr += (uint)step;
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
