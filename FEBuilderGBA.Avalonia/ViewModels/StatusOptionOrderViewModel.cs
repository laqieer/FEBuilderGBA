using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusOptionOrderViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _optionId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint OptionId { get => _optionId; set => SetField(ref _optionId, value); }

        public List<AddrResult> LoadStatusOptionOrderList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.status_game_option_order_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Read count from the count address
            uint countAddr = rom.RomInfo.status_game_option_order_count_address;
            uint count = 0;
            if (countAddr != 0 && U.isSafetyOffset(countAddr))
                count = rom.u8(countAddr);
            if (count == 0 || count > 0x40)
                count = 0x20; // reasonable default

            var result = new List<AddrResult>();
            for (uint i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint optionId = rom.u8(addr);
                string name = U.ToHexString(i) + " Option ID: 0x" + optionId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadStatusOptionOrder(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            OptionId = rom.u8(addr);
            CanWrite = true;
        }

        public void WriteStatusOptionOrder()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr >= (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr, (byte)OptionId);
        }

        public int GetListCount() => LoadStatusOptionOrderList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["OptionId"] = $"0x{OptionId:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a):X02}",
            };
        }
    }
}
