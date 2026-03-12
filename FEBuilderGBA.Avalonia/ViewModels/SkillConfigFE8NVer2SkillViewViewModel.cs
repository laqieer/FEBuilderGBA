using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillConfigFE8NVer2SkillViewViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "W0", "W2", "D4", "D8", "D12", "D16" });

        uint _currentAddr;
        bool _isLoaded;
        uint _textDetail, _palette;
        uint _unitSkillPointer, _classSkillPointer, _weaponItemSkillPointer, _heldItemSkillPointer;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint TextDetail { get => _textDetail; set => SetField(ref _textDetail, value); }
        public uint Palette { get => _palette; set => SetField(ref _palette, value); }
        public uint UnitSkillPointer { get => _unitSkillPointer; set => SetField(ref _unitSkillPointer, value); }
        public uint ClassSkillPointer { get => _classSkillPointer; set => SetField(ref _classSkillPointer, value); }
        public uint WeaponItemSkillPointer { get => _weaponItemSkillPointer; set => SetField(ref _weaponItemSkillPointer, value); }
        public uint HeldItemSkillPointer { get => _heldItemSkillPointer; set => SetField(ref _heldItemSkillPointer, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            TextDetail = values["W0"];
            Palette = values["W2"];
            UnitSkillPointer = values["D4"];
            ClassSkillPointer = values["D8"];
            WeaponItemSkillPointer = values["D12"];
            HeldItemSkillPointer = values["D16"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["W0"] = TextDetail,
                ["W2"] = Palette,
                ["D4"] = UnitSkillPointer,
                ["D8"] = ClassSkillPointer,
                ["D12"] = WeaponItemSkillPointer,
                ["D16"] = HeldItemSkillPointer,
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
                ["TextDetail"] = $"0x{TextDetail:X04}",
                ["Palette"] = $"0x{Palette:X04}",
                ["UnitSkillPointer"] = $"0x{UnitSkillPointer:X08}",
                ["ClassSkillPointer"] = $"0x{ClassSkillPointer:X08}",
                ["WeaponItemSkillPointer"] = $"0x{WeaponItemSkillPointer:X08}",
                ["HeldItemSkillPointer"] = $"0x{HeldItemSkillPointer:X08}",
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
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
            };
        }
    }
}
