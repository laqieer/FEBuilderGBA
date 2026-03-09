using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillConfigFE8NVer3SkillViewViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _textDetail, _palette;
        uint _unitClassPointer, _classSkillPointer, _weaponItemSkillPointer, _heldItemSkillPointer, _compositeSkillPointer;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint TextDetail { get => _textDetail; set => SetField(ref _textDetail, value); }
        public uint Palette { get => _palette; set => SetField(ref _palette, value); }
        public uint UnitClassPointer { get => _unitClassPointer; set => SetField(ref _unitClassPointer, value); }
        public uint ClassSkillPointer { get => _classSkillPointer; set => SetField(ref _classSkillPointer, value); }
        public uint WeaponItemSkillPointer { get => _weaponItemSkillPointer; set => SetField(ref _weaponItemSkillPointer, value); }
        public uint HeldItemSkillPointer { get => _heldItemSkillPointer; set => SetField(ref _heldItemSkillPointer, value); }
        public uint CompositeSkillPointer { get => _compositeSkillPointer; set => SetField(ref _compositeSkillPointer, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 24 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            TextDetail = rom.u16(addr + 0);
            Palette = rom.u16(addr + 2);
            UnitClassPointer = rom.u32(addr + 4);
            ClassSkillPointer = rom.u32(addr + 8);
            WeaponItemSkillPointer = rom.u32(addr + 12);
            HeldItemSkillPointer = rom.u32(addr + 16);
            CompositeSkillPointer = rom.u32(addr + 20);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, TextDetail);
            rom.write_u16(addr + 2, Palette);
            rom.write_u32(addr + 4, UnitClassPointer);
            rom.write_u32(addr + 8, ClassSkillPointer);
            rom.write_u32(addr + 12, WeaponItemSkillPointer);
            rom.write_u32(addr + 16, HeldItemSkillPointer);
            rom.write_u32(addr + 20, CompositeSkillPointer);
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
                ["UnitClassPointer"] = $"0x{UnitClassPointer:X08}",
                ["ClassSkillPointer"] = $"0x{ClassSkillPointer:X08}",
                ["WeaponItemSkillPointer"] = $"0x{WeaponItemSkillPointer:X08}",
                ["HeldItemSkillPointer"] = $"0x{HeldItemSkillPointer:X08}",
                ["CompositeSkillPointer"] = $"0x{CompositeSkillPointer:X08}",
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
                ["u32@0x14"] = $"0x{rom.u32(a + 20):X08}",
            };
        }
    }
}
