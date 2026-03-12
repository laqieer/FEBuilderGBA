using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventHaikuViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "W4", "W6", "D8" });

        uint _currentAddr;
        bool _isLoaded;
        uint _unit;
        uint _killerUnit;
        uint _route;
        uint _chapterID;
        uint _achievementFlag;
        uint _text;
        uint _eventPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Unit { get => _unit; set => SetField(ref _unit, value); }
        public uint KillerUnit { get => _killerUnit; set => SetField(ref _killerUnit, value); }
        public uint Route { get => _route; set => SetField(ref _route, value); }
        public uint ChapterID { get => _chapterID; set => SetField(ref _chapterID, value); }
        public uint AchievementFlag { get => _achievementFlag; set => SetField(ref _achievementFlag, value); }
        public uint Text { get => _text; set => SetField(ref _text, value); }
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_haiku_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 12;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;

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
            if (addr + 12 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            Unit = v["B0"];
            KillerUnit = v["B1"];
            Route = v["B2"];
            ChapterID = v["B3"];
            AchievementFlag = v["W4"];
            Text = v["W6"];
            EventPointer = v["D8"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = Unit, ["B1"] = KillerUnit,
                ["B2"] = Route, ["B3"] = ChapterID,
                ["W4"] = AchievementFlag, ["W6"] = Text,
                ["D8"] = EventPointer,
            };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Unit"] = $"0x{Unit:X02}",
                ["KillerUnit"] = $"0x{KillerUnit:X02}",
                ["Route"] = $"0x{Route:X02}",
                ["ChapterID"] = $"0x{ChapterID:X02}",
                ["AchievementFlag"] = $"0x{AchievementFlag:X04}",
                ["Text"] = $"0x{Text:X04}",
                ["EventPointer"] = $"0x{EventPointer:X08}",
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
                ["u8@0x01_KillerUnit"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Route"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_ChapterID"] = $"0x{rom.u8(a + 3):X02}",
                ["u16@0x04_AchievementFlag"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06_Text"] = $"0x{rom.u16(a + 6):X04}",
                ["u32@0x08_EventPointer"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
