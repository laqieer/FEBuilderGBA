using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _name = "";
        uint _nameId, _level;
        uint _hp, _str, _skl, _spd, _def, _res, _lck;
        uint _growHP, _growSTR, _growSKL, _growSPD, _growDEF, _growRES, _growLCK;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint NameId { get => _nameId; set => SetField(ref _nameId, value); }
        public uint Level { get => _level; set => SetField(ref _level, value); }
        public uint HP { get => _hp; set => SetField(ref _hp, value); }
        public uint Str { get => _str; set => SetField(ref _str, value); }
        public uint Skl { get => _skl; set => SetField(ref _skl, value); }
        public uint Spd { get => _spd; set => SetField(ref _spd, value); }
        public uint Def { get => _def; set => SetField(ref _def, value); }
        public uint Res { get => _res; set => SetField(ref _res, value); }
        public uint Lck { get => _lck; set => SetField(ref _lck, value); }
        public uint GrowHP { get => _growHP; set => SetField(ref _growHP, value); }
        public uint GrowSTR { get => _growSTR; set => SetField(ref _growSTR, value); }
        public uint GrowSKL { get => _growSKL; set => SetField(ref _growSKL, value); }
        public uint GrowSPD { get => _growSPD; set => SetField(ref _growSPD, value); }
        public uint GrowDEF { get => _growDEF; set => SetField(ref _growDEF, value); }
        public uint GrowRES { get => _growRES; set => SetField(ref _growRES, value); }
        public uint GrowLCK { get => _growLCK; set => SetField(ref _growLCK, value); }

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
            if (maxCount == 0) maxCount = 253;

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

            uint dataSize = rom.RomInfo.unit_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            NameId = rom.u16(addr + 0);
            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // FE7 unit struct offsets
            Level = rom.u8(addr + 11);
            HP = rom.u8(addr + 12);
            Str = rom.u8(addr + 13);
            Skl = rom.u8(addr + 14);
            Spd = rom.u8(addr + 15);
            Def = rom.u8(addr + 16);
            Res = rom.u8(addr + 17);
            Lck = rom.u8(addr + 18);

            // Growth rates
            GrowHP = rom.u8(addr + 28);
            GrowSTR = rom.u8(addr + 29);
            GrowSKL = rom.u8(addr + 30);
            GrowSPD = rom.u8(addr + 31);
            GrowDEF = rom.u8(addr + 32);
            GrowRES = rom.u8(addr + 33);
            GrowLCK = rom.u8(addr + 34);

            IsLoaded = true;
        }

        public int GetListCount() => LoadUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["NameId"] = $"0x{NameId:X04}",
                ["Level"] = $"0x{Level:X02}",
                ["HP"] = $"0x{HP:X02}",
                ["Str"] = $"0x{Str:X02}",
                ["Skl"] = $"0x{Skl:X02}",
                ["Spd"] = $"0x{Spd:X02}",
                ["Def"] = $"0x{Def:X02}",
                ["Res"] = $"0x{Res:X02}",
                ["Lck"] = $"0x{Lck:X02}",
                ["GrowHP"] = $"0x{GrowHP:X02}",
                ["GrowSTR"] = $"0x{GrowSTR:X02}",
                ["GrowSKL"] = $"0x{GrowSKL:X02}",
                ["GrowSPD"] = $"0x{GrowSPD:X02}",
                ["GrowDEF"] = $"0x{GrowDEF:X02}",
                ["GrowRES"] = $"0x{GrowRES:X02}",
                ["GrowLCK"] = $"0x{GrowLCK:X02}",
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
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u8@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21"] = $"0x{rom.u8(a + 33):X02}",
                ["u8@0x22"] = $"0x{rom.u8(a + 34):X02}",
            };
        }
    }
}
