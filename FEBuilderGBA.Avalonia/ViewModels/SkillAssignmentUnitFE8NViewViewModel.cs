using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillAssignmentUnitFE8NViewViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B39", "B40", "B41" });

        uint _currentAddr;
        bool _isLoaded;
        uint _personalSkill, _skillSet1, _skillSet2;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint PersonalSkill { get => _personalSkill; set => SetField(ref _personalSkill, value); }
        public uint SkillSet1 { get => _skillSet1; set => SetField(ref _skillSet1, value); }
        public uint SkillSet2 { get => _skillSet2; set => SetField(ref _skillSet2, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 42 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            PersonalSkill = values["B39"];
            SkillSet1 = values["B40"];
            SkillSet2 = values["B41"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["B39"] = PersonalSkill,
                ["B40"] = SkillSet1,
                ["B41"] = SkillSet2,
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
                ["PersonalSkill"] = $"0x{PersonalSkill:X02}",
                ["SkillSet1"] = $"0x{SkillSet1:X02}",
                ["SkillSet2"] = $"0x{SkillSet2:X02}",
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
                ["u8@0x27"] = $"0x{rom.u8(a + 39):X02}",
                ["u8@0x28"] = $"0x{rom.u8(a + 40):X02}",
                ["u8@0x29"] = $"0x{rom.u8(a + 41):X02}",
            };
        }
    }
}
