using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for the Event Condition block allocation and PLIST
    /// write-back (Avalonia EventCond editor, issue #865).
    ///
    /// Ported byte-exact from WF EventCondForm.PreciseEevntCondArea
    /// (lines 3117-3247) and MapPointerForm.Write_Plsit (lines 579-598).
    /// </summary>
    public static class EventCondCore
    {
        // Per-version constants (verified against WF PreciseEevntCondArea)

        // FE8: MapCond.Count = 20, header = 80 bytes, 10 active slots, total = 184
        internal const int FE8SlotCount  = 20;
        internal const int FE8Active     = 10;
        internal const int FE8HeaderSize = 80;
        internal const int FE8Total      = 184;

        // FE7: MapCond.Count = 16, header = 64 bytes, 6 active slots, total = 132
        internal const int FE7SlotCount  = 16;
        internal const int FE7Active     = 6;
        internal const int FE7HeaderSize = 64;
        internal const int FE7Total      = 132;

        // FE6: MapCond.Count = 7, header = 28 bytes, 4 active slots, total = 76
        internal const int FE6SlotCount  = 7;
        internal const int FE6Active     = 4;
        internal const int FE6HeaderSize = 28;
        internal const int FE6Total      = 76;

        public static uint AllocNewEventCondBlock(ROM rom, uint mapId)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            int ver = rom.RomInfo.version;

            uint write_addr;
            if (ver == 8)
            {
                uint eventSize = (uint)(FE8SlotCount * 4);
                uint turn      = eventSize;
                uint talk      = turn + 12;
                uint mapobject = talk + 16;
                uint always    = mapobject + 12;
                uint always2   = always + 12;
                uint always3   = always2 + 12;
                uint always4   = always3 + 12;
                uint tutorial  = always4 + 12;
                uint trap      = tutorial + 4;
                uint trap2     = trap + 6;
                uint total     = trap2 + 6;

                byte[] data = new byte[total];
                write_addr = MapEventUnitCore.AppendBinaryDataHeadless(rom, data, null);
                if (write_addr == U.NOT_FOUND || write_addr == 0) return U.NOT_FOUND;

                rom.write_p32(write_addr +  0, write_addr + turn);
                rom.write_p32(write_addr +  4, write_addr + talk);
                rom.write_p32(write_addr +  8, write_addr + mapobject);
                rom.write_p32(write_addr + 12, write_addr + always);
                rom.write_p32(write_addr + 16, write_addr + always2);
                rom.write_p32(write_addr + 20, write_addr + always3);
                rom.write_p32(write_addr + 24, write_addr + always4);
                rom.write_p32(write_addr + 28, write_addr + tutorial);
                rom.write_p32(write_addr + 32, write_addr + trap);
                rom.write_p32(write_addr + 36, write_addr + trap2);
            }
            else if (ver == 7)
            {
                uint eventSize = (uint)(FE7SlotCount * 4);
                uint turn      = eventSize;
                uint talk      = turn + 16;
                uint mapobject = talk + 16;
                uint always    = mapobject + 12;
                uint trap      = always + 12;
                uint trap2     = trap + 6;
                uint total     = trap2 + 6;

                byte[] data = new byte[total];
                write_addr = MapEventUnitCore.AppendBinaryDataHeadless(rom, data, null);
                if (write_addr == U.NOT_FOUND || write_addr == 0) return U.NOT_FOUND;

                rom.write_p32(write_addr +  0, write_addr + turn);
                rom.write_p32(write_addr +  4, write_addr + talk);
                rom.write_p32(write_addr +  8, write_addr + mapobject);
                rom.write_p32(write_addr + 12, write_addr + always);
                rom.write_p32(write_addr + 16, write_addr + trap);
                rom.write_p32(write_addr + 20, write_addr + trap2);
            }
            else
            {
                uint eventSize = (uint)(FE6SlotCount * 4);
                uint turn      = eventSize;
                uint talk      = turn + 12;
                uint mapobject = talk + 12;
                uint always    = mapobject + 12;
                uint total     = always + 12;

                byte[] data = new byte[total];
                write_addr = MapEventUnitCore.AppendBinaryDataHeadless(rom, data, null);
                if (write_addr == U.NOT_FOUND || write_addr == 0) return U.NOT_FOUND;

                rom.write_p32(write_addr +  0, write_addr + turn);
                rom.write_p32(write_addr +  4, write_addr + talk);
                rom.write_p32(write_addr +  8, write_addr + mapobject);
                rom.write_p32(write_addr + 12, write_addr + always);
            }

            return write_addr;
        }

        public static bool WriteEventPLIST(ROM rom, uint plist, uint newBlockOffset)
        {
            if (rom?.RomInfo == null) return false;
            if (plist == 0) return false;

            uint slotAddr = ResolveEventPlistSlotAddr(rom, plist);
            if (slotAddr == U.NOT_FOUND) return false;
            if (!U.isSafetyOffset(slotAddr, rom)) return false;

            rom.write_p32(slotAddr, newBlockOffset);
            return true;
        }

        public static uint ResolveEventPlistSlotAddr(ROM rom, uint plist)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            if (plist == 0u) return U.NOT_FOUND;

            uint limit = MapChangeCore.IsPlistSplit(rom) ? 256u : rom.RomInfo.map_map_pointer_list_default_size;
            if (limit == 0 || plist >= limit) return U.NOT_FOUND;

            uint basePointer = rom.RomInfo.map_event_pointer;
            if (basePointer == 0) return U.NOT_FOUND;

            uint baseAddr = rom.p32(basePointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return U.NOT_FOUND;

            uint slotAddr = baseAddr + plist * 4u;
            if (slotAddr + 4u > (uint)rom.Data.Length) return U.NOT_FOUND;
            return slotAddr;
        }
    }
}
