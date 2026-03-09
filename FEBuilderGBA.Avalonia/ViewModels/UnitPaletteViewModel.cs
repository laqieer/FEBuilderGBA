using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitPaletteViewModel : ViewModelBase, IDataVerifiable
    {
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
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Unit Palette Assignment", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 7 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            TraineeClass = rom.u8(addr + 0);
            BaseClass1 = rom.u8(addr + 1);
            BaseClass2 = rom.u8(addr + 2);
            AdvancedClass1 = rom.u8(addr + 3);
            AdvancedClass2 = rom.u8(addr + 4);
            AdvancedClass3 = rom.u8(addr + 5);
            AdvancedClass4 = rom.u8(addr + 6);
            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 7 > (uint)rom.Data.Length) return;

            rom.write_u8(addr + 0, (byte)TraineeClass);
            rom.write_u8(addr + 1, (byte)BaseClass1);
            rom.write_u8(addr + 2, (byte)BaseClass2);
            rom.write_u8(addr + 3, (byte)AdvancedClass1);
            rom.write_u8(addr + 4, (byte)AdvancedClass2);
            rom.write_u8(addr + 5, (byte)AdvancedClass3);
            rom.write_u8(addr + 6, (byte)AdvancedClass4);
        }

        public int GetListCount() => 0;

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
