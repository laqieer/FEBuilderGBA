using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemStatBonusesViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _hp, _str, _skill, _speed, _def, _res, _luck, _move, _con, _unknown9, _unknown10, _unknown11;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint HP { get => _hp; set => SetField(ref _hp, value); }
        public uint Str { get => _str; set => SetField(ref _str, value); }
        public uint Skill { get => _skill; set => SetField(ref _skill, value); }
        public uint Speed { get => _speed; set => SetField(ref _speed, value); }
        public uint Def { get => _def; set => SetField(ref _def, value); }
        public uint Res { get => _res; set => SetField(ref _res, value); }
        public uint Luck { get => _luck; set => SetField(ref _luck, value); }
        public uint Move { get => _move; set => SetField(ref _move, value); }
        public uint Con { get => _con; set => SetField(ref _con, value); }
        public uint Unknown9 { get => _unknown9; set => SetField(ref _unknown9, value); }
        public uint Unknown10 { get => _unknown10; set => SetField(ref _unknown10, value); }
        public uint Unknown11 { get => _unknown11; set => SetField(ref _unknown11, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadItemStatBonusesList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return new List<AddrResult>();

            uint itemBase = rom.p32(itemPtr);
            if (!U.isSafetyOffset(itemBase)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint itemAddr = (uint)(itemBase + i * dataSize);
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;

                // Check validity: offsets 12 and 16 should be pointer or null
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 16))) break;

                uint statBoosterPtr = rom.u32(itemAddr + 12);
                if (!U.isPointer(statBoosterPtr)) continue;

                uint boosterAddr = U.toOffset(statBoosterPtr);
                if (!U.isSafetyOffset(boosterAddr)) continue;

                string name = U.ToHexString(i) + " Item 0x" + i.ToString("X02");
                result.Add(new AddrResult(boosterAddr, name, i));
            }
            return result;
        }

        public void LoadItemStatBonuses(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 11 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            HP = rom.u8(addr + 0);
            Str = rom.u8(addr + 1);
            Skill = rom.u8(addr + 2);
            Speed = rom.u8(addr + 3);
            Def = rom.u8(addr + 4);
            Res = rom.u8(addr + 5);
            Luck = rom.u8(addr + 6);
            Move = rom.u8(addr + 7);
            Con = rom.u8(addr + 8);
            Unknown9 = rom.u8(addr + 9);
            Unknown10 = rom.u8(addr + 10);
            Unknown11 = rom.u8(addr + 11);

            CanWrite = true;
        }

        public void WriteItemStatBonuses()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, (byte)HP);
            rom.write_u8(addr + 1, (byte)Str);
            rom.write_u8(addr + 2, (byte)Skill);
            rom.write_u8(addr + 3, (byte)Speed);
            rom.write_u8(addr + 4, (byte)Def);
            rom.write_u8(addr + 5, (byte)Res);
            rom.write_u8(addr + 6, (byte)Luck);
            rom.write_u8(addr + 7, (byte)Move);
            rom.write_u8(addr + 8, (byte)Con);
            rom.write_u8(addr + 9, (byte)Unknown9);
            rom.write_u8(addr + 10, (byte)Unknown10);
            rom.write_u8(addr + 11, (byte)Unknown11);
        }

        public int GetListCount() => LoadItemStatBonusesList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["HP"] = $"0x{HP:X02}",
                ["Str"] = $"0x{Str:X02}",
                ["Skill"] = $"0x{Skill:X02}",
                ["Speed"] = $"0x{Speed:X02}",
                ["Def"] = $"0x{Def:X02}",
                ["Res"] = $"0x{Res:X02}",
                ["Luck"] = $"0x{Luck:X02}",
                ["Move"] = $"0x{Move:X02}",
                ["Con"] = $"0x{Con:X02}",
                ["Unknown9"] = $"0x{Unknown9:X02}",
                ["Unknown10"] = $"0x{Unknown10:X02}",
                ["Unknown11"] = $"0x{Unknown11:X02}",
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
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
