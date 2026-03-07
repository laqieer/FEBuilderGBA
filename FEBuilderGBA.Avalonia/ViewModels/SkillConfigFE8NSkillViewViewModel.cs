using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillConfigFE8NSkillViewViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _w0, _w2;
        uint _b4, _b5, _b6, _b7, _b8, _b9, _b10, _b11, _b12, _b13, _b14, _b15;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint W0 { get => _w0; set => SetField(ref _w0, value); }
        public uint W2 { get => _w2; set => SetField(ref _w2, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            W0 = rom.u16(addr + 0);
            W2 = rom.u16(addr + 2);
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            B6 = rom.u8(addr + 6);
            B7 = rom.u8(addr + 7);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);
            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            IsLoaded = true;
        }

        public void Initialize() { IsLoaded = true; }
        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string> { ["status"] = "skill_patch_required" };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
