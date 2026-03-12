using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitPaletteViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4", "B5", "B6" });

        uint _currentAddr;
        bool _isLoaded;
        uint _traineeClass;
        uint _baseClass1;
        uint _baseClass2;
        uint _advancedClass1;
        uint _advancedClass2;
        uint _advancedClass3;
        uint _advancedClass4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint TraineeClass { get => _traineeClass; set => SetField(ref _traineeClass, value); }
        public uint BaseClass1 { get => _baseClass1; set => SetField(ref _baseClass1, value); }
        public uint BaseClass2 { get => _baseClass2; set => SetField(ref _baseClass2, value); }
        public uint AdvancedClass1 { get => _advancedClass1; set => SetField(ref _advancedClass1, value); }
        public uint AdvancedClass2 { get => _advancedClass2; set => SetField(ref _advancedClass2, value); }
        public uint AdvancedClass3 { get => _advancedClass3; set => SetField(ref _advancedClass3, value); }
        public uint AdvancedClass4 { get => _advancedClass4; set => SetField(ref _advancedClass4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.unit_palette_color_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 7;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + (i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;

                string unitName = NameResolver.GetUnitName(i + 1);
                result.Add(new AddrResult(addr, $"0x{(i + 1):X2} {unitName}", i + 1));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 7 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            TraineeClass = values["B0"];
            BaseClass1 = values["B1"];
            BaseClass2 = values["B2"];
            AdvancedClass1 = values["B3"];
            AdvancedClass2 = values["B4"];
            AdvancedClass3 = values["B5"];
            AdvancedClass4 = values["B6"];
            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 7 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = TraineeClass,
                ["B1"] = BaseClass1,
                ["B2"] = BaseClass2,
                ["B3"] = AdvancedClass1,
                ["B4"] = AdvancedClass2,
                ["B5"] = AdvancedClass3,
                ["B6"] = AdvancedClass4,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0_TraineeClass"] = $"0x{TraineeClass:X02}",
                ["B1_BaseClass1"] = $"0x{BaseClass1:X02}",
                ["B2_BaseClass2"] = $"0x{BaseClass2:X02}",
                ["B3_AdvancedClass1"] = $"0x{AdvancedClass1:X02}",
                ["B4_AdvancedClass2"] = $"0x{AdvancedClass2:X02}",
                ["B5_AdvancedClass3"] = $"0x{AdvancedClass3:X02}",
                ["B6_AdvancedClass4"] = $"0x{AdvancedClass4:X02}",
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
                ["u8@0x00_TraineeClass"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_BaseClass1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_BaseClass2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_AdvancedClass1"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04_AdvancedClass2"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05_AdvancedClass3"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06_AdvancedClass4"] = $"0x{rom.u8(a + 6):X02}",
            };
        }
    }
}
