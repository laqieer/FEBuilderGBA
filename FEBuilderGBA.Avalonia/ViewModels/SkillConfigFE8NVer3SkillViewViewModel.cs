using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillConfigFE8NVer3SkillViewViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _w0, _w2;
        uint _p4, _p8, _p12, _p16, _p20;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint W0 { get => _w0; set => SetField(ref _w0, value); }
        public uint W2 { get => _w2; set => SetField(ref _w2, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }
        public uint P20 { get => _p20; set => SetField(ref _p20, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 24 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            W0 = rom.u16(addr + 0);
            W2 = rom.u16(addr + 2);
            P4 = rom.u32(addr + 4);
            P8 = rom.u32(addr + 8);
            P12 = rom.u32(addr + 12);
            P16 = rom.u32(addr + 16);
            P20 = rom.u32(addr + 20);
            IsLoaded = true;
        }

        public void Initialize() { IsLoaded = true; }
        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string> { ["status"] = "skill_patch_required" };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
