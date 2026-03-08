using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusUnitsMenuViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _order;
        uint _textId;
        uint _d8;
        uint _textId2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint Order { get => _order; set => SetField(ref _order, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        // D8: additional dword
        public uint D8 { get => _d8; set => SetField(ref _d8, value); }
        public uint TextId2 { get => _textId2; set => SetField(ref _textId2, value); }

        public List<AddrResult> LoadStatusUnitsMenuList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.status_units_menu_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 16);
                if (addr + 16 > (uint)rom.Data.Length) break;

                uint order = rom.u32(addr);
                if (order >= 0xFF) break;

                string name = U.ToHexString(i) + " Order:" + order.ToString();
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadStatusUnitsMenu(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Order = rom.u32(addr + 0);     // D0
            TextId = rom.u32(addr + 4);    // D4
            D8 = rom.u32(addr + 8);        // D8
            TextId2 = rom.u32(addr + 12);  // D12
            CanWrite = true;
        }

        public void WriteStatusUnitsMenu()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 16 > (uint)rom.Data.Length) return;

            rom.write_u32(addr + 0, Order);
            rom.write_u32(addr + 4, TextId);
            rom.write_u32(addr + 8, D8);
            rom.write_u32(addr + 12, TextId2);
        }

        public int GetListCount() => LoadStatusUnitsMenuList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Order"] = $"0x{Order:X08}",
                ["TextId"] = $"0x{TextId:X08}",
                ["TextId2"] = $"0x{TextId2:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
