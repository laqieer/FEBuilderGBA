using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageUnitWaitIconViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        ushort _w0, _w2;
        uint _p4;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public ushort W0 { get => _w0; set => SetField(ref _w0, value); }
        public ushort W2 { get => _w2; set => SetField(ref _w2, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 8 > (uint)rom.Data.Length) break;

                uint imgPtr = rom.u32(addr + 4);
                if (!U.isPointer(imgPtr)) break;

                string name = U.ToHexString(i) + " WaitIcon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            W0 = (ushort)rom.u16(addr + 0);
            W2 = (ushort)rom.u16(addr + 2);
            P4 = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0"] = $"0x{W0:X04}",
                ["W2"] = $"0x{W2:X04}",
                ["P4"] = $"0x{P4:X08}",
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
            };
        }
    }
}
