using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillAssignmentUnitFE8NViewViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _b39, _b40, _b41;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint B39 { get => _b39; set => SetField(ref _b39, value); }
        public uint B40 { get => _b40; set => SetField(ref _b40, value); }
        public uint B41 { get => _b41; set => SetField(ref _b41, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 42 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            B39 = rom.u8(addr + 39);
            B40 = rom.u8(addr + 40);
            B41 = rom.u8(addr + 41);
            IsLoaded = true;
        }

        public void Initialize() { IsLoaded = true; }
        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string> { ["status"] = "skill_patch_required", ["B39"] = $"0x{B39:X02}", ["B40"] = $"0x{B40:X02}", ["B41"] = $"0x{B41:X02}" };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
