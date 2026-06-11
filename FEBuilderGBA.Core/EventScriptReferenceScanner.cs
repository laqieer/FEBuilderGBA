using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, strictly READ-ONLY scanner that finds every
    /// event-script reference for a parameterized <see cref="EventScript.ArgType"/>.
    ///
    /// Two layers:
    ///  1. <see cref="EnumerateEventEntries"/> — a faithful port of WinForms
    ///     <c>EventCondForm.MakeEventScriptPointer</c> (+ the FE7 tutorial side
    ///     path <c>MakeEventScriptForFE7Tutorial</c>) that walks every map's
    ///     event-condition table and yields the event-script entry-point
    ///     addresses (Turn / Talk / Object[skip shop+chest] / Always / Tutorial /
    ///     Start / End conditions).
    ///  2. <see cref="FindAllArgReferences"/> — disassembles each entry point
    ///     (recursing through <see cref="EventScript.ArgType.POINTER_EVENT"/>
    ///     args with a cycle guard) and buckets every <paramref name="argType"/>
    ///     reference by its id value.
    ///
    /// This is the generic seam behind <see cref="BGReferenceFinder"/>
    /// (<see cref="EventScript.ArgType.BG"/>); future TEXT/SONG cross-reference
    /// parity work can reuse it with a different <see cref="EventScript.ArgType"/>.
    ///
    /// DOCUMENTED RESIDUAL GAPS (event-script references only; matches the
    /// scope of the WinForms references list this seam restores):
    ///  - Patch-config references (e.g. MULTICG / BGICON install metadata,
    ///    WinForms <c>PatchForm</c> struct-install scanner) are NOT covered —
    ///    that scanner is WinForms-bound and not yet ported to Core.
    ///  - ASM/MAP symbol references (WinForms <c>AsmMapFileAsmCache.GetVarsIDArray</c>)
    ///    are NOT covered — the headless ASM/MAP cache is a no-op.
    /// A BG referenced ONLY by patch metadata or an ASM/MAP symbol will still
    /// show an empty list; the common event-script "always empty" case is fixed.
    /// </summary>
    public static class EventScriptReferenceScanner
    {
        // WF cutoff: stop a single event-script walk after this many consecutive
        // UNKNOWN commands (corrupt event guard). Mirrors
        // MapEventUnitCore.ScanScriptForUnitPointers / EventCondForm.MakeVarsIDEventScan.
        const int UnknownCutoff = 10;

        // Defensive hard bound on the per-entry command walk so a pathological
        // ROM can never hang. WF relies on exit/unknown cutoffs; we add a step
        // cap for the same termination guarantee MapEventUnitCore uses.
        const int MaxSteps = 4096;

        /// <summary>
        /// Faithful port of WinForms <c>EventCondForm.MakeEventScriptPointer</c>
        /// over every map (plus the FE7-only tutorial table). Returns one
        /// <see cref="AddrResult"/> per event-script entry point:
        /// <c>addr</c> = the event-script start address, <c>name</c> =
        /// <c>"MAP {mapidHex} {slotName}"</c>, <c>tag</c> = mapid. Strictly
        /// read-only; every read is guarded and the method never throws.
        /// </summary>
        public static List<AddrResult> EnumerateEventEntries(ROM rom)
        {
            var list = new List<AddrResult>();
            if (rom?.RomInfo == null) return list;

            bool isFE7 = rom.RomInfo.version == 7;
            uint romLen = (uint)rom.Data.Length;

            // Per-version condition slot layout (Turn/Talk/Object/Always/...).
            var slots = MapEventUnitCore.GetCondSlots(rom);
            if (slots.Count == 0) return list;

            var maps = MapSettingCore.MakeMapIDList(rom);
            foreach (var map in maps)
            {
                uint mapid = map.tag;

                // mapcond_addr = the map's event-condition block base.
                // MapEventUnitCore.GetEventAddrForMap mirrors WF
                // MapSettingForm.GetEventAddrWhereMapID (map setting -> event_plist
                // -> PLIST -> cond block).
                uint mapcond_addr = MapEventUnitCore.GetEventAddrForMap(rom, mapid);
                if (mapcond_addr == U.NOT_FOUND || !U.isSafetyOffset(mapcond_addr, rom))
                    continue;

                for (int i = 0; i < slots.Count; i++)
                {
                    uint slotAddr = (uint)(mapcond_addr + 4 * i);
                    if (slotAddr + 4 > romLen) break;

                    var slot = slots[i];
                    string info = "MAP " + U.ToHexString(mapid) + " " + slot.Name;

                    if (slot.Type == MapEventUnitCore.CondType.StartEvent
                        || slot.Type == MapEventUnitCore.CondType.EndEvent)
                    {
                        // START/END: the cond-slot address itself is the
                        // event-script pointer (WF adds `addr`, the derefed
                        // pointer, directly).
                        uint eventAddr = rom.p32(slotAddr);
                        if (!U.isSafetyOffset(eventAddr, rom)) continue;
                        list.Add(new AddrResult(eventAddr, info, mapid));
                        continue;
                    }

                    uint addr = rom.p32(slotAddr);
                    if (!U.isSafetyOffset(addr, rom)) continue;

                    switch (slot.Type)
                    {
                        case MapEventUnitCore.CondType.Turn:
                            WalkTurn(rom, addr, info, mapid, isFE7, list);
                            break;
                        case MapEventUnitCore.CondType.Talk:
                            WalkTalk(rom, addr, info, mapid, list);
                            break;
                        case MapEventUnitCore.CondType.Object:
                            WalkObject(rom, addr, info, mapid, list);
                            break;
                        case MapEventUnitCore.CondType.Always:
                            WalkAlways(rom, addr, info, mapid, list);
                            break;
                        case MapEventUnitCore.CondType.Tutorial:
                            WalkTutorial(rom, addr, info, mapid, list);
                            break;
                        default:
                            // Trap / PlayerUnit / EnemyUnit / Freemap*Unit /
                            // Unknown — not event-script entry points in WF
                            // MakeEventScriptPointer (handled elsewhere). Skip.
                            break;
                    }
                }

                if (isFE7)
                {
                    AppendFE7Tutorial(rom, mapid, list);
                }
            }

            return list;
        }

        // TURN: stride eventcond_tern_size, event ptr p32(addr+4),
        // stop u32(addr)==0 || u8(addr)==0; FE7 short-turn type==1 -> addr+=12.
        static void WalkTurn(ROM rom, uint addr, string info, uint mapid, bool isFE7, List<AddrResult> list)
        {
            uint stride = rom.RomInfo.eventcond_tern_size;
            if (stride == 0) return;
            while (true)
            {
                // Bounds-check the CURRENT record BEFORE any read so a near-EOF
                // turn record can't throw (U.u32/u8 throw IndexOutOfRange past
                // the end). Mirrors the other Walk* methods' guard ordering.
                if (!U.isSafetyOffset(addr, rom)) break;
                if (!U.isSafetyOffset(addr + stride, rom)) break;
                if (rom.u32(addr) == 0) break;
                uint type = rom.u8(addr);
                if (type == 0) break;

                uint eventAddr = rom.p32(addr + 4);
                if (U.isSafetyOffset(eventAddr, rom))
                    list.Add(new AddrResult(eventAddr, info, mapid));

                if (isFE7 && type == 1)
                    addr += 12; // FE7 12-byte short-turn event
                else
                    addr += stride;
            }
        }

        // TALK: stride eventcond_talk_size, ptr p32(addr+4), stop u32(addr)==0.
        static void WalkTalk(ROM rom, uint addr, string info, uint mapid, List<AddrResult> list)
        {
            uint stride = rom.RomInfo.eventcond_talk_size;
            if (stride == 0) return;
            for (; true; addr += stride)
            {
                if (!U.isSafetyOffset(addr + stride, rom)) break;
                if (rom.u32(addr) == 0) break;

                uint eventAddr = rom.p32(addr + 4);
                if (!U.isSafetyOffset(eventAddr, rom)) continue;
                list.Add(new AddrResult(eventAddr, info, mapid));
            }
        }

        // OBJECT: fixed 12-byte, ptr p32(addr+4), object_type=u8(addr+10),
        // stop u32(addr)==0, SKIP shop/chest object types.
        static void WalkObject(ROM rom, uint addr, string info, uint mapid, List<AddrResult> list)
        {
            for (; true; addr += 12)
            {
                if (!U.isSafetyOffset(addr + 12, rom)) break;
                if (rom.u32(addr) == 0) break;

                uint eventAddr = rom.p32(addr + 4);
                if (!U.isSafetyOffset(eventAddr, rom)) continue;

                uint objectType = rom.u8(addr + 10);
                if (IsShopObjectType(rom, objectType) || IsChestObjectType(rom, objectType))
                {
                    // Shop or chest — WF skips these (their event scripts are
                    // not part of the references walk).
                }
                else
                {
                    list.Add(new AddrResult(eventAddr, info, mapid));
                }
            }
        }

        // ALWAYS: fixed 12-byte, ptr p32(addr+4), stop u32(addr)==0.
        static void WalkAlways(ROM rom, uint addr, string info, uint mapid, List<AddrResult> list)
        {
            for (; true; addr += 12)
            {
                if (!U.isSafetyOffset(addr + 12, rom)) break;
                if (rom.u32(addr) == 0) break;

                uint eventAddr = rom.p32(addr + 4);
                if (!U.isSafetyOffset(eventAddr, rom)) continue;
                list.Add(new AddrResult(eventAddr, info, mapid));
            }
        }

        // TUTORIAL (FE8 cond slot): fixed 4-byte, ptr p32(addr+0),
        // stop !isPointer(u32(addr)).
        static void WalkTutorial(ROM rom, uint addr, string info, uint mapid, List<AddrResult> list)
        {
            for (; true; addr += 4)
            {
                if (!U.isSafetyOffset(addr + 4, rom)) break;
                if (!U.isPointer(rom.u32(addr))) break;

                uint eventAddr = rom.p32(addr + 0);
                if (!U.isSafetyOffset(eventAddr, rom)) continue;
                list.Add(new AddrResult(eventAddr, info, mapid));
            }
        }

        // FE7-only tutorial table (WF MakeEventScriptForFE7Tutorial):
        // base p32(event_tutorial_pointer)+mapid*4, mapid<=0x30, 12-byte records,
        // ptr p32(addr+4), stop u32(addr)==0.
        static void AppendFE7Tutorial(ROM rom, uint mapid, List<AddrResult> list)
        {
            if (mapid > 0x30) return;

            uint tutorialPointer = rom.RomInfo.event_tutorial_pointer;
            if (tutorialPointer == 0) return;

            uint tutorialAddr = rom.p32(tutorialPointer);
            tutorialAddr = tutorialAddr + (mapid * 4);
            if (!U.isSafetyOffset(tutorialAddr, rom)) return;

            uint addr = rom.p32(tutorialAddr);
            if (!U.isSafetyOffset(addr, rom)) return;

            for (; true; addr += 12)
            {
                if (!U.isSafetyOffset(addr + 12, rom)) break;
                if (rom.u32(addr) == 0) break;

                uint eventAddr = rom.p32(addr + 4);
                if (!U.isSafetyOffset(eventAddr, rom)) continue;
                list.Add(new AddrResult(eventAddr, "Tutorial FE7", mapid));
            }
        }

        // WF EventCondForm.IsChestObjectType: FE8 chest=0x14, FE6/7 chest=0x12.
        static bool IsChestObjectType(ROM rom, uint objectType)
        {
            if (rom.RomInfo.version == 8)
                return objectType == 0x14;
            return objectType == 0x12;
        }

        // WF EventCondForm.IsShopObjectType: FE8 shop=0x16-0x18, FE6/7 shop=0x13-0x15.
        static bool IsShopObjectType(ROM rom, uint objectType)
        {
            if (rom.RomInfo.version == 8)
                return objectType == 0x16 || objectType == 0x17 || objectType == 0x18;
            return objectType == 0x13 || objectType == 0x14 || objectType == 0x15;
        }

        /// <summary>
        /// Build a <c>bgId -&gt; refs</c> map for the given
        /// <paramref name="argType"/> by disassembling every event-script entry
        /// point from <see cref="EnumerateEventEntries"/>.
        ///
        /// GATING: the Core EventScript disassembly path dereferences the static
        /// <see cref="CoreState.ROM"/> / <see cref="CoreState.EventScript"/> /
        /// <see cref="CoreState.CommentCache"/>, NOT the <paramref name="rom"/>
        /// parameter. To avoid mis-scanning (or a NullRef) when called with a ROM
        /// that is not the active <see cref="CoreState.ROM"/>, this returns an
        /// EMPTY map unless <paramref name="rom"/> IS the active
        /// <see cref="CoreState.ROM"/>, an <see cref="EventScript"/> is wired, AND
        /// <see cref="CoreState.CommentCache"/> is wired (the disasm path
        /// dereferences it). (Same discipline as
        /// <c>MapEventUnitCore.ScanEventScriptSlots</c>.)
        /// </summary>
        /// <param name="rom">Target ROM (must be the active CoreState.ROM).</param>
        /// <param name="argType">The event-script argument type to collect.</param>
        /// <param name="keepZeroId">When false, id 0 references are dropped
        /// (matches the WinForms behavior where id 0 is a "none" sentinel for
        /// most types). BG keeps 0 (a valid BG slot).</param>
        public static Dictionary<uint, List<AddrResult>> FindAllArgReferences(
            ROM rom, EventScript.ArgType argType, bool keepZeroId)
        {
            var bucket = new Dictionary<uint, List<AddrResult>>();

            var es = CoreState.EventScript;
            if (es == null) return bucket;
            // The disasm path dereferences the static CoreState.ROM AND
            // CoreState.CommentCache (EventScript.DisAseemble calls
            // CoreState.CommentCache.At(addr)); only scan when the passed ROM IS
            // the active one AND the comment cache is wired so headless/early
            // callers can never mis-scan or NullRef.
            if (CoreState.ROM == null || !ReferenceEquals(CoreState.ROM, rom)) return bucket;
            if (CoreState.CommentCache == null) return bucket;

            var entries = EnumerateEventEntries(rom);
            var tracelist = new List<uint>();

            foreach (var entry in entries)
            {
                ScanScriptForArg(rom, es, entry.addr, entry.name, argType, keepZeroId, tracelist, bucket);
            }

            // Dedup each bucket by AddrResult.addr (event-script-start address),
            // keeping the first occurrence — matches WF UseValsID.RemoveDuplicates
            // for a single id (collapses the same script reached from multiple
            // commands or maps into one entry).
            foreach (var kv in bucket)
            {
                DedupByAddr(kv.Value);
            }

            return bucket;
        }

        static void DedupByAddr(List<AddrResult> refs)
        {
            var seen = new HashSet<uint>();
            int write = 0;
            for (int read = 0; read < refs.Count; read++)
            {
                if (seen.Add(refs[read].addr))
                {
                    refs[write++] = refs[read];
                }
            }
            if (write < refs.Count)
                refs.RemoveRange(write, refs.Count - write);
        }

        /// <summary>
        /// Disassemble the event script at <paramref name="eventAddr"/> and
        /// bucket every <paramref name="argType"/> reference. Recurses through
        /// <see cref="EventScript.ArgType.POINTER_EVENT"/> args with a tracelist
        /// cycle guard. Exposed <c>internal</c> for synthetic scanner tests
        /// (InternalsVisibleTo FEBuilderGBA.Core.Tests). Strictly read-only.
        /// </summary>
        internal static void ScanScriptForArg(
            ROM rom, EventScript es, uint eventAddr, string info,
            EventScript.ArgType argType, bool keepZeroId,
            List<uint> tracelist, Dictionary<uint, List<AddrResult>> bucket)
        {
            uint addr = eventAddr;
            uint lastBranchAddr = 0;
            int unknownCount = 0;

            for (int guard = 0; guard < MaxSteps; guard++)
            {
                uint romLen = (uint)rom.Data.Length;
                if (U.toOffset(addr) + 4 > romLen) break;

                EventScript.OneCode code = es.DisAseemble(rom.Data, addr);
                if (code?.Script == null) break;

                if (EventScript.IsExitCode(code, addr, lastBranchAddr))
                    break;

                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    unknownCount++;
                    if (unknownCount > UnknownCutoff) break;
                }
                else
                {
                    unknownCount = 0;

                    if (code.Script.Has == EventScript.ScriptHas.IF_CONDITIONAL)
                    {
                        lastBranchAddr = addr;
                    }
                    else if (code.Script.Has == EventScript.ScriptHas.LABEL_CONDITIONAL)
                    {
                        lastBranchAddr = 0;
                    }

                    // WF MakeVarsIDEventScan scans EVERY command's args (not just
                    // POINTER_UNIT_OR_EVENT). Mirror that.
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
                                    ScanScriptForArg(rom, es, v, info, argType, keepZeroId, tracelist, bucket);
                                }
                            }
                            else if (arg.Type == argType)
                            {
                                uint v = EventScript.GetArgValue(code, arg);
                                if (v >= 0x7FFF) continue;
                                if (v == 0 && !keepZeroId) continue;

                                if (!bucket.TryGetValue(v, out var l))
                                {
                                    l = new List<AddrResult>();
                                    bucket[v] = l;
                                }
                                // refCommandAddr (addr) is the referencing
                                // command's address; the event-script start
                                // (eventAddr) is the dedup key applied later.
                                l.Add(new AddrResult(eventAddr, info, addr));
                            }
                        }
                    }
                }

                int step = code.Script.Size;
                if (step <= 0) break; // guard against a zero-size match loop
                addr += (uint)step;
            }
        }

        // ============================================================
        // #1028 Slice B — EventCond export-filter text-id collector.
        //
        // Faithful port of WinForms EventCondForm.MakeVarsIDEventScan: scans
        // EVERY command's args for the FOUR text-arg types
        // (TEXT / CONVERSATION_TEXT / SYSTEM_TEXT / ONELINE_TEXT), recurses
        // through POINTER_EVENT (cycle-guarded), and EXPANDS the three
        // pointer-table arg types:
        //   - POINTER_MENUEXTENDS    -> MenuDefinition -> MenuCommand {4,6}
        //   - POINTER_UNITSSHORTTEXT -> UnitsShortText {0} (size 2, count<0x46)
        //   - POINTER_TALKGROUP      -> EventTalkGroupFE7 {0} (size 4, count<=0xD)
        // After the linear walk, on FE8 it runs the special-pattern scan
        // (MakeTextIDEventScanFE8SPEvent). All reads guarded; collects into
        // the shared `ids` set (only 1 <= id < 0x7FFF, mirroring AppendTextID).
        // ============================================================

        /// <summary>
        /// Collect every text id referenced by the event-condition scripts of
        /// every map (filter category 10). Returns true if the scanner ran
        /// (prerequisites satisfied), false if it bailed (foreign ROM / no
        /// EventScript / no comment cache) — callers treat a false return as
        /// "version-absent category -> empty set", never as an error.
        /// </summary>
        public static bool CollectEventCondTextIds(ROM rom, HashSet<uint> ids)
        {
            if (rom?.Data == null || ids == null) return false;

            var es = CoreState.EventScript;
            if (es == null) return false;
            if (CoreState.ROM == null || !ReferenceEquals(CoreState.ROM, rom)) return false;
            if (CoreState.CommentCache == null) return false;

            var entries = EnumerateEventEntries(rom);
            var tracelist = new List<uint>();
            foreach (var entry in entries)
            {
                ScanScriptForTextIds(rom, es, entry.addr, tracelist, ids);
            }
            return true;
        }

        // Mirror of WinForms EventCondForm.MakeVarsIDEventScan (text-id path).
        internal static void ScanScriptForTextIds(
            ROM rom, EventScript es, uint eventAddr,
            List<uint> tracelist, HashSet<uint> ids)
        {
            // Defensive null guards: the BattleTalk/Haiku/EventCond export-filter
            // paths can reach here with a null EventScript (CoreState.EventScript
            // not yet wired) — DisAseemble would NRE. Honour the "never throws"
            // contract by no-op'ing when the scanner inputs aren't ready
            // (Copilot review finding 2).
            if (rom?.Data == null || es == null || ids == null) return;
            if (tracelist == null) return;

            uint addr = U.toOffset(eventAddr);
            if (!U.isSafetyOffset(addr, rom)) return;

            uint lastBranchAddr = 0;
            int unknownCount = 0;

            for (int guard = 0; guard < MaxSteps; guard++)
            {
                uint romLen = (uint)rom.Data.Length;
                if (U.toOffset(addr) + 4 > romLen) break;

                EventScript.OneCode code = es.DisAseemble(rom.Data, addr);
                if (code?.Script == null) break;

                if (EventScript.IsExitCode(code, addr, lastBranchAddr)) break;

                if (code.Script.Has == EventScript.ScriptHas.UNKNOWN)
                {
                    unknownCount++;
                    if (unknownCount > UnknownCutoff) break;
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
                            switch (arg.Type)
                            {
                                case EventScript.ArgType.TEXT:
                                case EventScript.ArgType.CONVERSATION_TEXT:
                                case EventScript.ArgType.SYSTEM_TEXT:
                                case EventScript.ArgType.ONELINE_TEXT:
                                    AddTextId(ids, EventScript.GetArgValue(code, arg));
                                    break;
                                case EventScript.ArgType.POINTER_MENUEXTENDS:
                                    ExpandMenuExtends(rom, EventScript.GetArgValue(code, arg), ids);
                                    break;
                                case EventScript.ArgType.POINTER_UNITSSHORTTEXT:
                                    ExpandUnitsShortText(rom, EventScript.GetArgValue(code, arg), ids);
                                    break;
                                case EventScript.ArgType.POINTER_TALKGROUP:
                                    ExpandTalkGroup(rom, EventScript.GetArgValue(code, arg), ids);
                                    break;
                                case EventScript.ArgType.POINTER_EVENT:
                                    uint v = U.toOffset(EventScript.GetArgValue(code, arg));
                                    if (U.isSafetyOffset(v, rom) && tracelist.IndexOf(v) < 0)
                                    {
                                        tracelist.Add(v);
                                        ScanScriptForTextIds(rom, es, v, tracelist, ids);
                                    }
                                    break;
                            }
                        }
                    }
                }

                int step = code.Script.Size;
                if (step <= 0) break;
                addr += (uint)step;
            }

            if (rom.RomInfo != null && rom.RomInfo.version == 8)
            {
                ScanFE8SpecialPattern(rom, U.toOffset(eventAddr), addr, ids);
            }
        }

        // WF UseValsID.AppendTextID guard: keep 1 <= id < 0x7FFF.
        static void AddTextId(HashSet<uint> ids, uint id)
        {
            if (id == 0 || id >= 0x7FFF) return;
            ids.Add(id);
        }

        // POINTER_MENUEXTENDS -> MenuExtendSplitMenuForm.MakeVarsIDArray ->
        // MenuDefinitionForm.MakeVarsIDArray(list, v, isDirectAddress:true).
        // MenuDefinition: ReInit(v) (direct base), entry size 36, count predicate
        // isPointer(u32(addr+8)); per entry MenuCommand.MakeVarsIDArray(8+p).
        static void ExpandMenuExtends(ROM rom, uint v, HashSet<uint> ids)
        {
            uint baseAddr = U.toOffset(v);
            if (baseAddr == 0 || !U.isSafetyOffset(baseAddr, rom)) return;

            const uint MENU_SIZE = 36;
            uint p = baseAddr;
            for (int i = 0; i < 0x100; i++, p += MENU_SIZE)
            {
                if (!U.isSafetyOffset(p + 8 + 3, rom)) break;
                if (!U.isPointer(rom.u32(p + 8))) break; // count predicate
                uint paddr = rom.p32(8 + p);
                if (!U.isSafetyOffset(paddr, rom)) continue;
                // MenuCommand.MakeVarsIDArray(list, 8 + p) — ReInitPointer(8+p)
                // dereferences (8+p) to a base, then scans {4,6}.
                ExpandMenuCommand(rom, 8 + p, ids);
            }
        }

        // MenuCommandForm: ReInitPointer(pointer) -> base = p32(pointer); entry
        // size 36, count predicate isPointer(u32(addr+0xc)); text ids {4,6} (u16).
        static void ExpandMenuCommand(ROM rom, uint pointer, HashSet<uint> ids)
        {
            if (!U.isSafetyOffset(pointer + 3, rom)) return;
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return;

            const uint MENU_SIZE = 36;
            uint p = baseAddr;
            for (int i = 0; i < 0x100; i++, p += MENU_SIZE)
            {
                if (!U.isSafetyOffset(p + 0xc + 3, rom)) break;
                if (!U.isPointer(rom.u32(p + 0xc))) break; // count predicate
                if (U.isSafetyOffset(p + 4 + 1, rom)) AddTextId(ids, rom.u16(p + 4));
                if (U.isSafetyOffset(p + 6 + 1, rom)) AddTextId(ids, rom.u16(p + 6));
            }
        }

        // UnitsShortTextForm: ReInit(v) direct base, entry size 2, count i<0x46,
        // text id at offset 0 (u16).
        static void ExpandUnitsShortText(ROM rom, uint v, HashSet<uint> ids)
        {
            uint baseAddr = U.toOffset(v);
            if (baseAddr == 0 || !U.isSafetyOffset(baseAddr, rom)) return;
            const uint DATAMAX = 0x45 + 1;
            uint p = baseAddr;
            for (uint i = 0; i < DATAMAX; i++, p += 2)
            {
                if (!U.isSafetyOffset(p + 1, rom)) break;
                AddTextId(ids, rom.u16(p));
            }
        }

        // EventTalkGroupFE7Form: ReInit(v) direct base, entry size 4, count i<=0xD,
        // text id at offset 0 (u16).
        static void ExpandTalkGroup(ROM rom, uint v, HashSet<uint> ids)
        {
            uint baseAddr = U.toOffset(v);
            if (baseAddr == 0 || !U.isSafetyOffset(baseAddr, rom)) return;
            uint p = baseAddr;
            for (uint i = 0; i <= 0xD; i++, p += 4)
            {
                if (!U.isSafetyOffset(p + 1, rom)) break;
                AddTextId(ids, rom.u16(p));
            }
        }

        // FE8 special-pattern scan (WF MakeTextIDEventScanFE8SPEvent): for each of
        // the 5 hand-rolled binary patterns (0xFF = text-id wildcard, 0xEE =
        // don't-care wildcard), grep the [event_addr, end_addr) window; at every
        // match read the u16 at each 0xFF position — if ALL are valid text ids
        // (1..0x7FFE) add them, else discard the whole match.
        static readonly byte[][] FE8SpecialPatterns = new byte[][]
        {
            new byte[]{ 0x20,0x12,0x2E,0x00,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x40,0x05,0x03,0x00,0xFF,0xFF,0x00,0x00,0x40,0x05,0x04,0x00,0xFF,0xFF,0x00,0x00,0x40,0x0A,0x00,0x00,0xEE,0xEE,0xEE,0xEE },
            new byte[]{ 0x40,0x05,0x07,0x00,0x01,0x00,0x00,0x00,0x40,0x0C,0x01,0x00,0x0C,0x00,0x07,0x00,0x40,0x05,0x07,0x00,0x02,0x00,0x00,0x00,0x40,0x0C,0x02,0x00,0x0C,0x00,0x07,0x00,0x20,0x08,0x00,0x00,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x09,0x03,0x00,0x20,0x08,0x01,0x00,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x09,0x03,0x00,0x20,0x08,0x02,0x00,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x08,0x03,0x00,0x20,0x1A,0x00,0x00,0x20,0x1B,0xEE,0xEE},
            new byte[]{ 0x40,0x05,0x04,0x00,0xEE,0xEE,0x00,0x00,0x40,0x05,0x0D,0x00,0x00,0x00,0x00,0x00,0x40,0x05,0x01,0x00,0xFF,0xFF,0x00,0x00,0x21,0x07,0x00,0x00,0x40,0x05,0x01,0x00,0xFF,0xFF,0x00,0x00,0x21,0x07,0x00,0x00,0x40,0x05,0x01,0x00,0xFF,0xFF,0x00,0x00,0x21,0x07,0x00,0x00,0x40,0x05,0x01,0x00,0xFF,0xFF,0x00,0x00,0x21,0x07,0x00,0x00,0x40,0x05,0x01,0x00,0xFF,0xFF,0x00,0x00,0x21,0x07,0x00,0x00,0x40,0x05,0x01,0x00,0xFF,0xFF,0x00,0x00,0x21,0x07,0x00,0x00,0x40,0x0A,0x00,0x00,0xEE,0xEE,0xEE,0xEE },
            new byte[]{ 0x22,0x33,0xEE,0x00,0x40,0x0C,0xEE,0xEE,0x0C,0x00,0x00,0x00,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x09,0xEE,0xEE,0x20,0x08,0xEE,0xEE,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x08,0xEE,0xEE,0x20,0x1B,0xEE,0xEE,0x20,0x1D,0x00,0x00,0x22,0x1B,0x00,0x00},
            new byte[]{ 0x20,0x19,0x00,0x00,0x40,0x05,0x01,0x00,0x02,0x00,0x00,0x00,0x41,0x0C,0xEE,0xEE,0x0C,0x00,0x01,0x00,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x09,0xEE,0xEE,0x20,0x08,0xEE,0xEE,0x40,0x05,0x02,0x00,0xFF,0xFF,0x00,0x00,0x20,0x08,0xEE,0xEE,0x20,0x1A,0x00,0x00,0x20,0x1B,0xEE,0xEE},
        };

        static void ScanFE8SpecialPattern(ROM rom, uint eventAddr, uint endAddr, HashSet<uint> ids)
        {
            if (!U.isSafetyOffset(eventAddr, rom)) return;
            uint romLen = (uint)rom.Data.Length;
            if (endAddr > romLen) endAddr = romLen;

            foreach (byte[] bin in FE8SpecialPatterns)
            {
                bool[] mask = U.MakeMask2(bin, 0xFF, 0xEE);
                uint addr = eventAddr;
                while (true)
                {
                    addr = U.GrepPatternMatch(rom.Data, bin, mask, addr, endAddr, 4);
                    if (addr == U.NOT_FOUND) break;

                    var temp = new List<uint>();
                    bool ok = true;
                    for (uint i = 0; i < bin.Length; i += 2)
                    {
                        if (bin[i] != 0xFF) continue;
                        if (!U.isSafetyOffset(addr + i + 1, rom)) { ok = false; break; }
                        uint textid = rom.u16(addr + i);
                        if (textid == 0 || textid >= 0x7FFF) { ok = false; break; }
                        temp.Add(textid);
                    }
                    if (ok && temp.Count > 0)
                    {
                        foreach (uint t in temp) ids.Add(t);
                    }
                    addr += (uint)bin.Length;
                    if (addr >= endAddr) break;
                }
            }
        }
    }
}
