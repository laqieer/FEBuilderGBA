namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Helper for AI sub-editors (CallTalk, Coordinate, Range, Tiles, Units)
    /// to find valid AI sub-data addresses from the AI pointer tables for standalone init.
    ///
    /// The AI1 and AI2 pointer tables contain 4-byte entries. Each entry is a pointer
    /// to an AI script. AI scripts contain commands that reference sub-data blocks
    /// via pointers. This helper scans the AI pointer tables to find the first valid
    /// pointer to sub-data of the requested size.
    /// </summary>
    public static class AISubEditorHelper
    {
        /// <summary>
        /// Scan AI1 and AI2 pointer tables to find the first valid sub-data pointer.
        /// AI entries are 4-byte pointers; each points to an AI script blob.
        /// AI scripts are sequences of 4-byte commands followed by parameter data.
        /// We look for any valid pointer within the AI script data that points to
        /// a reasonable ROM location, then use it as the sub-editor's address.
        /// </summary>
        /// <param name="rom">The ROM to scan.</param>
        /// <param name="entrySize">Minimum byte size the sub-data must have (1, 2, or 4).</param>
        /// <returns>A valid ROM offset, or 0 if none found.</returns>
        public static uint FindFirstValidAISubData(ROM rom, int entrySize)
        {
            if (rom?.RomInfo == null) return 0;

            // Try AI1 pointer table first, then AI2
            uint[] aiPointers = { rom.RomInfo.ai1_pointer, rom.RomInfo.ai2_pointer };

            foreach (uint aiPtr in aiPointers)
            {
                if (aiPtr == 0) continue;

                uint tableBase = rom.p32(aiPtr);
                if (!U.isSafetyOffset(tableBase)) continue;

                // Walk the AI pointer table (each entry = 4 bytes = one pointer)
                for (int i = 0; i < 256; i++)
                {
                    uint entryAddr = (uint)(tableBase + i * 4);
                    if (entryAddr + 4 > (uint)rom.Data.Length) break;

                    uint entryVal = rom.u32(entryAddr);

                    // Check for end of table
                    if (!U.isPointerOrNULL(entryVal)) break;
                    if (entryVal == 0) continue; // NULL entry, skip

                    // This is a pointer to an AI script
                    uint scriptAddr = U.toOffset(entryVal);
                    if (!U.isSafetyOffset(scriptAddr)) continue;

                    // AI script is a stream of commands. Each command has a code byte
                    // followed by parameters. Some parameters are pointers to sub-data.
                    // Scan the script for pointers that look valid.
                    uint addr = ScanAIScriptForSubPointer(rom, scriptAddr, entrySize);
                    if (addr != 0) return addr;
                }
            }

            // Fallback: use the AI3 pointer table (simpler structure)
            uint ai3Ptr = rom.RomInfo.ai3_pointer;
            if (ai3Ptr != 0)
            {
                uint ai3Base = rom.p32(ai3Ptr);
                if (U.isSafetyOffset(ai3Base) && ai3Base + (uint)entrySize <= (uint)rom.Data.Length)
                {
                    // AI3 table has direct data entries; use first non-zero entry
                    for (int i = 0; i < 64; i++)
                    {
                        uint addr = (uint)(ai3Base + i * 4);
                        if (addr + 4 > (uint)rom.Data.Length) break;

                        uint val = rom.u32(addr);
                        if (U.isPointer(val))
                        {
                            uint subAddr = U.toOffset(val);
                            if (U.isSafetyOffset(subAddr) && subAddr + (uint)entrySize <= (uint)rom.Data.Length)
                                return subAddr;
                        }
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Scan an AI script blob looking for pointer parameters that reference sub-data.
        /// AI scripts are variable-length command sequences. We scan 4-byte aligned
        /// dwords looking for valid ROM pointers.
        /// </summary>
        static uint ScanAIScriptForSubPointer(ROM rom, uint scriptAddr, int entrySize)
        {
            uint romLen = (uint)rom.Data.Length;

            // Scan up to 64 dwords within the script
            for (int j = 0; j < 64; j++)
            {
                uint off = (uint)(scriptAddr + j * 4);
                if (off + 4 > romLen) break;

                uint val = rom.u32(off);

                // Check if this looks like a pointer to sub-data
                if (U.isPointer(val))
                {
                    uint subAddr = U.toOffset(val);
                    if (U.isSafetyOffset(subAddr) && subAddr + (uint)entrySize <= romLen)
                    {
                        // Validate: the sub-data should have at least one non-zero byte
                        bool hasData = false;
                        for (int k = 0; k < entrySize && subAddr + k < romLen; k++)
                        {
                            if (rom.u8(subAddr + (uint)k) != 0)
                            {
                                hasData = true;
                                break;
                            }
                        }
                        if (hasData) return subAddr;
                    }
                }
            }
            return 0;
        }
    }
}
