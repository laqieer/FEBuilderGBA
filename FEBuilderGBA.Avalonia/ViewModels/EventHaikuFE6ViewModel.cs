using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventHaikuFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unit;
        uint _chapterID;
        uint _unknown02;
        uint _unknown03;
        uint _deathText;
        uint _unknown06;
        uint _unknown07;
        uint _achievementFlag;
        uint _unknown0A;
        uint _unknown0B;
        uint _finalChapterText;
        uint _unknown0E;
        uint _unknown0F;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Unit { get => _unit; set => SetField(ref _unit, value); }
        public uint ChapterID { get => _chapterID; set => SetField(ref _chapterID, value); }
        public uint Unknown02 { get => _unknown02; set => SetField(ref _unknown02, value); }
        public uint Unknown03 { get => _unknown03; set => SetField(ref _unknown03, value); }
        public uint DeathText { get => _deathText; set => SetField(ref _deathText, value); }
        public uint Unknown06 { get => _unknown06; set => SetField(ref _unknown06, value); }
        public uint Unknown07 { get => _unknown07; set => SetField(ref _unknown07, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Unknown0A { get => _unknown0A; set => SetField(ref _unknown0A, value); }
        public uint Unknown0B { get => _unknown0B; set => SetField(ref _unknown0B, value); }
        public uint FinalChapterText { get => _finalChapterText; set => SetField(ref _finalChapterText, value); }
        public uint Unknown0E { get => _unknown0E; set => SetField(ref _unknown0E, value); }
        public uint Unknown0F { get => _unknown0F; set => SetField(ref _unknown0F, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_haiku_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 16;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u8(addr) == 0x00) break;

                uint unitId = rom.u8(addr);
                string unitName = NameResolver.GetUnitName(unitId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {unitName}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            Unit = rom.u8(addr + 0);
            ChapterID = rom.u8(addr + 1);
            Unknown02 = rom.u8(addr + 2);
            Unknown03 = rom.u8(addr + 3);
            DeathText = rom.u16(addr + 4);
            Unknown06 = rom.u8(addr + 6);
            Unknown07 = rom.u8(addr + 7);
            AchievementFlag = rom.u16(addr + 8);
            Unknown0A = rom.u8(addr + 10);
            Unknown0B = rom.u8(addr + 11);
            FinalChapterText = rom.u16(addr + 12);
            Unknown0E = rom.u8(addr + 14);
            Unknown0F = rom.u8(addr + 15);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            rom.write_u8(a + 0, (byte)Unit);
            rom.write_u8(a + 1, (byte)ChapterID);
            rom.write_u8(a + 2, (byte)Unknown02);
            rom.write_u8(a + 3, (byte)Unknown03);
            rom.write_u16(a + 4, (ushort)DeathText);
            rom.write_u8(a + 6, (byte)Unknown06);
            rom.write_u8(a + 7, (byte)Unknown07);
            rom.write_u16(a + 8, (ushort)AchievementFlag);
            rom.write_u8(a + 10, (byte)Unknown0A);
            rom.write_u8(a + 11, (byte)Unknown0B);
            rom.write_u16(a + 12, (ushort)FinalChapterText);
            rom.write_u8(a + 14, (byte)Unknown0E);
            rom.write_u8(a + 15, (byte)Unknown0F);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Unit"] = $"0x{Unit:X02}",
                ["ChapterID"] = $"0x{ChapterID:X02}",
                ["Unknown02"] = $"0x{Unknown02:X02}",
                ["Unknown03"] = $"0x{Unknown03:X02}",
                ["DeathText"] = $"0x{DeathText:X04}",
                ["Unknown06"] = $"0x{Unknown06:X02}",
                ["Unknown07"] = $"0x{Unknown07:X02}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Unknown0A"] = $"0x{Unknown0A:X02}",
                ["Unknown0B"] = $"0x{Unknown0B:X02}",
                ["FinalChapterText"] = $"0x{FinalChapterText:X04}",
                ["Unknown0E"] = $"0x{Unknown0E:X02}",
                ["Unknown0F"] = $"0x{Unknown0F:X02}",
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
                ["u8@0x00_Unit"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_ChapterID"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unknown02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unknown03"] = $"0x{rom.u8(a + 3):X02}",
                ["u16@0x04_DeathText"] = $"0x{rom.u16(a + 4):X04}",
                ["u8@0x06_Unknown06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07_Unknown07"] = $"0x{rom.u8(a + 7):X02}",
                ["u16@0x08_AchievementFlag"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A_Unknown0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_Unknown0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u16@0x0C_FinalChapterText"] = $"0x{rom.u16(a + 12):X04}",
                ["u8@0x0E_Unknown0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_Unknown0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
