using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Helper for FE7 event sub-editors (BattleData, MoveData, TalkGroup)
    /// to find valid sub-data addresses from event script pointer tables.
    ///
    /// These sub-editors are normally opened from event scripts which provide
    /// a parent address. For standalone data-verify, we scan the chapter event
    /// condition blocks to find event scripts that reference sub-data pointers.
    /// </summary>
    public static class EventSubEditorHelper
    {
        /// <summary>
        /// Scan all chapter event scripts to find the first valid pointer to
        /// EventBattleData (FE7/FE6).
        /// Battle data: list of 4-byte entries, terminated by (val &amp; 0x800000) == 0x800000.
        /// </summary>
        public static uint FindFirstBattleDataAddr(ROM rom)
        {
            return ScanEventScriptsForSubData(rom, SubDataType.BattleData);
        }

        /// <summary>
        /// Scan all chapter event scripts to find the first valid pointer to
        /// EventMoveData (FE7/FE6).
        /// Move data: list of 1-byte direction codes (0-3, 9, 0xA, 0xC), variable length.
        /// </summary>
        public static uint FindFirstMoveDataAddr(ROM rom)
        {
            return ScanEventScriptsForSubData(rom, SubDataType.MoveData);
        }

        /// <summary>
        /// Scan all chapter event scripts to find the first valid pointer to
        /// TalkGroup data (FE7 only).
        /// Talk group: fixed 14 entries of 4 bytes each (0x38 bytes total).
        /// </summary>
        public static uint FindFirstTalkGroupAddr(ROM rom)
        {
            return ScanEventScriptsForSubData(rom, SubDataType.TalkGroup);
        }

        enum SubDataType { BattleData, MoveData, TalkGroup }

        /// <summary>
        /// Scan event condition blocks for all chapters.
        /// Each condition block has pointers to event scripts.
        /// We scan those event scripts for 4-byte aligned GBA pointers
        /// that point to valid sub-data of the requested type.
        /// </summary>
        static uint ScanEventScriptsForSubData(ROM rom, SubDataType type)
        {
            if (rom?.RomInfo == null) return 0;

            uint tablePointer = rom.RomInfo.map_event_pointer;
            if (tablePointer == 0) return 0;

            uint tableBase = rom.p32(tablePointer);
            if (!U.isSafetyOffset(tableBase)) return 0;

            uint romLen = (uint)rom.Data.Length;

            // Walk the PLIST event table (each entry = 4 bytes = pointer to event cond block)
            for (int plist = 1; plist < 256; plist++)
            {
                uint entryAddr = (uint)(tableBase + plist * 4);
                if (entryAddr + 4 > romLen) break;

                uint entryVal = rom.u32(entryAddr);
                if (!U.isPointer(entryVal)) continue;

                uint eventCondAddr = U.toOffset(entryVal);
                if (!U.isSafetyOffset(eventCondAddr)) continue;

                // Event condition block: array of 4-byte pointers to event scripts
                // FE7 has 12 slots (Turn, Talk, Object, Always, Trap*2, Enemy*4, Player*4, Start, End)
                int slotCount = rom.RomInfo.version == 7 ? 16 : 12;

                for (int slot = 0; slot < slotCount; slot++)
                {
                    uint slotAddr = (uint)(eventCondAddr + slot * 4);
                    if (slotAddr + 4 > romLen) break;

                    uint scriptPtr = rom.u32(slotAddr);
                    if (!U.isPointer(scriptPtr)) continue;

                    uint scriptAddr = U.toOffset(scriptPtr);
                    if (!U.isSafetyOffset(scriptAddr)) continue;

                    // Scan the event script data for pointers to sub-data
                    uint result = ScanScriptForSubPointer(rom, scriptAddr, type);
                    if (result != 0) return result;
                }
            }

            return 0;
        }

        /// <summary>
        /// Scan event script ROM data for 4-byte aligned dword values that are
        /// valid GBA pointers, then check if the pointed-to data matches the
        /// expected sub-data format.
        /// </summary>
        static uint ScanScriptForSubPointer(ROM rom, uint scriptAddr, SubDataType type)
        {
            uint romLen = (uint)rom.Data.Length;

            // Scan up to 128 dwords within the script
            for (int j = 0; j < 128; j++)
            {
                uint off = (uint)(scriptAddr + j * 4);
                if (off + 4 > romLen) break;

                uint val = rom.u32(off);
                if (!U.isPointer(val)) continue;

                uint subAddr = U.toOffset(val);
                if (!U.isSafetyOffset(subAddr)) continue;

                switch (type)
                {
                    case SubDataType.BattleData:
                        if (ValidateBattleData(rom, subAddr)) return subAddr;
                        break;
                    case SubDataType.MoveData:
                        if (ValidateMoveData(rom, subAddr)) return subAddr;
                        break;
                    case SubDataType.TalkGroup:
                        if (ValidateTalkGroup(rom, subAddr)) return subAddr;
                        break;
                }
            }

            return 0;
        }

        /// <summary>
        /// Validate that the address points to valid EventBattleData:
        /// - List of 4-byte entries
        /// - At least one entry where (val &amp; 0x800000) != 0x800000
        /// - Terminated by entry where (val &amp; 0x800000) == 0x800000
        /// </summary>
        static bool ValidateBattleData(ROM rom, uint addr)
        {
            uint romLen = (uint)rom.Data.Length;
            if (addr + 4 > romLen) return false;

            int validCount = 0;
            for (int i = 0; i < 64; i++)
            {
                uint entryAddr = (uint)(addr + i * 4);
                if (entryAddr + 4 > romLen) return false;

                uint val = rom.u32(entryAddr);

                // Terminator check
                if ((val & 0x800000) == 0x800000)
                {
                    // We need at least one valid entry before terminator
                    return validCount > 0;
                }

                // Validate: the attack type (u16) should be small, attacker (u8) should be a valid unit ID
                uint attackType = val & 0xFFFF;
                uint attacker = (val >> 16) & 0xFF;

                // Attack type should be reasonable (not a pointer-like value)
                if (attackType > 0x1000) return false;
                // Attacker byte should be a reasonable unit ID
                if (attacker > 0x80) return false;

                validCount++;
                if (validCount > 20) return false; // sanity limit
            }

            return false; // No terminator found
        }

        /// <summary>
        /// Validate that the address points to valid EventMoveData:
        /// - List of 1-byte direction codes
        /// - Valid codes: 0=Left, 1=Right, 2=Down, 3=Up, 9=Highlight, 0xA=Enemy, 0xC=Speed, 4=Term
        /// - Must have at least one direction (0-3) before a terminator (0x04) or invalid code
        /// </summary>
        static bool ValidateMoveData(ROM rom, uint addr)
        {
            uint romLen = (uint)rom.Data.Length;
            if (addr + 1 > romLen) return false;

            int directionCount = 0;
            for (int i = 0; i < 64; i++)
            {
                uint byteAddr = (uint)(addr + i);
                if (byteAddr >= romLen) return false;

                uint code = rom.u8(byteAddr);

                if (code == 0x04)
                {
                    // Terminator — need at least one direction
                    return directionCount > 0;
                }

                if (code <= 3)
                {
                    directionCount++;
                }
                else if (EventMoveDataFE7Core.IsEnableData(code))
                {
                    // Valid non-direction command (9=Highlight, 0xA=Collision mark, 0xC=Speed change).
                    // Only 9/0xC carry an extra parameter byte (stride 2); 0xA is single-byte.
                    // Use the Core stride logic as the single source of truth.
                    if (EventMoveDataFE7Core.IsAppendedData(code))
                        i++; // skip the parameter byte
                }
                else
                {
                    // Invalid code — not move data
                    return false;
                }
            }

            return false; // No terminator found
        }

        /// <summary>
        /// Validate that the address points to valid TalkGroup data:
        /// - Fixed 14 entries of 4 bytes each (text IDs / dialogue pointers)
        /// - At least one non-zero entry
        /// - Not all entries should look like ROM pointers (they should be text IDs)
        /// </summary>
        static bool ValidateTalkGroup(ROM rom, uint addr)
        {
            uint romLen = (uint)rom.Data.Length;
            uint totalSize = 4 * 14; // 0x38 bytes
            if (addr + totalSize > romLen) return false;

            int nonZeroCount = 0;
            int pointerCount = 0;
            for (int i = 0; i < 14; i++)
            {
                uint entryAddr = (uint)(addr + i * 4);
                uint val = rom.u32(entryAddr);

                if (val != 0)
                {
                    nonZeroCount++;
                    if (U.isPointer(val))
                        pointerCount++;
                }
            }

            // Should have at least one non-zero entry
            if (nonZeroCount == 0) return false;

            // If most entries are ROM pointers, this is likely not talk group data
            // (talk group entries are text IDs, which are small numbers)
            if (pointerCount > nonZeroCount / 2) return false;

            return true;
        }
    }
}
