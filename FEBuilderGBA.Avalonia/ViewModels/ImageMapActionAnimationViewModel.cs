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

        /// <summary>
        /// Find the map action animation pointer table by binary signature search,
        /// matching the WinForms FindAnimationPointer() approach.
        /// </summary>
        static uint FindAnimationPointer(ROM rom)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            // Only FE8 has this editor
            if (rom.RomInfo.version != 8) return U.NOT_FOUND;

            byte[] bin;
            if (rom.RomInfo.is_multibyte)
            {   // FE8J
                bin = new byte[] { 0x54, 0x3C, 0x08, 0x08, 0xEC, 0xE1, 0x03, 0x02,
                                   0xE8, 0xA4, 0x03, 0x02, 0x68, 0xA5, 0x03, 0x02,
                                   0xFF, 0xFF, 0x00, 0x00 };
            }
            else
            {   // FE8U
                bin = new byte[] { 0x14, 0x19, 0x08, 0x08, 0xF0, 0xE1, 0x03, 0x02,
                                   0xEC, 0xA4, 0x03, 0x02, 0x6C, 0xA5, 0x03, 0x02,
                                   0xFF, 0xFF, 0x00, 0x00 };
            }

            // Match WinForms: start search from compress_image_borderline_address
            // to avoid false positives and reduce scan cost
            uint startAddr = rom.RomInfo.compress_image_borderline_address;
            uint p = U.GrepEnd(rom.Data, bin, startAddr, 0, 4, 0, true);
            if (p == U.NOT_FOUND) return U.NOT_FOUND;

            p = p - (uint)bin.Length - 4;
            uint a = rom.u32(p);
            if (!U.isPointer(a)) return U.NOT_FOUND;
            return p;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint animeP = FindAnimationPointer(rom);
            if (animeP == U.NOT_FOUND) return new List<AddrResult>();

            uint baseAddr = rom.p32(animeP);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; ; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint a = rom.u32(addr);
                if (!U.isSafetyPointerOrNull(a)) break;

                string name = $"0x{i:X02} Map Action Animation {i}";
                result.Add(new AddrResult(addr, name, (uint)i));
            }
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

        public int GetListCount()
        {
            return LoadList().Count;
        }

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

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["AnimationPointer"] = "u32@0",
            ["Padding1"] = "u16@4",
            ["Padding2"] = "u16@6",
        };
    }
}
