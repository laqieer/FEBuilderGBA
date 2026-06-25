using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY, PURE Core port of the FE7 event move-data command walk
    /// (WinForms <c>EventMoveDataFE7Form</c>). A move-data block is a
    /// variable-length sequence of 1-byte commands; types 9 (Highlight) and
    /// 0xC (Speed change) carry an extra parameter byte (B1/time-speed) at
    /// offset+1, so they advance the cursor by 2 instead of 1.
    ///
    /// Single source of truth for the stride / enable logic shared by the
    /// editor list walk (<see cref="WalkCommands"/>) and the standalone
    /// move-data validator (<c>EventSubEditorHelper.ValidateMoveData</c>),
    /// mirroring WinForms <c>IsEnableData</c> / <c>IsAppnedData</c> verbatim.
    /// </summary>
    public static class EventMoveDataFE7Core
    {
        /// <summary>Hard cap on commands walked, to avoid runaway scans.</summary>
        public const int MaxCommands = 256;

        /// <summary>
        /// WinForms <c>IsEnableData</c>: a byte is a valid move command iff it
        /// is a direction (0..3), 0xA (enemy collision mark), 9 (highlight) or
        /// 0xC (speed change). 0x04 (terminator) and everything else stop the walk.
        /// </summary>
        public static bool IsEnableData(uint data)
        {
            return data <= 3 || data == 0xA || data == 9 || data == 0xC;
        }

        /// <summary>
        /// WinForms <c>IsAppnedData</c>: only types 9 (Highlight) and 0xC
        /// (Speed change) carry an extra parameter byte (B1/time) at offset+1.
        /// NOTE: 0xA is a single-byte command — it does NOT carry a parameter.
        /// </summary>
        public static bool IsAppnedData(uint data)
        {
            return data == 9 || data == 0xC;
        }

        /// <summary>
        /// Stride (in bytes) for a command of the given type: 2 for appended
        /// types (9/0xC), else 1. Mirrors the WinForms <c>Init</c> next-addr lambda.
        /// </summary>
        public static uint Stride(uint type)
        {
            return IsAppnedData(type) ? 2u : 1u;
        }

        /// <summary>
        /// Human-readable label for a move-command type byte.
        /// </summary>
        public static string DirectionLabel(uint type)
        {
            switch (type)
            {
                case 0x00: return "Left";
                case 0x01: return "Right";
                case 0x02: return "Down";
                case 0x03: return "Up";
                case 0x04: return "End";
                case 0x09: return "Highlight";
                case 0x0A: return "Collision mark";
                case 0x0C: return "Speed change";
                default: return "?";
            }
        }

        /// <summary>
        /// Walk the variable-length move-command sequence starting at
        /// <paramref name="baseAddr"/>, returning one <see cref="AddrResult"/>
        /// per command. Each command is its own selectable row. Stops at the
        /// first byte that is not an enable command (e.g. 0x04 terminator) or
        /// when the cursor would leave the ROM. Verbatim port of WinForms
        /// <c>EventMoveDataFE7Form.Init</c> (stride +2 for 9/0xC, else +1).
        /// </summary>
        public static List<AddrResult> WalkCommands(ROM rom, uint baseAddr)
        {
            var list = new List<AddrResult>();
            if (rom?.Data == null) return list;

            uint romLen = (uint)rom.Data.Length;
            uint addr = baseAddr;

            for (int i = 0; i < MaxCommands; i++)
            {
                if (addr >= romLen) break;

                uint type = rom.u8(addr);
                if (!IsEnableData(type)) break;

                // For appended (2-byte) commands, ensure the parameter byte is in range.
                uint stride = Stride(type);
                if (addr + stride > romLen) break;

                string name = string.Format("{0:X08} : {1}", addr, DirectionLabel(type));
                list.Add(new AddrResult(addr, name, type));

                addr += stride;
            }

            return list;
        }
    }
}
