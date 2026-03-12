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
            if (rom?.RomInfo == null || plist == 0) return U.NOT_FOUND;

            uint tablePointer = rom.RomInfo.map_event_pointer;
            if (tablePointer == 0) return U.NOT_FOUND;

            uint tableBase = rom.p32(tablePointer);
            if (!U.isSafetyOffset(tableBase)) return U.NOT_FOUND;

            uint entryAddr = (uint)(tableBase + plist * 4);
            if (!U.isSafetyOffset(entryAddr, rom)) return U.NOT_FOUND;

            uint eventAddr = rom.p32(entryAddr);
            if (!U.isSafetyOffset(eventAddr)) return U.NOT_FOUND;

            return eventAddr;
        }

        /// <summary>
        /// Get the event condition block address for a given map ID.
        /// </summary>
        public static uint GetEventAddrForMap(ROM rom, uint mapId)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            uint mapAddr = MapSettingCore.GetMapAddr(mapId);
            if (mapAddr == U.NOT_FOUND) return U.NOT_FOUND;

            uint eventPlistPos = rom.RomInfo.map_setting_event_plist_pos;
            if (eventPlistPos == 0) return U.NOT_FOUND;

            uint plist = rom.u8(mapAddr + eventPlistPos);
            if (plist == 0) return U.NOT_FOUND;

            return ResolvePlistToEventAddr(rom, plist);
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
                if (!U.isSafetyOffset(unitListAddr)) continue;
                if (unitListAddr == 0) continue;

                // Verify at least the first byte looks like a unit ID
                if (unitListAddr >= romLen) continue;

                result.Add(new AddrResult(unitListAddr, slots[i].Name, mapId));
            }

            return result;
        }

        /// <summary>
        /// Enumerate unit entries from a base address.
        /// Units are stored in fixed-size blocks terminated by UnitID == 0x00.
        /// </summary>
        public static List<AddrResult> EnumerateUnits(ROM rom, uint baseAddr)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;
            if (!U.isSafetyOffset(baseAddr)) return result;

            uint dataSize = rom.RomInfo.eventunit_data_size;
            uint romLen = (uint)rom.Data.Length;

            for (int i = 0; i < 200; i++) // safety limit
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > romLen) break;

                uint unitId = rom.u8(addr);
                if (unitId == 0) break; // null terminator

                uint classId = rom.u8(addr + 1);
                string unitName = NameResolver.GetUnitName(unitId);
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
    }
}
