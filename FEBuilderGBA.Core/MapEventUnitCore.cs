using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helper for extracting unit placement lists from map event data.
    /// Resolves map -> PLIST -> event condition block -> unit pointers.
    /// </summary>
    public static class MapEventUnitCore
    {
        /// <summary>
        /// Condition types for event condition block slots.
        /// Maps to entries in eventcond_FE*.en.txt config files.
        /// </summary>
        public enum CondType
        {
            Turn,
            Talk,
            Object,
            Always,
            Tutorial,
            Trap,
            PlayerUnit,
            EnemyUnit,
            FreemapPlayerUnit,
            FreemapEnemyUnit,
            StartEvent,
            EndEvent,
            Unknown,
        }

        /// <summary>
        /// Definition of a condition slot: its type and display name.
        /// </summary>
        public class CondSlot
        {
            public CondType Type;
            public string Name;
        }

        /// <summary>
        /// Get the condition slot definitions for the current ROM version.
        /// These define the layout of the event condition block (each slot = one 4-byte pointer).
        /// </summary>
        public static List<CondSlot> GetCondSlots(ROM rom)
        {
            if (rom?.RomInfo == null) return new List<CondSlot>();

            if (rom.RomInfo.version == 6)
                return GetCondSlotsFE6();
            else if (rom.RomInfo.version == 7)
                return GetCondSlotsFE7();
            else
                return GetCondSlotsFE8();
        }

        static List<CondSlot> GetCondSlotsFE6()
        {
            return new List<CondSlot>
            {
                new CondSlot { Type = CondType.Turn, Name = "Turn Conditions" },
                new CondSlot { Type = CondType.Talk, Name = "Talk Condition" },
                new CondSlot { Type = CondType.Object, Name = "Map Objects" },
                new CondSlot { Type = CondType.Always, Name = "Always Condition" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player Placement" },
                new CondSlot { Type = CondType.EnemyUnit, Name = "Enemy Placement" },
                new CondSlot { Type = CondType.EndEvent, Name = "Chapter End Event" },
            };
        }

        static List<CondSlot> GetCondSlotsFE7()
        {
            return new List<CondSlot>
            {
                new CondSlot { Type = CondType.Turn, Name = "Turn Conditions" },
                new CondSlot { Type = CondType.Talk, Name = "Talk Condition" },
                new CondSlot { Type = CondType.Object, Name = "Map Objects" },
                new CondSlot { Type = CondType.Always, Name = "Always Condition" },
                new CondSlot { Type = CondType.Trap, Name = "Trap (Eliwood)" },
                new CondSlot { Type = CondType.Trap, Name = "Trap (Hector)" },
                new CondSlot { Type = CondType.EnemyUnit, Name = "Enemy (Eliwood)" },
                new CondSlot { Type = CondType.EnemyUnit, Name = "Enemy (Eliwood Hard)" },
                new CondSlot { Type = CondType.EnemyUnit, Name = "Enemy (Hector)" },
                new CondSlot { Type = CondType.EnemyUnit, Name = "Enemy (Hector Hard)" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player (Eliwood)" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player (Eliwood Hard)" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player (Hector)" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player (Hector Hard)" },
                new CondSlot { Type = CondType.StartEvent, Name = "Chapter Start Event" },
                new CondSlot { Type = CondType.EndEvent, Name = "Chapter End Event" },
            };
        }

        static List<CondSlot> GetCondSlotsFE8()
        {
            return new List<CondSlot>
            {
                new CondSlot { Type = CondType.Turn, Name = "Turn Conditions" },
                new CondSlot { Type = CondType.Talk, Name = "Talk Condition" },
                new CondSlot { Type = CondType.Object, Name = "Map Objects" },
                new CondSlot { Type = CondType.Always, Name = "Always Condition" },
                new CondSlot { Type = CondType.Always, Name = "(DoNotUse) Player Selected" },
                new CondSlot { Type = CondType.Always, Name = "(DoNotUse) Player Move Decided" },
                new CondSlot { Type = CondType.Always, Name = "(DoNotUse) Player Moved Coord" },
                new CondSlot { Type = CondType.Tutorial, Name = "Tutorial" },
                new CondSlot { Type = CondType.Trap, Name = "Trap All Difficulties" },
                new CondSlot { Type = CondType.Trap, Name = "Trap Hard Mode" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player Placement" },
                new CondSlot { Type = CondType.PlayerUnit, Name = "Player Placement (Hard)" },
                new CondSlot { Type = CondType.FreemapPlayerUnit, Name = "Skirmish Player 1" },
                new CondSlot { Type = CondType.FreemapPlayerUnit, Name = "Skirmish Player 2" },
                new CondSlot { Type = CondType.FreemapPlayerUnit, Name = "Skirmish Player 3" },
                new CondSlot { Type = CondType.FreemapEnemyUnit, Name = "Skirmish Monster 1" },
                new CondSlot { Type = CondType.FreemapEnemyUnit, Name = "Skirmish Monster 2" },
                new CondSlot { Type = CondType.FreemapEnemyUnit, Name = "Skirmish Monster 3" },
                new CondSlot { Type = CondType.StartEvent, Name = "Chapter Start Event" },
                new CondSlot { Type = CondType.EndEvent, Name = "Chapter End Event" },
            };
        }

        /// <summary>
        /// Returns true if the given condition type is a unit placement type.
        /// </summary>
        public static bool IsUnitPlacementType(CondType type)
        {
            return type == CondType.PlayerUnit
                || type == CondType.EnemyUnit
                || type == CondType.FreemapPlayerUnit
                || type == CondType.FreemapEnemyUnit;
        }

        /// <summary>
        /// Resolve a PLIST value to the event condition block address.
        /// The event pointer table is: map_event_pointer -> base pointer table -> entry[plist] -> event cond block.
        /// </summary>
        public static uint ResolvePlistToEventAddr(ROM rom, uint plist)
        {
            return ResolvePlistToEventAddr(rom, plist, out _);
        }

        /// <summary>
        /// Like <see cref="ResolvePlistToEventAddr(ROM,uint)"/> but ALSO returns
        /// <paramref name="outPointer"/> — the EVENT-plist table slot
        /// (<c>tableBase + plist*4</c>) that holds the resolved cond-block address.
        /// Mirrors WF <c>MapPointerForm.PlistToOffsetAddrFast(EVENT, plist, out pointer)</c>:
        /// the rebuild producer relocates the cond block through this slot. On any
        /// failure path <paramref name="outPointer"/> is <see cref="U.NOT_FOUND"/>.
        /// </summary>
        public static uint ResolvePlistToEventAddr(ROM rom, uint plist, out uint outPointer)
        {
            outPointer = U.NOT_FOUND;
            if (rom?.RomInfo == null || plist == 0) return U.NOT_FOUND;

            uint tablePointer = U.toOffset(rom.RomInfo.map_event_pointer);
            if (tablePointer == 0) return U.NOT_FOUND;
            // Guard the FULL 4-byte slot (root+3) before each p32: isSafetyOffset(x) alone is a 1-byte
            // check, so a slot in [Length-3, Length-1] would still throw inside p32->u32->check_safety on a
            // near-EOF/synthetic ROM. On a valid ROM these are always in-bounds — this only hardens.
            if (!U.isSafetyOffset(tablePointer + 3, rom)) return U.NOT_FOUND;

            uint tableBase = rom.p32(tablePointer);
            if (!U.isSafetyOffset(tableBase, rom)) return U.NOT_FOUND;

            uint entryAddr = (uint)(tableBase + plist * 4);
            if (!U.isSafetyOffset(entryAddr + 3, rom)) return U.NOT_FOUND;

            uint eventAddr = rom.p32(entryAddr);
            if (!U.isSafetyOffset(eventAddr, rom)) return U.NOT_FOUND;

            outPointer = entryAddr;
            return eventAddr;
        }

        /// <summary>
        /// Get the event condition block address for a given map ID.
        /// </summary>
        public static uint GetEventAddrForMap(ROM rom, uint mapId)
        {
            return GetEventAddrForMap(rom, mapId, out _);
        }

        /// <summary>
        /// Like <see cref="GetEventAddrForMap(ROM,uint)"/> but ALSO returns
        /// <paramref name="mapcondPointer"/> — the EVENT-plist slot that holds the
        /// cond-block address (WF <c>GetEventAddrWhereMapID(mapid, out pointer)</c>).
        /// The rebuild producer needs this to emit the <c>EventCond Frame</c> block.
        /// On any failure path <paramref name="mapcondPointer"/> is
        /// <see cref="U.NOT_FOUND"/>.
        /// </summary>
        public static uint GetEventAddrForMap(ROM rom, uint mapId, out uint mapcondPointer)
        {
            mapcondPointer = U.NOT_FOUND;
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            // Use the ROM-pinned overload so this delegate never silently reads
            // CoreState.ROM — guarantees `rom` is the only ROM consulted.
            uint mapAddr = MapSettingCore.GetMapAddr(rom, mapId);
            if (mapAddr == U.NOT_FOUND) return U.NOT_FOUND;

            uint eventPlistPos = rom.RomInfo.map_setting_event_plist_pos;
            if (eventPlistPos == 0) return U.NOT_FOUND;

            uint plist = rom.u8(mapAddr + eventPlistPos);
            if (plist == 0) return U.NOT_FOUND;

            return ResolvePlistToEventAddr(rom, plist, out mapcondPointer);
        }

        /// <summary>
        /// Get all unit placement groups for a given map.
        /// Returns a list of (address, display-name, mapId) for each unit placement pointer in the event condition block.
        /// </summary>
        public static List<AddrResult> GetUnitGroupsForMap(ROM rom, uint mapId)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint eventAddr = GetEventAddrForMap(rom, mapId);
            if (eventAddr == U.NOT_FOUND) return result;

            var slots = GetCondSlots(rom);
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < slots.Count; i++)
            {
                if (!IsUnitPlacementType(slots[i].Type))
                    continue;

                uint slotAddr = (uint)(eventAddr + i * 4);
                if (slotAddr + 4 > romLen) break;

                uint unitListAddr = rom.p32(slotAddr);
                // ROM-pinned safety check (Copilot review #522 round 4)
                // — match the bounds against the rom actually being walked.
                if (!U.isSafetyOffset(unitListAddr, rom)) continue;
                if (unitListAddr == 0) continue;

                // Verify at least the first byte looks like a unit ID
                if (unitListAddr >= romLen) continue;

                result.Add(new AddrResult(unitListAddr, slots[i].Name, mapId));
            }

            // Event-script POINTER_UNIT scan (#776). The direct cond-slot
            // loop above only finds unit lists wired into a raw placement
            // slot. WF EventCondForm.MakeUnitPointer ALSO discovers unit
            // lists referenced from the event SCRIPT (the way a WF
            // "NEW"-allocated block becomes "booked" — the user references
            // it from a LOAD/placement command in the Event Script editor).
            // We mirror that here so the same script-referenced lists show
            // up in the Avalonia group list (EventUnitViewModel.LoadUnitGroups
            // calls this). The START_EVENT and END_EVENT cond slots hold a
            // direct event-script pointer (WF EventCondForm.cs:1730-1736), so
            // we disassemble each and collect POINTER_UNIT arg targets.
            ScanEventScriptSlots(rom, eventAddr, slots, mapId, result);

            return result;
        }

        /// <summary>
        /// Walk the START_EVENT / END_EVENT cond slots (each a direct
        /// event-script pointer) and append any
        /// <see cref="EventScript.ArgType.POINTER_UNIT"/>-referenced unit-list
        /// bases to <paramref name="result"/>, de-duplicated against the
        /// direct-cond-slot entries already there. Mirrors the script-scan
        /// half of WF <c>EventCondForm.MakeUnitPointer</c>.
        ///
        /// IMPORTANT — the Core EventScript disassembly path
        /// (<see cref="EventScript.DisAseemble"/>,
        /// <see cref="EventScript.IsExitCode"/> / FE8 dummy-end check, and the
        /// per-command <c>CommentCache</c> lookup) is **bound to the static
        /// <c>CoreState.ROM</c> / <c>CoreState.CommentCache</c>**, NOT to the
        /// <paramref name="rom"/> parameter. To avoid mis-scanning or throwing
        /// when <see cref="GetUnitGroupsForMap"/> is called with a ROM that is
        /// not the active <c>CoreState.ROM</c> (or when it is null), the
        /// script scan is **skipped** unless the passed ROM IS the active
        /// <c>CoreState.ROM</c>. When skipped, <see cref="GetUnitGroupsForMap"/>
        /// still returns the direct unit-placement cond-slot results — the
        /// editor (Avalonia <c>EventUnitViewModel.LoadUnitGroups</c> / WF) only
        /// ever calls this with the active ROM, so the script scan runs there.
        /// This is a read-path scan, so it never pins/swaps the global state.
        /// </summary>
        static void ScanEventScriptSlots(ROM rom, uint eventAddr, List<CondSlot> slots, uint mapId, List<AddrResult> result)
        {
            var es = CoreState.EventScript;
            if (es == null) return;

            // The EventScript disasm path dereferences the static CoreState.ROM
            // (+ RomInfo/Data/CommentCache) rather than `rom`; only run the scan
            // when they are the same instance so headless/multi-ROM callers can
            // never mis-scan or NullRef (#786 review on #776).
            if (CoreState.ROM == null || !ReferenceEquals(CoreState.ROM, rom)) return;

            uint romLen = (uint)rom.Data.Length;
            var tracelist = new List<uint>();

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Type != CondType.StartEvent && slots[i].Type != CondType.EndEvent)
                    continue;

                uint slotAddr = (uint)(eventAddr + i * 4);
                if (slotAddr + 4 > romLen) break;

                uint scriptAddr = rom.p32(slotAddr);
                if (!U.isSafetyOffset(scriptAddr, rom)) continue;

                ScanScriptForUnitPointers(rom, es, scriptAddr, slots[i].Name, mapId, tracelist, result);
            }
        }

        /// <summary>
        /// Disassemble the event script starting at <paramref name="scriptAddr"/>
        /// and collect <see cref="EventScript.ArgType.POINTER_UNIT"/> arg
        /// targets into <paramref name="result"/> (de-duped). Recurses through
        /// <see cref="EventScript.ArgType.POINTER_EVENT"/> args (with a
        /// trace-list cycle guard) the same way WF
        /// <c>EventCondForm.MakeUnitPointerEventScan</c> does. The scan stops
        /// on a real terminator (<see cref="EventScript.IsExitCode"/>) or after
        /// 10 consecutive UNKNOWN commands (the WF cutoff for corrupt events).
        /// </summary>
        static void ScanScriptForUnitPointers(ROM rom, EventScript es, uint scriptAddr, string name, uint mapId, List<uint> tracelist, List<AddrResult> result)
        {
            // WF INVALIDATE_UNIT_POINTER (EventUnitForm.cs:2517): a deliberate
            // "no unit specified" placeholder pointer that must not be listed.
            const uint INVALIDATE_UNIT_POINTER = 0xFFFFFF;

            uint addr = scriptAddr;
            uint lastBranchAddr = 0;
            int unknownCount = 0;

            // Bound the walk defensively (matches WF's reliance on exit/unknown
            // cutoffs but guarantees termination even on a pathological ROM).
            for (int guard = 0; guard < 4096; guard++)
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
                    if (unknownCount > 10) break;
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
                    else if (code.Script.Has == EventScript.ScriptHas.POINTER_UNIT_OR_EVENT)
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
                                    ScanScriptForUnitPointers(rom, es, v, name, mapId, tracelist, result);
                                }
                            }
                            else if (arg.Type == EventScript.ArgType.POINTER_UNIT)
                            {
                                uint v = EventScript.GetArgValue(code, arg);
                                if (!U.isPointer(v)) continue;
                                v = U.toOffset(v);
                                if (v == INVALIDATE_UNIT_POINTER) continue;
                                if (U.isSafetyOffset(v, rom) && U.FindList(result, v) == U.NOT_FOUND)
                                {
                                    result.Add(new AddrResult(v, name, mapId));
                                }
                            }
                        }
                    }
                }

                int step = code.Script.Size;
                if (step <= 0) break; // guard against a zero-size match loop
                addr += (uint)step;
            }
        }

        /// <summary>
        /// Resolve the event-condition pointer slot for a map's unit-list base.
        /// Mirrors the WF EventUnitForm pattern where the GUI needs the slot
        /// holding the pointer to the unit list (to allow repointing on
        /// table expansion via <see cref="ExpandUnitList"/>).
        ///
        /// Returns the ROM offset of the 4-byte slot whose stored pointer
        /// matches <paramref name="unitListBase"/>, or 0 if not found.
        /// </summary>
        public static uint FindEventPointerSlotForUnitList(ROM rom, uint mapId, uint unitListBase)
        {
            if (rom?.RomInfo == null) return 0;
            uint eventAddr = GetEventAddrForMap(rom, mapId);
            if (eventAddr == U.NOT_FOUND) return 0;

            var slots = GetCondSlots(rom);
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < slots.Count; i++)
            {
                if (!IsUnitPlacementType(slots[i].Type)) continue;
                uint slotAddr = (uint)(eventAddr + i * 4);
                if (slotAddr + 4 > romLen) break;
                uint p = rom.p32(slotAddr);
                if (p == unitListBase) return slotAddr;
            }
            return 0;
        }

        /// <summary>
        /// Count populated unit-block rows in the list at <paramref name="baseAddr"/>.
        /// Mirrors WF EventUnitForm.AddressList enumeration: counts rows up to
        /// the first UnitID == 0x00 (terminator).
        /// </summary>
        public static uint CountEventUnitRows(ROM rom, uint baseAddr)
        {
            if (rom?.RomInfo == null) return 0;
            // Use the rom-pinned safety overload so the bounds check matches
            // the ROM actually being walked (avoids issues when CoreState.ROM
            // differs from the supplied rom in headless / multi-ROM tooling).
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.eventunit_data_size;
            if (dataSize == 0) return 0;
            uint romLen = (uint)rom.Data.Length;
            uint count = 0;
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)i * dataSize;
                if (addr + dataSize > romLen) break;
                if (rom.u8(addr) == 0) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Enumerate unit entries from a base address.
        /// Units are stored in fixed-size blocks terminated by UnitID == 0x00.
        /// </summary>
        public static List<AddrResult> EnumerateUnits(ROM rom, uint baseAddr)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;
            // ROM-pinned safety check (Copilot review #522 round 4).
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            uint dataSize = rom.RomInfo.eventunit_data_size;
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < 200; i++) // safety limit
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > romLen) break;

                uint unitId = rom.u8(addr);
                if (unitId == 0) break; // null terminator

                uint classId = rom.u8(addr + 1);
                // 1-based ROM-stored unit ID (matches WinForms EventUnitForm).
                string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
                string className = NameResolver.GetClassName(classId);

                uint unitGrow;
                uint level;
                if (dataSize == 20)
                {
                    // FE8: W4 = unit growth
                    unitGrow = rom.u16(addr + 3);
                    level = U.ParseUnitGrowLV(unitGrow);
                }
                else
                {
                    // FE6/FE7: no level in the same way, use byte offsets
                    // Actually, FE6/FE7 don't have UnitGrowth in the 16-byte format
                    level = 0;
                }

                string display = $"{U.ToHexString((uint)i)} {unitName} ({className})";
                if (level > 0)
                    display += $" Lv:{level}";

                result.Add(new AddrResult(addr, display, (uint)i));
            }

            return result;
        }

        /// <summary>
        /// Get a human-readable description for AI1 (primary AI) byte value.
        /// Common values across all FE GBA games.
        /// </summary>
        public static string GetAI1Description(byte ai)
        {
            switch (ai)
            {
                case 0x00: return "0x00 No AI";
                case 0x01: return "0x01 Pursue and attack";
                case 0x02: return "0x02 Don't pursue, attack in range";
                case 0x03: return "0x03 Don't move, attack in range";
                case 0x04: return "0x04 Thief AI (steal & escape)";
                case 0x05: return "0x05 Pursue enemy if in range";
                case 0x06: return "0x06 Boss/Guard (attack in range, don't move)";
                case 0x07: return "0x07 Destroy cracked walls";
                case 0x08: return "0x08 Burn villages";
                case 0x09: return "0x09 Unknown/custom";
                case 0x0A: return "0x0A Attack wall only";
                case 0x0B: return "0x0B Pursue and use items";
                case 0x0C: return "0x0C Escape map";
                case 0x0D: return "0x0D Move to target coords";
                case 0x0E: return "0x0E Loot and escape";
                case 0x0F: return "0x0F Move to coords then attack";
                case 0x10: return "0x10 Link Arena AI";
                case 0x14: return "0x14 Berserk (attack anyone)";
                default:
                    if (ai >= 0x20)
                        return $"0x{ai:X02} Custom/Extended AI";
                    return $"0x{ai:X02} Unknown";
            }
        }

        /// <summary>
        /// Get a human-readable description for AI2 (secondary AI) byte value.
        /// </summary>
        public static string GetAI2Description(byte ai)
        {
            switch (ai)
            {
                case 0x00: return "0x00 No secondary AI";
                case 0x01: return "0x01 Pursue when attacked";
                case 0x02: return "0x02 Pursue when in range";
                case 0x03: return "0x03 Pursue at turn N";
                case 0x04: return "0x04 Boss AI (heal when low)";
                case 0x05: return "0x05 Random movement";
                case 0x06: return "0x06 Stationary guard";
                default: return $"0x{ai:X02} Unknown";
            }
        }

        /// <summary>
        /// Get a human-readable description for AI3 (target/recovery) byte value.
        /// </summary>
        public static string GetAI3Description(byte ai)
        {
            switch (ai)
            {
                case 0x00: return "0x00 Default target";
                case 0x01: return "0x01 Target weakest";
                case 0x02: return "0x02 Target leader";
                case 0x03: return "0x03 Target specific unit";
                case 0x04: return "0x04 Target lord";
                case 0x05: return "0x05 Random target";
                case 0x06: return "0x06 Heal allies priority";
                default: return $"0x{ai:X02} Unknown";
            }
        }

        /// <summary>
        /// Get a human-readable description for AI4 (retreat) byte value.
        /// </summary>
        public static string GetAI4Description(byte ai)
        {
            switch (ai)
            {
                case 0x00: return "0x00 No retreat";
                case 0x01: return "0x01 Retreat when low HP";
                case 0x20: return "0x20 Don't move, no move range display (Boss/NullifyMov)";
                default: return $"0x{ai:X02} Unknown";
            }
        }

        // ---------------------------------------------------------------
        // Event-unit list expansion (#431 — EventUnitFE7 gap-sweep)
        // ---------------------------------------------------------------

        /// <summary>
        /// Hard cap on the event-unit list size — mirrors the WF
        /// AddressListExpandsButton suffix on EventUnitFE7Form.Designer.cs
        /// (suffix is the inclusive max).
        /// </summary>
        public const uint EventUnitMaxListCount = 255;

        /// <summary>
        /// Expand an event-unit pointer table to <paramref name="newCount"/>
        /// entries. Mirrors the WF EventUnitForm.AddressListExpandsEvent
        /// behavior in headless / cross-platform form:
        /// <list type="number">
        ///   <item>Validate inputs (cap at <see cref="EventUnitMaxListCount"/>,
        ///     refuse shrinks, refuse zero pointer slot or zero oldCount).</item>
        ///   <item>Find a contiguous free region big enough for
        ///     <c>newCount * eventunit_data_size</c> bytes via
        ///     <see cref="ROM.FindFreeSpace"/>.</item>
        ///   <item>Copy the <paramref name="oldCount"/> existing rows to
        ///     the new region byte-for-byte.</item>
        ///   <item>Initialize each new row with <c>B0=0x01</c> (always) and
        ///     <c>B1 = starterB1</c> (defaults to <c>0x01</c> for FE7 per
        ///     EventUnitFE7Form.cs:485-490; FE8 callers pass <c>0x02</c>
        ///     per WF EventUnitForm.AddressListExpandsEvent).</item>
        ///   <item>Repoint the <paramref name="eventPointerSlot"/> to the
        ///     new region via <see cref="ROM.write_p32(uint,uint)"/>.</item>
        /// </list>
        ///
        /// Undo tracking is recorded through the ROM's ambient undo scope —
        /// the caller MUST open the scope (Undo.UndoData passed to
        /// <see cref="ROM.BeginUndoScope"/>) before calling this overload.
        /// </summary>
        /// <param name="rom">Target ROM. Must be non-null with valid
        ///   <c>RomInfo.eventunit_data_size</c>.</param>
        /// <param name="eventPointerSlot">The 4-byte slot containing the
        ///   GBA-format pointer to the current table. Will be repointed
        ///   to the new region on success.</param>
        /// <param name="oldBase">The current ROM-offset of the table.
        ///   Trusted (not re-derived from <paramref name="eventPointerSlot"/>)
        ///   so callers can pass synthesised values in tests.</param>
        /// <param name="oldCount">Currently-loaded number of rows.</param>
        /// <param name="newCount">Desired number of rows after expansion.</param>
        /// <param name="starterB1">Starter byte planted in B1 of each new
        ///   row. FE7 uses <c>0x01</c> (default); FE8 callers MUST pass
        ///   <c>0x02</c> per WF <c>EventUnitForm.AddressListExpandsEvent</c>
        ///   (which writes <c>B0=0x01, B1=0x02</c> for newly-allocated FE8
        ///   rows). The B0 starter is always <c>0x01</c>.</param>
        /// <returns>New base ROM offset, or <see cref="U.NOT_FOUND"/> on
        ///   any failure (no partial writes).</returns>
        public static uint ExpandUnitList(
            ROM rom,
            uint eventPointerSlot,
            uint oldBase,
            uint oldCount,
            uint newCount,
            byte starterB1 = 0x01)
        {
            if (rom == null || rom.RomInfo == null)
                return U.NOT_FOUND;
            if (eventPointerSlot == 0)
                return U.NOT_FOUND;
            if (oldCount == 0)
                return U.NOT_FOUND;
            if (newCount <= oldCount)
                return U.NOT_FOUND; // refuse shrinks and no-ops
            if (newCount > EventUnitMaxListCount)
                return U.NOT_FOUND;

            uint blockSize = rom.RomInfo.eventunit_data_size;
            if (blockSize == 0)
                return U.NOT_FOUND;

            // ROM-pinned safety checks so the bounds match the rom being
            // mutated (CoreState.ROM may differ in tests / multi-ROM tooling).
            if (!U.isSafetyOffset(oldBase, rom))
                return U.NOT_FOUND;
            if (!U.isSafetyOffset(eventPointerSlot, rom))
                return U.NOT_FOUND;
            if (eventPointerSlot + 4 > (uint)rom.Data.Length)
                return U.NOT_FOUND;
            if (oldBase + oldCount * blockSize > (uint)rom.Data.Length)
                return U.NOT_FOUND;

            // Find a contiguous free region. Start past the high-half so
            // we don't overlap the existing table (BattleAnimeImportCore
            // pattern — also used by ImageBattleBGCore.ExpandList).
            // Reserve room for the terminator row at the end:
            // `(newCount + 1) * blockSize` mirrors the WF allocation in
            // MoveToFreeSapceForm.cs which writes `term_onedata` (a zero
            // row) after the expanded rows. Without the explicit terminator,
            // a 0xFF-filled free region would let EnumerateUnits / WF
            // AddressList read past the table into garbage.
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint terminatorRows = 1;
            uint needSize = (newCount + terminatorRows) * blockSize;
            uint newBase = rom.FindFreeSpace(searchStart, needSize);
            if (newBase == U.NOT_FOUND)
            {
                newBase = rom.FindFreeSpace(0x100, needSize);
            }
            if (newBase == U.NOT_FOUND)
                return U.NOT_FOUND;

            // 1. Copy old rows. Undo captured by ambient scope.
            byte[] oldRows = rom.getBinaryData(oldBase, oldCount * blockSize);
            rom.write_range(newBase, oldRows);

            // 2. Initialize new rows with B0=0x01 (always) and the
            // caller-supplied starter B1 byte. FE7 callers use 0x01 (WF
            // EventUnitFE7Form.cs:485-490); FE8 callers pass 0x02 per WF
            // EventUnitForm.AddressListExpandsEvent. Remaining bytes are
            // already zero because we write a full zeroBuffer first
            // (handles 0xFF fill from FindFreeSpace). For FE8 rows the
            // zeroBuffer also clears B7 (after-coord count) and D8
            // (after-coord pointer) so the new row has no stale references
            // to the old table's coord blocks.
            // Allocate the zero buffer ONCE (Copilot review #522 third pass —
            // cast the uint blockSize to int via checked() so the intent is
            // explicit, and reuse the buffer across iterations).
            int blockSizeInt = checked((int)blockSize);
            byte[] zeroBuffer = new byte[blockSizeInt];
            for (uint i = oldCount; i < newCount; i++)
            {
                uint rowAddr = newBase + i * blockSize;
                // Write the full block as zeros first (handles 0xFF fill),
                // then plant the starter bytes.
                rom.write_range(rowAddr, zeroBuffer);
                rom.write_u8(rowAddr + 0, 0x01);
                rom.write_u8(rowAddr + 1, starterB1);
            }

            // 3. Write the zero terminator row after the expanded entries.
            // EnumerateUnits / CountEventUnitRows stop on UnitID == 0; the
            // explicit zero row makes the table well-terminated regardless
            // of whether FindFreeSpace returned a 0x00- or 0xFF-filled region.
            uint termAddr = newBase + newCount * blockSize;
            rom.write_range(termAddr, zeroBuffer);

            // 4. Repoint the event-unit pointer slot.
            rom.write_p32(eventPointerSlot, newBase);

            return newBase;
        }

        // ---------------------------------------------------------------
        // New-allocation of an unlinked event-unit list block
        // (#776 — Avalonia EventUnit New(Alloc) WF-parity)
        // ---------------------------------------------------------------

        /// <summary>
        /// Hard cap on the New-Allocation count picker — mirrors WF
        /// <c>EventUnitNewAllocForm.Designer.cs</c> (NumericUpDown
        /// Minimum=1, Maximum=50, Value=1).
        /// </summary>
        public const uint AllocCountMax = 50;

        /// <summary>
        /// Allocate a NEW, currently-unlinked event-unit list block of
        /// <paramref name="count"/> rows. Mirrors WF
        /// <c>EventUnitForm.CreateNewData</c> (EventUnitForm.cs:1665-1698)
        /// byte-for-byte:
        /// <list type="bullet">
        ///   <item>Builds <c>count * eventunit_data_size + 1</c> bytes — the
        ///     trailing <c>+1</c> byte stays <c>0x00</c> (terminator).</item>
        ///   <item>Plants <c>B0=0x01</c> in each of the <paramref name="count"/>
        ///     rows so they read as valid (non-terminator) list entries; all
        ///     other bytes stay <c>0x00</c>.</item>
        ///   <item>Allocates via the same dual path
        ///     <see cref="MapExitPointCore.NewAlloc"/> uses: the registered
        ///     <see cref="CoreState.AppendBinaryData"/> seam (production) when
        ///     wired alongside an ambient undo, else a headless
        ///     <see cref="ROM.FindFreeSpace"/> + <see cref="ROM.write_range"/>
        ///     fallback (so this is testable with no allocator wired).</item>
        /// </list>
        ///
        /// IMPORTANT — this writes NO map/event-condition pointer. WF
        /// <c>CreateNewData</c> never writes one either: the NEW block is a
        /// reserved editing convenience whose pointer the user references
        /// later from an event command. It becomes reachable through the
        /// event-script <see cref="EventScript.ArgType.POINTER_UNIT"/> scan
        /// in <see cref="GetUnitGroupsForMap"/>, not via a cond-slot write.
        ///
        /// Undo tracking is recorded through the ROM's ambient undo scope
        /// when the caller has opened one (the same discipline as
        /// <see cref="MapExitPointCore.NewAlloc"/>); passing a null
        /// <paramref name="undodata"/> is safe but the allocation will not be
        /// rollback-able through the explicit-undodata path.
        /// </summary>
        /// <param name="rom">Target ROM. Must be non-null with a valid
        ///   <c>RomInfo.eventunit_data_size</c>.</param>
        /// <param name="count">Number of rows to allocate. Must be in
        ///   <c>[1, <see cref="AllocCountMax"/>]</c>; anything outside that
        ///   range returns <see cref="U.NOT_FOUND"/> with no write.</param>
        /// <param name="undodata">Active undo group, or null to skip the
        ///   explicit-undodata allocation path.</param>
        /// <returns>The new block's ROM offset, or <see cref="U.NOT_FOUND"/>
        ///   on any failure (no partial writes).</returns>
        public static uint AllocNewUnitList(ROM rom, uint count, Undo.UndoData? undodata)
        {
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (count == 0 || count > AllocCountMax) return U.NOT_FOUND;

            uint size = rom.RomInfo.eventunit_data_size;
            if (size == 0) return U.NOT_FOUND;

            // WF payload: count * size + 1 bytes; B0 of each row = 0x01;
            // trailing terminator byte stays 0x00 (EventUnitForm.cs:1679-1683).
            byte[] data = new byte[count * size + 1];
            for (uint i = 0; i < count; i++)
            {
                data[i * size + 0] = 1;
            }

            uint newaddr = AppendBinaryDataHeadless(rom, data, undodata);
            if (newaddr == U.NOT_FOUND || newaddr == 0) return U.NOT_FOUND;
            return newaddr;
        }

        /// <summary>
        /// Headless equivalent of <c>InputFormRef.AppendBinaryData</c>,
        /// shared so all editors route through one tested allocation seam
        /// (extracted from the private copy that used to live in the Avalonia
        /// <c>EventCondViewModel</c>). Mirrors the dual path in
        /// <see cref="MapExitPointCore.NewAlloc"/>:
        /// <list type="number">
        ///   <item>When <see cref="CoreState.AppendBinaryData"/> is wired AND
        ///     an ambient undo is available, route through the delegate
        ///     (production — WinForms registers it via
        ///     <c>InputFormRef.AppendBinaryData</c>).</item>
        ///   <item>Otherwise fall back to a direct
        ///     <see cref="ROM.FindFreeSpace"/> (upper half, then lower half)
        ///     + <see cref="ROM.write_range"/> append, so headless callers /
        ///     tests work without a registered allocator.</item>
        /// </list>
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="buffer">Bytes to append.</param>
        /// <param name="undodata">Active undo group; when wired alongside the
        ///   delegate the delegate path is preferred.</param>
        /// <returns>The new ROM offset, or <see cref="U.NOT_FOUND"/> on
        ///   failure.</returns>
        public static uint AppendBinaryDataHeadless(ROM rom, byte[] buffer, Undo.UndoData? undodata)
        {
            if (rom == null) return U.NOT_FOUND;
            if (buffer == null || buffer.Length == 0) return U.NOT_FOUND;

            var allocator = CoreState.AppendBinaryData;
            if (allocator != null && undodata != null)
            {
                return allocator(buffer, undodata);
            }

            // Headless fallback: find free space in the upper half, fall back
            // to the lower half (same pattern as MapExitPointCore.NewAlloc /
            // ExpandUnitList). Captures undo through the ambient scope when
            // one is open via ROM.BeginUndoScope.
            uint searchStart = (uint)(rom.Data.Length / 2);
            uint newaddr = rom.FindFreeSpace(searchStart, (uint)buffer.Length);
            if (newaddr == U.NOT_FOUND)
            {
                newaddr = rom.FindFreeSpace(0x100u, (uint)buffer.Length);
            }
            if (newaddr == U.NOT_FOUND) return U.NOT_FOUND;
            rom.write_range(newaddr, buffer);
            return newaddr;
        }

        // ---------------------------------------------------------------
        // FE7 jump-target search helpers (#431 — EventUnitFE7 jumps)
        // ---------------------------------------------------------------

        /// <summary>
        /// Mirrors <c>EventBattleTalkFE7Form.JumpTo(unitId)</c>: search
        /// table 1 (event_ballte_talk_pointer, 16-byte blocks, terminator
        /// u16@0 == 0 || 0xFFFF) for the first row whose B0 OR B1 equals
        /// <paramref name="unitId"/>; on miss, search table 2
        /// (event_ballte_talk2_pointer, 12-byte blocks, terminator u8@0
        /// == 0 || 0xFF) with the same B0/B1 match rule.
        ///
        /// Returns the byte address of the first hit row, or 0 on no match.
        /// </summary>
        public static uint FindBattleTalkFE7UnitIdAddress(ROM rom, uint unitId)
        {
            if (rom == null || rom.RomInfo == null)
                return 0;

            // Table 1: 16-byte blocks
            uint hit = SearchUnitIdTable(rom,
                pointerSlot: rom.RomInfo.event_ballte_talk_pointer,
                blockSize: 16,
                unitId: unitId,
                checkSecondColumn: true,
                table1: true);
            if (hit != 0) return hit;

            // Table 2: 12-byte blocks
            hit = SearchUnitIdTable(rom,
                pointerSlot: rom.RomInfo.event_ballte_talk2_pointer,
                blockSize: 12,
                unitId: unitId,
                checkSecondColumn: true,
                table1: false);
            return hit;
        }

        /// <summary>
        /// Mirrors <c>SoundBossBGMForm.JumpTo(unitId)</c>: search
        /// sound_boss_bgm_pointer (8-byte blocks, terminator u16@0 ==
        /// 0xFFFF) for the first row whose B0 equals <paramref name="unitId"/>.
        ///
        /// Returns the byte address of the first hit row, or 0 on no match.
        /// </summary>
        public static uint FindBossBGMFE7UnitIdAddress(ROM rom, uint unitId)
        {
            if (rom == null || rom.RomInfo == null)
                return 0;

            uint pointerSlot = rom.RomInfo.sound_boss_bgm_pointer;
            if (pointerSlot == 0) return 0;

            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            uint romLen = (uint)rom.Data.Length;
            const uint blockSize = 8;
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > romLen) break;
                uint u16 = rom.u16(addr);
                if (u16 == 0xFFFF) break;
                uint u = rom.u8(addr);
                if (u == unitId) return addr;
            }
            return 0;
        }

        /// <summary>
        /// Mirrors <c>EventHaikuFE7Form.JumpTo(unitId, mapId)</c>:
        /// search the main haiku table (event_haiku_pointer, 16-byte
        /// blocks, terminator u8@0 == 0) for a row matching unitId.
        /// Match priority:
        /// <list type="number">
        ///   <item>Full match: unit == unitId AND (map == mapId OR map == 0x45 wildcard).</item>
        ///   <item>Unit-only fallback: unit == unitId, first occurrence wins.</item>
        /// </list>
        /// On main-table miss, search the two tutorial tables in sequence
        /// (event_haiku_tutorial_1_pointer + event_haiku_tutorial_2_pointer,
        /// 12-byte blocks, terminator u8@0 == 0) with the same priority.
        ///
        /// Returns the byte address of the first hit row, or 0 on no match.
        /// </summary>
        public static uint FindHaikuFE7Address(ROM rom, uint unitId, uint mapId)
        {
            if (rom == null || rom.RomInfo == null)
                return 0;

            // Main table: 16-byte blocks with map/wildcard match rules.
            uint hit = SearchHaikuTable(rom,
                pointerSlot: rom.RomInfo.event_haiku_pointer,
                blockSize: 16,
                unitId: unitId,
                mapId: mapId);
            if (hit != 0) return hit;

            // Tutorial tables (FE7-only): 12-byte blocks.
            uint p1 = rom.RomInfo.event_haiku_tutorial_1_pointer;
            uint p2 = rom.RomInfo.event_haiku_tutorial_2_pointer;
            foreach (uint ptr in new[] { p1, p2 })
            {
                if (ptr == 0) continue;
                hit = SearchHaikuTable(rom,
                    pointerSlot: ptr,
                    blockSize: 12,
                    unitId: unitId,
                    mapId: mapId);
                if (hit != 0) return hit;
            }
            return 0;
        }

        /// <summary>
        /// Generic unit-id table search used by BattleTalk Table1/Table2.
        /// </summary>
        static uint SearchUnitIdTable(
            ROM rom,
            uint pointerSlot,
            uint blockSize,
            uint unitId,
            bool checkSecondColumn,
            bool table1)
        {
            if (pointerSlot == 0) return 0;
            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            uint romLen = (uint)rom.Data.Length;
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > romLen) break;

                // Terminator: Table1 uses u16==0||0xFFFF; Table2 uses
                // u8@0==0||0xFF (per EventBattleTalkFE7Form.Init).
                if (table1)
                {
                    uint u16 = rom.u16(addr);
                    if (u16 == 0 || u16 == 0xFFFF) break;
                }
                else
                {
                    uint u = rom.u8(addr);
                    if (u == 0 || u == 0xFF) break;
                }

                uint u0 = rom.u8(addr);
                if (u0 == unitId) return addr;
                if (checkSecondColumn)
                {
                    uint u1 = rom.u8(addr + 1);
                    if (u1 == unitId) return addr;
                }
            }
            return 0;
        }

        /// <summary>
        /// Generic haiku-style table search with map/wildcard logic.
        /// Used by both the main haiku table and the tutorial tables.
        /// Implements the WF JumpToTable1/Table2 priority: full match
        /// (exact map or wildcard 0x45) wins; unit-only is fallback.
        /// </summary>
        static uint SearchHaikuTable(
            ROM rom,
            uint pointerSlot,
            uint blockSize,
            uint unitId,
            uint mapId)
        {
            if (pointerSlot == 0) return 0;
            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            uint romLen = (uint)rom.Data.Length;
            uint unitOnlyFallback = 0;

            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > romLen) break;

                uint u0 = rom.u8(addr);
                if (u0 == 0) break; // terminator

                if (u0 == unitId)
                {
                    uint m = rom.u8(addr + 1);
                    if (m == 0x45 || m == mapId)
                    {
                        return addr; // full match
                    }
                    if (unitOnlyFallback == 0)
                    {
                        unitOnlyFallback = addr;
                    }
                }
            }
            return unitOnlyFallback;
        }

        // ---------------------------------------------------------------
        // FE8 jump-target search helpers (#420 — EventUnit jumps)
        //
        // FE8 differs from FE7 (PR #522) in:
        //   - BattleTalk: 16-byte blocks, u16@0/u16@2 unit-id match,
        //                 u16@4 map filter. Terminator u16==0xFFFF.
        //   - Haiku: 12-byte blocks, u16@0 unit-id match, u8@3 map filter
        //            (0xFF = wildcard). Terminator u16==0xFFFF.
        //   - BossBGM: 8-byte blocks, u8@0 unit-id match. Terminator
        //              u16==0xFFFF.
        // ---------------------------------------------------------------

        /// <summary>
        /// Mirrors <c>EventBattleTalkForm.JumpTo(unit_id, map_id)</c> (FE8):
        /// search event_ballte_talk_pointer (16-byte blocks, terminator
        /// u16@0 == 0xFFFF) for the first row whose <c>u16@0</c> OR
        /// <c>u16@2</c> equals <paramref name="unitId"/>. Prefer full
        /// (unit + map) match; fall back to unit-only match.
        ///
        /// Returns the byte address of the first hit row, or 0 on no match.
        /// </summary>
        public static uint FindBattleTalkFE8UnitIdAddress(ROM rom, uint unitId, uint mapId)
        {
            if (rom == null || rom.RomInfo == null)
                return 0;

            uint pointerSlot = rom.RomInfo.event_ballte_talk_pointer;
            if (pointerSlot == 0) return 0;

            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            uint romLen = (uint)rom.Data.Length;
            const uint blockSize = 16;
            uint unitOnlyFallback = 0;

            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > romLen) break;

                uint u16Term = rom.u16(addr);
                if (u16Term == 0xFFFF) break; // terminator

                uint c0 = rom.u16(addr + 0);
                uint c2 = rom.u16(addr + 2);
                uint mid = rom.u16(addr + 4);
                if (c0 == unitId || c2 == unitId)
                {
                    if (mid == mapId) return addr; // full match
                    if (unitOnlyFallback == 0) unitOnlyFallback = addr;
                }
            }
            return unitOnlyFallback;
        }

        /// <summary>
        /// Mirrors <c>EventHaikuForm.JumpTo(unit_id, map_id)</c> (FE8):
        /// search event_haiku_pointer (12-byte blocks, terminator
        /// u16@0 == 0xFFFF) for the first row whose <c>u16@0</c> equals
        /// <paramref name="unitId"/>. Prefer match where <c>u8@3</c> is
        /// 0xFF (wildcard) or equals <paramref name="mapId"/>; fall back
        /// to unit-only.
        ///
        /// Returns the byte address of the first hit row, or 0 on no match.
        /// </summary>
        public static uint FindHaikuFE8Address(ROM rom, uint unitId, uint mapId)
        {
            if (rom == null || rom.RomInfo == null)
                return 0;

            uint pointerSlot = rom.RomInfo.event_haiku_pointer;
            if (pointerSlot == 0) return 0;

            uint baseAddr = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            uint romLen = (uint)rom.Data.Length;
            const uint blockSize = 12;
            uint unitOnlyFallback = 0;

            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > romLen) break;

                uint u16Term = rom.u16(addr);
                if (u16Term == 0xFFFF) break; // terminator

                uint unitU16 = rom.u16(addr + 0);
                uint mapU8 = rom.u8(addr + 3);
                if (unitU16 == unitId)
                {
                    if (mapU8 == 0xFF || mapU8 == mapId)
                    {
                        return addr; // full match
                    }
                    if (unitOnlyFallback == 0) unitOnlyFallback = addr;
                }
            }
            return unitOnlyFallback;
        }

        /// <summary>
        /// Mirrors <c>SoundBossBGMForm.JumpTo(unit_id)</c> (FE8 — schema
        /// identical to FE7): 8-byte blocks, terminator u16@0 == 0xFFFF.
        /// Searches u8@0 against <paramref name="unitId"/>.
        ///
        /// Returns the byte address of the first hit row, or 0 on no match.
        /// </summary>
        public static uint FindBossBGMFE8UnitIdAddress(ROM rom, uint unitId)
        {
            // FE8 schema identical to FE7 helper — delegate to the
            // existing implementation to avoid duplicating the loop logic.
            return FindBossBGMFE7UnitIdAddress(rom, unitId);
        }
    }
}
