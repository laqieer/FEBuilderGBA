using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusParamViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _data0;
        uint _data4;
        uint _colorType;
        uint _namePointer;
        string _nameText = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Data0 { get => _data0; set => SetField(ref _data0, value); }
        public uint Data4 { get => _data4; set => SetField(ref _data4, value); }
        public uint ColorType { get => _colorType; set => SetField(ref _colorType, value); }
        public uint NamePointer { get => _namePointer; set => SetField(ref _namePointer, value); }
        public string NameText { get => _nameText; set => SetField(ref _nameText, value); }

        public List<AddrResult> LoadStatusParamList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.status_param1_pointer;
            if (ptr == 0) return new List<AddrResult>();

            if (!U.isSafetyOffset(ptr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(ptr + i * 16);
                if (addr + 16 > (uint)rom.Data.Length) break;

                // Validate: offset +12 should be a pointer
                uint namePtr = rom.u32(addr + 12);
                if (!U.isPointer(namePtr)) break;

                string name = U.ToHexString(i) + " Status Param";
                // Try to resolve name from pointer
                try
                {
                    uint nameAddr = U.toOffset(namePtr);
                    if (U.isSafetyOffset(nameAddr))
                    {
                        string resolved = rom.getString(nameAddr, 32);
                        if (!string.IsNullOrEmpty(resolved))
                            name = U.ToHexString(i) + " " + resolved;
                    }
                }
                catch { }

                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadStatusParam(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Data0 = rom.u32(addr + 0);
            Data4 = rom.u32(addr + 4);
            ColorType = rom.u8(addr + 8);
            NamePointer = rom.u32(addr + 12);

            // Resolve name
            NameText = "";
            try
            {
                if (U.isPointer(NamePointer))
                {
                    uint nameAddr = U.toOffset(NamePointer);
                    if (U.isSafetyOffset(nameAddr))
                        NameText = rom.getString(nameAddr, 32);
                }
            }
            catch { }

            IsLoaded = true;
        }

        public int GetListCount() => LoadStatusParamList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Data0"] = $"0x{Data0:X08}",
                ["Data4"] = $"0x{Data4:X08}",
                ["ColorType"] = $"0x{ColorType:X02}",
                ["NamePointer"] = $"0x{NamePointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
