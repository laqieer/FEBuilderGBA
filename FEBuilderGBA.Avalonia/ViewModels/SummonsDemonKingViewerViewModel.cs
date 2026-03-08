using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SummonsDemonKingViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unitId;
        uint _classId;
        uint _unknown1;
        uint _b3;
        uint _w4;
        uint _b6, _b7;
        uint _p8;
        uint _b12, _b13, _b14, _b15, _b16, _b17, _b18, _b19;
        uint _unitGrow;
        uint _level;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }
        public uint UnitGrow { get => _unitGrow; set => SetField(ref _unitGrow, value); }
        public uint Level { get => _level; set => SetField(ref _level, value); }

        public List<AddrResult> LoadSummonsDemonKingList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.summons_demon_king_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint maxCount = 0;
            uint countAddr = rom.RomInfo.summons_demon_king_count_address;
            if (countAddr != 0 && U.isSafetyOffset(countAddr))
            {
                maxCount = rom.u8(countAddr);
            }
            if (maxCount == 0 || maxCount >= 100) maxCount = 20;

            var result = new List<AddrResult>();
            for (uint i = 0; i <= maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;

                uint unitId = rom.u8(addr);
                string name;
                if (unitId == 0)
                {
                    name = U.ToHexString(i) + " -EMPTY-";
                }
                else
                {
                    uint classId = rom.u8(addr + 1);
                    name = U.ToHexString(i) + " Demon King Summon Unit:0x" + unitId.ToString("X02") + " Class:0x" + classId.ToString("X02");
                }
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSummonsDemonKing(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UnitId = rom.u8(addr + 0);
            ClassId = rom.u8(addr + 1);
            Unknown1 = rom.u8(addr + 2);
            B3 = rom.u8(addr + 3);
            W4 = rom.u16(addr + 4);
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            P8 = rom.u32(addr + 8);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            B18 = rom.u8(addr + 18);
            B19 = rom.u8(addr + 19);
            UnitGrow = rom.u16(addr + 3);
            Level = U.ParseUnitGrowLV(UnitGrow);
            IsLoaded = true;
        }

        public int GetListCount() => LoadSummonsDemonKingList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["W4"] = $"0x{W4:X04}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["P8"] = $"0x{P8:X08}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
                ["B16"] = $"0x{B16:X02}",
                ["B17"] = $"0x{B17:X02}",
                ["B18"] = $"0x{B18:X02}",
                ["B19"] = $"0x{B19:X02}",
                ["UnitGrow"] = $"0x{UnitGrow:X04}",
                ["Level"] = $"0x{Level:X08}",
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
                ["u16@0x03"] = $"0x{rom.u16(a + 3):X04}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
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
    }
}
