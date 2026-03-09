using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Support Unit Editor for FE7/FE8.
    /// Block size = 24 bytes.  Layout (all u8):
    ///   B0-B6   : Partner unit IDs (7 slots)
    ///   B7-B13  : Initial support values
    ///   B14-B20 : Support growth rates
    ///   B21     : Support partner count
    ///   B22-B23 : Separator / padding
    /// </summary>
    public class SupportUnitEditorViewModel : ViewModelBase, IDataVerifiable
    {
        const uint BLOCK_SIZE = 24;

        uint _currentAddr;
        bool _canWrite;

        // Partner unit IDs (7 slots)
        uint _partner1, _partner2, _partner3, _partner4, _partner5, _partner6, _partner7;
        // Initial support values
        uint _initialValue1, _initialValue2, _initialValue3, _initialValue4;
        uint _initialValue5, _initialValue6, _initialValue7;
        // Support growth rates
        uint _growthRate1, _growthRate2, _growthRate3, _growthRate4;
        uint _growthRate5, _growthRate6, _growthRate7;
        // Partner count + separator
        uint _partnerCount, _separator1, _separator2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // Partner unit IDs
        public uint Partner1 { get => _partner1; set => SetField(ref _partner1, value); }
        public uint Partner2 { get => _partner2; set => SetField(ref _partner2, value); }
        public uint Partner3 { get => _partner3; set => SetField(ref _partner3, value); }
        public uint Partner4 { get => _partner4; set => SetField(ref _partner4, value); }
        public uint Partner5 { get => _partner5; set => SetField(ref _partner5, value); }
        public uint Partner6 { get => _partner6; set => SetField(ref _partner6, value); }
        public uint Partner7 { get => _partner7; set => SetField(ref _partner7, value); }

        // Initial support values
        public uint InitialValue1 { get => _initialValue1; set => SetField(ref _initialValue1, value); }
        public uint InitialValue2 { get => _initialValue2; set => SetField(ref _initialValue2, value); }
        public uint InitialValue3 { get => _initialValue3; set => SetField(ref _initialValue3, value); }
        public uint InitialValue4 { get => _initialValue4; set => SetField(ref _initialValue4, value); }
        public uint InitialValue5 { get => _initialValue5; set => SetField(ref _initialValue5, value); }
        public uint InitialValue6 { get => _initialValue6; set => SetField(ref _initialValue6, value); }
        public uint InitialValue7 { get => _initialValue7; set => SetField(ref _initialValue7, value); }

        // Growth rates
        public uint GrowthRate1 { get => _growthRate1; set => SetField(ref _growthRate1, value); }
        public uint GrowthRate2 { get => _growthRate2; set => SetField(ref _growthRate2, value); }
        public uint GrowthRate3 { get => _growthRate3; set => SetField(ref _growthRate3, value); }
        public uint GrowthRate4 { get => _growthRate4; set => SetField(ref _growthRate4, value); }
        public uint GrowthRate5 { get => _growthRate5; set => SetField(ref _growthRate5, value); }
        public uint GrowthRate6 { get => _growthRate6; set => SetField(ref _growthRate6, value); }
        public uint GrowthRate7 { get => _growthRate7; set => SetField(ref _growthRate7, value); }

        // Partner count + separator
        public uint PartnerCount { get => _partnerCount; set => SetField(ref _partnerCount, value); }
        public uint Separator1 { get => _separator1; set => SetField(ref _separator1, value); }
        public uint Separator2 { get => _separator2; set => SetField(ref _separator2, value); }

        public List<AddrResult> LoadSupportUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr;
            if (ptr >= 0x08000000)
                baseAddr = ptr - 0x08000000;
            else
            {
                baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();
            }

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * BLOCK_SIZE);
                if (addr + BLOCK_SIZE > (uint)rom.Data.Length) break;

                uint firstWord = rom.u16(addr);
                if (firstWord == 0 && i > 0)
                {
                    bool hasMore = false;
                    for (uint j = 1; j <= 4 && (i + j) < 0x100; j++)
                    {
                        uint checkAddr = (uint)(baseAddr + (i + j) * BLOCK_SIZE);
                        if (checkAddr + BLOCK_SIZE > (uint)rom.Data.Length) break;
                        if (rom.u16(checkAddr) != 0) { hasMore = true; break; }
                    }
                    if (!hasMore) break;
                }

                string name = U.ToHexString(i) + " Entry " + U.ToHexString(i);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BLOCK_SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            Partner1 = rom.u8(addr + 0);
            Partner2 = rom.u8(addr + 1);
            Partner3 = rom.u8(addr + 2);
            Partner4 = rom.u8(addr + 3);
            Partner5 = rom.u8(addr + 4);
            Partner6 = rom.u8(addr + 5);
            Partner7 = rom.u8(addr + 6);

            InitialValue1 = rom.u8(addr + 7);
            InitialValue2 = rom.u8(addr + 8);
            InitialValue3 = rom.u8(addr + 9);
            InitialValue4 = rom.u8(addr + 10);
            InitialValue5 = rom.u8(addr + 11);
            InitialValue6 = rom.u8(addr + 12);
            InitialValue7 = rom.u8(addr + 13);

            GrowthRate1 = rom.u8(addr + 14);
            GrowthRate2 = rom.u8(addr + 15);
            GrowthRate3 = rom.u8(addr + 16);
            GrowthRate4 = rom.u8(addr + 17);
            GrowthRate5 = rom.u8(addr + 18);
            GrowthRate6 = rom.u8(addr + 19);
            GrowthRate7 = rom.u8(addr + 20);

            PartnerCount = rom.u8(addr + 21);
            Separator1 = rom.u8(addr + 22);
            Separator2 = rom.u8(addr + 23);

            CanWrite = true;
        }

        public void WriteSupportUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + BLOCK_SIZE > (uint)rom.Data.Length) return;

            rom.write_u8(a + 0,  Partner1);
            rom.write_u8(a + 1,  Partner2);
            rom.write_u8(a + 2,  Partner3);
            rom.write_u8(a + 3,  Partner4);
            rom.write_u8(a + 4,  Partner5);
            rom.write_u8(a + 5,  Partner6);
            rom.write_u8(a + 6,  Partner7);

            rom.write_u8(a + 7,  InitialValue1);
            rom.write_u8(a + 8,  InitialValue2);
            rom.write_u8(a + 9,  InitialValue3);
            rom.write_u8(a + 10, InitialValue4);
            rom.write_u8(a + 11, InitialValue5);
            rom.write_u8(a + 12, InitialValue6);
            rom.write_u8(a + 13, InitialValue7);

            rom.write_u8(a + 14, GrowthRate1);
            rom.write_u8(a + 15, GrowthRate2);
            rom.write_u8(a + 16, GrowthRate3);
            rom.write_u8(a + 17, GrowthRate4);
            rom.write_u8(a + 18, GrowthRate5);
            rom.write_u8(a + 19, GrowthRate6);
            rom.write_u8(a + 20, GrowthRate7);

            rom.write_u8(a + 21, PartnerCount);
            rom.write_u8(a + 22, Separator1);
            rom.write_u8(a + 23, Separator2);
        }

        public int GetListCount() => LoadSupportUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Partner1"] = $"0x{Partner1:X02}",
                ["Partner2"] = $"0x{Partner2:X02}",
                ["Partner3"] = $"0x{Partner3:X02}",
                ["Partner4"] = $"0x{Partner4:X02}",
                ["Partner5"] = $"0x{Partner5:X02}",
                ["Partner6"] = $"0x{Partner6:X02}",
                ["Partner7"] = $"0x{Partner7:X02}",
                ["InitialValue1"] = $"0x{InitialValue1:X02}",
                ["InitialValue2"] = $"0x{InitialValue2:X02}",
                ["InitialValue3"] = $"0x{InitialValue3:X02}",
                ["InitialValue4"] = $"0x{InitialValue4:X02}",
                ["InitialValue5"] = $"0x{InitialValue5:X02}",
                ["InitialValue6"] = $"0x{InitialValue6:X02}",
                ["InitialValue7"] = $"0x{InitialValue7:X02}",
                ["GrowthRate1"] = $"0x{GrowthRate1:X02}",
                ["GrowthRate2"] = $"0x{GrowthRate2:X02}",
                ["GrowthRate3"] = $"0x{GrowthRate3:X02}",
                ["GrowthRate4"] = $"0x{GrowthRate4:X02}",
                ["GrowthRate5"] = $"0x{GrowthRate5:X02}",
                ["GrowthRate6"] = $"0x{GrowthRate6:X02}",
                ["GrowthRate7"] = $"0x{GrowthRate7:X02}",
                ["PartnerCount"] = $"0x{PartnerCount:X02}",
                ["Separator1"] = $"0x{Separator1:X02}",
                ["Separator2"] = $"0x{Separator2:X02}",
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
                ["Partner1@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["Partner2@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["Partner3@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["Partner4@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["Partner5@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["Partner6@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["Partner7@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["InitialValue1@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["InitialValue2@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["InitialValue3@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["InitialValue4@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["InitialValue5@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["InitialValue6@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["InitialValue7@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["GrowthRate1@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["GrowthRate2@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["GrowthRate3@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["GrowthRate4@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["GrowthRate5@0x12"] = $"0x{rom.u8(a + 18):X02}",
                ["GrowthRate6@0x13"] = $"0x{rom.u8(a + 19):X02}",
                ["GrowthRate7@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["PartnerCount@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["Separator1@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["Separator2@0x17"] = $"0x{rom.u8(a + 23):X02}",
            };
        }
    }
}
