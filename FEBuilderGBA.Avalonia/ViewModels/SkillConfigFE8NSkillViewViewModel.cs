using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillConfigFE8NSkillViewViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "W0", "W2", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11", "B12", "B13", "B14", "B15" });

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
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            Icon = values["W0"];
            Description = values["W2"];
            ConditionUnit1 = values["B4"];
            ConditionUnit2 = values["B5"];
            ConditionUnit3 = values["B6"];
            ConditionUnit4 = values["B7"];
            ConditionClass1 = values["B8"];
            ConditionClass2 = values["B9"];
            ConditionClass3 = values["B10"];
            ConditionClass4 = values["B11"];
            ConditionItem1 = values["B12"];
            ConditionItem2 = values["B13"];
            ConditionItem3 = values["B14"];
            ConditionItem4 = values["B15"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["W0"] = Icon,
                ["W2"] = Description,
                ["B4"] = ConditionUnit1,
                ["B5"] = ConditionUnit2,
                ["B6"] = ConditionUnit3,
                ["B7"] = ConditionUnit4,
                ["B8"] = ConditionClass1,
                ["B9"] = ConditionClass2,
                ["B10"] = ConditionClass3,
                ["B11"] = ConditionClass4,
                ["B12"] = ConditionItem1,
                ["B13"] = ConditionItem2,
                ["B14"] = ConditionItem3,
                ["B15"] = ConditionItem4,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
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
