using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageTSAAnime2ViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _unknown0, _unknown2, _unknown4, _unknown6;
        uint _tsaHeaderPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // W0: Unknown parameter 0
        public uint Unknown0 { get => _unknown0; set => SetField(ref _unknown0, value); }
        // W2: Unknown parameter 2
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        // W4: Unknown parameter 4
        public uint Unknown4 { get => _unknown4; set => SetField(ref _unknown4, value); }
        // W6: Unknown parameter 6
        public uint Unknown6 { get => _unknown6; set => SetField(ref _unknown6, value); }
        // D8: TSA header pointer
        public uint TSAHeaderPointer { get => _tsaHeaderPointer; set => SetField(ref _tsaHeaderPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "TSA Animation Editor v2", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            Unknown0 = rom.u16(addr + 0);
            Unknown2 = rom.u16(addr + 2);
            Unknown4 = rom.u16(addr + 4);
            Unknown6 = rom.u16(addr + 6);
            TSAHeaderPointer = rom.u32(addr + 8);

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u16(addr + 0, Unknown0);
            rom.write_u16(addr + 2, Unknown2);
            rom.write_u16(addr + 4, Unknown4);
            rom.write_u16(addr + 6, Unknown6);
            rom.write_u32(addr + 8, TSAHeaderPointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Unknown0"] = $"0x{Unknown0:X04}",
                ["Unknown2"] = $"0x{Unknown2:X04}",
                ["Unknown4"] = $"0x{Unknown4:X04}",
                ["Unknown6"] = $"0x{Unknown6:X04}",
                ["TSAHeaderPointer"] = $"0x{TSAHeaderPointer:X08}",
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
                ["u16@0"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@2"] = $"0x{rom.u16(a + 2):X04}",
                ["u16@4"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@6"] = $"0x{rom.u16(a + 6):X04}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
