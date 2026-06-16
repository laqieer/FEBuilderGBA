using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Unit Talk Group tool — parity with WinForms <c>ToolUnitTalkGroupForm</c> (#1197).
    /// READ-ONLY: lists each talk group (0..0xD) with the player units assigned to it
    /// (units sharing a non-zero talk group converse as a set). The talk-group byte lives
    /// at unit-struct offset +48 (FE7/FE8 only — FE6 has no talk groups).
    /// </summary>
    public class ToolUnitTalkGroupViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _detail = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The unit list for the selected talk group (one per line).</summary>
        public string Detail { get => _detail; set => SetField(ref _detail, value); }

        public const uint MaxTalkGroup = 0xD;        // groups 0..0xD (WF DummyAlloc 0xD+1)
        public const uint MaxPlayerUnitId = 0x45;    // talk groups only recorded up to 0x45
        const int TalkGroupOffset = 48;              // unit-struct offset of the talk-group byte

        // Group 0 -> AddrResult.addr 0 trips isNULL(); encode addr = group + 1.
        public static uint AddrFromGroup(uint g) => g + 1;
        public static uint GroupFromAddr(uint addr) => addr - 1;

        // FE6 has no talk groups (UnitForm.GetTalkGroupByAddr returns NOT_FOUND on v6).
        public bool SupportsTalkGroup => CoreState.ROM?.RomInfo != null && CoreState.ROM.RomInfo.version != 6;

        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            if (!SupportsTalkGroup) return result;
            for (uint g = 0; g <= MaxTalkGroup; g++)
            {
                int count = UnitsInGroup(g).Count;
                result.Add(new AddrResult(AddrFromGroup(g), R._("Talk Group") + " 0x" + U.ToHexString(g) + "  (" + count + ")", g));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            IsLoaded = true;
            if (!SupportsTalkGroup) { Detail = ""; return; }
            var names = UnitsInGroup(GroupFromAddr(addr));
            Detail = names.Count == 0 ? R._("(no units in this talk group)") : string.Join("\n", names);
        }

        /// <summary>Unit "id name" entries whose talk-group byte == g, within the player-unit range.</summary>
        public List<string> UnitsInGroup(uint g)
        {
            var names = new List<string>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return names;
            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return names;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return names;

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount == 0 ? 0x100u : rom.RomInfo.unit_maxcount;
            maxCount = Math.Min(maxCount, MaxPlayerUnitId + 1);

            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (!U.isSafetyOffset(addr + (uint)TalkGroupOffset)) break;
                if (rom.u8(addr + (uint)TalkGroupOffset) == g)
                {
                    string name = NameResolver.GetUnitName(i);
                    names.Add("0x" + U.ToHexString(i) + " " + (string.IsNullOrEmpty(name) ? "?" : name));
                }
            }
            return names;
        }
    }
}
