using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillSystemsEffectivenessReworkClassTypeViewViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _w50;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint W50 { get => _w50; set => SetField(ref _w50, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 52 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            W50 = rom.u16(addr + 50);
            IsLoaded = true;
        }

        public void Initialize() { IsLoaded = true; }
        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string> { ["status"] = "skill_patch_required", ["W50"] = $"0x{W50:X04}" };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
