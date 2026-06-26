using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Status R-Menu list builder (Core, READ-ONLY, PURE) — port of WinForms
    /// <c>StatusRMenuForm</c>'s multi-table FilterComboBox + <c>ListFounder</c>
    /// directional-pointer traversal (#1459).
    ///
    /// WinForms exposes up to SIX independent RMenu "menu graphs" via a
    /// FilterComboBox: status params, items held, weapon level, battle forecast
    /// 1, battle forecast 2, and — on FE8 only — the status screen. Each table
    /// is rooted at one of the six <c>RomInfo.status_rmenu*_pointer</c> slots and
    /// is discovered by following the four directional pointers
    /// (up/down/left/right at +0/+4/+8/+12) of each 28-byte node
    /// (<c>IsMemoryNotContinuous</c> — the nodes are NOT contiguous in ROM).
    ///
    /// The legacy Avalonia editor only ever read <c>status_rmenu_unit_pointer</c>
    /// and used a weaker linear <c>+i*28</c> scan, so 5 of the 6 tables were
    /// invisible. This class restores full parity: the table switcher
    /// (<see cref="TableCount"/>/<see cref="GetTablePointer"/>) plus the WF
    /// directional traversal (<see cref="BuildTableList"/>).
    /// </summary>
    public static class StatusRMenuListCore
    {
        /// <summary>Each RMenu node is 28 (0x1C) bytes — identical across all six tables.</summary>
        public const int RMENU_STRIDE = 28;

        /// <summary>
        /// Number of selectable RMenu tables for this ROM: 6 on FE8 (the 6th is
        /// the FE8-only status-screen table), 5 otherwise. Mirrors WF's
        /// version-gated FilterComboBox population.
        /// </summary>
        public static int TableCount(ROM rom)
        {
            if (rom?.RomInfo == null) return 0;
            return rom.RomInfo.version == 8 ? 6 : 5;
        }

        /// <summary>
        /// Map a table index (0..5) to its root pointer slot. Mirrors WF
        /// <c>FilterComboBox_SelectedIndexChanged</c>:
        /// 0=unit, 1=game, 2=rmenu3, 3=rmenu4, 4=rmenu5, 5=rmenu6.
        /// Returns 0 for an out-of-range index.
        /// </summary>
        public static uint GetTablePointer(ROM rom, int tableIndex)
        {
            if (rom?.RomInfo == null) return 0;
            var info = rom.RomInfo;
            switch (tableIndex)
            {
                case 0: return info.status_rmenu_unit_pointer;
                case 1: return info.status_rmenu_game_pointer;
                case 2: return info.status_rmenu3_pointer;
                case 3: return info.status_rmenu4_pointer;
                case 4: return info.status_rmenu5_pointer;
                case 5: return info.status_rmenu6_pointer;
                default: return 0;
            }
        }

        /// <summary>
        /// Localized FilterComboBox labels (WF Japanese source keys, run through
        /// the en/ja/zh translation chain by the caller via R._). Index 5 is the
        /// FE8-only status-screen entry. The caller takes the first
        /// <see cref="TableCount"/> entries.
        /// </summary>
        public static readonly string[] TableLabelKeys =
        {
            "0=ステータスパラメータ", // 0 Status parameters
            "1=所持アイテム",         // 1 Items held
            "2=武器レベル",           // 2 Weapon level
            "3=戦闘予測1",            // 3 Battle forecast 1
            "4=戦闘予測2",            // 4 Battle forecast 2
            "5=状況画面",             // 5 Status screen (FE8 only)
        };

        /// <summary>
        /// Build the RMenu node list for the given table via the WF directional
        /// traversal (port of <c>StatusRMenuForm.Init</c> + <c>ListFounder</c>):
        /// seed from <c>p32(root)</c>, include every safely-reachable 28-byte
        /// node, follow the four directional pointers (+0/+4/+8/+12), dedup.
        /// A node is included regardless of whether its directional pointers are
        /// valid (terminal nodes count). Returns an empty list (never throws) for
        /// a 0 root or unsafe addresses.
        /// </summary>
        public static List<AddrResult> BuildTableList(ROM rom, int tableIndex)
        {
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint root = GetTablePointer(rom, tableIndex);
            if (root == 0) return result;
            if (!U.isSafetyOffset(root + 3, rom)) return result;

            uint start = rom.p32(root);
            if (!U.isSafetyOffset(start, rom)) return result;

            // Breadth-first walk over the menu graph. `already` mirrors WF's
            // dedup list; `queue` mirrors the WF rmenulist work queue. The start
            // node is always visited first (index 0).
            var already = new HashSet<uint>();
            var queue = new Queue<uint>();
            queue.Enqueue(start);
            already.Add(start);

            int i = 0;
            while (queue.Count > 0)
            {
                uint addr = queue.Dequeue();
                // Guard the full 28-byte node like WF's
                // `isSafetyOffset(addr) && isSafetyOffset(addr+28)`.
                if (!U.isSafetyOffset(addr, rom) || !U.isSafetyOffset(addr + RMENU_STRIDE, rom))
                {
                    continue;
                }

                string menuName = GetMenuName(rom, addr);
                string name = $"{U.ToHexString(i)} {menuName}";
                result.Add(new AddrResult(addr, name, (uint)i));
                i++;

                // ListFounder: enqueue the four directional children.
                EnqueueChild(rom, addr + 0, already, queue);
                EnqueueChild(rom, addr + 4, already, queue);
                EnqueueChild(rom, addr + 8, already, queue);
                EnqueueChild(rom, addr + 12, already, queue);
            }

            return result;
        }

        static void EnqueueChild(ROM rom, uint slotAddr, HashSet<uint> already, Queue<uint> queue)
        {
            if (!U.isSafetyOffset(slotAddr + 3, rom)) return;
            uint child = rom.p32(slotAddr);
            if (U.isSafetyOffset(child, rom) && !already.Contains(child))
            {
                already.Add(child);
                queue.Enqueue(child);
            }
        }

        /// <summary>
        /// Port of WF <c>StatusRMenuForm.GetMenuName</c>: the RMenu node's text id
        /// at +18; blank for <c>tid &lt;= 0x10</c>; the decoded name truncated at
        /// the first CRLF (WF <c>U.cut(name, "\r\n")</c>). We decode via
        /// <c>FETextDecode.Direct</c> (the Core port of the <c>TextForm.Direct</c>
        /// decode), apply the first-line cut on the RAW decode so the `\r\n`
        /// boundary is still present, then strip residual control/escape codes for
        /// a clean single-line list label (the convention every Avalonia list
        /// uses). Safe / never throws.
        /// </summary>
        public static string GetMenuName(ROM rom, uint addr)
        {
            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr, rom) || !U.isSafetyOffset(addr + RMENU_STRIDE, rom))
            {
                return "";
            }

            uint tid = rom.u16(addr + 18);
            if (tid <= 0x10)
            {
                return "";
            }

            string raw;
            try { raw = FETextDecode.Direct(tid) ?? ""; }
            catch { return ""; }
            if (raw == null) return "";

            // WF cuts at the first CRLF on the decoded (escape-bearing) text,
            // BEFORE any control-code stripping would remove the newline.
            int idx = raw.IndexOf("\r\n", StringComparison.Ordinal);
            if (idx >= 0) raw = raw.Substring(0, idx);

            // Clean single-line label (matches NameResolver.GetTextById's output
            // shape used by every other Avalonia list).
            return NameResolver.StripControlCodes(raw);
        }
    }
}
