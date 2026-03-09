using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillConfigFE8NSkillViewViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _icon, _description;
        uint _conditionUnit1, _conditionUnit2, _conditionUnit3, _conditionUnit4;
        uint _conditionClass1, _conditionClass2, _conditionClass3, _conditionClass4;
        uint _conditionItem1, _conditionItem2, _conditionItem3, _conditionItem4;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Icon { get => _icon; set => SetField(ref _icon, value); }
        public uint Description { get => _description; set => SetField(ref _description, value); }
        public uint ConditionUnit1 { get => _conditionUnit1; set => SetField(ref _conditionUnit1, value); }
        public uint ConditionUnit2 { get => _conditionUnit2; set => SetField(ref _conditionUnit2, value); }
        public uint ConditionUnit3 { get => _conditionUnit3; set => SetField(ref _conditionUnit3, value); }
        public uint ConditionUnit4 { get => _conditionUnit4; set => SetField(ref _conditionUnit4, value); }
        public uint ConditionClass1 { get => _conditionClass1; set => SetField(ref _conditionClass1, value); }
        public uint ConditionClass2 { get => _conditionClass2; set => SetField(ref _conditionClass2, value); }
        public uint ConditionClass3 { get => _conditionClass3; set => SetField(ref _conditionClass3, value); }
        public uint ConditionClass4 { get => _conditionClass4; set => SetField(ref _conditionClass4, value); }
        public uint ConditionItem1 { get => _conditionItem1; set => SetField(ref _conditionItem1, value); }
        public uint ConditionItem2 { get => _conditionItem2; set => SetField(ref _conditionItem2, value); }
        public uint ConditionItem3 { get => _conditionItem3; set => SetField(ref _conditionItem3, value); }
        public uint ConditionItem4 { get => _conditionItem4; set => SetField(ref _conditionItem4, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            Icon = rom.u16(addr + 0);
            Description = rom.u16(addr + 2);
            ConditionUnit1 = rom.u8(addr + 4);
            ConditionUnit2 = rom.u8(addr + 5);
            ConditionUnit3 = rom.u8(addr + 6);
            ConditionUnit4 = rom.u8(addr + 7);
            ConditionClass1 = rom.u8(addr + 8);
            ConditionClass2 = rom.u8(addr + 9);
            ConditionClass3 = rom.u8(addr + 10);
            ConditionClass4 = rom.u8(addr + 11);
            ConditionItem1 = rom.u8(addr + 12);
            ConditionItem2 = rom.u8(addr + 13);
            ConditionItem3 = rom.u8(addr + 14);
            ConditionItem4 = rom.u8(addr + 15);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, Icon);
            rom.write_u16(addr + 2, Description);
            rom.write_u8(addr + 4, ConditionUnit1);
            rom.write_u8(addr + 5, ConditionUnit2);
            rom.write_u8(addr + 6, ConditionUnit3);
            rom.write_u8(addr + 7, ConditionUnit4);
            rom.write_u8(addr + 8, ConditionClass1);
            rom.write_u8(addr + 9, ConditionClass2);
            rom.write_u8(addr + 10, ConditionClass3);
            rom.write_u8(addr + 11, ConditionClass4);
            rom.write_u8(addr + 12, ConditionItem1);
            rom.write_u8(addr + 13, ConditionItem2);
            rom.write_u8(addr + 14, ConditionItem3);
            rom.write_u8(addr + 15, ConditionItem4);
        }

        public void Initialize() { IsLoaded = true; }
        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Icon"] = $"0x{Icon:X04}",
                ["Description"] = $"0x{Description:X04}",
                ["ConditionUnit1"] = $"0x{ConditionUnit1:X02}",
                ["ConditionUnit2"] = $"0x{ConditionUnit2:X02}",
                ["ConditionUnit3"] = $"0x{ConditionUnit3:X02}",
                ["ConditionUnit4"] = $"0x{ConditionUnit4:X02}",
                ["ConditionClass1"] = $"0x{ConditionClass1:X02}",
                ["ConditionClass2"] = $"0x{ConditionClass2:X02}",
                ["ConditionClass3"] = $"0x{ConditionClass3:X02}",
                ["ConditionClass4"] = $"0x{ConditionClass4:X02}",
                ["ConditionItem1"] = $"0x{ConditionItem1:X02}",
                ["ConditionItem2"] = $"0x{ConditionItem2:X02}",
                ["ConditionItem3"] = $"0x{ConditionItem3:X02}",
                ["ConditionItem4"] = $"0x{ConditionItem4:X02}",
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
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
