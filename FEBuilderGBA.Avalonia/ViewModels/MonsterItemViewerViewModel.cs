using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MonsterItemViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _itemId;
        uint _dropRate;
        uint _unknown1, _unknown2, _unknown3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint DropRate { get => _dropRate; set => SetField(ref _dropRate, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }

        public List<AddrResult> LoadMonsterItemList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.monster_item_item_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 5);
                if (addr + 5 > (uint)rom.Data.Length) break;

                if (rom.u8(addr) == 0xFF) break;

                string name = U.ToHexString(i) + " Monster Item";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMonsterItem(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 5 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ItemId = rom.u8(addr + 0);
            DropRate = rom.u8(addr + 1);
            Unknown1 = rom.u8(addr + 2);
            Unknown2 = rom.u8(addr + 3);
            Unknown3 = rom.u8(addr + 4);
            CanWrite = true;
        }

        public void WriteMonsterItem()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 5 > (uint)rom.Data.Length) return;

            rom.write_u8(CurrentAddr + 0, (byte)ItemId);
            rom.write_u8(CurrentAddr + 1, (byte)DropRate);
            rom.write_u8(CurrentAddr + 2, (byte)Unknown1);
            rom.write_u8(CurrentAddr + 3, (byte)Unknown2);
            rom.write_u8(CurrentAddr + 4, (byte)Unknown3);
        }

        public int GetListCount() => LoadMonsterItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ItemId"] = $"0x{ItemId:X02}",
                ["DropRate"] = $"0x{DropRate:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["Unknown2"] = $"0x{Unknown2:X02}",
                ["Unknown3"] = $"0x{Unknown3:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
            };
        }
    }
}
