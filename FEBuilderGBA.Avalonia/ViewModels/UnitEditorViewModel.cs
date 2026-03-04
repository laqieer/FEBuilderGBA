using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _selectedId;
        string _name = "";
        uint _nameId, _classId, _level, _affiliation;
        uint _hp, _str, _skl, _spd, _def, _res, _lck, _con;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint Level { get => _level; set => SetField(ref _level, value); }
        public uint Affiliation { get => _affiliation; set => SetField(ref _affiliation, value); }
        public uint HP { get => _hp; set => SetField(ref _hp, value); }
        public uint Str { get => _str; set => SetField(ref _str, value); }
        public uint Skl { get => _skl; set => SetField(ref _skl, value); }
        public uint Spd { get => _spd; set => SetField(ref _spd, value); }
        public uint Def { get => _def; set => SetField(ref _def, value); }
        public uint Res { get => _res; set => SetField(ref _res, value); }
        public uint Lck { get => _lck; set => SetField(ref _lck, value); }
        public uint Con { get => _con; set => SetField(ref _con, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Bounds check: unit data extends to at least addr + 20 (offset 19 + 1 byte)
            uint minSize = 20;
            if (addr + minSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            NameId = rom.u16(addr + 0);
            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // Offsets vary by version — use FE8U layout as base
            if (rom.RomInfo.version == 6)
            {
                ClassId = rom.u8(addr + 4);
                Level = rom.u8(addr + 8);
                HP = rom.u8(addr + 12);
                Str = rom.u8(addr + 13);
                Skl = rom.u8(addr + 14);
                Spd = rom.u8(addr + 15);
                Def = rom.u8(addr + 16);
                Res = rom.u8(addr + 17);
                Lck = rom.u8(addr + 18);
                Con = rom.u8(addr + 19);
            }
            else
            {
                // FE7/FE8 layout
                ClassId = rom.u8(addr + 4);
                Level = rom.u8(addr + 8);
                HP = rom.u8(addr + 12);
                Str = rom.u8(addr + 13);
                Skl = rom.u8(addr + 14);
                Spd = rom.u8(addr + 15);
                Def = rom.u8(addr + 16);
                Res = rom.u8(addr + 17);
                Lck = rom.u8(addr + 18);
                Con = rom.u8(addr + 19);
            }

            CanWrite = true;
        }

        public int GetListCount() => LoadUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["NameId"] = $"0x{NameId:X04}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["Level"] = $"0x{Level:X02}",
                ["HP"] = $"0x{HP:X02}",
                ["Str"] = $"0x{Str:X02}",
                ["Skl"] = $"0x{Skl:X02}",
                ["Spd"] = $"0x{Spd:X02}",
                ["Def"] = $"0x{Def:X02}",
                ["Res"] = $"0x{Res:X02}",
                ["Lck"] = $"0x{Lck:X02}",
                ["Con"] = $"0x{Con:X02}",
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x13"] = $"0x{rom.u8(a + 19):X02}",
            };
        }

        public void WriteUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, NameId);
            rom.write_u8(addr + 4, ClassId);
            rom.write_u8(addr + 8, Level);
            rom.write_u8(addr + 12, HP);
            rom.write_u8(addr + 13, Str);
            rom.write_u8(addr + 14, Skl);
            rom.write_u8(addr + 15, Spd);
            rom.write_u8(addr + 16, Def);
            rom.write_u8(addr + 17, Res);
            rom.write_u8(addr + 18, Lck);
            rom.write_u8(addr + 19, Con);
        }
    }
}
