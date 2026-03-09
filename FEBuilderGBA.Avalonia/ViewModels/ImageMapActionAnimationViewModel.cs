using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageMapActionAnimationViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 8;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _animationPointer, _padding1, _padding2;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // D0: Animation data pointer
        public uint AnimationPointer { get => _animationPointer; set => SetField(ref _animationPointer, value); }
        // W4: Padding / reserved
        public uint Padding1 { get => _padding1; set => SetField(ref _padding1, value); }
        // W6: Padding / reserved
        public uint Padding2 { get => _padding2; set => SetField(ref _padding2, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Map Action Animation", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            AnimationPointer = rom.u32(addr + 0);
            Padding1 = rom.u16(addr + 4);
            Padding2 = rom.u16(addr + 6);

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, AnimationPointer);
            rom.write_u16(addr + 4, Padding1);
            rom.write_u16(addr + 6, Padding2);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AnimationPointer"] = $"0x{AnimationPointer:X08}",
                ["Padding1"] = $"0x{Padding1:X04}",
                ["Padding2"] = $"0x{Padding2:X04}",
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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@4"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@6"] = $"0x{rom.u16(a + 6):X04}",
            };
        }
    }
}
